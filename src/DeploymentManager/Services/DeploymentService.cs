using GenericWeb.DeploymentManager.Models;
using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GenericWeb.DeploymentManager.Services;

public sealed class DeploymentService
{
    private readonly CommandRunner _runner;
    private readonly SqlServerService _sql;
    private readonly AppDataService _appData;
    private SmartDeployState? _lastPreparedState;
    private string? _activeRollbackFolder;
    public DeploymentService(CommandRunner runner, SqlServerService sql, AppDataService appData) { _runner = runner; _sql = sql; _appData = appData; }

    public BuildPerformanceTrace? LastPerformanceSnapshot { get; private set; }

    public sealed record PreparedPublishValidation(bool Ok, string Detail, string? ApplicationBinary = null, bool MetadataPresent = false);

    public static PreparedPublishValidation ValidatePreparedPublish(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return new(false, "Prepared Publish: INVALID — folder does not exist.");
        var webConfig = Path.Combine(folder, "web.config");
        if (!File.Exists(webConfig)) return new(false, "Prepared Publish: INVALID — web.config is missing.");
        var wwwroot = Path.Combine(folder, "wwwroot");
        if (!Directory.Exists(wwwroot)) return new(false, "Prepared Publish: INVALID — wwwroot is missing.");
        var binaries = Directory.EnumerateFiles(folder, "*.dll", SearchOption.TopDirectoryOnly)
            .Concat(Directory.EnumerateFiles(folder, "*.exe", SearchOption.TopDirectoryOnly))
            .Where(f => !Path.GetFileName(f).StartsWith("apphost", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (binaries.Length == 0) return new(false, "Prepared Publish: INVALID — no primary application DLL/EXE was found.");
        var metadata = Path.Combine(folder, "BUILD-METADATA.json");
        if (File.Exists(metadata))
        {
            try
            {
                var node = JsonNode.Parse(File.ReadAllText(metadata));
                string Read(string key) => node?[key]?.ToString()?.Trim() ?? string.Empty;
                foreach (var key in new[] { "build", "publish", "iis_validation" })
                    if (!string.Equals(Read(key), "PASS", StringComparison.OrdinalIgnoreCase))
                        return new(false, $"Prepared Publish: INVALID — BUILD-METADATA.json requires {key}=PASS (actual: {Read(key)}).", Path.GetFileName(binaries[0]), true);
            }
            catch (Exception ex) { InternalDiagnostics.Record(ex, "Validate BUILD-METADATA.json"); return new(false, "Prepared Publish: INVALID — BUILD-METADATA.json could not be parsed: " + ex.Message, Path.GetFileName(binaries[0]), true); }
        }
        return new(true, $"Prepared Publish: VALID — Application: {Path.GetFileName(binaries[0])}; Source Build Required: NO; Frontend Build Required: NO; .NET Publish Required: NO; Metadata: {(File.Exists(metadata) ? "PASS" : "not present")}", Path.GetFileName(binaries[0]), File.Exists(metadata));
    }

    private sealed record SmartDeployState(
        string FrontendHash,
        string BackendHash,
        string DependencyHash,
        string MigrationHash,
        DateTime CreatedAtUtc,
        string BackendDependencyHash = "",
        string FrontendOutputHash = "",
        string SourceVersion = "",
        bool DependenciesValidated = false);

    private sealed record WorkspaceSyncResult(DeploymentProfile Profile, string Workspace, int Added, int Updated, int Unchanged, int Removed, int Verified);

    public void ResetPerformanceSnapshot()
    {
        LastPerformanceSnapshot = null;
        _lastPreparedState = null;
    }

    private static string HashFiles(IEnumerable<string> files, string root)
    {
        using var sha = SHA256.Create();
        foreach (var file in files.Where(File.Exists).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            var rel = Path.GetRelativePath(root, file).Replace('\\','/');
            var name = System.Text.Encoding.UTF8.GetBytes(rel + "\n");
            sha.TransformBlock(name, 0, name.Length, null, 0);
            using var fs = File.OpenRead(file);
            var buffer = new byte[64 * 1024];
            int read;
            while ((read = fs.Read(buffer, 0, buffer.Length)) > 0) sha.TransformBlock(buffer, 0, read, null, 0);
        }
        sha.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
        return Convert.ToHexString(sha.Hash!);
    }

    private static string HashDirectory(string folder)
    {
        if (!Directory.Exists(folder)) return "NONE";
        return HashFiles(Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories), folder);
    }

    private static bool IsGeneratedPath(string rel)
    {
        var p = "/" + rel.Replace('\\','/').Trim('/') + "/";
        return p.Contains("/node_modules/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/bin/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/.git/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/.worktrees/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/dist/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/build/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/out/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/.vite/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/iis-publish/", StringComparison.OrdinalIgnoreCase)
            || p.Contains("/publish/", StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateSourceFiles(string root, Func<string,bool> include)
    {
        if (!Directory.Exists(root)) return Array.Empty<string>();
        return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(f =>
        {
            var rel = Path.GetRelativePath(root, f).Replace('\\','/');
            if (IsGeneratedPath(rel)) return false;
            return include(rel);
        });
    }

    private static IReadOnlyList<string> FindFrontendRoots(DeploymentProfile profile)
    {
        if (!Directory.Exists(profile.SourceFolder)) return Array.Empty<string>();
        return Directory.EnumerateFiles(profile.SourceFolder, "package.json", SearchOption.AllDirectories)
            .Where(x => !IsGeneratedPath(Path.GetRelativePath(profile.SourceFolder, x)))
            .Select(Path.GetDirectoryName)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => Path.GetFullPath(x!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool IsUnderAnyRoot(string file, IReadOnlyList<string> roots)
    {
        var full = Path.GetFullPath(file).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return roots.Any(root => full.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    private SmartDeployState ComputeSmartState(DeploymentProfile profile, string frontendOutputHash = "")
    {
        var root = profile.SourceFolder;
        var frontendRoots = FindFrontendRoots(profile);
        var frontendExcludeRoots = frontendRoots.Where(fr => !PathsEqual(fr, root)).ToArray();
        var frontendFiles = new List<string>();
        var dependencyFiles = new List<string>();
        foreach (var fr in frontendRoots)
        {
            frontendFiles.AddRange(EnumerateSourceFiles(fr, rel =>
                !rel.Equals("package.json", StringComparison.OrdinalIgnoreCase) &&
                !rel.Equals("package-lock.json", StringComparison.OrdinalIgnoreCase) &&
                !rel.Equals("npm-shrinkwrap.json", StringComparison.OrdinalIgnoreCase) &&
                !rel.Equals("pnpm-lock.yaml", StringComparison.OrdinalIgnoreCase) &&
                !rel.Equals("yarn.lock", StringComparison.OrdinalIgnoreCase)));
            foreach (var name in new[]{"package.json","package-lock.json","npm-shrinkwrap.json","pnpm-lock.yaml","yarn.lock"})
            {
                var candidate = Path.Combine(fr, name);
                if (File.Exists(candidate)) dependencyFiles.Add(candidate);
            }
        }

        var frontendHash = frontendRoots.Count == 0 ? "NONE" : HashFiles(frontendFiles, root);
        var dependencyHash = frontendRoots.Count == 0 ? "NONE" : HashFiles(dependencyFiles, root);
        var backendHash = HashFiles(EnumerateSourceFiles(root, rel =>
        {
            var full = Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar));
            if (IsUnderAnyRoot(full, frontendExcludeRoots)) return false;
            if (rel.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase)) return false;
            if (rel.Contains("/wwwroot/", StringComparison.OrdinalIgnoreCase)) return false;
            return rel.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || rel.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || rel.EndsWith(".props", StringComparison.OrdinalIgnoreCase)
                || rel.EndsWith(".targets", StringComparison.OrdinalIgnoreCase)
                || rel.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }), root);
        var migrationHash = HashFiles(EnumerateSourceFiles(root, rel => rel.Contains("/Migrations/", StringComparison.OrdinalIgnoreCase) || rel.EndsWith("DbInitializer.cs", StringComparison.OrdinalIgnoreCase)), root);
        var backendDependencyHash = HashFiles(EnumerateSourceFiles(root, rel =>
            rel.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
            rel.EndsWith(".props", StringComparison.OrdinalIgnoreCase) ||
            rel.EndsWith(".targets", StringComparison.OrdinalIgnoreCase) ||
            rel.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase) ||
            rel.EndsWith("nuget.config", StringComparison.OrdinalIgnoreCase)), root);
        return new SmartDeployState(frontendHash, backendHash, dependencyHash, migrationHash, DateTime.UtcNow, backendDependencyHash, frontendOutputHash, TryReadSourceVersion(profile));
    }

    private string GetPersistentSmartStatePath(DeploymentProfile profile)
    {
        var repository = string.IsNullOrWhiteSpace(profile.DeploymentRepositoryFolder) ? profile.InstallerCacheFolder : profile.DeploymentRepositoryFolder;
        var keySeed = $"{profile.ApplicationName}|{Path.GetFullPath(profile.SourceFolder).TrimEnd('\\')}|{Path.GetFileName(profile.ApiProjectPath)}";
        var key = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(keySeed)))[..20];
        var root = Path.Combine(repository, "BuildState", SanitizeFolderName(profile.ApplicationName));
        Directory.CreateDirectory(root);
        return Path.Combine(root, key + ".json");
    }

    private SmartDeployState? ReadSmartState(DeploymentProfile profile)
    {
        try
        {
            var path = GetPersistentSmartStatePath(profile);
            if (File.Exists(path)) return JsonSerializer.Deserialize<SmartDeployState>(File.ReadAllText(path));
            // V14.26/14.27 compatibility: import the old hosted state once.
            var legacy = Path.Combine(profile.HostingRoot, ".smart-deploy-state.json");
            return File.Exists(legacy) ? JsonSerializer.Deserialize<SmartDeployState>(File.ReadAllText(legacy)) : null;
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Read persistent smart deploy state"); return null; }
    }

    private void WriteSmartState(string output, SmartDeployState state)
    {
        File.WriteAllText(Path.Combine(output,".smart-deploy-state.json"), JsonSerializer.Serialize(state, new JsonSerializerOptions{WriteIndented=true}));
        _lastPreparedState = state;
    }

    private void CommitPersistentSmartState(DeploymentProfile profile, SmartDeployState state)
    {
        var path = GetPersistentSmartStatePath(profile);
        var temp = path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, path, true);
        _runner.Report($"[FAST] Persistent successful build state committed: {path}");
    }

    private static string TryReadSourceVersion(DeploymentProfile profile)
    {
        try
        {
            var candidates = Directory.EnumerateFiles(profile.SourceFolder, "*", SearchOption.AllDirectories)
                .Where(f => !IsGeneratedPath(Path.GetRelativePath(profile.SourceFolder, f)))
                .Where(f => new[]{"version.txt","version.json","build-version.txt","release-version.txt"}.Contains(Path.GetFileName(f), StringComparer.OrdinalIgnoreCase))
                .Take(8);
            foreach (var file in candidates)
            {
                var text = File.ReadAllText(file).Trim();
                if (text.Length == 0 || text.Length > 512) continue;
                if (file.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                {
                    var node = JsonNode.Parse(text);
                    var value = node?["version"]?.ToString() ?? node?["Version"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
                }
                else return text.Split(new[]{'\r','\n'}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? "";
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Read source version metadata"); }
        return "";
    }

    private static string? ResolveFrontendOutputFolder(string clientFolder)
    {
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(clientFolder, "..", "wwwroot")),
            Path.Combine(clientFolder, "wwwroot"),
            Path.Combine(clientFolder, "dist"),
            Path.Combine(clientFolder, "build"),
            Path.Combine(clientFolder, "out")
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (candidates.Length == 0) return null;
        return candidates.OrderByDescending(GetDirectoryLatestWriteUtc).First();
    }

    private static DateTime GetDirectoryLatestWriteUtc(string folder)
    {
        try { return Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(Directory.GetLastWriteTimeUtc(folder)).Max(); }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Read directory latest write time"); return DateTime.MinValue; }
    }

    private sealed record FastCachePaths(string Root, string NpmCache, string NugetPackages);

    private static FastCachePaths GetFastCachePaths(DeploymentProfile profile)
    {
        var repository = string.IsNullOrWhiteSpace(profile.DeploymentRepositoryFolder)
            ? profile.InstallerCacheFolder
            : profile.DeploymentRepositoryFolder;
        return new FastCachePaths(repository, Path.Combine(repository, "Node", "npm-cache"), Path.Combine(repository, "NuGet", "packages"));
    }

    public async Task<string> PublishAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        _lastPreparedState = null;
        profile.DependencyResolutionMode = NormalizeDependencyMode(profile.DependencyResolutionMode);
        profile.OfflineBuild = IsOfflineOnly(profile);
        var trace = new BuildPerformanceTrace(profile.BuildMode, profile.DependencyResolutionMode);
        LastPerformanceSnapshot = trace;
        try
        {
        // Future Git updates must finish in Incoming before entering this pipeline.
        // Keep the workspace exclusively owned through verification and publish.
        // V74: validate source provenance before copying anything to the stable workspace.
        if (!string.Equals(profile.BuildMode, "Prepared IIS Publish", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(profile.GitSourceVerificationMode, "Strict", StringComparison.OrdinalIgnoreCase))
            VerifyIncomingGitProvenance(profile);
        else
            _runner.Report("[SOURCE] Git verification OFF; source path and workspace SHA256 controls remain active.");
        using var workspaceLease = AcquireWorkspaceLease(profile);
        if (!string.Equals(profile.BuildMode, "Prepared IIS Publish", StringComparison.OrdinalIgnoreCase))
        {
            var incoming = profile.SourceFolder;
            var sync = trace.Measure("Stable workspace sync", () => PrepareStableWorkspace(profile, ct), "Synchronizes changed source files for every source-based mode while preserving node_modules/bin/obj build caches.");
            profile = sync.Profile;
            _runner.Report($"[WORKSPACE] Incoming source (read-only): {incoming}");
            _runner.Report($"[WORKSPACE] Path: {sync.Workspace}");
            _runner.Report($"[WORKSPACE] Added: {sync.Added}; Updated: {sync.Updated}; Unchanged: {sync.Unchanged}; Removed: {sync.Removed}; Verified: {sync.Verified}; Mismatch: 0");
        }
        if (!string.Equals(profile.BuildMode, "Prepared IIS Publish", StringComparison.OrdinalIgnoreCase))
            VerifyBuildSourceIdentity(profile);
        var version = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var stagingRoot = Path.Combine(profile.DeploymentRoot, "Staging");
        var output = Path.Combine(stagingRoot, version);
        trace.Measure("Staging cleanup / create", () =>
        {
            Directory.CreateDirectory(stagingRoot);
            foreach (var stale in Directory.GetDirectories(stagingRoot))
            {
                try { Directory.Delete(stale, recursive: true); } catch (Exception ex) { InternalDiagnostics.Record(ex, "Delete stale staging folder: " + stale); }
            }
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, ".deployment-incomplete"), DateTime.Now.ToString("O"));
        }, "Deletes stale staging folders and creates the new publish staging folder.");
        _runner.Report($"[DEPLOY] Building into staging folder: {output}");
        _runner.Report($"[BUILD] Build mode: {profile.BuildMode}");
        _runner.Report($"[BUILD] Dependency mode: {profile.DependencyResolutionMode}");
        _runner.Report($"[BUILD] Offline build: {(profile.OfflineBuild ? "ENABLED" : "DISABLED")}");
        ReportDependencyCheck(profile);
        if (profile.OfflineBuild)
            _runner.Report("[OFFLINE] Internet NuGet/npm dependency retrieval is disabled for build stages. Change-aware modes may skip restore/build when the fingerprint proves it safe.");

        if (string.Equals(profile.BuildMode, "Fast Local Test", StringComparison.OrdinalIgnoreCase))
        {
            await BuildFastLocalTestAsync(profile, output, trace, ct);
            return output;
        }

        // Fast Build is the default deployment path. It uses the last successful hosted state as a trusted
        // incremental baseline and keeps reusable package caches outside the source tree in Deployment Repository.
        SmartDeployState? fastCurrentState = null;
        SmartDeployState? fastPreviousState = null;
        var fastSkipFrontendBuild = false;
        var fastBackendDependenciesChanged = true;
        var fastDependenciesValidated = false;
        string? builtFrontendOutputFolder = null;
        if (false && profile.BuildMode == "Fast IIS Update" && Directory.Exists(profile.HostingRoot))
        {
            var currentState = trace.Measure("Fast fingerprint scan", () => ComputeSmartState(profile), "Hashes frontend/backend/dependency/migration source state.");
            var previousState = trace.Measure("Read previous build state", () => ReadSmartState(profile), "Loads the persistent successful Fast Build state.");
            fastCurrentState = currentState;
            fastPreviousState = previousState;
            if (previousState is not null)
            {
                var frontendChanged = !string.Equals(currentState.FrontendHash, previousState.FrontendHash, StringComparison.Ordinal);
                var backendChanged = !string.Equals(currentState.BackendHash, previousState.BackendHash, StringComparison.Ordinal);
                var dependenciesChanged = !string.Equals(currentState.DependencyHash, previousState.DependencyHash, StringComparison.Ordinal);
                var migrationsChanged = !string.Equals(currentState.MigrationHash, previousState.MigrationHash, StringComparison.Ordinal);
                fastBackendDependenciesChanged = string.IsNullOrWhiteSpace(previousState.BackendDependencyHash) ||
                    !string.Equals(currentState.BackendDependencyHash, previousState.BackendDependencyHash, StringComparison.Ordinal);
                fastDependenciesValidated = previousState.DependenciesValidated && !dependenciesChanged && !fastBackendDependenciesChanged;
                fastSkipFrontendBuild = !frontendChanged && !dependenciesChanged;
                _runner.Report($"[FAST] Frontend changed: {frontendChanged}; Backend changed: {backendChanged}; Frontend dependencies changed: {dependenciesChanged}; Backend dependencies changed: {fastBackendDependenciesChanged}; DB/init changed: {migrationsChanged}");

                if (!backendChanged && !migrationsChanged)
                {
                    _runner.Report("[FAST] Backend/database are unchanged. Using the last successful hosted deployment as the staging baseline.");
                    trace.Measure("Hosted baseline copy", () => CopyDirectory(profile.HostingRoot, output, ct), "Copies the last successful hosted deployment into staging.");
                    if (fastDependenciesValidated)
                    {
                        trace.AddSkipped("Offline dependency validation", "Dependency fingerprints unchanged and the previous successful state is dependency-ready.");
                        trace.AddSkipped("Frontend dependency repair", "Dependency fingerprint unchanged; previous successful dependency-ready state reused.");
                    }
                    if (profile.RequiresNode && (frontendChanged || dependenciesChanged))
                    {
                        var clientFolder = FindClientFolder(profile) ?? throw new InvalidOperationException("Node frontend was expected but package.json was not found.");
                        var npm = EnvironmentService.ResolveExecutable("npm.cmd", @"C:\Program Files\nodejs\npm.cmd");
                        var health = profile.BuildMode == "Fast IIS Update"
                            ? trace.Measure("Frontend dependency check", () => CheckFrontendDependenciesFast(clientFolder), "Fast node_modules / local executable validation.")
                            : await trace.MeasureAsync("Frontend dependency check", () => CheckFrontendDependenciesAsync(npm, clientFolder, ct), "npm dependency validation.");
                        if (profile.OfflineBuild) _runner.Report("[OFFLINE] Fast Build is validating only the frontend dependency stage because backend/database are proven unchanged.");
                        if ((!health.Ok || dependenciesChanged) && !fastDependenciesValidated)
                            await trace.MeasureAsync("Frontend dependency repair", () => RepairFrontendDependenciesAsync(npm, clientFolder, profile, ct), "npm repair/restore for changed or incomplete frontend dependencies.");
                        _runner.Report("[FAST] Building frontend only; dotnet publish and database initialization build steps are skipped.");
                        var build = await trace.MeasureAsync("Frontend build", () => _runner.RunAsync(npm, "run build --loglevel=error", clientFolder, ct), "Project-defined npm build script.");
                        if (build.ExitCode != 0) ThrowBuildFailure(npm, build);
                        builtFrontendOutputFolder = ResolveFrontendOutputFolder(clientFolder);
                        var targetWwwroot = Path.Combine(output, "wwwroot");
                        if (builtFrontendOutputFolder is null) throw new DirectoryNotFoundException("Frontend build completed but no supported frontend output folder was found (wwwroot/dist/build/out).");
                        if (Directory.Exists(targetWwwroot)) Directory.Delete(targetWwwroot, true);
                        trace.Measure("Frontend output copy", () => CopyDirectory(builtFrontendOutputFolder, targetWwwroot, ct), "Copies fresh frontend output into staging wwwroot.");
                        _runner.Report($"[FAST] Fresh frontend output copied from: {builtFrontendOutputFolder}");
                    }
                    else
                    {
                        _runner.Report("[FAST] No source changes requiring compilation were detected. Reusing the last successful binaries.");
                        trace.AddSkipped("Frontend build", "No frontend source/dependency change detected.");
                        trace.AddSkipped("dotnet publish", "Backend/database unchanged; last successful binaries reused.");
                    }
                    WriteConnectionString(output, profile);
                    var outputHash = trace.Measure("Frontend output hash", () => HashDirectory(Path.Combine(output, "wwwroot")), "Hashes staged wwwroot for stale-UI protection.");
                    trace.Measure("Write build state", () => WriteSmartState(output, currentState with { FrontendOutputHash = outputHash, DependenciesValidated = fastDependenciesValidated || previousState.DependenciesValidated }), "Writes staging Fast Build state manifest.");
                    return output;
                }
                _runner.Report("[FAST] Backend or database initialization changed. Safety rule requires a full publish.");
            }
            else _runner.Report("[FAST] No trusted previous source-state manifest exists. Running a full publish and creating one after success.");
        }

        if (profile.BuildMode == "Prepared IIS Publish")
        {
            _runner.Report("[PREPARED] MODE = PREPARED_PUBLISH");
            _runner.Report($"[PREPARED] SOURCE = {profile.ReadyPublishFolder}");
            var validation = ValidatePreparedPublish(profile.ReadyPublishFolder);
            _runner.Report($"[PREPARED] PREPARED_VALIDATION = {(validation.Ok ? "PASS" : "FAIL")}");
            if (!validation.Ok) throw new InvalidOperationException("PREPARED_PUBLISH_INVALID: " + validation.Detail);
            trace.AddSkipped("Dependency resolution", "Prepared Publish is already built; package resolution is forbidden.");
            trace.AddSkipped("Frontend build", "Prepared Publish is already built; npm/vite are forbidden.");
            trace.AddSkipped("Backend build", "Prepared Publish is already built; dotnet build is forbidden.");
            trace.AddSkipped("dotnet publish", "Prepared Publish is already published.");
            _runner.Report("[PREPARED] BUILD_FRONTEND = SKIPPED");
            _runner.Report("[PREPARED] BUILD_BACKEND = SKIPPED");
            _runner.Report("[PREPARED] DOTNET_PUBLISH = SKIPPED");
            _runner.Report($"[PREPARED] Staging validated prepared files: {profile.ReadyPublishFolder} -> {output}");
            trace.Measure("Prepared publish copy", () => CopyDirectory(profile.ReadyPublishFolder, output, ct), "Copies the validated prepared publish into deployment staging without invoking any source-build stage.");
            _runner.Report("[PREPARED] FILE_STAGE = PASS");
            return output;
        }

        if (profile.OfflineBuild && fastDependenciesValidated)
        {
            _runner.Report("[FAST] Dependency fingerprints are unchanged and the previous successful state is dependency-ready. Offline validation is skipped.");
            trace.AddSkipped("Offline dependency validation", "Dependency fingerprints unchanged and previous successful validation is trusted.");
        }
        else if (profile.OfflineBuild)
        {
            var validation = await trace.MeasureAsync("Offline dependency validation", () => ValidateOfflinePackageAsync(profile, ct), "Validates NuGet/npm readiness for Offline Only mode.");
            _runner.Report($"[OFFLINE] Offline Package Validation: {(validation.Ok ? "PASSED" : "FAILED")}");
            if (!validation.Ok)
            {
                _runner.Report("[OFFLINE] Missing dependency detected. Internet fallback is DISABLED by Offline Only mode.");
                throw new InvalidOperationException(
                    "Offline Dependency Repository is not ready for this source. No Internet fallback was attempted. " +
                    validation.Detail + " Use 'Sync Missing Online Once' while Internet is available, then return to Offline Only.");
            }
            fastDependenciesValidated = true;
        }

        if (!File.Exists(profile.ApiProjectPath))
            throw new FileNotFoundException("ASP.NET Core project was not found.", profile.ApiProjectPath);

        // Build the frontend explicitly first so npm output/errors are visible in the Deployment Manager log.
        if (profile.RequiresNode)
        {
            var clientFolder = FindClientFolder(profile);
            if (clientFolder is null)
                throw new InvalidOperationException("Node.js is required but no package.json was found under the selected source.");

            var npm = EnvironmentService.ResolveExecutable("npm.cmd", @"C:\Program Files\nodejs\npm.cmd");
            var repaired = false;

            _runner.Report($"[FRONTEND] Client folder: {clientFolder}");
            if (false && profile.BuildMode == "Fast IIS Update" && fastSkipFrontendBuild && Directory.Exists(profile.HostingRoot))
            {
                _runner.Report("[FAST] Frontend source and package files are unchanged. npm dependency validation and frontend compilation are skipped.");
                trace.AddSkipped("Frontend build", "Frontend source and package files are unchanged.");
                trace.AddSkipped("Frontend dependency check", "Frontend and dependency fingerprints unchanged; previous successful state reused.");
                trace.AddSkipped("Frontend dependency repair", "Dependency fingerprint unchanged; no repair required.");
            }
            else
            {
                _runner.Report("[FRONTEND] Checking installed npm dependencies before build...");
            }
            var health = (Ok: true, Detail: "Skipped because the frontend/dependency fingerprint is unchanged.");
            if (!(profile.BuildMode == "Fast IIS Update" && fastSkipFrontendBuild && fastDependenciesValidated && Directory.Exists(profile.HostingRoot)))
            {
                health = profile.BuildMode == "Fast IIS Update"
                    ? trace.Measure("Frontend dependency check", () => CheckFrontendDependenciesFast(clientFolder), "Fast node_modules / local executable validation.")
                    : await trace.MeasureAsync("Frontend dependency check", () => CheckFrontendDependenciesAsync(npm, clientFolder, ct), "npm dependency validation.");
                _runner.Report("[FRONTEND] Dependency check: " + health.Detail);
            }

            if (false && profile.BuildMode == "Fast IIS Update" && fastSkipFrontendBuild && Directory.Exists(profile.HostingRoot))
            {
                // The hosted wwwroot is copied back after dotnet publish so stale source-tree frontend output cannot leak in.
            }
            else if (!health.Ok)
            {
                _runner.Report(profile.BuildMode == "Visual Studio prepared"
                    ? "[FRONTEND] Visual Studio prepared mode was selected, but the dependency tree is incomplete. Repairing it automatically instead of failing."
                    : "[FRONTEND] Frontend dependencies are missing/incomplete. Repairing them automatically.");
                await trace.MeasureAsync("Frontend dependency repair", () => RepairFrontendDependenciesAsync(npm, clientFolder, profile, ct), "npm repair/restore for incomplete frontend dependencies.");
                repaired = true;

                health = await trace.MeasureAsync("Frontend post-repair check", () => CheckFrontendDependenciesAsync(npm, clientFolder, ct), "Validates frontend dependencies after repair.");
                _runner.Report("[FRONTEND] Post-repair dependency check: " + health.Detail);
                if (!health.Ok)
                    throw new InvalidOperationException("npm dependency repair completed, but the frontend is still incomplete. " + health.Detail);
            }
            else
            {
                _runner.Report(profile.BuildMode == "Visual Studio prepared"
                    ? "[FRONTEND] Visual Studio prepared mode: dependencies are healthy; npm install is not repeated."
                    : "[FRONTEND] Dependencies are healthy; using the existing npm cache/node_modules without an unnecessary reinstall.");
            }

            if (!(profile.BuildMode == "Fast IIS Update" && fastSkipFrontendBuild && Directory.Exists(profile.HostingRoot)))
            {
                _runner.Report("[FRONTEND] Starting the project-defined frontend build script. All npm/TypeScript/Vite output will be streamed below...");
                var build = await trace.MeasureAsync("Frontend build", () => _runner.RunAsync(npm, "run build --loglevel=error", clientFolder, ct), "Project-defined npm/TypeScript/Vite build script.");

                // One automatic repair/retry is allowed only for a missing local build tool such as tsc/vite.
                if (build.ExitCode != 0 && !repaired && LooksLikeMissingBuildTool(build))
                {
                    _runner.Report("[FRONTEND] Build failed because a local build executable appears to be missing. Running one automatic npm repair, then retrying build once...");
                    await trace.MeasureAsync("Frontend dependency repair (retry)", () => RepairFrontendDependenciesAsync(npm, clientFolder, profile, ct), "One automatic repair after a missing local build tool.");
                    repaired = true;
                    build = await trace.MeasureAsync("Frontend build retry", () => _runner.RunAsync(npm, "run build --loglevel=error", clientFolder, ct), "One automatic build retry after dependency repair.");
                }

                if (build.ExitCode != 0)
                    ThrowBuildFailure(npm, build);

                _runner.Report("[FRONTEND] Frontend build completed successfully.");
                builtFrontendOutputFolder = ResolveFrontendOutputFolder(clientFolder);
                if (builtFrontendOutputFolder is null)
                    throw new DirectoryNotFoundException("Frontend build succeeded but no supported output folder was found (wwwroot/dist/build/out). Fast Deploy refuses to continue with a possibly stale UI.");
                _runner.Report($"[FRONTEND] Fresh output detected: {builtFrontendOutputFolder}");
            }
        }

        var dotnet = EnvironmentService.ResolveExecutable("dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe");
        var noRestore = " --no-restore";
        if (profile.OfflineBuild && profile.BuildMode != "Visual Studio prepared")
        {
            var offline = GetOfflinePaths(profile);
            _runner.Report("[BACKEND] .NET Restore: OFFLINE — already completed by Offline Package Validation.");
            _runner.Report($"[BACKEND] NuGet config: {offline.Config}");
            _runner.Report($"[BACKEND] NuGet packages: {offline.NugetPackages}");
            trace.AddSkipped("dotnet restore", "Offline Package Validation already completed dependency restore/readiness work.");
        }
        else if (profile.BuildMode == "Visual Studio prepared")
        {
            _runner.Report("[BACKEND] Visual Studio prepared mode: dotnet restore is skipped; starting final publish.");
            trace.AddSkipped("dotnet restore", "Visual Studio prepared mode.");
        }
        else
        {
            var fastCache = GetFastCachePaths(profile);
            Directory.CreateDirectory(fastCache.NugetPackages);
            var assetsFile = Path.Combine(Path.GetDirectoryName(profile.ApiProjectPath)!, "obj", "project.assets.json");
            var canSkipRestore = profile.BuildMode == "Fast IIS Update" && fastPreviousState is not null && !fastBackendDependenciesChanged && File.Exists(assetsFile);
            if (canSkipRestore)
            {
                _runner.Report("[FAST] Backend dependency files are unchanged and project.assets.json exists. dotnet restore is skipped.");
                trace.AddSkipped("dotnet restore", "Backend dependency files unchanged and project.assets.json exists.");
            }
            else
            {
                _runner.Report($"[BACKEND] Restoring with persistent NuGet repository: {fastCache.NugetPackages}");
                Directory.CreateDirectory(fastCache.NugetPackages);
                var restoreArgs = $"restore \"{profile.ApiProjectPath}\" --packages \"{fastCache.NugetPackages}\" --verbosity minimal";
                if (string.Equals(profile.DependencyResolutionMode, "Hybrid", StringComparison.OrdinalIgnoreCase))
                {
                    var localFirstConfig = WriteNugetConfig(profile, offlineOnly: false);
                    restoreArgs += $" --configfile \"{localFirstConfig}\"";
                    _runner.Report("[BACKEND] Resolution order: Local Repository -> System NuGet Cache -> Internet.");
                }
                else
                {
                    _runner.Report("[BACKEND] Online mode: offline repository validation/configuration is skipped; configured Internet sources may be contacted.");
                }
                await trace.MeasureAsync("dotnet restore", () => _runner.RunCheckedAsync(dotnet, restoreArgs, profile.SourceFolder, ct), "NuGet restore using the configured persistent repository/cache.");
            }
        }

        _runner.Report(profile.BuildMode == "Fast IIS Update"
            ? "[FAST] Starting incremental dotnet publish with restore disabled."
            : profile.OfflineBuild
                ? "[BACKEND] Offline restore passed; starting dotnet publish with --no-restore."
                : "[BACKEND] Starting dotnet publish with persistent dependency cache.");
        var publishArguments = $"publish \"{profile.ApiProjectPath}\" -c Release -o \"{output}\"{noRestore} -p:SkipClientBuild=true --verbosity minimal";
        await trace.MeasureAsync("dotnet publish", async () =>
        {
            var first = await _runner.RunAsync(dotnet, publishArguments, profile.SourceFolder, ct);
            if (first.ExitCode == 0) return;

            var diagnostic = first.Error + "\r\n" + first.Output;
            if (!IsStaleStaticWebAssetFailure(diagnostic))
                throw new InvalidOperationException($"{dotnet} exited with code {first.ExitCode}.\r\n{diagnostic}");

            _runner.Report("[STATIC-ASSETS] Stale ASP.NET Static Web Assets metadata detected. Repairing generated metadata and retrying publish once.");
            var removed = RemoveStaleStaticWebAssetMetadata(profile.SourceFolder);
            _runner.Report($"[STATIC-ASSETS] Removed {removed} generated metadata item(s). Source files and node_modules were not deleted.");
            if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
            Directory.CreateDirectory(output);
            File.WriteAllText(Path.Combine(output, ".deployment-incomplete"), DateTime.Now.ToString("O"));
            var retry = await _runner.RunAsync(dotnet, publishArguments, profile.SourceFolder, ct);
            if (retry.ExitCode != 0)
                throw new InvalidOperationException($"Static Web Assets repair was attempted once, but dotnet publish still failed with code {retry.ExitCode}.\r\n{retry.Error}\r\n{retry.Output}");
            _runner.Report("[STATIC-ASSETS] Recovery publish succeeded.");
        }, "Release publish to staging with SkipClientBuild=true; repairs stale generated Static Web Assets metadata once when required.");
        _runner.Report("[BACKEND] dotnet publish completed successfully.");

        if (builtFrontendOutputFolder is not null)
        {
            var outputWwwroot = Path.Combine(output, "wwwroot");
            if (Directory.Exists(outputWwwroot)) Directory.Delete(outputWwwroot, true);
            trace.Measure("Frontend staging overlay", () => CopyDirectory(builtFrontendOutputFolder, outputWwwroot, ct), "Overlays fresh frontend output into publish staging.");
            _runner.Report("[FRONTEND] Fresh frontend output explicitly overlaid into publish staging; stale source-tree wwwroot cannot win.");
        }

        // V77: enforce frontend/backend source-tree identity before accepting staging.
        if (profile.BuildMode != "Prepared IIS Publish" && profile.RequiresNode)
        {
            var clientIdentity = FindClientFolder(profile)
                ?? throw new InvalidOperationException("RELEASE_IDENTITY: Frontend source cannot be resolved.");
            var apiIdentity = Path.GetDirectoryName(Path.GetFullPath(profile.ApiProjectPath))
                ?? throw new InvalidOperationException("RELEASE_IDENTITY: Backend source cannot be resolved.");
            var rootIdentity = Path.GetFullPath(profile.SourceFolder).TrimEnd(Path.DirectorySeparatorChar);
            foreach (var component in new[] { apiIdentity, clientIdentity })
            {
                var relative = Path.GetRelativePath(rootIdentity, component);
                if (Path.IsPathRooted(relative) || relative == ".." ||
                    relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) ||
                    IsGeneratedPath(relative))
                    throw new InvalidOperationException("RELEASE_IDENTITY: Component escapes the incoming source root: " + relative);
            }
            var apiRelative = Path.GetRelativePath(rootIdentity, apiIdentity);
            var clientRelative = Path.GetRelativePath(rootIdentity, clientIdentity);
            if (!clientRelative.StartsWith(apiRelative + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("RELEASE_IDENTITY: Frontend does not belong to the selected backend project.");
            _runner.Report("[RELEASE-IDENTITY] PASS: frontend/backend share the same API project source tree.");
        }

        // V75: fail closed unless staged frontend equals this build's output.
        if (profile.BuildMode != "Prepared IIS Publish" && profile.RequiresNode)
        {
            var client = FindClientFolder(profile)
                ?? throw new InvalidOperationException("RELEASE_INTEGRITY: Frontend source is missing.");
            if (builtFrontendOutputFolder is null)
                throw new InvalidOperationException("RELEASE_INTEGRITY: Fresh frontend output was not built.");
            var staged = Path.Combine(output, "wwwroot");
            if (!Directory.Exists(staged) || !Directory.EnumerateFiles(staged, "*", SearchOption.AllDirectories).Any())
                throw new InvalidOperationException("RELEASE_INTEGRITY: Frontend staging is empty.");
            var stagedHash = HashDirectory(staged);
            var builtHash = HashDirectory(builtFrontendOutputFolder);
            if (!string.Equals(stagedHash, builtHash, StringComparison.Ordinal))
                throw new InvalidOperationException("RELEASE_INTEGRITY: Staged frontend differs from freshly built output.");
            // V101: compatibility does not fabricate UI-version evidence. A missing
            // app-owned contract is explicitly unverified; a present but invalid or
            // mismatched contract always fails, regardless of mode.
            var strictUiVersion = IsStrictUiVersionMode(profile);
            var uiVersionFile = Path.Combine(staged, "gwd-ui-version.json");
            var uiVersionVerified = File.Exists(uiVersionFile);
            if (!uiVersionVerified)
            {
                if (strictUiVersion)
                    throw new InvalidOperationException("UI_VERSION_CONTRACT: Strict mode requires gwd-ui-version.json in the fresh frontend build.");
                _runner.Report("[UI-VERSION] UNVERIFIED (Compatible): frontend did not emit gwd-ui-version.json. Source/hash/release-ID checks remain mandatory.");
            }
            else
            {
                using var uiVersionDoc = JsonDocument.Parse(File.ReadAllText(uiVersionFile));
                if (!uiVersionDoc.RootElement.TryGetProperty("version", out var uiVersionNode) ||
                    uiVersionNode.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(uiVersionNode.GetString()))
                    throw new InvalidOperationException("UI_VERSION_CONTRACT: Present frontend version file is invalid.");
                var uiVersion = uiVersionNode.GetString()!.Trim();
                var sourceRelease = TryReadSourceVersion(profile);
                if (!ReleaseVersionContract.Matches(sourceRelease, uiVersion))
                    throw new InvalidOperationException($"UI_VERSION_CONTRACT: Frontend {uiVersion} != source release {sourceRelease}; deployment blocked.");
                _runner.Report($"[UI-VERSION] VERIFIED: built frontend version {uiVersion} matches source release.");
            }
            // Public build identity marker: served by IIS from the same wwwroot as the UI.
            // This proves which frontend assets are active, independently of API health.
            var releaseId = Guid.NewGuid().ToString("N");
            var frontendMarker = new
            {
                schemaVersion = 1,
                releaseId,
                sourceVersion = TryReadSourceVersion(profile),
                frontendBuildSha256 = builtHash
            };
            File.WriteAllText(Path.Combine(staged, "gwd-frontend-release.json"),
                JsonSerializer.Serialize(frontendMarker));
            stagedHash = HashDirectory(staged);
            var manifest = new
            {
                releaseId,
                schemaVersion = 1,
                sourceRoot = Path.GetFullPath(profile.SourceFolder),
                apiProject = Path.GetRelativePath(profile.SourceFolder, profile.ApiProjectPath),
                frontend = Path.GetRelativePath(profile.SourceFolder, client),
                sourceVersion = TryReadSourceVersion(profile),
                frontendSha256 = stagedHash,
                uiVersionVerified,
                verifiedAtUtc = DateTime.UtcNow
            };
            File.WriteAllText(Path.Combine(output, "gwd-release-integrity.json"),
                JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            _runner.Report($"[RELEASE-INTEGRITY] PASS: staged frontend SHA256={stagedHash}; source version={manifest.sourceVersion}");
        }

        if (false && profile.BuildMode == "Fast IIS Update" && fastSkipFrontendBuild && Directory.Exists(profile.HostingRoot))
        {
            var hostedWwwroot = Path.Combine(profile.HostingRoot, "wwwroot");
            var outputWwwroot = Path.Combine(output, "wwwroot");
            if (Directory.Exists(hostedWwwroot))
            {
                if (Directory.Exists(outputWwwroot)) Directory.Delete(outputWwwroot, true);
                trace.Measure("Reuse hosted wwwroot", () => CopyDirectory(hostedWwwroot, outputWwwroot, ct), "Frontend unchanged; copies last successful hosted wwwroot into staging.");
                _runner.Report("[FAST] Frontend was unchanged; reused wwwroot from the last successful deployment.");
            }
        }
        WriteConnectionString(output, profile);
        var finalFrontendOutputHash = trace.Measure("Frontend output hash", () => HashDirectory(Path.Combine(output, "wwwroot")), "Hashes staged wwwroot for stale-UI protection.");
        var finalState = trace.Measure("Final fingerprint scan", () => ComputeSmartState(profile, finalFrontendOutputHash) with { DependenciesValidated = fastDependenciesValidated || !profile.OfflineBuild }, "Recomputes source fingerprints for the successful staging output.");
        trace.Measure("Write build state", () => WriteSmartState(output, finalState), "Writes staging Fast Build state manifest.");
        return output;
        }
        finally
        {
            trace.Complete();
            _runner.Report($"[PERF] Build pipeline total: {trace.TotalElapsed:hh\\:mm\\:ss\\.fff}. Use Build Performance for the stage breakdown.");
        }
    }

    private static bool IsStaleStaticWebAssetFailure(string text) =>
        text.Contains("Microsoft.NET.Sdk.StaticWebAssets", StringComparison.OrdinalIgnoreCase) &&
        (text.Contains("No file exists for the asset", StringComparison.OrdinalIgnoreCase) ||
         text.Contains("StaticWebAsset", StringComparison.OrdinalIgnoreCase));

    private static int RemoveStaleStaticWebAssetMetadata(string sourceRoot)
    {
        var removed = 0;
        foreach (var obj in Directory.EnumerateDirectories(sourceRoot, "obj", SearchOption.AllDirectories).ToArray())
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(obj, "*staticwebassets*", SearchOption.AllDirectories).OrderByDescending(x => x.Length).ToArray())
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                    else if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
                    else continue;
                    removed++;
                }
                catch (Exception ex) { InternalDiagnostics.Record(ex, "Remove stale static web asset metadata: " + path); }
            }
        }
        return removed;
    }

    private static FileStream? AcquireWorkspaceLease(DeploymentProfile profile)
    {
        if (string.Equals(profile.BuildMode, "Prepared IIS Publish", StringComparison.OrdinalIgnoreCase)) return null;
        if (string.IsNullOrWhiteSpace(profile.WorkspaceRoot)) throw new InvalidOperationException("Choose a Stable Build Workspace folder.");
        var source = ResolveCanonicalSourceRoot(profile.SourceFolder, profile.ApiProjectPath);
        var workspace = Path.GetFullPath(Path.Combine(profile.WorkspaceRoot, SanitizeFolderName(profile.ApplicationName))).TrimEnd(Path.DirectorySeparatorChar);
        if (PathsEqual(source, workspace) || workspace.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            || source.StartsWith(workspace + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Incoming and workspace must be separate, non-nested directories.");
        Directory.CreateDirectory(workspace);
        return new FileStream(Path.Combine(workspace, ".gwd-workspace.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    }

    private WorkspaceSyncResult PrepareStableWorkspace(DeploymentProfile sourceProfile, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(sourceProfile.WorkspaceRoot))
            throw new InvalidOperationException("Choose a Stable Build Workspace folder on the first page.");
        if (!Directory.Exists(sourceProfile.SourceFolder))
            throw new DirectoryNotFoundException("Incoming source folder was not found: " + sourceProfile.SourceFolder);

        var sourceRoot = ResolveCanonicalSourceRoot(sourceProfile.SourceFolder, sourceProfile.ApiProjectPath);
        var workspaceRoot = Path.GetFullPath(sourceProfile.WorkspaceRoot).TrimEnd(Path.DirectorySeparatorChar);
        var workspace = Path.Combine(workspaceRoot, SanitizeFolderName(sourceProfile.ApplicationName));
        var workspaceFull = Path.GetFullPath(workspace).TrimEnd(Path.DirectorySeparatorChar);
        if (PathsEqual(sourceRoot, workspaceFull) || workspaceFull.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Stable Build Workspace must be outside the incoming source folder.");

        var apiRelative = Path.GetRelativePath(sourceRoot, Path.GetFullPath(sourceProfile.ApiProjectPath));
        if (apiRelative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) || Path.IsPathRooted(apiRelative))
            throw new InvalidOperationException("The detected API project is outside the incoming source folder.");
        var workspaceApi = Path.Combine(workspaceFull, apiRelative);
        if (!File.Exists(sourceProfile.ApiProjectPath) || WorkspaceSynchronizer.IsExcluded(apiRelative)) throw new FileNotFoundException("API project must exist in non-excluded incoming source.", sourceProfile.ApiProjectPath);

        var sync = WorkspaceSynchronizer.Sync(sourceRoot, workspaceFull, _runner.Report, ct);

        var cloned = JsonSerializer.Deserialize<DeploymentProfile>(JsonSerializer.Serialize(sourceProfile))
            ?? throw new InvalidOperationException("Could not create the stable workspace build profile.");
        cloned.SqlPassword = sourceProfile.SqlPassword;
        cloned.SqlCredentialConfigured = sourceProfile.SqlCredentialConfigured;
        cloned.RemotePassword = sourceProfile.RemotePassword;
        _sql.Connections.ShareSession(sourceProfile, cloned);
        cloned.SourceFolder = workspaceFull;
        cloned.ApiProjectPath = workspaceApi;
        return new WorkspaceSyncResult(cloned, workspaceFull, sync.Added, sync.Updated, sync.Unchanged, sync.Removed, sync.Verified);
    }

    public static string ResolveCanonicalSourceRoot(string selectedFolder, string? apiProjectPath = null)
    {
        var selected = Path.GetFullPath(selectedFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!Directory.Exists(selected)) return selected;

        // A release package often wraps the real repository in one versioned directory.
        // Use the nearest ancestor of the detected web project that contains the repository src folder,
        // so the stable workspace receives src/... directly and never the Vxx package wrapper.
        var api = !string.IsNullOrWhiteSpace(apiProjectPath) && File.Exists(apiProjectPath)
            ? Path.GetFullPath(apiProjectPath)
            : Directory.EnumerateFiles(selected, "*.csproj", SearchOption.AllDirectories)
                .FirstOrDefault(p => File.ReadAllText(p).Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase));
        if (api is not null)
        {
            var cursor = Directory.GetParent(api)?.FullName;
            while (!string.IsNullOrWhiteSpace(cursor) && cursor.StartsWith(selected, StringComparison.OrdinalIgnoreCase))
            {
                if (Directory.Exists(Path.Combine(cursor, "src"))) return cursor.TrimEnd(Path.DirectorySeparatorChar);
                if (PathsEqual(cursor, selected)) break;
                cursor = Directory.GetParent(cursor)?.FullName;
            }
        }

        var children = Directory.GetDirectories(selected);
        if (children.Length == 1 &&
            (Directory.Exists(Path.Combine(children[0], "src")) || Directory.EnumerateFiles(children[0], "*.sln", SearchOption.TopDirectoryOnly).Any()))
            return Path.GetFullPath(children[0]).TrimEnd(Path.DirectorySeparatorChar);
        return selected;
    }

    private static string HashSingleFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private async Task BuildFastLocalTestAsync(DeploymentProfile profile, string output, BuildPerformanceTrace trace, CancellationToken ct)
    {
        if (!File.Exists(profile.ApiProjectPath))
            throw new FileNotFoundException("ASP.NET Core project was not found.", profile.ApiProjectPath);

        _runner.Report("[LOCAL] Fast Local Test: no dotnet publish, IIS staging/activation, file deployment, or rollback preparation will run.");
        var current = trace.Measure("Fast fingerprint scan", () => ComputeSmartState(profile), "Hashes frontend/backend/dependency/migration source state.");
        var previous = trace.Measure("Read previous build state", () => ReadSmartState(profile), "Loads the last successful change-aware state.");
        var frontendChanged = previous is null || !string.Equals(current.FrontendHash, previous.FrontendHash, StringComparison.Ordinal);
        var backendChanged = previous is null || !string.Equals(current.BackendHash, previous.BackendHash, StringComparison.Ordinal) || !string.Equals(current.MigrationHash, previous.MigrationHash, StringComparison.Ordinal);
        var dependenciesChanged = previous is null || !string.Equals(current.DependencyHash, previous.DependencyHash, StringComparison.Ordinal) || !string.Equals(current.BackendDependencyHash, previous.BackendDependencyHash, StringComparison.Ordinal);
        var dependenciesReady = previous?.DependenciesValidated == true && !dependenciesChanged;
        var frontendWorkRequired = frontendChanged || dependenciesChanged;
        _runner.Report($"[LOCAL] Frontend changed: {frontendChanged}; Backend changed: {backendChanged}; Dependencies changed: {dependenciesChanged}");

        if (profile.OfflineBuild && !dependenciesReady)
        {
            var validation = await trace.MeasureAsync("Offline dependency validation", () => ValidateOfflinePackageAsync(profile, ct), "Runs only when dependency fingerprints changed or no trusted state exists.");
            if (!validation.Ok) throw new InvalidOperationException(validation.Detail);
            dependenciesReady = true;
        }
        else
        {
            trace.AddSkipped("Offline dependency validation", profile.OfflineBuild ? "Dependency fingerprints unchanged and previous validation is trusted." : "Offline validation is not selected.");
        }

        if (profile.RequiresNode && frontendWorkRequired)
        {
            var client = FindClientFolder(profile) ?? throw new InvalidOperationException("Node.js is required but no package.json was found under the selected source.");
            var npm = EnvironmentService.ResolveExecutable("npm.cmd", @"C:\Program Files\nodejs\npm.cmd");
            var health = trace.Measure("Frontend dependency check", () => CheckFrontendDependenciesFast(client), "Fast local node_modules/tool validation.");
            if (!health.Ok || dependenciesChanged)
                await trace.MeasureAsync("Frontend dependency repair", () => RepairFrontendDependenciesAsync(npm, client, profile, ct), "Repairs only an incomplete or changed frontend dependency set.");
            var build = await trace.MeasureAsync("Frontend incremental build", () => _runner.RunAsync(npm, "run build --loglevel=error", client, ct), "Runs only when the frontend fingerprint changed.");
            if (build.ExitCode != 0) ThrowBuildFailure(npm, build);
        }
        else
        {
            trace.AddSkipped("Frontend dependency check", profile.RequiresNode ? "Frontend and dependency fingerprints unchanged." : "Project does not require Node.js.");
            trace.AddSkipped("Frontend build", profile.RequiresNode ? "Frontend and dependency fingerprints unchanged." : "Project does not require Node.js.");
        }

        if (backendChanged)
        {
            var dotnet = EnvironmentService.ResolveExecutable("dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe");
            var noRestore = dependenciesReady ? " --no-restore" : string.Empty;
            var buildArguments = $"build \"{profile.ApiProjectPath}\" -c Debug{noRestore} -p:SkipClientBuild=true --verbosity minimal";
            await trace.MeasureAsync("dotnet incremental build", async () =>
            {
                var first = await _runner.RunAsync(dotnet, buildArguments, profile.SourceFolder, ct);
                if (first.ExitCode == 0) return;

                var diagnostic = first.Error + "\r\n" + first.Output;
                if (!IsStaleStaticWebAssetFailure(diagnostic))
                    throw new InvalidOperationException($"{dotnet} exited with code {first.ExitCode}.\r\n{diagnostic}");

                _runner.Report("[STATIC-ASSETS] Fast Local Test detected stale ASP.NET Static Web Assets metadata. The first build already synchronized the project's bundled frontend; generated metadata will be repaired and build retried once.");
                var removed = RemoveStaleStaticWebAssetMetadata(profile.SourceFolder);
                _runner.Report($"[STATIC-ASSETS] Removed {removed} generated metadata item(s). Source, node_modules, bin, and NuGet restore state were preserved.");
                var retry = await _runner.RunAsync(dotnet, buildArguments, profile.SourceFolder, ct);
                if (retry.ExitCode != 0)
                    throw new InvalidOperationException($"Static Web Assets repair was attempted once during Fast Local Test, but dotnet build still failed with code {retry.ExitCode}.\r\n{retry.Error}\r\n{retry.Output}");
                _runner.Report("[STATIC-ASSETS] Fast Local Test recovery build succeeded.");
            }, "Incremental Debug build; repairs stale generated Static Web Assets metadata and retries once when required. dotnet publish is intentionally not used.");
        }
        else
        {
            trace.AddSkipped("dotnet incremental build", "Backend and migration fingerprints unchanged.");
        }

        trace.AddSkipped("dotnet publish", "Fast Local Test never publishes.");
        trace.AddSkipped("IIS activation", "Fast Local Test does not modify IIS.");
        trace.AddSkipped("Rollback preparation", "No hosted files were changed.");
        var finalState = current with { DependenciesValidated = dependenciesReady || !profile.OfflineBuild };
        trace.Measure("Write local build state", () => CommitPersistentSmartState(profile, finalState), "Commits fingerprints only after all required local builds succeed.");
        WriteSmartState(output, finalState);
        _runner.Report("[LOCAL] Fast Local Test completed successfully. Source compiled as required; no deployment was performed.");
    }

    private static (bool Ok, string Detail) CheckFrontendDependenciesFast(string clientFolder)
    {
        var nodeModules = Path.Combine(clientFolder, "node_modules");
        if (!Directory.Exists(nodeModules))
            return (false, "node_modules is missing.");

        var missingBins = DetectBuildCommandBinaries(clientFolder)
            .Where(command => !LocalNpmBinaryExists(clientFolder, command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingBins.Length > 0)
            return (false, "Missing local build executable(s): " + string.Join(", ", missingBins));

        return (true, "Fast check passed; node_modules and required local build executables are present. npm ls was skipped.");
    }

    private async Task<(bool Ok, string Detail)> CheckFrontendDependenciesAsync(string npm, string clientFolder, CancellationToken ct)
    {
        var nodeModules = Path.Combine(clientFolder, "node_modules");
        if (!Directory.Exists(nodeModules))
            return (false, "node_modules is missing.");

        var missingBins = DetectBuildCommandBinaries(clientFolder)
            .Where(command => !LocalNpmBinaryExists(clientFolder, command))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (missingBins.Length > 0)
            return (false, "Missing local build executable(s): " + string.Join(", ", missingBins));

        var ls = await _runner.RunAsync(npm, "ls --depth=0 --loglevel=error", clientFolder, ct);
        if (ls.ExitCode != 0)
            return (false, "npm reports an incomplete/invalid top-level dependency tree.");

        return (true, "node_modules and the package dependency tree are healthy.");
    }

    private async Task RepairFrontendDependenciesAsync(string npm, string clientFolder, DeploymentProfile profile, CancellationToken ct)
    {
        var cache = GetFastCachePaths(profile).NpmCache;
        Directory.CreateDirectory(cache);
        var mode = NormalizeDependencyMode(profile.DependencyResolutionMode);
        var arguments = BuildNpmInstallArguments(mode, cache, "install", ignoreScripts: false);
        if (mode == "Offline Only")
        {
            _runner.Report($"[FRONTEND] Frontend Strategy: Offline Only. Shared npm cache: {cache}");
            _runner.Report("[FRONTEND] npm network access: DISABLED (--offline).");
            try
            {
                await _runner.RunCheckedAsync(npm, arguments, clientFolder, ct, TimeSpan.FromMinutes(2));
            }
            catch (Exception ex)
            {
                if (LooksLikeBrokenNativeFrontendBinary(ex.ToString()))
                {
                    await RepairBrokenEsbuildAndRetryAsync(npm, arguments, clientFolder, ct, TimeSpan.FromMinutes(2));
                    _runner.Report("[FRONTEND] npm dependency restore/repair completed after native esbuild repair.");
                    return;
                }
                throw new InvalidOperationException("Offline frontend dependency restore failed. Required npm packages are not present in the shared local repository/cache. No Internet fallback was attempted. " + ex.Message, ex);
            }
        }
        else
        {
            _runner.Report(mode == "Hybrid"
                ? "[FRONTEND] Hybrid npm: local repository/system cache first; Internet registry fallback enabled."
                : "[FRONTEND] Online npm: standard Internet registry resolution; offline flags are disabled.");
            try
            {
                await _runner.RunCheckedAsync(npm, arguments, clientFolder, ct);
            }
            catch (Exception ex) when (mode == "Hybrid" && ex is not OperationCanceledException)
            {
                if (LooksLikeBrokenNativeFrontendBinary(ex.ToString()))
                {
                    await RepairBrokenEsbuildAndRetryAsync(npm, arguments, clientFolder, ct, null);
                    _runner.Report("[FRONTEND] npm dependency restore/repair completed after native esbuild repair.");
                    return;
                }
                if (LooksLikeNodeOutOfMemory(ex.ToString()))
                    throw new InvalidOperationException("Frontend dependency processing ran out of Node.js memory while using the adaptive, system-safe heap limit. Close other memory-heavy applications and retry. This is not an Internet/cache dependency failure.", ex);
                throw new InvalidOperationException("Hybrid dependency resolution exhausted local caches and the online registry fallback failed. Check Internet/registry access and the requested package version. " + ex.Message, ex);
            }
            catch (Exception ex) when (mode == "Online" && ex is not OperationCanceledException && LooksLikeBrokenNativeFrontendBinary(ex.ToString()))
            {
                await RepairBrokenEsbuildAndRetryAsync(npm, arguments, clientFolder, ct, null);
                _runner.Report("[FRONTEND] npm dependency restore/repair completed after native esbuild repair.");
                return;
            }
        }
        _runner.Report("[FRONTEND] npm dependency restore/repair completed.");
    }

    private async Task RepairBrokenEsbuildAndRetryAsync(string npm, string arguments, string clientFolder, CancellationToken ct, TimeSpan? timeout)
    {
        _runner.Report("[FRONTEND] Broken/incompatible esbuild native binary detected (EFTYPE/ENOEXEC). Removing only esbuild packages from the stable workspace and retrying npm once.");
        var nodeModules = Path.Combine(clientFolder, "node_modules");
        foreach (var folder in new[] { Path.Combine(nodeModules, "esbuild"), Path.Combine(nodeModules, "@esbuild") })
        {
            if (!Directory.Exists(folder)) continue;
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception cleanupEx) { throw new InvalidOperationException("Could not remove the broken esbuild package from the stable workspace: " + folder + ". " + cleanupEx.Message, cleanupEx); }
        }
        try { await _runner.RunCheckedAsync(npm, arguments, clientFolder, ct, timeout); }
        catch (Exception retryEx) { throw new InvalidOperationException("Automatic esbuild native-binary repair was attempted once, but npm still failed. The source folder was not modified; review Diagnostics for the retry output. " + retryEx.Message, retryEx); }
    }

    private static string BuildNpmInstallArguments(string mode, string cache, string command, bool ignoreScripts)
    {
        mode = NormalizeDependencyMode(mode);
        var scriptOption = ignoreScripts ? " --ignore-scripts" : " --foreground-scripts";
        return mode switch
        {
            "Offline Only" => $"{command} --offline --cache \"{cache}\" --no-audit --no-fund{scriptOption} --loglevel=error",
            "Hybrid" => $"{command} --prefer-offline --cache \"{cache}\" --no-audit --no-fund{scriptOption} --loglevel=error",
            _ => $"{command} --no-audit --no-fund{scriptOption} --loglevel=error"
        };
    }

    private static string NormalizeDependencyMode(string? mode)
    {
        if (string.Equals(mode, "Offline Only", StringComparison.OrdinalIgnoreCase)) return "Offline Only";
        if (string.Equals(mode, "Hybrid", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Local First", StringComparison.OrdinalIgnoreCase)) return "Hybrid";
        if (string.Equals(mode, "Online", StringComparison.OrdinalIgnoreCase) || string.Equals(mode, "Online / Normal", StringComparison.OrdinalIgnoreCase)) return "Online";
        return "Offline Only";
    }

    private static bool IsOfflineOnly(DeploymentProfile profile) => NormalizeDependencyMode(profile.DependencyResolutionMode) == "Offline Only";

    public sealed record OfflinePackageValidation(bool Ok, string Detail);
    private sealed record OfflinePaths(string Root, string NugetPackages, string NugetFeed, string Config, string NpmCache, string Metadata);

    private static OfflinePaths GetOfflinePaths(DeploymentProfile profile)
    {
        var repository = string.IsNullOrWhiteSpace(profile.DeploymentRepositoryFolder) ? profile.InstallerCacheFolder : profile.DeploymentRepositoryFolder;
        var projectKey = SanitizeFolderName(string.IsNullOrWhiteSpace(profile.ApplicationName) ? "default-project" : profile.ApplicationName);
        var metadataRoot = Path.Combine(repository, "Projects", projectKey);
        return new OfflinePaths(repository, Path.Combine(repository, "NuGet", "packages"), Path.Combine(repository, "NuGet", "feed"), Path.Combine(repository, "Config", "NuGet.Offline.config"), Path.Combine(repository, "Node", "npm-cache"), Path.Combine(metadataRoot, "offline-package.json"));
    }

    private static OfflinePaths GetOfflinePathsForSource(string anyPathInsideSource)
    {
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GenericWebDeploymentManager", "Repository");
        return new OfflinePaths(fallback, Path.Combine(fallback, "NuGet", "packages"), Path.Combine(fallback, "NuGet", "feed"), Path.Combine(fallback, "Config", "NuGet.Offline.config"), Path.Combine(fallback, "Node", "npm-cache"), Path.Combine(fallback, "Projects", "legacy", "offline-package.json"));
    }

    private static string WriteNugetConfig(DeploymentProfile profile, bool offlineOnly)
    {
        var paths = GetOfflinePaths(profile);
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.NugetPackages);
        Directory.CreateDirectory(paths.NugetFeed);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Config)!);
        Directory.CreateDirectory(Path.GetDirectoryName(paths.Metadata)!);
        var configPath = offlineOnly ? paths.Config : Path.Combine(Path.GetDirectoryName(paths.Config)!, "NuGet.LocalFirst.config");
        var escPackages = System.Security.SecurityElement.Escape(paths.NugetPackages) ?? paths.NugetPackages;
        var escFeed = System.Security.SecurityElement.Escape(paths.NugetFeed) ?? paths.NugetFeed;
        var systemPackages = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var escSystem = System.Security.SecurityElement.Escape(systemPackages) ?? systemPackages;
        var lines = new List<string>
        {
            "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
            "<configuration>",
            "  <config>",
            $"    <add key=\"globalPackagesFolder\" value=\"{escPackages}\" />",
            "  </config>"
        };
        if (Directory.Exists(systemPackages))
        {
            lines.Add("  <fallbackPackageFolders>");
            lines.Add($"    <add key=\"System NuGet Cache\" value=\"{escSystem}\" />");
            lines.Add("  </fallbackPackageFolders>");
        }
        lines.Add("  <packageSources>");
        lines.Add("    <clear />");
        lines.Add($"    <add key=\"DeploymentManager Local\" value=\"{escFeed}\" />");
        if (!offlineOnly) lines.Add("    <add key=\"nuget.org\" value=\"https://api.nuget.org/v3/index.json\" protocolVersion=\"3\" />");
        lines.Add("  </packageSources>");
        lines.Add("</configuration>");
        lines.Add(string.Empty);
        File.WriteAllLines(configPath, lines);
        return configPath;
    }

    private static void WriteOfflineNugetConfig(OfflinePaths paths)
    {
        // Kept for legacy call sites; profile-aware configuration is generated by WriteNugetConfig.
        Directory.CreateDirectory(paths.Root);
        Directory.CreateDirectory(paths.NugetPackages);
        Directory.CreateDirectory(paths.NugetFeed);
    }

    private void ReportDependencyCheck(DeploymentProfile profile)
    {
        try
        {
            var paths = GetOfflinePaths(profile);
            var mode = NormalizeDependencyMode(profile.DependencyResolutionMode);
            _runner.Report("[DEPENDENCY]");
            _runner.Report($"Mode: {mode}");
            if (mode == "Online")
            {
                _runner.Report("Offline Repository: Skipped");
                _runner.Report("NPM Source: Internet");
                return;
            }
            if (mode == "Offline Only") _runner.Report("Cache Validation Started");
            else _runner.Report("Resolution: Local Repository -> System Cache -> Internet Registry");
            _runner.Report($"[DEPENDENCY-CHECK] Repository: {paths.Root}");
            _runner.Report($"[DEPENDENCY-CHECK] Mode: {mode}");
            if (File.Exists(profile.ApiProjectPath))
            {
                var doc = System.Xml.Linq.XDocument.Load(profile.ApiProjectPath);
                foreach (var pr in doc.Descendants().Where(x => x.Name.LocalName == "PackageReference"))
                {
                    var name = pr.Attribute("Include")?.Value ?? pr.Attribute("Update")?.Value;
                    var version = pr.Attribute("Version")?.Value ?? pr.Elements().FirstOrDefault(x => x.Name.LocalName == "Version")?.Value;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(version)) continue;
                    var local = Directory.Exists(Path.Combine(paths.NugetPackages, name.ToLowerInvariant(), version.ToLowerInvariant()));
                    var system = Directory.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages", name.ToLowerInvariant(), version.ToLowerInvariant()));
                    var status = local ? "LOCAL" : system ? "SYSTEM CACHE" : profile.OfflineBuild ? "MISSING" : "ONLINE REQUIRED";
                    _runner.Report($"[DEPENDENCY-CHECK] [{status}] NuGet {name} {version}");
                }
            }
        }
        catch (Exception ex) { _runner.Report("[DEPENDENCY-CHECK] Scanner warning: " + ex.Message); }
    }

    private void CollectNugetPackagesIntoLocalFeed(OfflinePaths paths)
    {
        Directory.CreateDirectory(paths.NugetFeed);
        var roots = new[]
        {
            paths.NugetPackages,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages")
        }.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        var copied = 0;
        foreach (var root in roots)
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.nupkg", SearchOption.AllDirectories))
            {
                try
                {
                    var destination = Path.Combine(paths.NugetFeed, Path.GetFileName(file));
                    if (!File.Exists(destination) || new FileInfo(destination).Length != new FileInfo(file).Length)
                    {
                        File.Copy(file, destination, true);
                        copied++;
                    }
                }
                catch (Exception ex) { InternalDiagnostics.Record(ex, "Copy NuGet archive into local feed: " + file); }
            }
        }
        _runner.Report($"[OFFLINE-PREP] NuGet local feed synchronized. New/updated package archives: {copied}; feed: {paths.NugetFeed}");
    }

    private async Task<CommandResult> PopulateNpmOfflineCacheFromCleanInstallAsync(string npm, string client, string cache, CancellationToken ct)
    {
        Directory.CreateDirectory(cache);
        var temp = Path.Combine(Path.GetTempPath(), "GenericWebDeploymentManager", "npm-offline-prep", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (var name in new[] { "package.json", "package-lock.json", "npm-shrinkwrap.json", ".npmrc" })
            {
                var source = Path.Combine(client, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(temp, name), true);
            }
            if (!File.Exists(Path.Combine(temp, "package.json")))
                return new CommandResult(-1, "", "package.json is missing from the detected frontend folder.");

            var hasLock = File.Exists(Path.Combine(temp, "package-lock.json")) || File.Exists(Path.Combine(temp, "npm-shrinkwrap.json"));
            var command = hasLock ? "ci" : "install";
            _runner.Report($"[OFFLINE-PREP] Populating npm cache from a CLEAN temporary {command}. Existing source node_modules will not be trusted.");
            return await _runner.RunAsync(npm,
                $"{command} --cache \"{cache}\" --prefer-online --no-audit --no-fund --ignore-scripts --loglevel=error",
                temp, ct, TimeSpan.FromMinutes(10));
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (Exception ex) { InternalDiagnostics.Record(ex, "Cleanup temporary npm online-prep folder"); }
        }
    }

    private async Task<CommandResult> ValidateNpmOfflineCacheFromCleanInstallAsync(string npm, string client, string cache, CancellationToken ct)
    {
        var temp = Path.Combine(Path.GetTempPath(), "GenericWebDeploymentManager", "npm-offline-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        try
        {
            foreach (var name in new[] { "package.json", "package-lock.json", "npm-shrinkwrap.json", ".npmrc" })
            {
                var source = Path.Combine(client, name);
                if (File.Exists(source)) File.Copy(source, Path.Combine(temp, name), true);
            }
            if (!File.Exists(Path.Combine(temp, "package.json")))
                return new CommandResult(-1, "", "package.json is missing from the detected frontend folder.");

            var hasLock = File.Exists(Path.Combine(temp, "package-lock.json")) || File.Exists(Path.Combine(temp, "npm-shrinkwrap.json"));
            var command = hasLock ? "ci" : "install";
            _runner.Report($"[OFFLINE-TEST] Verifying npm cache with a CLEAN temporary {command} and --offline. No source node_modules reuse.");
            return await _runner.RunAsync(npm,
                BuildNpmInstallArguments("Offline Only", cache, command, ignoreScripts: true),
                temp, ct, TimeSpan.FromMinutes(5));
        }
        finally
        {
            try { if (Directory.Exists(temp)) Directory.Delete(temp, true); } catch (Exception ex) { InternalDiagnostics.Record(ex, "Cleanup temporary npm offline-test folder"); }
        }
    }

    public async Task<OfflinePackageValidation> PrepareOfflinePackageAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        if (NormalizeDependencyMode(profile.DependencyResolutionMode) == "Online")
            return new(true, "Online mode: offline repository preparation skipped.");
        if (profile.BuildMode == "Prepared IIS Publish") return new(true, "Prepared publish mode does not require source dependency caches.");
        if (!File.Exists(profile.ApiProjectPath)) return new(false, "ASP.NET Core project was not detected.");
        var paths = GetOfflinePaths(profile);
        var onlineConfig = WriteNugetConfig(profile, offlineOnly: false);
        var dotnet = EnvironmentService.ResolveExecutable("dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe");
        _runner.Report("[OFFLINE-PREP] Preparing reusable dependency cache in the selected Deployment Repository.");
        _runner.Report($"[OFFLINE-PREP] Root: {paths.Root}");
        _runner.Report("[ONLINE-ONCE] Synchronizing missing .NET packages into the shared local repository. Internet may be used for missing packages only.");
        await _runner.RunCheckedAsync(dotnet,
            $"restore \"{profile.ApiProjectPath}\" --configfile \"{onlineConfig}\" --packages \"{paths.NugetPackages}\" --verbosity minimal",
            profile.SourceFolder, ct);
        CollectNugetPackagesIntoLocalFeed(paths);

        if (profile.RequiresNode)
        {
            var client = FindClientFolder(profile) ?? throw new InvalidOperationException("Node frontend was detected but package.json was not found.");
            var npm = EnvironmentService.ResolveExecutable("npm.cmd", @"C:\Program Files\nodejs\npm.cmd");
            Directory.CreateDirectory(paths.NpmCache);
            _runner.Report($"[ONLINE-ONCE] Synchronizing COMPLETE npm cache in shared repository: {paths.NpmCache}");
            var npmPrep = await PopulateNpmOfflineCacheFromCleanInstallAsync(npm, client, paths.NpmCache, ct);
            if (npmPrep.ExitCode != 0)
                return new(false, "Online npm cache preparation failed. " + npmPrep.Error);
        }

        var validation = await ValidateOfflinePackageCoreAsync(profile, ct);
        if (validation.Ok)
        {
            var metadata = JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                application = profile.ApplicationName,
                apiProject = Path.GetRelativePath(profile.SourceFolder, profile.ApiProjectPath),
                targetFramework = profile.TargetFrameworkMajor,
                frontend = profile.RequiresNode,
                preparedAtUtc = DateTime.UtcNow
            }, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(paths.Metadata, metadata);
        }
        return validation;
    }

