using GenericWeb.DeploymentManager.Models;
using GenericWeb.DeploymentManager.Services;
using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

return await CliApplication.RunAsync(args);

internal static class CliApplication
{
    private static readonly string Version = typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static async Task<int> RunAsync(string[] args)
    {
        var started = Stopwatch.StartNew();
        Arguments? parsed = null;
        CliResult result;
        try
        {
            parsed = Arguments.Parse(args);
            if (args.Length == 0 || parsed.Has("help") || parsed.Command is "help" or "--help" or "-h")
            {
                Console.Out.WriteLine(HelpText());
                return 0;
            }
            AppContext.SetSwitch("Switch.Microsoft.Data.SqlClient.UseManagedNetworkingOnWindows", true);
            result = await ExecuteAsync(parsed);
        }
        catch (CliFailure ex)
        {
            result = CliResult.Fail(parsed?.Command ?? args.FirstOrDefault() ?? "", parsed?.Project, Redact(ex.Message), ex.ExitCode);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Redact(ex.ToString()));
            result = CliResult.Fail(parsed?.Command ?? args.FirstOrDefault() ?? "", parsed?.Project, Redact(ex.Message), 1);
        }
        result.DurationSeconds = Math.Round(started.Elapsed.TotalSeconds, 3);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        if (!(parsed?.Command == "backups" && parsed.Subcommand == "plan") && !string.IsNullOrWhiteSpace(parsed?.Get("result-file")))
        {
            var file = Path.GetFullPath(parsed.Get("result-file")!);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, json, new UTF8Encoding(false));
        }
        if (string.Equals(parsed?.Get("output"), "json", StringComparison.OrdinalIgnoreCase)) Console.Out.WriteLine(json);
        else Console.Out.WriteLine(result.Success ? $"SUCCESS: {result.Operation}" : $"FAILED ({result.ExitCode}): {string.Join("; ", result.Errors)}");
        return result.ExitCode;
    }

    private static async Task<CliResult> ExecuteAsync(Arguments args)
    {
        if (args.Command == "backups" && args.Subcommand == "plan")
        {
            var root = args.Get("root");
            if (string.IsNullOrWhiteSpace(root)) throw new CliFailure(2, "Read-only capacity planning requires --root with an absolute artifact container; no profile is loaded or saved.");
            var keep = 5;
            var days = 30;
            if (args.Has("keep") && (!int.TryParse(args.Get("keep"), out keep) || keep < 1)) throw new CliFailure(2, "--keep must be a positive integer.");
            if (args.Has("min-age-days") && (!int.TryParse(args.Get("min-age-days"), out days) || days < 1)) throw new CliFailure(2, "--min-age-days must be a positive integer.");
            return CliResult.Ok("backups plan", null, new() { ["plan"] = RetentionPlanner.Inspect(new[] { root }, keep, days) });
        }
        var appData = new AppDataService();
        if (args.Command == "projects" && args.Subcommand == "list")
        {
            var projects = appData.ListProjectProfiles().Select(PublicProfile).ToList();
            var legacy = appData.GetDeploymentIdentitySettings();
            if (!projects.Any(x => string.Equals(x["name"]?.ToString(), legacy.ApplicationName, StringComparison.OrdinalIgnoreCase)))
                projects.Add(PublicProfile(appData.CreateLegacyProjectProfile(legacy.ApplicationName)));
            return CliResult.Ok("projects list", null, new() { ["projects"] = projects });
        }

        var projectName = RequireProject(args);
        var profile = ResolveProject(appData, projectName);
        if (args.Command == "project" && args.Subcommand == "show") return CliResult.Ok("project show", projectName, PublicProfile(profile));
        if (args.Command == "backups" && args.Subcommand == "list") return ListBackups(profile);
        if (args.Command == "status") return await StatusAsync(profile);
        if (args.Command == "health-check") return await HealthAsync(profile);
        if (args.Command == "validate") return await ValidateResultAsync(profile, requireAdministrator: true, requireIisSite: true, "validate");
        if (args.Command == "publish") return await PublishAsync(profile, args);
        if (args.Command == "deploy") return await DeployAsync(profile, args);
        if (args.Command == "rollback") return await RollbackAsync(profile, args);
        throw new CliFailure(2, "Unknown command. Use --help for supported commands.");
    }

    private static async Task<CliResult> ValidateResultAsync(DeploymentProfile profile, bool requireAdministrator, bool requireIisSite, string operation)
    {
        var validation = await ValidateAsync(profile, requireAdministrator, requireIisSite);
        if (!validation.Success) throw new CliFailure(validation.ExitCode, string.Join("; ", validation.Errors));
        return CliResult.Ok(operation, profile.ApplicationName, validation.Data);
    }

    private static async Task<CliResult> PublishAsync(DeploymentProfile profile, Arguments args)
    {
        using var projectLock = AcquireProjectLock(profile.ApplicationName);
        var validation = await ValidateAsync(profile, false, false);
        if (!validation.Success) throw new CliFailure(validation.ExitCode, string.Join("; ", validation.Errors));
        profile.BuildMode = "Full IIS Deploy";
        var (runner, deployment, _) = CreateEngine();
        runner.Output += line => Console.Error.WriteLine(Redact(line));
        string stage;
        try { stage = await PublishWithQualityAsync(runner, deployment, profile); }
        catch (Exception ex) { throw new CliFailure(3, "Build/publish failed: " + ex.Message); }
        ValidatePublishedOutput(profile, stage);
        return CliResult.Ok("publish", profile.ApplicationName, new() { ["sourcePath"] = profile.SourceFolder, ["publishPath"] = stage, ["version"] = DetectVersion(stage) });
    }

    private static async Task<string> PublishWithQualityAsync(CommandRunner runner, DeploymentService deployment, DeploymentProfile profile)
    {
        var quality = new CodeQualitySession();
        try
        {
            var appData = new AppDataService();
            await Task.Run(() => quality.Start(appData, profile, new DeploymentHistoryService().GetProjectId(profile), Guid.NewGuid().ToString("N")));
            runner.CodeQuality = quality;
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "CLI Code Quality start (non-blocking)"); }
        var result = "Failed";
        try
        {
            var publish = await deployment.PublishAsync(profile);
            result = "Successful";
            return publish;
        }
        catch (OperationCanceledException) { result = "Cancelled"; throw; }
        finally { quality.Finish(result); runner.CodeQuality = null; }
    }

    private static async Task<CliResult> DeployAsync(DeploymentProfile profile, Arguments args)
    {
        RequireNonInteractive(args);
        using var projectLock = AcquireProjectLock(profile.ApplicationName);
        var mode = (args.Get("mode") ?? "full").ToLowerInvariant();
        if (mode is not ("full" or "fast")) throw new CliFailure(2, "--mode must be full or fast.");
        var dryRun = args.Has("dry-run");
        var validation = await ValidateAsync(profile, !dryRun, true);
        if (!validation.Success) throw new CliFailure(validation.ExitCode, string.Join("; ", validation.Errors));
        var previousVersion = DetectVersion(profile.HostingRoot);
        var dryRunData = new Dictionary<string, object?>(validation.Data)
        {
            ["mode"] = mode,
            ["dryRun"] = dryRun,
            ["administrator"] = IsAdministrator(),
            ["readyForActualDeploy"] = validation.Success && IsAdministrator(),
            ["plannedActions"] = new[] { "validate", "build/publish to staging", "validate artifacts", "backup", "preserve production settings", "stop application pool", "activate files", "start IIS", "health check with retry", "automatic rollback on failure" },
            ["previousVersion"] = previousVersion
        };
        if (dryRun)
        {
            var result = CliResult.Ok("deploy", profile.ApplicationName, dryRunData);
            if (!IsAdministrator()) result.Warnings.Add("Dry run made no changes. Administrator access will be required for an actual deployment.");
            return result;
        }

        profile.BuildMode = mode == "fast" ? "Fast IIS Update" : "Full IIS Deploy";
        var (runner, deployment, sql) = CreateEngine();
        runner.Output += line => Console.Error.WriteLine(Redact(line));
        using var iisAttempt = deployment.BeginIisAttempt(profile);
        await deployment.ValidateIisPreflightAsync(profile);
        deployment.IisStage("BUILD", "RUNNING");
        string stage;
        try { stage = await PublishWithQualityAsync(runner, deployment, profile); }
        catch (Exception ex) { throw new CliFailure(3, "Build/publish failed: " + ex.Message); }
        ValidatePublishedOutput(profile, stage);
        deployment.IisStage("BUILD", "PASS");

        string backup;
        try { backup = CreateBackup(profile); }
        catch (Exception ex) { throw new CliFailure(4, "Backup failed: " + ex.Message); }
        PreserveProductionSettings(profile, stage);
        var previousSitePath = await deployment.GetSitePhysicalPathAsync(profile);
        string target = "";
        try
        {
            target = await deployment.ActivateStagingAsync(profile, stage);
            deployment.IisStage("CONFIGURE", "RUNNING");
            await deployment.ConfigureIisAsync(profile, target);
            deployment.IisStage("CONFIGURE", "PASS");
            if (!string.IsNullOrWhiteSpace(profile.SqlInstance)) await sql.CreateDatabaseAndAppPoolLoginAsync(profile);
            deployment.IisStage("START", "RUNNING");
            await deployment.RestartAppAsync(profile);
            deployment.IisStage("START", "PASS");
            deployment.IisStage("HEALTH", "RUNNING");
            var engineHealth = await deployment.HealthCheckAsync(profile);
            deployment.RecordDeploymentHealth(engineHealth.Classification + "; " + engineHealth.Detail);
            if (!engineHealth.Ok) throw new CliFailure(5, engineHealth.Detail);
            var health = await HealthWithRetryAsync(profile, 4, TimeSpan.FromSeconds(3));
            if (!health.Passed) throw new CliFailure(5, "HTTP health failed after deployment.");
            var verification = deployment.VerifyActivatedDeployment(profile, target);
            if (!verification.Ok) throw new CliFailure(5, verification.Detail);
            var apiRelease = await deployment.VerifyRunningApiReleaseAsync(profile, target);
            if (!apiRelease.Ok) throw new CliFailure(5, apiRelease.Detail);
            var frontendRelease = await deployment.VerifyRunningFrontendReleaseAsync(profile, target);
            if (!frontendRelease.Ok) throw new CliFailure(5, frontendRelease.Detail);
            deployment.IisStage("HEALTH", "PASS");
            deployment.IisStage("COMMIT", "RUNNING");
            deployment.FinalizeSuccessfulDeployment(profile, target);
            deployment.IisStage("COMMIT", "PASS");
            deployment.ResolveIisAttempt();
            deployment.MarkDeploymentSuccessful(target);
            return CliResult.Ok("deploy", profile.ApplicationName, new()
            {
                ["mode"] = mode, ["sourcePath"] = profile.SourceFolder, ["publishPath"] = target,
                ["targetPath"] = profile.HostingRoot, ["backupPath"] = backup,
                ["previousVersion"] = previousVersion, ["deployedVersion"] = DetectVersion(target),
                ["healthCheck"] = health, ["rolledBack"] = false
            });
        }
        catch (Exception ex)
        {
            try { await deployment.CollectIisFailureDiagnosticsAsync(profile); }
            catch (Exception diagnosticEx) { runner.Report("[DIAGNOSTICS] Collection failed; primary deployment error preserved: " + diagnosticEx.Message); }
            if (!args.Has("no-auto-rollback"))
            {
                try { await deployment.CleanupFailedDeploymentAsync(profile, target, previousSitePath); }
                catch (Exception recoveryEx) { throw new CliFailure(7, "Deploy failed: " + ex.Message + "; Recovery: " + recoveryEx.Message); }
            }
            throw new CliFailure(5, "Deploy failed: " + ex.Message);
        }
    }

    private static async Task<CliResult> RollbackAsync(DeploymentProfile profile, Arguments args)
    {
        RequireNonInteractive(args);
        EnsureAdministrator();
        using var projectLock = AcquireProjectLock(profile.ApplicationName);
        var backups = GetBackups(profile).ToArray();
        var requested = args.Get("to") ?? "latest";
        var backup = requested.Equals("latest", StringComparison.OrdinalIgnoreCase) ? backups.FirstOrDefault() : backups.FirstOrDefault(x => Path.GetFileName(x).Equals(requested, StringComparison.OrdinalIgnoreCase));
        if (backup is null) throw new CliFailure(9, "Requested backup was not found.");
        var (runner, deployment, _) = CreateEngine();
        using var iisAttempt = deployment.BeginIisAttempt(profile);
        await deployment.ValidateIisPreflightAsync(profile);
        var displacedOriginal = false;
        var replacementStarted = false;
        var failed = profile.HostingRoot.TrimEnd('\\') + ".__cli_failed_" + DateTime.Now.ToString("yyyyMMddHHmmss") + "_" + Guid.NewGuid().ToString("N");
        try
        {
            DeploymentPathSafety.RequireSeparate(backup, profile.HostingRoot);
            DeploymentPathSafety.RequirePlainTree(backup);
            DeploymentPathSafety.RequirePlainTree(profile.HostingRoot);
            BackupIntegrity.Require(backup, profile.ApplicationName, profile.HostingRoot);
            await deployment.StopSiteForDeploymentAsync(profile);
            BackupIntegrity.Require(backup, profile.ApplicationName, profile.HostingRoot);
            if (Directory.Exists(profile.HostingRoot)) { Directory.Move(profile.HostingRoot, failed); displacedOriginal = true; }
            replacementStarted = true;
            var restoreManifest = BackupIntegrity.Require(backup, profile.ApplicationName, profile.HostingRoot);
            CopyTree(backup, profile.HostingRoot);
            BackupIntegrity.Verify(profile.HostingRoot, restoreManifest.Inventory);
            await deployment.ConfigureIisAsync(profile, profile.HostingRoot);
            await deployment.RestartAppAsync(profile);
            var engineHealth = await deployment.HealthCheckAsync(profile);
            deployment.RecordDeploymentHealth(engineHealth.Classification + "; " + engineHealth.Detail);
            if (!engineHealth.Ok) throw new CliFailure(5, engineHealth.Detail);
            var health = await HealthWithRetryAsync(profile, 4, TimeSpan.FromSeconds(3));
            if (!health.Passed) throw new IOException("Restored backup failed health check.");
            // Keep the displaced original until the successful attempt is durably resolved.
            // If resolution fails, the catch path still needs these files for recovery.
            deployment.ResolveIisAttempt();
            // Retain the displaced original as a manual recovery point; no age-based deletion.
            return CliResult.Ok("rollback", profile.ApplicationName, new() { ["backupPath"] = backup, ["targetPath"] = profile.HostingRoot, ["healthCheck"] = health });
        }
        catch (Exception ex)
        {
            if (!displacedOriginal)
            {
                if (replacementStarted)
                    throw new CliFailure(7, "Rollback failed without a displaced original; partial files and recovery markers retained: " + ex.Message);
                try { await deployment.CleanupFailedDeploymentAsync(profile, null, null, CancellationToken.None); }
                catch (Exception recoveryEx) { throw new CliFailure(7, "Rollback failed: " + ex.Message + "; IIS state recovery failed: " + recoveryEx.Message); }
                throw new CliFailure(5, "Rollback failed before file replacement; IIS recovery handler completed (see attempt journal): " + ex.Message);
            }
            try
            {
                await deployment.StopSiteForDeploymentAsync(profile);
                // Never remove the attempted restore unless the displaced original still exists.
                // A missing original requires manual recovery, not destructive cleanup.
                if (!Directory.Exists(failed))
                    throw new IOException("Pre-rollback site folder is missing; attempted restore preserved for inspection.");
                DeploymentPathSafety.RequirePlainTree(failed);
                DeploymentPathSafety.RequirePlainTree(profile.HostingRoot);
                if (Directory.Exists(profile.HostingRoot)) Directory.Delete(profile.HostingRoot, true);
                Directory.Move(failed, profile.HostingRoot);
                await deployment.RestoreOriginalIisStatesAsync(profile);
                runner.Report("[ROLLBACK] Original files and IIS states restored; HTTP health not checked; unresolved evidence retained.");
            }
            catch (Exception recoveryEx) { throw new CliFailure(7, "Rollback failed: " + ex.Message + "; Pre-rollback recovery failed: " + recoveryEx.Message); }
            throw new CliFailure(5, "Rollback failed; original files and IIS states restored, HTTP health not checked: " + ex.Message);
        }
    }

    private static async Task<Validation> ValidateAsync(DeploymentProfile profile, bool requireAdministrator, bool requireIisSite)
    {
        var errors = new List<string>();
        var data = new Dictionary<string, object?> { ["sourcePath"] = profile.SourceFolder, ["apiProjectPath"] = profile.ApiProjectPath, ["targetPath"] = profile.HostingRoot, ["siteName"] = profile.SiteName, ["backupRoot"] = profile.BackupRoot };
        if (requireAdministrator && !IsAdministrator()) return new(false, 8, ["Administrator access is required."], data);
        if (!Directory.Exists(profile.SourceFolder)) errors.Add("Source folder was not found.");
        if (!File.Exists(profile.ApiProjectPath)) errors.Add("Backend project file was not found.");
        var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        data["iisInstalled"] = File.Exists(appcmd);
        if (requireIisSite)
        {
            if (!File.Exists(appcmd)) errors.Add("IIS appcmd.exe was not found.");
            else
            {
                var runner = new CommandRunner();
                var site = await runner.RunAsync(appcmd, $"list site /name:\"{profile.SiteName}\" /text:state");
                if (!requireAdministrator && site.ExitCode == 5)
                {
                    data["iisSiteFound"] = null;
                    data["iisSiteCheck"] = "Not readable without Administrator access; actual deploy will revalidate.";
                }
                else
                {
                    data["iisSiteFound"] = IisSafety.ParseState(site) is "Started" or "Stopped";
                    if (!(bool)data["iisSiteFound"]!) errors.Add("IIS site state invalid/unknown: " + profile.SiteName);
                    var pool = await runner.RunAsync(appcmd, $"list apppool /name:\"{profile.AppPoolName}\" /text:state");
                    if (IisSafety.ParseState(pool) is not ("Started" or "Stopped")) errors.Add("IIS app pool state invalid/unknown: " + profile.AppPoolName);
                }
            }
        }
        data["sourceFreeBytes"] = FreeBytes(profile.SourceFolder);
        data["targetFreeBytes"] = FreeBytes(Path.GetPathRoot(profile.HostingRoot) ?? profile.HostingRoot);
        data["backupFreeBytes"] = FreeBytes(Path.GetPathRoot(profile.BackupRoot) ?? profile.BackupRoot);
        if ((long)data["targetFreeBytes"]! < 1_073_741_824L) errors.Add("Less than 1 GB is free on the target drive.");
        if ((long)data["backupFreeBytes"]! < 1_073_741_824L) errors.Add("Less than 1 GB is free on the backup drive.");
        if (errors.Count == 0) return new(true, 0, errors, data);
        var notFound = errors.Any(x => x.Contains("not found", StringComparison.OrdinalIgnoreCase));
        return new(false, notFound ? 9 : 2, errors, data);
    }

    private static async Task<CliResult> StatusAsync(DeploymentProfile profile)
    {
        var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        string status = "IIS unavailable";
        if (File.Exists(appcmd))
        {
            var r = await new CommandRunner().RunAsync(appcmd, $"list site /name:\"{profile.SiteName}\"");
            if (r.ExitCode == 5) throw new CliFailure(8, "Administrator access is required to read IIS status.");
            if (r.ExitCode != 0 || string.IsNullOrWhiteSpace(r.Output)) throw new CliFailure(9, "IIS site was not found: " + profile.SiteName);
            status = r.Output.Trim();
        }
        return CliResult.Ok("status", profile.ApplicationName, new() { ["site"] = status, ["targetPath"] = profile.HostingRoot, ["deployedVersion"] = DetectVersion(profile.HostingRoot) });
    }

    private static async Task<CliResult> HealthAsync(DeploymentProfile profile)
    {
        var health = await HealthWithRetryAsync(profile, 3, TimeSpan.FromSeconds(2));
        if (!health.Passed)
        {
            var failed = CliResult.Fail("health-check", profile.ApplicationName, health.Error ?? "Health check failed.", 5);
            failed.Data["healthCheck"] = health;
            return failed;
        }
        return CliResult.Ok("health-check", profile.ApplicationName, new() { ["healthCheck"] = health });
    }

    private static async Task<HealthResult> HealthWithRetryAsync(DeploymentProfile profile, int attempts, TimeSpan delay)
    {
        var host = string.IsNullOrWhiteSpace(profile.HostHeader) ? "localhost" : profile.HostHeader;
        var url = profile.HttpPort == 80 ? $"http://{host}{profile.HealthPath}" : $"http://{host}:{profile.HttpPort}{profile.HealthPath}";
        string? error = null;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                using var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode) return new(url, (int)response.StatusCode, true, attempt, null);
                error = $"HTTP {(int)response.StatusCode}";
            }
            catch (Exception ex) { error = ex.Message; }
            if (attempt < attempts) await Task.Delay(delay);
        }
        return new(url, null, false, attempts, error);
    }

    private static CliResult ListBackups(DeploymentProfile profile) => CliResult.Ok("backups list", profile.ApplicationName, new() { ["backups"] = GetBackups(profile).Select(x => new { path = x, version = DetectVersion(x), createdUtc = Directory.GetCreationTimeUtc(x) }).ToArray() });
    private static IEnumerable<string> GetBackups(DeploymentProfile profile) => Directory.Exists(ProjectBackupRoot(profile)) ? Directory.EnumerateDirectories(ProjectBackupRoot(profile)).Where(path => !Path.GetFileName(path).StartsWith(".incomplete-", StringComparison.OrdinalIgnoreCase)).OrderByDescending(Directory.GetCreationTimeUtc) : [];
    private static string ProjectBackupRoot(DeploymentProfile p)
    {
        var name = SafeName(p.ApplicationName);
        if (string.IsNullOrWhiteSpace(name) || name is "." or "..") throw new IOException("Invalid backup project folder name.");
        var root = DeploymentPathSafety.Normalize(p.BackupRoot);
        var project = DeploymentPathSafety.RequireApplicationDirectory(Path.Combine(root, name));
        if (!DeploymentPathSafety.Contains(root, project) || root.Equals(project, StringComparison.OrdinalIgnoreCase))
            throw new IOException("Backup project folder escapes its root.");
        return project;
    }

    private static string CreateBackup(DeploymentProfile profile)
    {
        if (!Directory.Exists(profile.HostingRoot)) throw new DirectoryNotFoundException("Hosted site folder was not found: " + profile.HostingRoot);
        var root = ProjectBackupRoot(profile);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + SafeName(DetectVersion(profile.HostingRoot) ?? "unknown") + "-" + Guid.NewGuid().ToString("N"));
        var staging = Path.Combine(root, ".incomplete-" + Path.GetFileName(path));
        var inventory = BackupIntegrity.Capture(profile.HostingRoot);
        CopyTree(profile.HostingRoot, staging);
        // Equal file counts and aggregate sizes cannot establish a recoverable backup.
        // Compare each relative path and its SHA256 before permitting deployment.
        var sourceFiles = Directory.EnumerateFiles(profile.HostingRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(profile.HostingRoot, file), StringComparer.OrdinalIgnoreCase);
        var backupFiles = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
            .ToDictionary(file => Path.GetRelativePath(staging, file), StringComparer.OrdinalIgnoreCase);
        if (sourceFiles.Count == 0 || sourceFiles.Count != backupFiles.Count)
            throw new IOException("Backup verification failed: file count does not match the hosted site.");
        foreach (var (relative, sourceFile) in sourceFiles)
        {
            if (!backupFiles.TryGetValue(relative, out var backupFile)
                || new FileInfo(sourceFile).Length != new FileInfo(backupFile).Length
                || !string.Equals(WorkspaceSynchronizer.Hash(sourceFile), WorkspaceSynchronizer.Hash(backupFile), StringComparison.Ordinal))
                throw new IOException("Backup verification failed: missing or mismatched file: " + relative);
        }
        BackupIntegrity.Verify(staging, inventory);
        BackupIntegrity.Verify(profile.HostingRoot, inventory);
        Directory.Move(staging, path);
        BackupIntegrity.Complete(path, profile.ApplicationName, profile.HostingRoot, inventory);
        return path;
    }

    private static void PreserveProductionSettings(DeploymentProfile profile, string stage)
    {
        if (!Directory.Exists(profile.HostingRoot)) return;
        foreach (var pattern in profile.PreserveProductionFiles ?? [])
        foreach (var source in Directory.EnumerateFiles(profile.HostingRoot, pattern, SearchOption.TopDirectoryOnly))
            File.Copy(source, Path.Combine(stage, Path.GetFileName(source)), true);
    }

    private static void ValidatePublishedOutput(DeploymentProfile profile, string stage)
    {
        if (!Directory.Exists(stage)) throw new CliFailure(3, "Publish staging folder is missing.");
        if (!File.Exists(Path.Combine(stage, "web.config"))) throw new CliFailure(3, "Published web.config is missing.");
        var expectedDll = Path.GetFileNameWithoutExtension(profile.ApiProjectPath) + ".dll";
        if (!File.Exists(Path.Combine(stage, expectedDll))) throw new CliFailure(3, "Published main application DLL is missing: " + expectedDll);
        var wwwroot = Path.Combine(stage, "wwwroot");
        if (profile.RequiresNode && (!Directory.Exists(wwwroot) || !Directory.EnumerateFiles(wwwroot, "*", SearchOption.AllDirectories).Any())) throw new CliFailure(3, "Published frontend files are missing.");
    }

    private static (CommandRunner Runner, DeploymentService Deployment, SqlServerService Sql) CreateEngine()
    {
        var appData = new AppDataService();
        var runner = new CommandRunner();
        var prereq = new PrerequisiteService(runner);
        var sql = new SqlServerService(runner, prereq);
        return (runner, new DeploymentService(runner, sql, appData), sql);
    }

    private static DeploymentProfile ResolveProject(AppDataService appData, string name)
    {
        var stored = appData.LoadProjectProfile(name);
        if (stored is not null) return stored;
        var identity = appData.GetDeploymentIdentitySettings();
        if (!string.Equals(identity.ApplicationName, name, StringComparison.OrdinalIgnoreCase)) throw new CliFailure(9, "Project was not found: " + name);
        var legacy = appData.CreateLegacyProjectProfile(name);
        appData.SaveProjectProfile(legacy);
        return legacy;
    }

    private static IDisposable AcquireProjectLock(string project)
    {
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(project.ToUpperInvariant())))[..24];
        var mutex = new Mutex(false, @"Global\NSP.DeploymentManager." + key);
        try { if (!mutex.WaitOne(0)) { mutex.Dispose(); throw new CliFailure(10, "Another operation is already running for this project."); } }
        catch (AbandonedMutexException) { }
        return new MutexLease(mutex);
    }

    private static void RequireNonInteractive(Arguments args)
    {
        if (!args.Has("non-interactive")) throw new CliFailure(2, "Deploy and rollback require --non-interactive in CLI mode.");
    }
    private static string RequireProject(Arguments args) => string.IsNullOrWhiteSpace(args.Project) ? throw new CliFailure(2, "--project is required.") : args.Project!;
    private static void EnsureAdministrator() { if (!IsAdministrator()) throw new CliFailure(8, "Administrator access is required."); }
    private static bool IsAdministrator() { try { using var id = WindowsIdentity.GetCurrent(); return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator); } catch { return false; } }
    private static long FreeBytes(string path) { try { var root = Path.GetPathRoot(Path.GetFullPath(path)); return root is null ? 0 : new DriveInfo(root).AvailableFreeSpace; } catch { return 0; } }
    private static void CopyTree(string source, string target)
    {
        DeploymentPathSafety.RequireSeparate(source, target);
        DeploymentPathSafety.RequirePlainTree(source);
        DeploymentPathSafety.RequirePlainTree(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        Directory.CreateDirectory(target);
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(target, Path.GetRelativePath(source, file));
            DeploymentPathSafety.Normalize(file);
            DeploymentPathSafety.Normalize(dest);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, true);
        }
    }
    private static string SafeName(string value) { foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-'); return value.Trim(); }
    private static string? DetectVersion(string folder) { try { var state = Path.Combine(folder, ".smart-deploy-state.json"); if (File.Exists(state)) { using var doc = JsonDocument.Parse(File.ReadAllText(state)); if (doc.RootElement.TryGetProperty("SourceVersion", out var v)) return v.GetString(); if (doc.RootElement.TryGetProperty("sourceVersion", out v)) return v.GetString(); } var dll = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault() : null; return dll is null ? null : FileVersionInfo.GetVersionInfo(dll).FileVersion; } catch { return null; } }
    private static string Redact(string text) => SecretRedactor.Redact(text);
    private static Dictionary<string, object?> PublicProfile(DeploymentProfile p) => new() { ["name"] = p.ApplicationName, ["sourcePath"] = p.SourceFolder, ["apiProjectPath"] = p.ApiProjectPath, ["siteName"] = p.SiteName, ["appPoolName"] = p.AppPoolName, ["targetPath"] = p.HostingRoot, ["healthUrl"] = (p.HttpPort == 80 ? "http://localhost" : $"http://localhost:{p.HttpPort}") + p.HealthPath, ["backupRoot"] = p.BackupRoot, ["dependencyMode"] = p.DependencyResolutionMode, ["workspaceRoot"] = p.WorkspaceRoot, ["preserveProductionFiles"] = p.PreserveProductionFiles };

    private static string HelpText() => """
Deployment Manager CLI V{VERSION}

DeploymentManager.exe projects list --output json
DeploymentManager.exe project show --project "NSP Office" --output json
DeploymentManager.exe validate --project "NSP Office" --output json
DeploymentManager.exe publish --project "NSP Office" --output json
DeploymentManager.exe deploy --project "NSP Office" --mode full|fast --non-interactive [--dry-run] [--no-auto-rollback] --output json
DeploymentManager.exe status --project "NSP Office" --output json
DeploymentManager.exe health-check --project "NSP Office" --output json
DeploymentManager.exe rollback --project "NSP Office" --to latest --non-interactive --output json
DeploymentManager.exe backups list --project "NSP Office" --output json
DeploymentManager.exe backups plan --root "C:\Deploy" [--keep 5] [--min-age-days 30] --output json
  Read-only stdout report; --result-file is ignored for backups plan.

Optional: --result-file "C:\Temp\deploy-result.json"
""".Replace("{VERSION}", Version.Split('.')[0]);
}

