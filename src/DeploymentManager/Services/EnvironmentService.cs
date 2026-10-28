using Microsoft.Win32;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;
using GenericWeb.DeploymentManager.Models;

namespace GenericWeb.DeploymentManager.Services;

public sealed record IisSiteInfo(string Name, string State, string Bindings, string PhysicalPath, string AppPool);
public sealed record IisAppPoolInfo(string Name, string State);

public sealed class EnvironmentService
{
    private readonly CommandRunner _runner;
    public EnvironmentService(CommandRunner runner) => _runner = runner;

    public string GetWindowsDescription()
    {
        var product = ReadRegistryString(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "ProductName");
        var display = ReadRegistryString(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion");
        var build = ReadRegistryString(RegistryHive.LocalMachine, @"SOFTWARE\Microsoft\Windows NT\CurrentVersion", "CurrentBuildNumber");
        return $"{product} {display} (Build {build})".Replace("  ", " ").Trim();
    }

    public bool IsLocalTarget(string host)
    {
        if (string.IsNullOrWhiteSpace(host)) return true;
        return host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals(".", StringComparison.OrdinalIgnoreCase)
            || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            || host.Equals(Dns.GetHostName(), StringComparison.OrdinalIgnoreCase);
    }

    public async Task<PrerequisiteState> CheckIisAsync()
    {
        var appCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (File.Exists(appCmd)) return new(true, "Installed — IIS administration tools (appcmd.exe) detected.");

        var result = await _runner.RunAsync("dism.exe", "/Online /Get-FeatureInfo /FeatureName:IIS-WebServerRole /English");
        var enabled = result.Output.Contains("State : Enabled", StringComparison.OrdinalIgnoreCase);
        return enabled ? new(true, "Installed — IIS Web Server Windows feature is enabled.") : new(false, "Missing — IIS Web Server role is not enabled.");
    }

    public async Task InstallIisAsync()
    {
        var args = "/Online /Enable-Feature /FeatureName:IIS-WebServerRole /FeatureName:IIS-WebServer /FeatureName:IIS-CommonHttpFeatures /FeatureName:IIS-DefaultDocument /FeatureName:IIS-StaticContent /FeatureName:IIS-HttpErrors /FeatureName:IIS-HttpLogging /FeatureName:IIS-RequestFiltering /FeatureName:IIS-WebServerManagementTools /FeatureName:IIS-ManagementConsole /FeatureName:IIS-ManagementScriptingTools /All /NoRestart /English";
        await _runner.RunCheckedAsync("dism.exe", args);
    }

    public PrerequisiteState CheckHostingBundle(int major)
    {
        var dll = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "IIS", "Asp.Net Core Module", "V2", "aspnetcorev2.dll");
        var runtimeRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet", "shared", "Microsoft.AspNetCore.App");
        var matchingRuntime = Directory.Exists(runtimeRoot) && Directory.EnumerateDirectories(runtimeRoot)
            .Select(Path.GetFileName)
            .Any(x => x is not null && x.StartsWith(major + ".", StringComparison.OrdinalIgnoreCase));
        if (!File.Exists(dll) || !matchingRuntime)
            return new(false, $"Missing/incomplete — ASP.NET Core {major} runtime + IIS Hosting Bundle are required.");
        var version = FileVersionInfo.GetVersionInfo(dll).FileVersion ?? "installed";
        return new(true, $"Installed — ASP.NET Core {major} runtime detected; IIS Module V2 {version}.");
    }