    public async Task<OfflinePackageValidation> ValidateOfflinePackageAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        if (!IsOfflineOnly(profile))
            return new(false, $"Offline readiness validation was skipped because Dependency Mode is {NormalizeDependencyMode(profile.DependencyResolutionMode)}.");
        return await ValidateOfflinePackageCoreAsync(profile, ct);
    }

    private async Task<OfflinePackageValidation> ValidateOfflinePackageCoreAsync(DeploymentProfile profile, CancellationToken ct)
    {
        if (profile.BuildMode == "Prepared IIS Publish") return new(true, "Prepared publish folder is available; source build is skipped.");
        if (!File.Exists(profile.ApiProjectPath)) return new(false, "ASP.NET Core project file is missing.");
        var paths = GetOfflinePaths(profile);
        WriteNugetConfig(profile, offlineOnly: true);
        if (!File.Exists(paths.Config) || !Directory.Exists(paths.NugetPackages))
            return new(false, $"Offline .NET package cache is missing. Use Prepare / Validate Offline Package while Internet is available. Expected: {paths.Root}");
        try
        {
            var dotnet = EnvironmentService.ResolveExecutable("dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe");
            _runner.Report("[OFFLINE] .NET Restore: OFFLINE validation");
            _runner.Report($"[OFFLINE] NuGet Sources: local-only config {paths.Config}");
            var restore = await _runner.RunAsync(dotnet,
                $"restore \"{profile.ApiProjectPath}\" --configfile \"{paths.Config}\" --packages \"{paths.NugetPackages}\" --no-http-cache --verbosity minimal",
                profile.SourceFolder, ct, TimeSpan.FromMinutes(2));
            if (restore.ExitCode != 0)
                return new(false, "Offline dependency restore failed. One or more required NuGet packages/versions are unavailable in the local cache. Internet NuGet is disabled. " + restore.Error);

            if (profile.RequiresNode)
            {
                var client = FindClientFolder(profile);
                if (client is null) return new(false, "Frontend was detected but package.json is missing.");
                var npm = EnvironmentService.ResolveExecutable("npm.cmd", @"C:\Program Files\nodejs\npm.cmd");
                if (!Directory.Exists(paths.NpmCache)) return new(false, "Frontend npm offline cache is missing: " + paths.NpmCache);
                var install = await ValidateNpmOfflineCacheFromCleanInstallAsync(npm, client, paths.NpmCache, ct);
                if (install.ExitCode != 0)
                    return new(false, "Offline frontend dependency validation failed from a clean temporary install. Required npm packages are unavailable in the local cache. Internet npm is disabled. " + install.Error);
            }
            return new(true, $"Offline Package: READY. Local dependency root: {paths.Root}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new(false, ex.Message);
        }
    }

    private static IReadOnlyList<string> DetectBuildCommandBinaries(string clientFolder)
    {
        try
        {
            var packagePath = Path.Combine(clientFolder, "package.json");
            if (!File.Exists(packagePath)) return Array.Empty<string>();
            var root = JsonNode.Parse(File.ReadAllText(packagePath));
            var build = root?["scripts"]?["build"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(build)) return Array.Empty<string>();

            var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "node", "npm", "npm.cmd", "npx", "npx.cmd", "cmd", "cmd.exe",
                "powershell", "powershell.exe", "pwsh", "echo", "set", "if", "call"
            };

            var commands = new List<string>();
            foreach (var segment in Regex.Split(build, @"\s*(?:&&|\|\||;)\s*"))
            {
                var trimmed = segment.Trim();
                if (trimmed.Length == 0) continue;
                var token = Regex.Match(trimmed, @"^[^\s]+").Value.Trim('"', '\'');
                if (string.IsNullOrWhiteSpace(token) || token.Contains('=') || skip.Contains(token)) continue;
                if (token.Contains('\\') || token.Contains('/')) continue;
                commands.Add(token);
            }
            return commands;
        }
        catch (Exception ex)
        {
            InternalDiagnostics.Record(ex, "Parse frontend package build commands");
            return Array.Empty<string>();
        }
    }

    private static bool LocalNpmBinaryExists(string clientFolder, string command)
    {
        var bin = Path.Combine(clientFolder, "node_modules", ".bin");
        return File.Exists(Path.Combine(bin, command + ".cmd"))
            || File.Exists(Path.Combine(bin, command + ".exe"))
            || File.Exists(Path.Combine(bin, command));
    }

    private static bool LooksLikeMissingBuildTool(CommandResult result)
    {
        var text = result.Output + "\n" + result.Error;
        return text.Contains("is not recognized as an internal or external command", StringComparison.OrdinalIgnoreCase)
            || text.Contains("command not found", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ENOENT", StringComparison.OrdinalIgnoreCase)
            || text.Contains("could not determine executable", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeNodeOutOfMemory(string text) =>
        text.Contains("heap out of memory", StringComparison.OrdinalIgnoreCase)
        || text.Contains("NewSpace::EnsureCurrentCapacity", StringComparison.OrdinalIgnoreCase)
        || text.Contains("Allocation failed", StringComparison.OrdinalIgnoreCase);

    private static bool LooksLikeBrokenNativeFrontendBinary(string text) =>
        (text.Contains("esbuild", StringComparison.OrdinalIgnoreCase) || text.Contains("rollup", StringComparison.OrdinalIgnoreCase))
        && (text.Contains("EFTYPE", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ENOEXEC", StringComparison.OrdinalIgnoreCase)
            || text.Contains("not a valid Win32 application", StringComparison.OrdinalIgnoreCase));

    private static void ThrowBuildFailure(string npm, CommandResult build)
    {
        var detail = build.Error + "\r\n" + build.Output;
        if (LooksLikeNodeOutOfMemory(detail))
            throw new InvalidOperationException("Frontend build ran out of Node.js memory while using the adaptive, system-safe heap limit. Close other memory-heavy applications and retry.\r\n" + detail);
        throw new InvalidOperationException(
            $"{Path.GetFileName(npm)} frontend build exited with code {build.ExitCode}.\r\n" +
            "The detailed npm/TypeScript/Vite output is already in View Logs.\r\n" +
            build.Error + "\r\n" + build.Output);
    }

    public async Task<string> ActivateStagingAsync(DeploymentProfile profile, string stagingFolder, CancellationToken ct = default)
    {
        if (!Directory.Exists(stagingFolder)) throw new DirectoryNotFoundException("Staging folder was not found: " + stagingFolder);
        var target = ResolveActivePublishFolder(profile);
        var rollback = CreateUniqueRollbackFolder(profile);
        _activeRollbackFolder = rollback;

        _runner.Report($"[DEPLOY] BACKUP = PENDING; target={target}; backup={rollback}");
        Directory.CreateDirectory(Path.GetDirectoryName(target) ?? profile.HostingRoot);

        // V62: prepare and validate a unique backup BEFORE stopping IIS. A stale backup can
        // never collide with a new deployment and a backup failure does not take the site down.
        if (string.Equals(profile.DeploymentLayout, "Single Folder", StringComparison.OrdinalIgnoreCase) && Directory.Exists(target))
        {
            CopyDirectory(target, rollback, ct);
            // V66: validate the backup against the actual source inventory instead of rejecting an
            // intentionally empty web root. Report useful counts so failures are diagnosable.
            var sourceFiles = Directory.EnumerateFiles(target, "*", SearchOption.AllDirectories).ToList();
            var backupFiles = Directory.Exists(rollback) ? Directory.EnumerateFiles(rollback, "*", SearchOption.AllDirectories).ToList() : new List<string>();
            var sourceRelative = sourceFiles.Select(x => Path.GetRelativePath(target, x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var backupRelative = backupFiles.Select(x => Path.GetRelativePath(rollback, x)).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missing = sourceRelative.Where(x => !backupRelative.Contains(x)).Take(20).ToList();
            _runner.Report($"[DEPLOY] BACKUP_VALIDATE source_files={sourceFiles.Count}; backup_files={backupFiles.Count}; missing={missing.Count}");
            if (!Directory.Exists(rollback) || missing.Count > 0)
            {
                var detail = missing.Count == 0 ? "backup directory was not created" : "missing: " + string.Join(", ", missing);
                throw new IOException("Deployment backup validation failed (" + detail + "); IIS was not stopped and hosted files were not changed.");
            }
            _runner.Report($"[DEPLOY] BACKUP = PASS; path={rollback}");
        }
        else
        {
            _runner.Report("[DEPLOY] BACKUP = NOT_REQUIRED");
        }

        await StopSiteForDeploymentAsync(profile, ct);
        _runner.Report("[DEPLOY] IIS_STOP = PASS");

        // V65: IIS may report the site/pool stopped before worker-process file handles are fully released.
        // Wait for the pool state and use bounded lock-aware retry instead of a one-shot Directory.Delete.
        try
        {
            // A wait failure after stopping the site must enter the activation recovery handler.
            await WaitForAppPoolStoppedAsync(profile, ct);
            // Target deletion belongs to the guarded activation phase: a failed delete
            // must also trigger recovery instead of bypassing the catch block.
            // Copy the entire staged release to a sibling directory before touching the live folder.
            // This prevents a staging-copy failure from destroying the currently hosted files.
            var fullTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var parentFolder = Path.GetDirectoryName(fullTarget) ?? throw new IOException("Hosted path has no parent directory.");
            var preparedRelease = Path.Combine(parentFolder, Path.GetFileName(fullTarget) + ".gwd-new-" + Guid.NewGuid().ToString("N"));
            var displacedRelease = Path.Combine(parentFolder, Path.GetFileName(fullTarget) + ".gwd-previous-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyDirectory(stagingFolder, preparedRelease, ct);
                ct.ThrowIfCancellationRequested();
                if (Directory.Exists(fullTarget)) Directory.Move(fullTarget, displacedRelease);
                try { Directory.Move(preparedRelease, fullTarget); }
                catch
                {
                    if (!Directory.Exists(fullTarget) && Directory.Exists(displacedRelease))
                        Directory.Move(displacedRelease, fullTarget);
                    throw;
                }
            }
            finally
            {
                if (Directory.Exists(preparedRelease))
                {
                    try { Directory.Delete(preparedRelease, recursive: true); }
                    catch (Exception ex) { InternalDiagnostics.Record(ex, "Remove incomplete prepared release"); }
                }
            }
            if (Directory.Exists(rollback))
            {
                foreach (var relative in profile.PreserveProductionFiles ?? Array.Empty<string>())
                {
                    if (string.IsNullOrWhiteSpace(relative)) continue;
                    var previous = Path.Combine(rollback, relative);
                    var deployed = Path.Combine(target, relative);
                    if (!File.Exists(previous)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(deployed) ?? target);
                    File.Copy(previous, deployed, overwrite: true);
                    _runner.Report($"[CONFIG] Preserved production file: {relative}");
                }
            }
            Directory.Delete(stagingFolder, recursive: true);
            _runner.Report("[DEPLOY] FILE_DEPLOY = PASS");
            _runner.Report($"[DEPLOY] Activated hosted site folder: {target}");
            return target;
        }
        catch (Exception activationEx)
        {
            InternalDiagnostics.Record(activationEx, "Activate staging folder");
            // Recovery is a compensating operation: never reuse a cancelled deployment token.
            // Keep the original activation failure if recovery also fails, and report both.
            if (string.Equals(profile.DeploymentLayout, "Single Folder", StringComparison.OrdinalIgnoreCase) && Directory.Exists(rollback))
            {
                try
                {
                    await RestoreHostedFolderSafelyAsync(rollback, target);
                    _runner.Report("[ROLLBACK] Activation failure restored the previous hosted folder.");
                }
                catch (Exception recoveryEx)
                {
                    InternalDiagnostics.Record(recoveryEx, "Restore hosted folder after activation failure");
                    throw new AggregateException("Activation failed and rollback also failed; hosted folder may be incomplete.", activationEx, recoveryEx);
                }
            }
            throw;
        }
    }

    // Prepare the backup copy before changing the active folder. If activation failed
    // midway through copying, keep that partial folder available until the replacement
    // is installed. This method intentionally ignores deployment cancellation.
    private Task RestoreHostedFolderSafelyAsync(string rollback, string target)
    {
        if (!Directory.Exists(rollback))
            throw new DirectoryNotFoundException("Rollback backup was not found: " + rollback);

        var fullTarget = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(fullTarget) ?? throw new IOException("Hosted path has no parent directory.");
        var prepared = Path.Combine(parent, Path.GetFileName(fullTarget) + ".gwd-restore-" + Guid.NewGuid().ToString("N"));
        var displaced = Path.Combine(parent, Path.GetFileName(fullTarget) + ".gwd-displaced-" + Guid.NewGuid().ToString("N"));
        try
        {
            CopyRecoveryPayloadWithDeadline(rollback, prepared);
            if (Directory.Exists(fullTarget)) Directory.Move(fullTarget, displaced);
            try
            {
                Directory.Move(prepared, fullTarget);
            }
            catch
            {
                if (!Directory.Exists(fullTarget) && Directory.Exists(displaced))
                    Directory.Move(displaced, fullTarget);
                throw;
            }
            if (Directory.Exists(displaced))
            {
                try { Directory.Delete(displaced, recursive: true); }
                catch (Exception ex) { InternalDiagnostics.Record(ex, "Cleanup displaced hosted folder after activation rollback"); }
            }
        }
        finally
        {
            if (Directory.Exists(prepared))
            {
                try { Directory.Delete(prepared, recursive: true); }
                catch (Exception ex) { InternalDiagnostics.Record(ex, "Cleanup prepared activation rollback folder"); }
            }
        }
        return Task.CompletedTask;
    }

    private async Task WaitForAppPoolStoppedAsync(DeploymentProfile profile, CancellationToken ct)
    {
        var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(appcmd) || string.IsNullOrWhiteSpace(profile.AppPoolName)) return;
        for (var attempt = 1; attempt <= 20; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            var state = await _runner.RunAsync(appcmd, $"list apppool /name:\"{profile.AppPoolName}\" /text:state", ct: ct);
            if (state.ExitCode != 0 || state.Output.Contains("Stopped", StringComparison.OrdinalIgnoreCase))
            {
                _runner.Report($"[DEPLOY] APPPOOL_STOP_CONFIRMED = PASS; attempts={attempt}");
                return;
            }
            await Task.Delay(250, ct);
        }
        _runner.Report("[DEPLOY] APPPOOL_STOP_CONFIRMED = TIMEOUT; continuing with lock-aware file replacement retry.");
    }

    private async Task DeleteDirectoryWithRetryAsync(string path, CancellationToken ct)
    {
        const int maxAttempts = 12;
        Exception? last = null;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (!Directory.Exists(path)) return;
                // Clear read-only attributes that can otherwise masquerade as replacement failures.
                foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
                {
                    try { File.SetAttributes(file, FileAttributes.Normal); } catch { }
                }
                Directory.Delete(path, recursive: true);
                _runner.Report($"[DEPLOY] HOSTED_FOLDER_RELEASE = PASS; attempts={attempt}");
                return;
            }
            catch (IOException ex) { last = ex; }
            catch (UnauthorizedAccessException ex) { last = ex; }

            var delayMs = Math.Min(2000, 250 * attempt);
            _runner.Report($"[DEPLOY] HOSTED_FOLDER_LOCK = RETRY {attempt}/{maxAttempts}; wait={delayMs}ms; {last!.Message}");
            await Task.Delay(delayMs, ct);
        }
        throw new IOException($"Hosted folder is still locked after {maxAttempts} attempts: {path}. Another process is holding files in the IIS content folder. IIS site/app pool remain recoverable and the validated rollback backup was preserved.", last);
    }

    public async Task StopSiteForDeploymentAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(appcmd)) return;
        var portOwner = new EnvironmentService(_runner).GetIisSiteUsingPort(profile.HttpPort);
        if (!string.IsNullOrWhiteSpace(portOwner) && !string.Equals(portOwner, profile.SiteName, StringComparison.OrdinalIgnoreCase))
        {
            _runner.Report($"[IIS] Stopping existing site '{portOwner}' before activating files for port {profile.HttpPort}.");
            await _runner.RunAsync(appcmd, $"stop site /site.name:\"{portOwner}\"", ct: ct);
        }
        await _runner.RunAsync(appcmd, $"stop site /site.name:\"{profile.SiteName}\"", ct: ct);
        await _runner.RunAsync(appcmd, $"stop apppool /apppool.name:\"{profile.AppPoolName}\"", ct: ct);
    }

    public (bool Ok, string Detail) VerifyActivatedDeployment(DeploymentProfile profile, string publishFolder)
    {
        try
        {
            if (string.Equals(profile.BuildMode, "Prepared IIS Publish", StringComparison.OrdinalIgnoreCase))
            {
                var prepared = ValidatePreparedPublish(publishFolder);
                _runner.Report($"[PREPARED] ACTIVATED_VALIDATION = {(prepared.Ok ? "PASS" : "FAIL")}");
                return prepared.Ok
                    ? (true, "Prepared Publish activated validation passed. Source compilation was not executed.")
                    : (false, "Prepared Publish activated validation failed: " + prepared.Detail);
            }
            var statePath = Path.Combine(publishFolder, ".smart-deploy-state.json");
            if (!File.Exists(statePath)) return (false, "Fast Deploy state manifest is missing from the activated deployment.");
            var state = JsonSerializer.Deserialize<SmartDeployState>(File.ReadAllText(statePath));
            if (state is null) return (false, "Fast Deploy state manifest is invalid.");

            // PublishAsync builds from the synchronized Stable Workspace. Comparing against the
            // original version-wrapped source profile caused false stale-frontend failures.
            var currentSource = _lastPreparedState ?? ComputeSmartState(profile);
            if (!string.Equals(currentSource.FrontendHash, state.FrontendHash, StringComparison.Ordinal) ||
                !string.Equals(currentSource.DependencyHash, state.DependencyHash, StringComparison.Ordinal))
                return (false, "STALE FRONTEND DETECTED — deployed build-state does not match the current frontend source fingerprint.");
            if (!string.Equals(currentSource.BackendHash, state.BackendHash, StringComparison.Ordinal) ||
                !string.Equals(currentSource.MigrationHash, state.MigrationHash, StringComparison.Ordinal))
                return (false, "STALE BACKEND DETECTED — deployed build-state does not match the current backend source fingerprint.");

            // V76: activated release must retain the same frontend bytes as the verified staging output.
            var integrityPath = Path.Combine(publishFolder, "gwd-release-integrity.json");
            if (!File.Exists(integrityPath))
                return (false, "RELEASE_INTEGRITY: Activated deployment is missing release integrity manifest.");
            using var integrityDoc = JsonDocument.Parse(File.ReadAllText(integrityPath));
            var integrity = integrityDoc.RootElement;
            if (!integrity.TryGetProperty("frontendSha256", out var hashProperty) ||
                hashProperty.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(hashProperty.GetString()))
                return (false, "RELEASE_INTEGRITY: Frontend hash is missing from activated manifest.");
            if (!integrity.TryGetProperty("apiProject", out var apiProperty) ||
                !integrity.TryGetProperty("frontend", out var clientProperty) ||
                apiProperty.ValueKind != JsonValueKind.String ||
                clientProperty.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(apiProperty.GetString()) ||
                string.IsNullOrWhiteSpace(clientProperty.GetString()) ||
                IsGeneratedPath(apiProperty.GetString()!) ||
                IsGeneratedPath(clientProperty.GetString()!))
                return (false, "RELEASE_INTEGRITY: Invalid component paths in activated manifest.");
            var deployedWwwroot = Path.Combine(publishFolder, "wwwroot");
            var deployedOutputHash = HashDirectory(deployedWwwroot);
            if (!string.Equals(hashProperty.GetString(), deployedOutputHash, StringComparison.Ordinal))
                return (false, "RELEASE_INTEGRITY: Activated frontend differs from staged release SHA256.");
            if (integrity.TryGetProperty("sourceVersion", out var manifestVersion) &&
                manifestVersion.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(state.SourceVersion) &&
                !string.Equals(manifestVersion.GetString(), state.SourceVersion, StringComparison.Ordinal))
                return (false, "RELEASE_INTEGRITY: Source version differs between build state and release manifest.");
            _runner.Report("[RELEASE-INTEGRITY] PASS: Activated frontend hash matches release manifest.");
            if (!string.IsNullOrWhiteSpace(state.FrontendOutputHash) && state.FrontendOutputHash != "NONE" &&
                !string.Equals(deployedOutputHash, state.FrontendOutputHash, StringComparison.Ordinal))
                return (false, "STALE FRONTEND DETECTED — activated wwwroot hash does not match the frontend output produced/reused for this deployment.");

            var versionInfo = string.IsNullOrWhiteSpace(state.SourceVersion) ? "" : $" Source version: {state.SourceVersion}.";
            return (true, $"Fast Deploy verification passed. Source fingerprints and deployed frontend output are consistent.{versionInfo}");
        }
        catch (Exception ex)
        {
            return (false, "Post-deploy verification failed: " + ex.Message);
        }
    }

    // V78: Query the running API rather than trusting the staging manifest alone.
    // A healthy HTTP 200 is not sufficient if IIS is still serving a different release.
    public async Task<(bool Ok, string Detail)> VerifyRunningApiReleaseAsync(
        DeploymentProfile profile, string publishFolder, CancellationToken ct = default)
    {
        try
        {
            // Prepared packages must carry the same release identity contract as source builds.
            // Never turn missing identity evidence into a successful deployment.

            var manifestPath = Path.Combine(publishFolder, "gwd-release-integrity.json");
            if (!File.Exists(manifestPath))
                return (false, "RELEASE_VERSION: Release integrity manifest missing.");
            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
            var strictApiVersion = string.Equals(profile.ApiVersionContractMode, "Strict", StringComparison.OrdinalIgnoreCase);
            var hasExpectedVersion = doc.RootElement.TryGetProperty("sourceVersion", out var expectedValue) &&
                expectedValue.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(expectedValue.GetString()) &&
                !string.Equals(expectedValue.GetString(), "unknown", StringComparison.OrdinalIgnoreCase);
            if (!hasExpectedVersion && strictApiVersion)
                return (false, "RELEASE_VERSION: Strict mode requires a valid expected API version.");
            var expected = hasExpectedVersion ? expectedValue.GetString()!.Trim() : "";
            var host = string.IsNullOrWhiteSpace(profile.HostHeader) ? "localhost" : profile.HostHeader;
            var url = $"http://{host}:{profile.HttpPort}/api/v1/health";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return (false, $"RELEASE_VERSION: API version probe returned HTTP {(int)response.StatusCode}.");
            using var actual = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var hasObservedVersion = actual.RootElement.TryGetProperty("version", out var version) &&
                version.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(version.GetString()) &&
                !string.Equals(version.GetString(), "unknown", StringComparison.OrdinalIgnoreCase);
            if (!hasObservedVersion && strictApiVersion)
                return (false, "RELEASE_VERSION: Strict mode requires a valid version in API health response.");
            if (!hasExpectedVersion || !hasObservedVersion)
            {
                _runner.Report("[API-VERSION] UNVERIFIED (Compatible): expected or observed API version is missing; HTTP health passed, release identity not asserted.");
                return (true, "RELEASE_VERSION: API HTTP health PASS; API version UNVERIFIED (Compatible).");
            }
            var observed = version.GetString()!.Trim();
            if (!ReleaseVersionContract.Matches(expected, observed))
                return (false, $"RELEASE_VERSION: Expected API {expected}, running API reports {observed}.");
            return (true, $"RELEASE_VERSION: Running API version verified: {observed}.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, "RELEASE_VERSION: API verification failed: " + ex.Message);
        }
    }

    // V79: Read the frontend marker over HTTP, not from the deployment directory.
    // Prevent success when the API is new but IIS serves an older wwwroot.
    private static bool IsStrictUiVersionMode(DeploymentProfile profile) =>
        string.Equals(profile.UiVersionContractMode, "Strict", StringComparison.OrdinalIgnoreCase);

    public async Task<(bool Ok, string Detail)> VerifyRunningFrontendReleaseAsync(
        DeploymentProfile profile, string publishFolder, CancellationToken ct = default)
    {
        try
        {
            // Prepared publish is not exempt from live frontend release verification.

            var manifestPath = Path.Combine(publishFolder, "gwd-release-integrity.json");
            if (!File.Exists(manifestPath))
                return (false, "FRONTEND_RELEASE: Integrity manifest missing.");
            using var manifest = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, ct));
            var root = manifest.RootElement;
            if (!root.TryGetProperty("releaseId", out var releaseId) ||
                releaseId.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(releaseId.GetString()))
                return (false, "FRONTEND_RELEASE: Release ID missing.");
            if (!root.TryGetProperty("frontendSha256", out var hash) ||
                hash.ValueKind != JsonValueKind.String)
                return (false, "FRONTEND_RELEASE: Frontend hash missing.");
            var host = string.IsNullOrWhiteSpace(profile.HostHeader) ? "localhost" : profile.HostHeader;
            var url = $"http://{host}:{profile.HttpPort}/gwd-frontend-release.json";
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
                return (false, $"FRONTEND_RELEASE: HTTP {(int)response.StatusCode} retrieving frontend marker.");
            using var observed = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            if (!observed.RootElement.TryGetProperty("releaseId", out var liveId) ||
                !string.Equals(liveId.GetString(), releaseId.GetString(), StringComparison.Ordinal))
                return (false, "FRONTEND_RELEASE: IIS is serving another frontend release.");
            // V101: use the *recorded build evidence*, not a runtime guess.
            // Strict mode rejects missing evidence; Compatible mode logs UNVERIFIED.
            var strictUiVersion = IsStrictUiVersionMode(profile);
            var hasEvidence = root.TryGetProperty("uiVersionVerified", out var evidence) &&
                evidence.ValueKind == JsonValueKind.True;
            if (!hasEvidence && strictUiVersion)
                return (false, "UI_VERSION_CONTRACT: Strict mode requires verified frontend version evidence.");
            if (hasEvidence)
            {
                using var uiResponse = await client.GetAsync(
                    $"http://{host}:{profile.HttpPort}/gwd-ui-version.json", ct);
                if (!uiResponse.IsSuccessStatusCode)
                    return (false, $"UI_VERSION_CONTRACT: Live UI endpoint returned HTTP {(int)uiResponse.StatusCode}.");
                using var liveUi = JsonDocument.Parse(await uiResponse.Content.ReadAsStringAsync(ct));
                if (!liveUi.RootElement.TryGetProperty("version", out var liveVersion) ||
                    liveVersion.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(liveVersion.GetString()))
                    return (false, "UI_VERSION_CONTRACT: Live UI version is missing.");
                if (!root.TryGetProperty("sourceVersion", out var expectedVersion) ||
                    expectedVersion.ValueKind != JsonValueKind.String ||
                    !ReleaseVersionContract.Matches(expectedVersion.GetString(), liveVersion.GetString()))
                    return (false, $"UI_VERSION_CONTRACT: Live UI {liveVersion.GetString()} differs from source release.");
            }
            else
                _runner.Report("[UI-VERSION] UNVERIFIED (Compatible): live UI version not asserted; HTTP release ID and activated asset hash are still checked.");
            var deployedHash = HashDirectory(Path.Combine(publishFolder, "wwwroot"));
            if (!string.Equals(hash.GetString(), deployedHash, StringComparison.Ordinal))
                return (false, "FRONTEND_RELEASE: Activated frontend differs from staged release.");
            return (true, "FRONTEND_RELEASE: HTTP frontend release ID and activated asset hash verified.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, "FRONTEND_RELEASE: Verification failed: " + ex.Message);
        }
    }

    public void FinalizeSuccessfulDeployment(DeploymentProfile profile, string currentPublishFolder)
    {
        try
        {
            var statePath = Path.Combine(currentPublishFolder, ".smart-deploy-state.json");
            if (File.Exists(statePath))
            {
                var state = JsonSerializer.Deserialize<SmartDeployState>(File.ReadAllText(statePath));
                if (state is not null) CommitPersistentSmartState(profile, state);
            }
        }
        catch (Exception ex) { _runner.Report("[FAST] Warning: persistent build-state commit failed: " + ex.Message); }
        // V62: successful deployment backups are intentionally retained for rollback/history.
        // Keep only the newest five backups for this application.
        CleanupOldRollbackBackups(profile, keep: 5);
        _activeRollbackFolder = null;
        CleanupPreviousDeployments(profile, currentPublishFolder);
    }

    private string ResolveActivePublishFolder(DeploymentProfile profile)
    {
        if (string.Equals(profile.DeploymentLayout, "Single Folder", StringComparison.OrdinalIgnoreCase))
            return profile.HostingRoot.TrimEnd('\\');

        var sourceName = Path.GetFileName(profile.SourceFolder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(sourceName)) sourceName = "release-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var release = SanitizeFolderName(sourceName);
        var candidate = Path.Combine(profile.HostingRoot, release);
        if (Directory.Exists(candidate)) candidate += "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        return candidate;
    }

    private string GetRollbackFolder(DeploymentProfile profile)
        => !string.IsNullOrWhiteSpace(_activeRollbackFolder)
            ? _activeRollbackFolder
            : Path.Combine(GetRollbackRoot(profile), "legacy-unresolved");

    private static string GetRollbackRoot(DeploymentProfile profile)
        => Path.Combine(profile.DeploymentRoot, "Backups", SanitizeFolderName(profile.ApplicationName));

    private static string CreateUniqueRollbackFolder(DeploymentProfile profile)
    {
        var root = GetRollbackRoot(profile);
        Directory.CreateDirectory(root);
        var id = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N")[..8];
        return Path.Combine(root, id);
    }

    private void CleanupOldRollbackBackups(DeploymentProfile profile, int keep)
    {
        try
        {
            var root = GetRollbackRoot(profile);
            if (!Directory.Exists(root)) return;
            var dirs = new DirectoryInfo(root).EnumerateDirectories()
                .OrderByDescending(d => d.CreationTimeUtc).ToArray();
            foreach (var dir in dirs.Skip(Math.Max(1, keep)))
            {
                try { dir.Delete(recursive: true); _runner.Report($"[CLEANUP] Removed old deployment backup: {dir.FullName}"); }
                catch (Exception ex) { InternalDiagnostics.Record(ex, "Cleanup old deployment backup"); }
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Enumerate deployment backups"); }
    }

    private static string SanitizeFolderName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
        return string.IsNullOrWhiteSpace(value) ? "release" : value.Trim();
    }

    public async Task ConfigureIisAsync(DeploymentProfile profile, string publishFolder, CancellationToken ct = default)
    {
        var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(appcmd)) throw new InvalidOperationException("IIS appcmd.exe was not found. Install IIS first.");

        var listPool = await _runner.RunAsync(appcmd, $"list apppool /name:\"{profile.AppPoolName}\"");
        if (listPool.ExitCode != 0 || string.IsNullOrWhiteSpace(listPool.Output))
            await _runner.RunCheckedAsync(appcmd, $"add apppool /name:\"{profile.AppPoolName}\"");
        await _runner.RunCheckedAsync(appcmd, $"set apppool /apppool.name:\"{profile.AppPoolName}\" /managedRuntimeVersion:\"\" /processModel.identityType:ApplicationPoolIdentity");

        var portOwner = new EnvironmentService(_runner).GetIisSiteUsingPort(profile.HttpPort);
        if (!string.IsNullOrWhiteSpace(portOwner) && !string.Equals(portOwner, profile.SiteName, StringComparison.OrdinalIgnoreCase))
        {
            _runner.Report($"[IIS] Port {profile.HttpPort} is currently owned by IIS site '{portOwner}'. Taking over the requested port: stopping and removing the conflicting IIS site before creating '{profile.SiteName}'.");
            await _runner.RunAsync(appcmd, $"stop site /site.name:\"{portOwner}\"", ct: ct);
            await _runner.RunCheckedAsync(appcmd, $"delete site /site.name:\"{portOwner}\"", ct: ct);
        }

        var listSite = await _runner.RunAsync(appcmd, $"list site /name:\"{profile.SiteName}\"");
        if (listSite.ExitCode == 0 && !string.IsNullOrWhiteSpace(listSite.Output))
        {
            _runner.Report($"[IIS] Existing site '{profile.SiteName}' detected. Reusing the requested binding by replacing only this IIS site; IIS itself and unrelated sites remain running.");
            await _runner.RunAsync(appcmd, $"stop site /site.name:\"{profile.SiteName}\"");
            await _runner.RunCheckedAsync(appcmd, $"delete site /site.name:\"{profile.SiteName}\"");
        }

        var binding = string.IsNullOrWhiteSpace(profile.HostHeader) ? $"http/*:{profile.HttpPort}:" : $"http/*:{profile.HttpPort}:{profile.HostHeader}";
        await _runner.RunCheckedAsync(appcmd, $"add site /name:\"{profile.SiteName}\" /physicalPath:\"{publishFolder}\" /bindings:\"{binding}\"");
        await _runner.RunCheckedAsync(appcmd, $"set app /app.name:\"{profile.SiteName}/\" /applicationPool:\"{profile.AppPoolName}\"");

        var dataFolder = Path.Combine(profile.DeploymentRoot, "Data");
        Directory.CreateDirectory(dataFolder);
        await _runner.RunCheckedAsync("icacls.exe", $"\"{dataFolder}\" /grant \"IIS AppPool\\{profile.AppPoolName}:(OI)(CI)M\" /T");
        await _runner.RunAsync(appcmd, $"start apppool /apppool.name:\"{profile.AppPoolName}\"");
        await _runner.RunAsync(appcmd, $"start site /site.name:\"{profile.SiteName}\"");
    }


    public async Task<string?> GetSitePhysicalPathAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(appcmd)) return null;
        var result = await _runner.RunAsync(appcmd, $"list vdir /vdir.name:\"{profile.SiteName}/\" /text:physicalPath", ct: ct);
        if (result.ExitCode != 0) return null;
        var value = result.Output.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim('"');
    }

    private async Task RunRollbackCommandCheckedAsync(string executable, string arguments, string operation)
    {
        try
        {
            var result = await _runner.RunAsync(executable, arguments, ct: CancellationToken.None, timeout: TimeSpan.FromSeconds(60));
            if (result.ExitCode != 0)
            {
                var detail = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
                throw new IOException($"Rollback {operation} failed (exit {result.ExitCode}): {detail}");
            }
        }
        catch (TimeoutException ex)
        {
            throw new IOException($"Rollback {operation} timed out after 60 seconds.", ex);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new IOException($"Rollback {operation} could not start the required command.", ex);
        }
    }

    public async Task CleanupFailedDeploymentAsync(DeploymentProfile profile, string? failedPublishFolder, string? previousSitePath, CancellationToken ct = default)
    {
        // A null publish path is not evidence that no hosted files were changed.
        // For Single Folder deployments, a valid rollback backup still requires recovery.
        var hasSingleFolderBackup = string.Equals(profile.DeploymentLayout, "Single Folder", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(GetRollbackFolder(profile));
        if (string.IsNullOrWhiteSpace(failedPublishFolder) && !hasSingleFolderBackup) return;
        // Recovery must finish even if the original deployment was cancelled.
        // Callers must not pass the cancelled deployment token into compensating actions.
        var recoveryToken = CancellationToken.None;
        // External rollback commands have individual 60-second limits; file moves
        // remain non-cancellable to avoid interrupting the recovery swap.
        _runner.Report($"[CLEANUP] Cleaning failed deployment: {failedPublishFolder}");

        var rollback = GetRollbackFolder(profile);
        // A missing backup cannot safely restore an existing Single Folder installation.
        // Do not fall through to versioned-folder cleanup and remove the active site.
        if (string.Equals(profile.DeploymentLayout, "Single Folder", StringComparison.OrdinalIgnoreCase)
            && !Directory.Exists(rollback)
            && !string.IsNullOrWhiteSpace(previousSitePath)
            && Directory.Exists(previousSitePath))
        {
            throw new IOException("Single Folder rollback cannot proceed: previous deployment exists but its backup is missing. Active files and site configuration were left unchanged.");
        }
        if (string.Equals(profile.DeploymentLayout, "Single Folder", StringComparison.OrdinalIgnoreCase) && Directory.Exists(rollback))
        {
            _runner.Report("[ROLLBACK] Restoring the previous Single Folder deployment.");
            await StopSiteForDeploymentAsync(profile, recoveryToken);
            var target = Path.GetFullPath(profile.HostingRoot);
            // Prepare the recovery payload before touching the active folder. A failed
            // copy must not destroy the only remaining hosted deployment.
            var parent = Path.GetDirectoryName(target) ?? throw new IOException("Hosted path has no parent directory.");
            var prepared = Path.Combine(parent, Path.GetFileName(target) + ".gwd-restore-" + Guid.NewGuid().ToString("N"));
            var displaced = Path.Combine(parent, Path.GetFileName(target) + ".gwd-displaced-" + Guid.NewGuid().ToString("N"));
            try
            {
                CopyRecoveryPayloadWithDeadline(rollback, prepared);

                // Once replacement begins, cancellation must not interrupt recovery.
                if (Directory.Exists(target)) Directory.Move(target, displaced);
                try
                {
                    Directory.Move(prepared, target);
                }
                catch
                {
                    if (!Directory.Exists(target) && Directory.Exists(displaced))
                        Directory.Move(displaced, target);
                    throw;
                }
                // Preserve displaced content if deletion fails; recovery is already active.
                if (Directory.Exists(displaced))
                {
                    try { Directory.Delete(displaced, recursive: true); }
                    catch (Exception ex) { InternalDiagnostics.Record(ex, "Cleanup displaced hosted folder after rollback"); }
                }
            }
            finally
            {
                if (Directory.Exists(prepared))
                {
                    try { Directory.Delete(prepared, recursive: true); }
                    catch (Exception ex) { InternalDiagnostics.Record(ex, "Cleanup prepared rollback folder"); }
                }
            }
            await ConfigureIisAsync(profile, profile.HostingRoot, recoveryToken);
            await RestartAppAsync(profile, recoveryToken);
            // Restoring files and configuration does not establish that the prior
            // release is actually responding over HTTP. Avoid a verified-success claim.
            // Verify live service health after rollback, not just successful IIS commands.
            var recoveredHealth = await HealthCheckAsync(profile, recoveryToken);
            if (!recoveredHealth.Ok)
                throw new IOException("Rollback restored files but HTTP health failed: " + recoveredHealth.Detail);
            var recoveredManifest = Path.Combine(target, "gwd-release-integrity.json");
            if (File.Exists(recoveredManifest))
            {
                var api = await VerifyRunningApiReleaseAsync(profile, target, recoveryToken);
                if (!api.Ok) throw new IOException("Rollback API identity verification failed: " + api.Detail);
                var frontend = await VerifyRunningFrontendReleaseAsync(profile, target, recoveryToken);
                if (!frontend.Ok) throw new IOException("Rollback frontend identity verification failed: " + frontend.Detail);
                _runner.Report("[ROLLBACK] Recovery VERIFIED: HTTP health and API/frontend release identity.");
            }
            else
            {
                _runner.Report("[ROLLBACK] Recovery HTTP health PASS; release identity NOT VERIFIED (legacy backup without manifest).");
            }
            return;
        }
        var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (File.Exists(appcmd))
        {
            var current = await GetSitePhysicalPathAsync(profile, recoveryToken);
            if (!string.IsNullOrWhiteSpace(current) && PathsEqual(current, failedPublishFolder))
            {
                if (!string.IsNullOrWhiteSpace(previousSitePath) && Directory.Exists(previousSitePath) && !PathsEqual(previousSitePath, failedPublishFolder))
                {
                    _runner.Report($"[ROLLBACK] Restoring IIS site '{profile.SiteName}' to previous path: {previousSitePath}");
                    await RunRollbackCommandCheckedAsync(appcmd, $"set vdir /vdir.name:\"{profile.SiteName}/\" /physicalPath:\"{previousSitePath}\"", "restore previous site path");
                    await RunRollbackCommandCheckedAsync(appcmd, $"start site /site.name:\"{profile.SiteName}\"", "start restored site");
                }
                else
                {
                    _runner.Report($"[CLEANUP] Removing IIS site '{profile.SiteName}' because it points to the failed deployment and no previous site is available.");
                    await RunRollbackCommandCheckedAsync(appcmd, $"delete site /site.name:\"{profile.SiteName}\"", "remove failed site");
                }
            }
        }

        try
        {
            if (Directory.Exists(failedPublishFolder)) Directory.Delete(failedPublishFolder, recursive: true);
            _runner.Report("[CLEANUP] Failed deployment folder removed. Persistent Data, Logs, backups, and databases were not deleted.");
        }
        catch (Exception ex)
        {
            _runner.Report("[CLEANUP] Could not completely remove the failed deployment folder: " + ex.Message);
            InternalDiagnostics.Record(ex, "Remove failed deployment folder after rollback");
            // The deployment has already failed. Propagate cleanup failure so callers
            // cannot mistake the incomplete cleanup for a completed recovery.
            throw new IOException($"Failed deployment cleanup is incomplete; folder remains at '{failedPublishFolder}'.", ex);
        }
    }

    public void MarkDeploymentSuccessful(string publishFolder)
    {
        var marker = Path.Combine(publishFolder, ".deployment-incomplete");
        try { if (File.Exists(marker)) File.Delete(marker); } catch (Exception ex) { InternalDiagnostics.Record(ex, "Remove deployment incomplete marker"); }
        try { File.WriteAllText(Path.Combine(publishFolder, ".deployment-success"), DateTime.Now.ToString("O")); } catch (Exception ex) { InternalDiagnostics.Record(ex, "Write deployment success marker"); }
    }

    private static bool PathsEqual(string a, string b)
    {
        try { return string.Equals(Path.GetFullPath(a).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Compare deployment paths"); return string.Equals(a.TrimEnd('\\'), b.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase); }
    }

    public async Task RestartAppAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(appcmd)) return;
        var pool = await _runner.RunAsync(appcmd, $"list apppool /name:\"{profile.AppPoolName}\"", ct: ct);
        if (pool.ExitCode == 0 && !string.IsNullOrWhiteSpace(pool.Output))
            await _runner.RunAsync(appcmd, $"recycle apppool /apppool.name:\"{profile.AppPoolName}\"", ct: ct);
        else
            _runner.Report($"[IIS] App pool '{profile.AppPoolName}' does not exist; recycle skipped.");
        var site = await _runner.RunAsync(appcmd, $"list site /name:\"{profile.SiteName}\"", ct: ct);
        if (site.ExitCode == 0 && !string.IsNullOrWhiteSpace(site.Output))
            await _runner.RunAsync(appcmd, $"start site /site.name:\"{profile.SiteName}\"", ct: ct);
        else
            _runner.Report($"[IIS] Site '{profile.SiteName}' does not exist; start skipped.");
    }

    public sealed record HealthCheckResult(bool Ok, string Detail, string Classification, int? HttpStatus = null, string? Url = null);

    public async Task<HealthCheckResult> HealthCheckAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (File.Exists(appcmd))
        {
            var pool = await _runner.RunAsync(appcmd, $"list apppool /name:\"{profile.AppPoolName}\"", ct: ct);
            if (pool.ExitCode != 0 || !pool.Output.Contains("state:Started", StringComparison.OrdinalIgnoreCase))
                return new(false, $"IIS AppPool '{profile.AppPoolName}' is not Started.\r\n{pool.Output} {pool.Error}".Trim(), "IIS_APPPOOL_DOWN");
            var site = await _runner.RunAsync(appcmd, $"list site /name:\"{profile.SiteName}\"", ct: ct);
            if (site.ExitCode != 0 || !site.Output.Contains("state:Started", StringComparison.OrdinalIgnoreCase))
                return new(false, $"IIS Site '{profile.SiteName}' is not Started.\r\n{site.Output} {site.Error}".Trim(), "IIS_SITE_DOWN");
        }

        var host = string.IsNullOrWhiteSpace(profile.HostHeader) ? "localhost" : profile.HostHeader;
        var path = string.IsNullOrWhiteSpace(profile.HealthPath) ? "/" : profile.HealthPath;
        if (!path.StartsWith('/')) path = "/" + path;
        var url = $"http://{host}:{profile.HttpPort}{path}";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        HealthCheckResult? last = null;
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var response = await http.GetAsync(url, ct);
                var body = await response.Content.ReadAsStringAsync(ct);
                if (body.Length > 4000) body = body[..4000] + "\r\n[response truncated]";
                var status = (int)response.StatusCode;
                var classification = status == 500 && body.Contains("500.30", StringComparison.OrdinalIgnoreCase)
                    ? "ASP.NET_CORE_STARTUP_FAILURE_500_30"
                    : status == 404 ? "HEALTH_ENDPOINT_NOT_FOUND"
                    : response.IsSuccessStatusCode ? "HEALTHY" : "HTTP_FAILURE";
                last = new(response.IsSuccessStatusCode, $"Layer IIS: Site/AppPool Started. Layer HTTP/Application: {status} {response.StatusCode} — {url} (attempt {attempt}/4)\r\n{body}", classification, status, url);
                if (last.Ok) return last;
                if (classification == "HEALTH_ENDPOINT_NOT_FOUND" && path != "/")
                {
                    try
                    {
                        var rootUrl = $"http://{host}:{profile.HttpPort}/";
                        using var rootResponse = await http.GetAsync(rootUrl, ct);
                        if (rootResponse.IsSuccessStatusCode)
                            return new(true, $"Configured health endpoint returned 404, but application root is reachable: {(int)rootResponse.StatusCode} {rootResponse.StatusCode} — {rootUrl}. No rollback required for a missing optional endpoint.", "HEALTH_ENDPOINT_MISSING_APP_REACHABLE", (int)rootResponse.StatusCode, rootUrl);
                    }
                    catch (Exception ex) { _runner.Report("[HEALTH] Root fallback after health-endpoint 404 failed: " + ex.Message); }
                }
                // A persistent startup crash benefits from one short retry, while warm-up/transient failures get all retries.
                if (classification == "ASP.NET_CORE_STARTUP_FAILURE_500_30" && attempt >= 2) break;
            }
            catch (Exception ex)
            {
                last = new(false, $"Layer IIS: Site/AppPool Started. Layer HTTP: request failed — {url} (attempt {attempt}/4)\r\n{ex.Message}", "HTTP_UNREACHABLE", null, url);
            }
            if (attempt < 4) await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct);
        }
        return last ?? new(false, $"Health check did not complete — {url}", "UNKNOWN", null, url);
    }

    public async Task<string> CollectStartupDiagnosticsAsync(DeploymentProfile profile, string publishFolder, HealthCheckResult health, CancellationToken ct = default)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("=== V71 POST-DEPLOY STARTUP DIAGNOSTICS ===");
        sb.AppendLine($"Classification: {health.Classification}");
        sb.AppendLine($"URL: {health.Url ?? "(none)"}");
        sb.AppendLine($"HTTP: {(health.HttpStatus?.ToString() ?? "n/a")}");
        sb.AppendLine(health.Detail);
        sb.AppendLine();
        try
        {
            var appcmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
            if (File.Exists(appcmd))
            {
                var pool = await _runner.RunAsync(appcmd, $"list apppool /name:\"{profile.AppPoolName}\"", ct: ct);
                var site = await _runner.RunAsync(appcmd, $"list site /name:\"{profile.SiteName}\"", ct: ct);
                sb.AppendLine("--- IIS ---"); sb.AppendLine(pool.Output); sb.AppendLine(site.Output);
            }
            var dotnet = await _runner.RunAsync("dotnet", "--list-runtimes", ct: ct, timeout: TimeSpan.FromSeconds(10));
            sb.AppendLine("--- Installed .NET runtimes ---"); sb.AppendLine(dotnet.Output);
        }
        catch (Exception ex) { sb.AppendLine("Runtime/IIS diagnostic warning: " + ex.Message); }

        try
        {
            var webConfig = Path.Combine(publishFolder, "web.config");
            if (File.Exists(webConfig))
            {
                var xml = File.ReadAllText(webConfig);
                xml = Regex.Replace(xml, "(?is)<connectionStrings.*?</connectionStrings>", "<connectionStrings>[REDACTED]</connectionStrings>");
                xml = Regex.Replace(xml, @"(?i)(password|pwd|user id|uid)\s*=\s*[^;""']+", "$1=[REDACTED]");
                sb.AppendLine("--- web.config (secrets redacted) ---"); sb.AppendLine(xml.Length > 12000 ? xml[..12000] + "\r\n[truncated]" : xml);
            }
        }
        catch (Exception ex) { sb.AppendLine("web.config diagnostic warning: " + ex.Message); }

        try
        {
            var candidates = Directory.Exists(publishFolder)
                ? Directory.EnumerateFiles(publishFolder, "*.log", SearchOption.AllDirectories)
                    .Where(x => Path.GetFileName(x).StartsWith("stdout", StringComparison.OrdinalIgnoreCase) || x.Contains("Logs", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(File.GetLastWriteTimeUtc).Take(4).ToArray() : Array.Empty<string>();
            foreach (var file in candidates)
            {
                var text = File.ReadAllText(file);
                if (text.Length > 12000) text = text[^12000..];
                text = Regex.Replace(text, @"(?i)(password|pwd)\s*[=:]\s*\S+", "$1=[REDACTED]");
                sb.AppendLine($"--- log: {file} ---"); sb.AppendLine(text);
            }
        }
        catch (Exception ex) { sb.AppendLine("stdout/log diagnostic warning: " + ex.Message); }

        try
        {
            var ps = "Get-WinEvent -FilterHashtable @{LogName='Application'; StartTime=(Get-Date).AddMinutes(-10)} -ErrorAction SilentlyContinue | " +
                     "Where-Object { $_.ProviderName -match 'IIS|AspNetCore|.NET Runtime|Application Error' } | " +
                     "Select-Object -First 30 TimeCreated,ProviderName,Id,LevelDisplayName,Message | Format-List";
            var ev = await _runner.RunAsync("powershell.exe", "-NoProfile -NonInteractive -Command \"" + ps.Replace("\"", "`\"") + "\"", ct: ct, timeout: TimeSpan.FromSeconds(15));
            sb.AppendLine("--- Windows Application Event Log (last 10 min) ---");
            sb.AppendLine(ev.Output); if (!string.IsNullOrWhiteSpace(ev.Error)) sb.AppendLine(ev.Error);
        }
        catch (Exception ex) { sb.AppendLine("Event Log diagnostic warning: " + ex.Message); }
        return SecretRedactor.Redact(sb.ToString());
    }

    // Source integrity gate: never discover a frontend from a nested Git worktree.
    // A valid frontend belongs to the same API project tree as the selected backend.
    private static string? FindClientFolder(DeploymentProfile profile)
    {
        if (!Directory.Exists(profile.SourceFolder) || !File.Exists(profile.ApiProjectPath)) return null;
        var sourceRoot = Path.GetFullPath(profile.SourceFolder).TrimEnd(Path.DirectorySeparatorChar);
        var apiRoot = Path.GetDirectoryName(Path.GetFullPath(profile.ApiProjectPath))!;
        if (!apiRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SOURCE_INTEGRITY: API project is outside the selected Source Root.");
        if (IsGeneratedPath(Path.GetRelativePath(sourceRoot, apiRoot)))
            throw new InvalidOperationException("SOURCE_INTEGRITY: API project is inside a generated or Git worktree directory.");
        var expected = Path.Combine(apiRoot, "ClientApp");
        if (!File.Exists(Path.Combine(expected, "package.json"))) return null;
        using var package = JsonDocument.Parse(File.ReadAllText(Path.Combine(expected, "package.json")));
        if (!package.RootElement.TryGetProperty("scripts", out var scripts) ||
            !scripts.TryGetProperty("build", out var build) || string.IsNullOrWhiteSpace(build.GetString()))
            throw new InvalidOperationException("SOURCE_INTEGRITY: API ClientApp has no valid build script.");
        return expected;
    }

    // V74: Detect a nested repository or stale worktree masquerading as the frontend.
    // The source may be a clean exported ZIP (without .git); in that case
    // WorkspaceSynchronizer's content hash verification remains authoritative.
    private void VerifyIncomingGitProvenance(DeploymentProfile profile)
    {
        var root = ResolveCanonicalSourceRoot(profile.SourceFolder, profile.ApiProjectPath);
        var api = Path.GetDirectoryName(Path.GetFullPath(profile.ApiProjectPath))
            ?? throw new InvalidOperationException("SOURCE_INTEGRITY: API directory missing.");
        if (!Path.GetFullPath(api).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("SOURCE_INTEGRITY: Backend outside incoming root.");
        if (IsGeneratedPath(Path.GetRelativePath(root, api)))
            throw new InvalidOperationException("SOURCE_INTEGRITY: Backend is inside an excluded worktree.");
        var client = Path.Combine(api, "ClientApp");
        if (Directory.Exists(client) && IsGeneratedPath(Path.GetRelativePath(root, client)))
            throw new InvalidOperationException("SOURCE_INTEGRITY: Frontend is inside an excluded worktree.");

        string? Git(string directory, string args)
        {
            using var process = new System.Diagnostics.Process();
            process.StartInfo = new System.Diagnostics.ProcessStartInfo("git")
            {
                RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, CreateNoWindow = true
            };
            process.StartInfo.ArgumentList.Add("-C");
            process.StartInfo.ArgumentList.Add(directory);
            foreach (var arg in args.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                process.StartInfo.ArgumentList.Add(arg);
            try
            {
                if (!process.Start()) return null;
                var output = process.StandardOutput.ReadToEnd().Trim();
                if (!process.WaitForExit(10000))
                {
                    try { process.Kill(entireProcessTree: true); } catch { }
                    throw new InvalidOperationException("SOURCE_INTEGRITY: Git provenance timed out.");
                }
                return process.ExitCode == 0 ? output : null;
            }
            catch (System.ComponentModel.Win32Exception) { return null; }
        }

        var rootGit = Git(root, "rev-parse --show-toplevel");
        if (rootGit is null)
        {
            _runner.Report("[SOURCE] Git provenance unavailable; enforcing canonical paths and workspace SHA256 verification.");
            return;
        }
        var expected = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (!PathsEqual(rootGit, expected))
            throw new InvalidOperationException("SOURCE_INTEGRITY: Incoming source is not the Git repository root.");
        foreach (var component in new[] { api, client }.Where(Directory.Exists))
        {
            var actual = Git(component, "rev-parse --show-toplevel");
            if (actual is null || !PathsEqual(actual, expected))
                throw new InvalidOperationException("SOURCE_INTEGRITY: Component belongs to another Git repository/worktree: " + component);
        }
        var sha = Git(root, "rev-parse HEAD");
        if (string.IsNullOrWhiteSpace(sha))
            throw new InvalidOperationException("SOURCE_INTEGRITY: Git HEAD cannot be resolved.");
        _runner.Report("[SOURCE] Verified single Git root and HEAD: " + sha);
    }

    private static void VerifyBuildSourceIdentity(DeploymentProfile profile)
    {
        var api = Path.GetFullPath(profile.ApiProjectPath);
        var root = Path.GetFullPath(profile.SourceFolder).TrimEnd(Path.DirectorySeparatorChar);
        if (!api.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            IsGeneratedPath(Path.GetRelativePath(root, api)))
            throw new InvalidOperationException("SOURCE_INTEGRITY: Backend project must be inside the canonical Source Root, not a worktree.");
        if (profile.RequiresNode)
        {
            var frontend = FindClientFolder(profile) ?? throw new InvalidOperationException("SOURCE_INTEGRITY: Expected API ClientApp is missing.");
            if (IsGeneratedPath(Path.GetRelativePath(root, frontend)))
                throw new InvalidOperationException("SOURCE_INTEGRITY: Frontend is inside an excluded or worktree path.");
        }
    }

    // The deadline applies to preparing the replacement, never to the directory swap.
    // File.Copy itself is synchronous; cancellation is checked between files only.
    private static void CopyRecoveryPayloadWithDeadline(string source, string destination)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(10));
        try
        {
            CopyDirectory(source, destination, deadline.Token);
        }
        catch (OperationCanceledException ex) when (deadline.IsCancellationRequested)
        {
            throw new TimeoutException("Recovery preparation exceeded 10 minutes; folder replacement was not started.", ex);
        }
    }

    private static void CopyDirectory(string sourceDir, string destinationDir, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        Directory.CreateDirectory(destinationDir);
        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            File.Copy(file, Path.Combine(destinationDir, Path.GetFileName(file)), overwrite: true);
        }
        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
        {
            ct.ThrowIfCancellationRequested();
            CopyDirectory(directory, Path.Combine(destinationDir, Path.GetFileName(directory)), ct);
        }
    }

    public void WriteConnectionString(string publishFolder, DeploymentProfile profile)
    {
        foreach (var name in new[] { "appsettings.json", "appsettings.Production.json" })
        {
            var path = Path.Combine(publishFolder, name);
            if (!File.Exists(path)) continue;
            var node = JsonNode.Parse(File.ReadAllText(path))?.AsObject() ?? new JsonObject();
            node["ConnectionStrings"] ??= new JsonObject();
            node["ConnectionStrings"]!["DefaultConnection"] = _sql.BuildConnectionString(profile);
            node["ConnectionStrings"]!["NSPOffice"] = _sql.BuildConnectionString(profile);
            File.WriteAllText(path, node.ToJsonString(new() { WriteIndented = true }));
        }
    }

    public void CleanupPreviousDeployments(DeploymentProfile profile, string currentPublishFolder)
    {
        if (!profile.CleanupPreviousDeployments) return;
        if (!string.Equals(profile.DeploymentLayout, "Versioned Folders", StringComparison.OrdinalIgnoreCase)) return;
        var root = profile.HostingRoot;
        if (!Directory.Exists(root)) return;
        foreach (var dir in Directory.GetDirectories(root))
        {
            if (PathsEqual(dir, currentPublishFolder)) continue;
            if (dir.EndsWith(".__rollback", StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                Directory.Delete(dir, true);
                _runner.Report($"[CLEANUP] Removed previous deployment folder: {dir}");
            }
            catch (Exception ex)
            {
                _runner.Report($"[CLEANUP] Could not remove previous deployment folder '{dir}': {ex.Message}");
            }
        }
    }

}