internal sealed class Arguments
{
    private readonly Dictionary<string, string?> _options = new(StringComparer.OrdinalIgnoreCase);
    public string Command { get; private init; } = "";
    public string? Subcommand { get; private set; }
    public string? Project => Get("project");
    public string? Get(string name) => _options.TryGetValue(name, out var value) ? value : null;
    public bool Has(string name) => _options.ContainsKey(name);
    public static Arguments Parse(string[] args)
    {
        var result = new Arguments { Command = args.FirstOrDefault()?.ToLowerInvariant() ?? "" };
        var index = 1;
        if (result.Command is "projects" or "project" or "backups") { result.Subcommand = index < args.Length && !args[index].StartsWith("--") ? args[index++].ToLowerInvariant() : null; }
        while (index < args.Length)
        {
            var token = args[index++];
            if (!token.StartsWith("--")) throw new CliFailure(2, "Unexpected argument: " + token);
            var name = token[2..];
            string? value = null;
            if (index < args.Length && !args[index].StartsWith("--")) value = args[index++];
            result._options[name] = value;
        }
        return result;
    }
}

internal sealed class CliResult
{
    public bool Success { get; set; }
    public string Operation { get; set; } = "";
    public string? Project { get; set; }
    public int ExitCode { get; set; }
    public double DurationSeconds { get; set; }
    public List<string> Warnings { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public Dictionary<string, object?> Data { get; set; } = new();
    public static CliResult Ok(string operation, string? project, Dictionary<string, object?>? data = null) => new() { Success = true, Operation = operation, Project = project, Data = data ?? new() };
    public static CliResult Fail(string operation, string? project, string error, int code) => new() { Success = false, Operation = operation, Project = project, ExitCode = code, Errors = [error] };
}

internal sealed record Validation(bool Success, int ExitCode, List<string> Errors, Dictionary<string, object?> Data);
internal sealed record HealthResult(string Url, int? StatusCode, bool Passed, int Attempts, string? Error);
internal sealed class CliFailure(int exitCode, string message) : Exception(message) { public int ExitCode { get; } = exitCode; }
internal sealed class MutexLease(Mutex mutex) : IDisposable { public void Dispose() { try { mutex.ReleaseMutex(); } catch { } mutex.Dispose(); } }