    public async Task<PrerequisiteState> CheckDotNetSdkAsync(int major)
    {
        try
        {
            var result = await _runner.RunAsync(ResolveExecutable("dotnet.exe", @"C:\Program Files\dotnet\dotnet.exe"), "--list-sdks");
            var line = result.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault(x => x.TrimStart().StartsWith(major + ".", StringComparison.OrdinalIgnoreCase));
            return line is null
                ? new(false, $"Missing — .NET {major} SDK was not found.")
                : new(true, $"Installed — {line.Trim()}");
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Check .NET SDK"); return new(false, $"Missing — dotnet.exe / .NET {major} SDK was not found."); }
    }

    public async Task<PrerequisiteState> CheckNodeAsync(bool required)
    {
        if (!required) return new(true, "Not required — no package.json was detected in this source.");
        try
        {
            var node = await _runner.RunAsync(ResolveExecutable("node.exe", @"C:\Program Files\nodejs\node.exe"), "--version");
            var npm = await _runner.RunAsync(ResolveExecutable("npm.cmd", @"C:\Program Files\nodejs\npm.cmd"), "--version");
            if (node.ExitCode == 0 && npm.ExitCode == 0)
                return new(true, $"Installed — Node {node.Output.Trim()}, npm {npm.Output.Trim()}.");
            return new(false, "Missing/broken — Node.js or npm is not working.");
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Check Node.js/npm"); return new(false, "Missing — Node.js/npm was not found."); }
    }

    public int DetectTargetFrameworkMajor(string projectPath)
    {
        if (!File.Exists(projectPath)) return 9;
        var text = File.ReadAllText(projectPath);
        var match = Regex.Match(text, @"<TargetFrameworks?>\s*net(?<major>\d+)\.", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups["major"].Value, out var major) ? major : 9;
    }

    public bool DetectNodeRequirement(string sourceFolder, string projectPath)
    {
        if (Directory.Exists(sourceFolder) && Directory.EnumerateFiles(sourceFolder, "package.json", SearchOption.AllDirectories).Any()) return true;
        if (!File.Exists(projectPath)) return false;
        var text = File.ReadAllText(projectPath);
        return text.Contains("npm ", StringComparison.OrdinalIgnoreCase)
            || text.Contains("ClientApp", StringComparison.OrdinalIgnoreCase)
            || text.Contains("package.json", StringComparison.OrdinalIgnoreCase);
    }

    public bool IsPortFree(int port) => !IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners().Any(x => x.Port == port);

    public async Task<IReadOnlyList<IisSiteInfo>> ListIisSitesAsync(CancellationToken ct = default)
    {
        var appcmd = AppCmdPath();
        if (!File.Exists(appcmd)) return Array.Empty<IisSiteInfo>();
        var namesResult = await _runner.RunAsync(appcmd, "list site /text:name", ct: ct);
        if (namesResult.ExitCode != 0) return Array.Empty<IisSiteInfo>();
        var names = namesResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().Trim('"')).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var list = new List<IisSiteInfo>();
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            var state = await ReadAppCmdTextAsync(appcmd, $"list site /name:\"{EscapeArg(name)}\" /text:state", ct);
            var bindings = await ReadAppCmdTextAsync(appcmd, $"list site /name:\"{EscapeArg(name)}\" /text:bindings", ct);
            var path = await ReadAppCmdTextAsync(appcmd, $"list vdir /vdir.name:\"{EscapeArg(name)}/\" /text:physicalPath", ct);
            var pool = await ReadAppCmdTextAsync(appcmd, $"list app /app.name:\"{EscapeArg(name)}/\" /text:applicationPool", ct);
            list.Add(new IisSiteInfo(name, string.IsNullOrWhiteSpace(state) ? "Unknown" : state, bindings, path.Trim('"'), pool.Trim('"')));
        }
        return list;
    }

