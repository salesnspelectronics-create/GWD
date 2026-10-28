using Microsoft.Win32;
using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using GenericWeb.DeploymentManager.Models;

namespace GenericWeb.DeploymentManager.Services;

public enum BrowseContext
{
    General,
    Source,
    Publish,
    Installer,
    Repository,
    Workspace,
    Hosting,
    ReleaseOutput
}

public sealed class AppDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public string Root { get; } = ResolveRoot();
    public string Logs => Path.Combine(Root, "Logs");
    public string Cache => Path.Combine(Root, "Cache");
    public string NpmCache => Path.Combine(Cache, "npm");
    public string StateFile => Path.Combine(Root, "deployment-state.json");
    public string SettingsFile => Path.Combine(Root, "settings.json");
    public string ErrorReports => Path.Combine(Root, "ErrorReports");
    public string CrashReports => Path.Combine(Root, "CrashReports");
    public string Projects => Path.Combine(Root, "Projects");
    public string InternalDiagnosticsFolder => Path.Combine(Root, "InternalDiagnostics");
    private const string RegistryPath = @"Software\NSP\DeploymentManager";

    public AppDataService()
    {
        Directory.CreateDirectory(Root);
        InternalDiagnostics.Configure(Root);
        EnsureDirectory(Logs, nameof(Logs));
        EnsureDirectory(Cache, nameof(Cache));
        EnsureDirectory(NpmCache, nameof(NpmCache));
        EnsureDirectory(ErrorReports, nameof(ErrorReports));
        EnsureDirectory(CrashReports, nameof(CrashReports));
        EnsureDirectory(Projects, nameof(Projects));
        EnsureDirectory(InternalDiagnosticsFolder, nameof(InternalDiagnosticsFolder));
    }

    public DeploymentManagerSettings GetSettings() => LoadSettings();

    public void SaveSettings(DeploymentManagerSettings settings) => SaveSettingsAtomic(settings);

    public string GetInstallerCacheFolder()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            var registryValue = key?.GetValue("InstallerCacheFolder") as string;
            if (!string.IsNullOrWhiteSpace(registryValue)) return registryValue;
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Read InstallerCacheFolder registry"); }

        var settings = LoadSettings();
        return !string.IsNullOrWhiteSpace(settings.InstallerCacheFolder) ? settings.InstallerCacheFolder : GetDefaultRepositoryFolder();
    }

    public void SaveInstallerCacheFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            key.SetValue("InstallerCacheFolder", folder, RegistryValueKind.String);
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Write InstallerCacheFolder registry"); }

        var settings = LoadSettings();
        settings.InstallerCacheFolder = folder;
        settings.DeploymentRepositoryFolder = folder;
        SaveSettingsAtomic(settings);
    }

    public string GetDeploymentRepositoryFolder()
    {
        var settings = LoadSettings();
        return !string.IsNullOrWhiteSpace(settings.DeploymentRepositoryFolder) ? settings.DeploymentRepositoryFolder : GetInstallerCacheFolder();
    }

    public void SaveDeploymentRepositoryFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        SaveInstallerCacheFolder(folder);
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var relative in new[] { "NuGet\\packages", "NuGet\\feed", "DotNet", "Node\\npm-cache", "Tools", "Installers", "Cache", "Scripts", "Config", "Projects", "BuildState", "Workspaces" })
                Directory.CreateDirectory(Path.Combine(folder, relative));
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Create deployment repository structure"); }
    }

    public string GetWorkspaceRoot()
    {
        var settings = LoadSettings();
        return !string.IsNullOrWhiteSpace(settings.WorkspaceRoot) ? settings.WorkspaceRoot : Path.Combine(GetDeploymentRepositoryFolder(), "Workspaces");
    }

    public void SaveWorkspaceRoot(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        try { Directory.CreateDirectory(folder); }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Create workspace root"); }
        var settings = LoadSettings();
        settings.WorkspaceRoot = folder;
        SaveSettingsAtomic(settings);
    }

    public string GetManagerReleaseRoot()
    {
        var settings = LoadSettings();
        if (!string.IsNullOrWhiteSpace(settings.ManagerReleaseRoot)) return settings.ManagerReleaseRoot;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "GenericWebDeploymentManager", "Releases");
    }

    public void SaveManagerReleaseRoot(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        var settings = LoadSettings();
        settings.ManagerReleaseRoot = Path.GetFullPath(folder);
        SaveSettingsAtomic(settings);
    }

    public string GetDefaultProjectFolder()
    {
        var settings = LoadSettings();
        return ResolveExistingFolder(settings.DefaultProjectFolder);
    }

    public void SaveDefaultProjectFolder(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder)) return;
        var settings = LoadSettings();
        settings.DefaultProjectFolder = Path.GetFullPath(folder);
        SaveSettingsAtomic(settings);
    }

    public string GetLastBrowseFolder(BrowseContext context = BrowseContext.General)
    {
        var settings = LoadSettings();
        var candidate = context switch
        {
            BrowseContext.Source => settings.LastSourceBrowseFolder,
            BrowseContext.Publish => settings.LastPublishBrowseFolder,
            BrowseContext.Installer => settings.LastInstallerBrowseFolder,
            BrowseContext.Repository => settings.LastRepositoryBrowseFolder,
            BrowseContext.Workspace => settings.LastWorkspaceBrowseFolder,
            BrowseContext.Hosting => settings.LastHostingBrowseFolder,
            BrowseContext.ReleaseOutput => settings.LastReleaseOutputBrowseFolder,
            _ => settings.LastBrowseFolder
        };

        var resolved = ResolveExistingFolder(candidate);
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;

        resolved = ResolveExistingFolder(settings.LastBrowseFolder);
        if (!string.IsNullOrWhiteSpace(resolved)) return resolved;

        if (context == BrowseContext.Source)
        {
            // Source selection is driven only by actual last-used source history/state.
            // Legacy DefaultProjectFolder is intentionally ignored from V55 onward.
            resolved = ResolveExistingFolder(LoadState().LastSourceFolder);
            if (!string.IsNullOrWhiteSpace(resolved)) return resolved;
        }

        return string.Empty;
    }

    public void SaveLastBrowseFolder(string selectedPath, BrowseContext context = BrowseContext.General)
    {
        if (string.IsNullOrWhiteSpace(selectedPath)) return;
        try
        {
            var folder = Directory.Exists(selectedPath) ? selectedPath : Path.GetDirectoryName(selectedPath);
            if (string.IsNullOrWhiteSpace(folder)) return;
            folder = Path.GetFullPath(folder);
            var settings = LoadSettings();
            settings.LastBrowseFolder = folder;
            switch (context)
            {
                case BrowseContext.Source: settings.LastSourceBrowseFolder = folder; break;
                case BrowseContext.Publish: settings.LastPublishBrowseFolder = folder; break;
                case BrowseContext.Installer: settings.LastInstallerBrowseFolder = folder; break;
                case BrowseContext.Repository: settings.LastRepositoryBrowseFolder = folder; break;
                case BrowseContext.Workspace: settings.LastWorkspaceBrowseFolder = folder; break;
                case BrowseContext.Hosting: settings.LastHostingBrowseFolder = folder; break;
                case BrowseContext.ReleaseOutput: settings.LastReleaseOutputBrowseFolder = folder; break;
            }
            SaveSettingsAtomic(settings);
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, $"Save browse folder ({context})"); }
    }

    public static string ResolveExistingFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        try
        {
            var candidate = path.Trim().Trim('"');
            if (File.Exists(candidate)) candidate = Path.GetDirectoryName(candidate) ?? string.Empty;
            while (!string.IsNullOrWhiteSpace(candidate))
            {
                if (Directory.Exists(candidate)) return candidate;
                var root = Path.GetPathRoot(candidate);
                var parent = Path.GetDirectoryName(candidate.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (string.IsNullOrWhiteSpace(parent) || string.Equals(parent, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    if (!string.IsNullOrWhiteSpace(root) && Directory.Exists(root)) return root;
                    break;
                }
                candidate = parent;
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Resolve browse fallback folder"); }
        return string.Empty;
    }

    public (string Host, string UserName, string Password, bool Remember) GetRemoteCredential()
    {
        try
        {
            var settings = LoadSettings();
            if (!settings.RememberRemoteCredential || string.IsNullOrWhiteSpace(settings.RemotePasswordProtected))
                return (settings.RemoteHost, settings.RemoteUserName, "", false);
            var protectedBytes = Convert.FromBase64String(settings.RemotePasswordProtected);
            var password = Encoding.UTF8.GetString(ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser));
            return (settings.RemoteHost, settings.RemoteUserName, password, true);
        }
        catch (Exception ex)
        {
            InternalDiagnostics.Record(ex, "Read remote credential");
            return ("", "", "", false);
        }
    }

    public void SaveRemoteCredential(string host, string userName, string password, bool remember)
    {
        try
        {
            var settings = LoadSettings();
            settings.RemoteHost = remember ? host : "";
            settings.RemoteUserName = remember ? userName : "";
            settings.RememberRemoteCredential = remember;
            settings.RemotePasswordProtected = remember
                ? Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(password), null, DataProtectionScope.CurrentUser))
                : "";
            SaveSettingsAtomic(settings);
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Save remote credential"); }
    }

    public string GetDependencyResolutionMode()
    {
        var settings = LoadSettings();
        return !string.IsNullOrWhiteSpace(settings.DependencyResolutionMode) ? settings.DependencyResolutionMode : "Offline Only";
    }

    public void SaveDependencyResolutionMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return;
        var settings = LoadSettings();
        settings.DependencyResolutionMode = mode;
        SaveSettingsAtomic(settings);
    }

    private static string GetDefaultRepositoryFolder()
    {
        try { if (Directory.Exists(@"D:\")) return @"D:\Developer-Packages"; }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Resolve default repository root"); }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GenericWebDeploymentManager", "Repository");
    }

    public (string HostingRoot, string DeploymentLayout, bool CleanupPreviousDeployments, bool UseDefaultIisWebsite) GetHostingSettings()
    {
        var defaultRoot = @"C:\inetpub\wwwroot\NSPOfficeAutomation";
        var defaultLayout = "Single Folder";
        var cleanup = true;
        var useDefaultIisWebsite = false;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            var root = key?.GetValue("HostingRoot") as string;
            var layout = key?.GetValue("DeploymentLayout") as string;
            var cleanupValue = key?.GetValue("CleanupPreviousDeployments");
            var defaultSiteValue = key?.GetValue("UseDefaultIisWebsite");
            if (!string.IsNullOrWhiteSpace(root)) defaultRoot = root;
            if (!string.IsNullOrWhiteSpace(layout)) defaultLayout = layout;
            if (cleanupValue is int i) cleanup = i != 0;
            if (defaultSiteValue is int d) useDefaultIisWebsite = d != 0;
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Read hosting settings registry"); }
        return (defaultRoot, defaultLayout, cleanup, useDefaultIisWebsite);
    }

    public void SaveHostingSettings(string hostingRoot, string deploymentLayout, bool cleanupPreviousDeployments, bool useDefaultIisWebsite = false)
    {
        if (string.IsNullOrWhiteSpace(hostingRoot)) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            key.SetValue("HostingRoot", hostingRoot, RegistryValueKind.String);
            key.SetValue("DeploymentLayout", deploymentLayout, RegistryValueKind.String);
            key.SetValue("CleanupPreviousDeployments", cleanupPreviousDeployments ? 1 : 0, RegistryValueKind.DWord);
            key.SetValue("UseDefaultIisWebsite", useDefaultIisWebsite ? 1 : 0, RegistryValueKind.DWord);
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Write hosting settings registry"); }
    }

    public (string ApplicationName, string SiteName, string DatabaseName) GetDeploymentIdentitySettings()
    {
        var app = "NSP Office Automation";
        var site = "NSPOfficeAutomation";
        var db = "NSPOfficeAutomation";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
            app = key?.GetValue("ApplicationName") as string ?? app;
            site = key?.GetValue("SiteName") as string ?? site;
            db = key?.GetValue("DatabaseName") as string ?? db;
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Read deployment identity registry"); }
        return (app, site, db);
    }

    public void SaveDeploymentIdentitySettings(string applicationName, string siteName, string databaseName)
    {
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true);
            key.SetValue("ApplicationName", applicationName, RegistryValueKind.String);
            key.SetValue("SiteName", siteName, RegistryValueKind.String);
            key.SetValue("DatabaseName", databaseName, RegistryValueKind.String);
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Write deployment identity registry"); }
    }

    public string SaveErrorReport(string content)
    {
        Directory.CreateDirectory(ErrorReports);
        var path = Path.Combine(ErrorReports, $"Deployment-Error-{DateTime.Now:yyyyMMdd-HHmmss}.txt");
        File.WriteAllText(path, SecretRedactor.Redact(content));
        return path;
    }

    public void SaveState(DeploymentState state) => WriteJsonAtomic(StateFile, state);

    public DeploymentState LoadState()
    {
        try { return File.Exists(StateFile) ? JsonSerializer.Deserialize<DeploymentState>(File.ReadAllText(StateFile)) ?? new() : new(); }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Load deployment state"); return new(); }
    }

    public void SaveProjectProfile(DeploymentProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ApplicationName)) return;
        var safe = CloneWithoutSecrets(profile);
        var path = Path.Combine(Projects, SafeProjectFileName(profile.ApplicationName) + ".json");
        WriteJsonAtomic(path, safe);
    }

    public DeploymentProfile? LoadProjectProfile(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName)) return null;
        var direct = Path.Combine(Projects, SafeProjectFileName(projectName) + ".json");
        try
        {
            if (File.Exists(direct)) return JsonSerializer.Deserialize<DeploymentProfile>(File.ReadAllText(direct));
            foreach (var path in Directory.EnumerateFiles(Projects, "*.json"))
            {
                var profile = JsonSerializer.Deserialize<DeploymentProfile>(File.ReadAllText(path));
                if (string.Equals(profile?.ApplicationName, projectName, StringComparison.OrdinalIgnoreCase)) return profile;
            }
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Load project profile"); }
        return null;
    }

    public IReadOnlyList<DeploymentProfile> ListProjectProfiles()
    {
        var result = new List<DeploymentProfile>();
        if (!Directory.Exists(Projects)) return result;
        foreach (var path in Directory.EnumerateFiles(Projects, "*.json"))
        {
            try
            {
                var profile = JsonSerializer.Deserialize<DeploymentProfile>(File.ReadAllText(path));
                if (profile is not null && !string.IsNullOrWhiteSpace(profile.ApplicationName)) result.Add(profile);
            }
            catch (Exception ex) { InternalDiagnostics.Record(ex, "Read project profile list item: " + path); }
        }
        return result.OrderBy(x => x.ApplicationName, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public DeploymentProfile CreateLegacyProjectProfile(string projectName)
    {
        var state = LoadState();
        var identity = GetDeploymentIdentitySettings();
        var hosting = GetHostingSettings();
        var source = state.LastSourceFolder;
        var api = "";
        try
        {
            if (Directory.Exists(source))
                api = Directory.EnumerateFiles(source, "*.csproj", SearchOption.AllDirectories)
                    .FirstOrDefault(path => File.ReadAllText(path).Contains("Microsoft.NET.Sdk.Web", StringComparison.OrdinalIgnoreCase)) ?? "";
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Create legacy project profile"); }
        return new DeploymentProfile
        {
            ApplicationName = string.IsNullOrWhiteSpace(projectName) ? identity.ApplicationName : projectName,
            SiteName = identity.SiteName,
            AppPoolName = identity.SiteName,
            DatabaseName = identity.DatabaseName,
            SourceFolder = source,
            ApiProjectPath = api,
            HostingRoot = hosting.HostingRoot,
            DeploymentLayout = hosting.DeploymentLayout,
            CleanupPreviousDeployments = hosting.CleanupPreviousDeployments,
            UseDefaultIisWebsite = hosting.UseDefaultIisWebsite,
            DeploymentRepositoryFolder = GetDeploymentRepositoryFolder(),
            InstallerCacheFolder = GetInstallerCacheFolder(),
            WorkspaceRoot = GetWorkspaceRoot(),
            DependencyResolutionMode = GetDependencyResolutionMode(),
            OfflineBuild = string.Equals(GetDependencyResolutionMode(), "Offline Only", StringComparison.OrdinalIgnoreCase),
            HttpPort = hosting.UseDefaultIisWebsite ? 80 : 8080,
            HostHeader = "",
            HealthPath = "/api/v1/health",
            BackupRoot = @"D:\NSPBackups"
        };
    }

    private DeploymentManagerSettings LoadSettings()
    {
        try
        {
            return File.Exists(SettingsFile)
                ? JsonSerializer.Deserialize<DeploymentManagerSettings>(File.ReadAllText(SettingsFile)) ?? new()
                : new();
        }
        catch (Exception ex)
        {
            InternalDiagnostics.Record(ex, "Load settings.json");
            return new();
        }
    }

    private void SaveSettingsAtomic(DeploymentManagerSettings settings) => WriteJsonAtomic(SettingsFile, settings);

    private static void WriteJsonAtomic<T>(string path, T value)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptions));
            File.Move(temp, path, true);
        }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Atomic JSON save: " + path); throw; }
    }

    private static void EnsureDirectory(string path, string context)
    {
        try { Directory.CreateDirectory(path); }
        catch (Exception ex) { InternalDiagnostics.Record(ex, "Create directory: " + context); }
    }

    private static DeploymentProfile CloneWithoutSecrets(DeploymentProfile profile)
    {
        var clone = JsonSerializer.Deserialize<DeploymentProfile>(JsonSerializer.Serialize(profile)) ?? new DeploymentProfile();
        clone.RemotePassword = "";
        clone.SqlPassword = "";
        return clone;
    }

    private static string SafeProjectFileName(string value)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) value = value.Replace(c, '-');
        return string.IsNullOrWhiteSpace(value) ? "project" : value.Trim();
    }

    private static string ResolveRoot()
    {
        var overridden = Environment.GetEnvironmentVariable("GWDM_APPDATA_ROOT");
        return string.IsNullOrWhiteSpace(overridden)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "NSP", "DeploymentManager")
            : Path.GetFullPath(overridden);
    }
}

