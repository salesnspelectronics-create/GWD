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
        var parsed = Arguments.Parse(args);
        CliResult result;
        try
        {
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
            result = CliResult.Fail(parsed.Command, parsed.Project, Redact(ex.Message), ex.ExitCode);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(Redact(ex.ToString()));
            result = CliResult.Fail(parsed.Command, parsed.Project, Redact(ex.Message), 1);
        }
        result.DurationSeconds = Math.Round(started.Elapsed.TotalSeconds, 3);
        var json = JsonSerializer.Serialize(result, JsonOptions);
        if (!string.IsNullOrWhiteSpace(parsed.Get("result-file")))
        {
            var file = Path.GetFullPath(parsed.Get("result-file")!);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, json, new UTF8Encoding(false));
        }
        if (string.Equals(parsed.Get("output"), "json", StringComparison.OrdinalIgnoreCase)) Console.Out.WriteLine(json);
        else Console.Out.WriteLine(result.Success ? $"SUCCESS: {result.Operation}" : $"FAILED ({result.ExitCode}): {string.Join("; ", result.Errors)}");
        return result.ExitCode;
    }

    private static async Task<CliResult> ExecuteAsync(Arguments args)
    {
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
        try { stage = await deployment.PublishAsync(profile); }
        catch (Exception ex) { throw new CliFailure(3, "Build/publish failed: " + ex.Message); }
        ValidatePublishedOutput(profile, stage);
        return CliResult.Ok("publish", profile.ApplicationName, new() { ["sourcePath"] = profile.SourceFolder, ["publishPath"] = stage, ["version"] = DetectVersion(stage) });
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
        string stage;
        try { stage = await deployment.PublishAsync(profile); }
        catch (Exception ex) { throw new CliFailure(3, "Build/publish failed: " + ex.Message); }
        ValidatePublishedOutput(profile, stage);

        string backup;
        try { backup = CreateBackup(profile); }
        catch (Exception ex) { throw new CliFailure(4, "Backup failed: " + ex.Message); }
        PreserveProductionSettings(profile, stage);
        var previousSitePath = await deployment.GetSitePhysicalPathAsync(profile);
        string target = "";
        try
        {
            target = await deployment.ActivateStagingAsync(profile, stage);
            await deployment.ConfigureIisAsync(profile, target);
            if (!string.IsNullOrWhiteSpace(profile.SqlInstance)) await sql.CreateDatabaseAndAppPoolLoginAsync(profile);
            await deployment.RestartAppAsync(profile);
            var health = await HealthWithRetryAsync(profile, 4, TimeSpan.FromSeconds(3));
            if (!health.Passed)
            {
                if (args.Has("no-auto-rollback")) throw new CliFailure(5, "Health check failed and automatic rollback was disabled.");
                try
                {
                    await deployment.CleanupFailedDeploymentAsync(profile, target, previousSitePath);
                    var failure = CliResult.Fail("deploy", profile.ApplicationName, "Health check failed; automatic rollback succeeded.", 6);
                    failure.Data["healthCheck"] = health;
                    failure.Data["rolledBack"] = true;
                    failure.Data["backupPath"] = backup;
                    return failure;
                }
                catch (Exception rollbackEx) { throw new CliFailure(7, "Health check and automatic rollback failed: " + rollbackEx.Message); }
            }
            var verification = deployment.VerifyActivatedDeployment(profile, target);
            if (!verification.Ok) throw new CliFailure(5, verification.Detail);
            deployment.MarkDeploymentSuccessful(target);
            deployment.FinalizeSuccessfulDeployment(profile, target);
            return CliResult.Ok("deploy", profile.ApplicationName, new()
            {
                ["mode"] = mode, ["sourcePath"] = profile.SourceFolder, ["publishPath"] = target,
                ["targetPath"] = profile.HostingRoot, ["backupPath"] = backup,
                ["previousVersion"] = previousVersion, ["deployedVersion"] = DetectVersion(target),
                ["healthCheck"] = health, ["rolledBack"] = false
            });
        }
        catch (CliFailure) { throw; }
        catch (Exception ex) { throw new CliFailure(5, "Deploy failed: " + ex.Message); }
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
        var (_, deployment, _) = CreateEngine();
        var failed = profile.HostingRoot.TrimEnd('\\') + ".__cli_failed_" + DateTime.Now.ToString("yyyyMMddHHmmss");
        try
        {
            await deployment.StopSiteForDeploymentAsync(profile);
            if (Directory.Exists(profile.HostingRoot)) Directory.Move(profile.HostingRoot, failed);
            CopyTree(backup, profile.HostingRoot);
            await deployment.ConfigureIisAsync(profile, profile.HostingRoot);
            await deployment.RestartAppAsync(profile);
            var health = await HealthWithRetryAsync(profile, 4, TimeSpan.FromSeconds(3));
            if (!health.Passed) throw new IOException("Restored backup failed health check.");
            try { if (Directory.Exists(failed)) Directory.Delete(failed, true); } catch { }
            return CliResult.Ok("rollback", profile.ApplicationName, new() { ["backupPath"] = backup, ["targetPath"] = profile.HostingRoot, ["healthCheck"] = health });
        }
        catch (Exception ex)
        {
            try
            {
                if (Directory.Exists(profile.HostingRoot)) Directory.Delete(profile.HostingRoot, true);
                if (Directory.Exists(failed)) Directory.Move(failed, profile.HostingRoot);
                await deployment.RestartAppAsync(profile);
            }
            catch { throw new CliFailure(7, "Rollback failed and the pre-rollback site could not be restored: " + ex.Message); }
            throw new CliFailure(5, "Rollback failed; the pre-rollback site was restored: " + ex.Message);
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
                var site = await runner.RunAsync(appcmd, $"list site /name:\"{profile.SiteName}\"");
                if (!requireAdministrator && site.ExitCode == 5)
                {
                    data["iisSiteFound"] = null;
                    data["iisSiteCheck"] = "Not readable without Administrator access; actual deploy will revalidate.";
                }
                else
                {
                    data["iisSiteFound"] = site.ExitCode == 0 && !string.IsNullOrWhiteSpace(site.Output);
                    if (!(bool)data["iisSiteFound"]!) errors.Add("IIS site was not found: " + profile.SiteName);
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
    private static IEnumerable<string> GetBackups(DeploymentProfile profile) => Directory.Exists(ProjectBackupRoot(profile)) ? Directory.EnumerateDirectories(ProjectBackupRoot(profile)).OrderByDescending(Directory.GetCreationTimeUtc) : [];
    private static string ProjectBackupRoot(DeploymentProfile p) => Path.Combine(p.BackupRoot, SafeName(p.ApplicationName));

    private static string CreateBackup(DeploymentProfile profile)
    {
        if (!Directory.Exists(profile.HostingRoot)) throw new DirectoryNotFoundException("Hosted site folder was not found: " + profile.HostingRoot);
        var root = ProjectBackupRoot(profile);
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + SafeName(DetectVersion(profile.HostingRoot) ?? "unknown"));
        CopyTree(profile.HostingRoot, path);
        var sourceFiles = Directory.EnumerateFiles(profile.HostingRoot, "*", SearchOption.AllDirectories).ToArray();
        var backupFiles = Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).ToArray();
        if (backupFiles.Length == 0 || backupFiles.Length != sourceFiles.Length || backupFiles.Sum(x => new FileInfo(x).Length) != sourceFiles.Sum(x => new FileInfo(x).Length))
            throw new IOException("Backup verification failed: file count or total byte length does not match the hosted site.");
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
    private static void CopyTree(string source, string target) { foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir))); Directory.CreateDirectory(target); foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) { var dest = Path.Combine(target, Path.GetRelativePath(source, file)); Directory.CreateDirectory(Path.GetDirectoryName(dest)!); File.Copy(file, dest, true); } }
    private static string SafeName(string value) { foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-'); return value.Trim(); }
    private static string? DetectVersion(string folder) { try { var state = Path.Combine(folder, ".smart-deploy-state.json"); if (File.Exists(state)) { using var doc = JsonDocument.Parse(File.ReadAllText(state)); if (doc.RootElement.TryGetProperty("SourceVersion", out var v)) return v.GetString(); if (doc.RootElement.TryGetProperty("sourceVersion", out v)) return v.GetString(); } var dll = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly).FirstOrDefault() : null; return dll is null ? null : FileVersionInfo.GetVersionInfo(dll).FileVersion; } catch { return null; } }
    private static string Redact(string text) => System.Text.RegularExpressions.Regex.Replace(text, "(?i)(password|token|connectionstring)\\s*[:=]\\s*[^;\\s]+", "$1=[REDACTED]");
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