    public async Task<IReadOnlyList<IisAppPoolInfo>> ListIisAppPoolsAsync(CancellationToken ct = default)
    {
        var appcmd = AppCmdPath();
        if (!File.Exists(appcmd)) return Array.Empty<IisAppPoolInfo>();
        var namesResult = await _runner.RunAsync(appcmd, "list apppool /text:name", ct: ct);
        if (namesResult.ExitCode != 0) return Array.Empty<IisAppPoolInfo>();
        var names = namesResult.Output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim().Trim('"')).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase);
        var list = new List<IisAppPoolInfo>();
        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            var state = await ReadAppCmdTextAsync(appcmd, $"list apppool /apppool.name:\"{EscapeArg(name)}\" /text:state", ct);
            list.Add(new IisAppPoolInfo(name, string.IsNullOrWhiteSpace(state) ? "Unknown" : state));
        }
        return list;
    }

    public async Task StartIisAppPoolAsync(string name, CancellationToken ct = default)
        => await _runner.RunCheckedAsync(AppCmdPath(), $"start apppool /apppool.name:\"{EscapeArg(name)}\"", ct: ct);

    public async Task StopIisAppPoolAsync(string name, CancellationToken ct = default)
        => await _runner.RunAsync(AppCmdPath(), $"stop apppool /apppool.name:\"{EscapeArg(name)}\"", ct: ct);

    public async Task RecycleIisAppPoolAsync(string name, CancellationToken ct = default)
        => await _runner.RunAsync(AppCmdPath(), $"recycle apppool /apppool.name:\"{EscapeArg(name)}\"", ct: ct);

    public async Task DeleteIisAppPoolAsync(string name, CancellationToken ct = default)
    {
        if (name.Equals("DefaultAppPool", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("DefaultAppPool cannot be deleted from Deployment Manager.");
        var used = await _runner.RunAsync(AppCmdPath(), $"list app /apppool.name:\"{EscapeArg(name)}\" /text:name", ct: ct);
        if (!string.IsNullOrWhiteSpace(used.Output))
            throw new InvalidOperationException($"Application pool '{name}' is still used by: {used.Output.Trim()}");
        await _runner.RunAsync(AppCmdPath(), $"stop apppool /apppool.name:\"{EscapeArg(name)}\"", ct: ct);
        await _runner.RunCheckedAsync(AppCmdPath(), $"delete apppool /apppool.name:\"{EscapeArg(name)}\"", ct: ct);
    }

    public async Task StartIisSiteAsync(string siteName, CancellationToken ct = default)
    {
        var appcmd = AppCmdPath();
        await _runner.RunCheckedAsync(appcmd, $"start site /site.name:\"{EscapeArg(siteName)}\"", ct: ct);
    }

    public async Task StopIisSiteAsync(string siteName, CancellationToken ct = default)
    {
        var appcmd = AppCmdPath();
        await _runner.RunAsync(appcmd, $"stop site /site.name:\"{EscapeArg(siteName)}\"", ct: ct);
    }

    public async Task RemoveIisSiteAndFilesAsync(IisSiteInfo site, bool deletePhysicalFiles, CancellationToken ct = default)
    {
        var appcmd = AppCmdPath();
        if (!File.Exists(appcmd)) throw new InvalidOperationException("IIS appcmd.exe was not found.");
        _runner.Report($"[IIS CLEANUP] Removing site '{site.Name}'.");
        await _runner.RunAsync(appcmd, $"stop site /site.name:\"{EscapeArg(site.Name)}\"", ct: ct);
        await _runner.RunCheckedAsync(appcmd, $"delete site /site.name:\"{EscapeArg(site.Name)}\"", ct: ct);

        if (!string.IsNullOrWhiteSpace(site.AppPool) && !site.AppPool.Equals("DefaultAppPool", StringComparison.OrdinalIgnoreCase))
        {
            var remaining = await _runner.RunAsync(appcmd, $"list app /apppool.name:\"{EscapeArg(site.AppPool)}\" /text:name", ct: ct);
            if (remaining.ExitCode == 0 && string.IsNullOrWhiteSpace(remaining.Output))
            {
                _runner.Report($"[IIS CLEANUP] Removing unused application pool '{site.AppPool}'.");
                await _runner.RunAsync(appcmd, $"stop apppool /apppool.name:\"{EscapeArg(site.AppPool)}\"", ct: ct);
                await _runner.RunAsync(appcmd, $"delete apppool /apppool.name:\"{EscapeArg(site.AppPool)}\"", ct: ct);
            }
        }

        if (!deletePhysicalFiles || string.IsNullOrWhiteSpace(site.PhysicalPath)) return;
        var path = Environment.ExpandEnvironmentVariables(site.PhysicalPath.Trim().Trim('"'));
        if (!Directory.Exists(path)) { _runner.Report($"[IIS CLEANUP] Physical folder already absent: {path}"); return; }
        EnsureSafeIisCleanupPath(path);
        _runner.Report($"[IIS CLEANUP] Deleting physical site folder and files: {path}");
        Directory.Delete(path, recursive: true);
    }

    private async Task<string> ReadAppCmdTextAsync(string appcmd, string args, CancellationToken ct)
    {
        var r = await _runner.RunAsync(appcmd, args, ct: ct);
        return r.ExitCode == 0 ? r.Output.Trim() : string.Empty;
    }

    private static string AppCmdPath() => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
    private static string EscapeArg(string value) => value.Replace("\"", "\\\"");
    private static void EnsureSafeIisCleanupPath(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.IsNullOrWhiteSpace(full) || string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete a drive root as IIS cleanup.");
        var protectedPaths = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.System)
        }.Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => Path.GetFullPath(x).TrimEnd('\\')).ToArray();
        if (protectedPaths.Any(x => string.Equals(full, x, StringComparison.OrdinalIgnoreCase) || full.StartsWith(x + "\\", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException($"Refusing to delete protected system folder '{full}'.");
        if (string.Equals(full, @"C:\inetpub", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to delete C:\\inetpub itself. Delete a site folder below it instead.");
    }

    public string? GetIisSiteUsingPort(int port)
    {
        var appCmd = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "inetsrv", "appcmd.exe");
        if (!File.Exists(appCmd)) return null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = appCmd,
                Arguments = "list site",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var process = Process.Start(psi);
            if (process is null) return null;
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(4000);
            if (process.ExitCode != 0) return null;

            // Typical line: SITE "WebApplication" (id:2,bindings:http/*:8080:,state:Started)
            foreach (var line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Regex.IsMatch(line, $@"bindings:[^\r\n]*[:]{port}(?::|,)", RegexOptions.IgnoreCase)) continue;
                var match = Regex.Match(line, "SITE \\\"(?<name>[^\\\"]+)\\\"", RegexOptions.IgnoreCase);
                if (match.Success) return match.Groups["name"].Value;
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Inspect IIS port owner"); }
        return null;
    }

    public bool IsPortUsableForSite(int port, string? siteName, out string detail)
    {
        if (IsPortFree(port))
        {
            detail = "Available";
            return true;
        }

        var owner = GetIisSiteUsingPort(port);
        if (!string.IsNullOrWhiteSpace(owner) && string.Equals(owner, siteName, StringComparison.OrdinalIgnoreCase))
        {
            detail = $"In use by this IIS site ({owner}) — existing deployment can be upgraded";
            return true;
        }

        if (!string.IsNullOrWhiteSpace(owner))
        {
            detail = $"In use by IIS site '{owner}' — Deployment Manager will stop/replace that IIS site on this port";
            return true;
        }

        detail = "In use by another process/service";
        return false;
    }

    public string? DetectApiProject(string sourceFolder)
    {
        if (!Directory.Exists(sourceFolder)) return null;
        var projects = Directory.EnumerateFiles(sourceFolder, "*.csproj", SearchOption.AllDirectories).ToList();
        foreach (var project in projects)
        {
            var text = File.ReadAllText(project);
            if (text.Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)) return project;
        }
        return null;
    }

    public static string ResolveExecutable(string name, params string[] candidates)
    {
        foreach (var candidate in candidates) if (File.Exists(candidate)) return candidate;
        return name;
    }

    public static string? ReadRegistryString(RegistryHive hive, string path, string name)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var key = baseKey.OpenSubKey(path);
                var value = key?.GetValue(name)?.ToString();
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
            catch (Exception ex) { InternalDiagnostics.Record(ex, "Read registry value"); }
        }
        return null;
    }
}