public sealed class DeploymentState
{
    public string LastSourceFolder { get; set; } = "";
    public string LastSiteName { get; set; } = "";
    public string LastStage { get; set; } = "Welcome";
    public string LastResult { get; set; } = "NotStarted";
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
}

public sealed class DeploymentManagerSettings
{
    public string InstallerCacheFolder { get; set; } = string.Empty;
    public string DeploymentRepositoryFolder { get; set; } = string.Empty;
    public string WorkspaceRoot { get; set; } = string.Empty;
    public string ManagerReleaseRoot { get; set; } = string.Empty;
    public string DefaultProjectFolder { get; set; } = string.Empty;
    public string DependencyResolutionMode { get; set; } = "Offline Only";
    public string RemoteHost { get; set; } = string.Empty;
    public string RemoteUserName { get; set; } = string.Empty;
    public string RemotePasswordProtected { get; set; } = string.Empty;
    public bool RememberRemoteCredential { get; set; }
    public string LastBrowseFolder { get; set; } = string.Empty;
    public string LastSourceBrowseFolder { get; set; } = string.Empty;
    public string LastPublishBrowseFolder { get; set; } = string.Empty;
    public string LastInstallerBrowseFolder { get; set; } = string.Empty;
    public string LastRepositoryBrowseFolder { get; set; } = string.Empty;
    public string LastWorkspaceBrowseFolder { get; set; } = string.Empty;
    public string LastHostingBrowseFolder { get; set; } = string.Empty;
    public string LastReleaseOutputBrowseFolder { get; set; } = string.Empty;
}
