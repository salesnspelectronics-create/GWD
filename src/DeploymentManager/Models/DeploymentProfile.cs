using System.Text.Json.Serialization;
using GenericWeb.DeploymentManager.Services;
namespace GenericWeb.DeploymentManager.Models;

public sealed class DeploymentProfile
{
    public string ApplicationName { get; set; } = "NSP Office Automation";
    public string TargetHost { get; set; } = Environment.MachineName;
    public string TargetMode { get; set; } = "Local Server";
    public int RemoteWinRmPort { get; set; } = 5985;
    public bool RemoteUseSsl { get; set; } = false;
    public string RemoteUserName { get; set; } = string.Empty;
    [JsonIgnore]
    public string RemotePassword { get => _remotePassword; set { _remotePassword = value ?? ""; SecretRedactor.Register(_remotePassword); } }
    private string _remotePassword = ""; // session-only; never persist
    public string SourceFolder { get; set; } = string.Empty;
    public string ApiProjectPath { get; set; } = string.Empty;
    public int TargetFrameworkMajor { get; set; } = 9;
    public bool RequiresNode { get; set; } = true;
    // Compatible (default): missing app-owned UI version is reported as unverified.
    // Strict: require an app-owned version file and an exact source-version match.
    public string UiVersionContractMode { get; set; } = "Compatible";
    public string ApiVersionContractMode { get; set; } = "Compatible";
    // Off by default: deployment does not require Git. Set to Strict to enforce repository provenance.
    public string GitSourceVerificationMode { get; set; } = "Off";
    public string BuildMode { get; set; } = "Fast Local Test";
    public bool OfflineBuild { get; set; } = true;
    public string DependencyResolutionMode { get; set; } = "Offline Only";
    public string ReadyPublishFolder { get; set; } = string.Empty;
    public bool SourceTestProjectStructure { get; set; } = true;
    public bool SourceTestTargetFramework { get; set; } = true;
    public bool SourceTestNodeAssets { get; set; } = true;
    public bool SourceTestWritable { get; set; } = true;

    public string InstallerCacheFolder { get; set; } = DefaultRepositoryRoot();
    public string DeploymentRepositoryFolder { get; set; } = DefaultRepositoryRoot();
    public string WorkspaceRoot { get; set; } = Path.Combine(DefaultRepositoryRoot(), "Workspaces");
    public string HostingBundleInstallerPath { get; set; } = string.Empty;
    public string DotNetSdkInstallerPath { get; set; } = string.Empty;
    public string NodeInstallerPath { get; set; } = string.Empty;
    public string SqlInstallerPath { get; set; } = string.Empty;

    public string DeploymentRoot { get; set; } = @"C:\WebDeployments"; // persistent Data/Logs/backup root
    public string BackupRoot { get; set; } = @"D:\NSPBackups";
    public string[] PreserveProductionFiles { get; set; } = ["appsettings.json", "appsettings.Production.json"];
    public string HostingRoot { get; set; } = @"C:\inetpub\wwwroot\NSPOfficeAutomation";
    public string DeploymentLayout { get; set; } = "Single Folder";
    public bool CleanupPreviousDeployments { get; set; } = true;
    public bool UseDefaultIisWebsite { get; set; } = false;
    public string SiteName { get; set; } = "NSPOfficeAutomation";
    public string AppPoolName { get; set; } = "NSPOfficeAutomation";
    public int HttpPort { get; set; } = 8080;
    public string HostHeader { get; set; } = string.Empty;
    public string HealthPath { get; set; } = "/api/v1/health";

    public string SqlInstance { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "NSPOfficeAutomation";
    public bool UseWindowsAuthentication { get; set; } = true;
    public string SqlUserName { get; set; } = "sa";
    [JsonIgnore]
    public string SqlPassword { get => _sqlPassword; set { if (_sqlPassword != (value ?? "")) SqlCredentialConfigured = !string.IsNullOrEmpty(value); _sqlPassword = value ?? ""; SecretRedactor.Register(_sqlPassword); } }
    private string _sqlPassword = "";
    [JsonIgnore]
    public bool SqlCredentialConfigured { get; set; } // session-only suggestion/user input; never persist
    private static string DefaultRepositoryRoot()
    {
        try { if (Directory.Exists(@"D:\")) return @"D:\Developer-Packages"; } catch (Exception ex) { GenericWeb.DeploymentManager.Services.InternalDiagnostics.Record(ex, "Resolve default repository root from DeploymentProfile"); }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "GenericWebDeploymentManager", "Repository");
    }

    public string SqlExpressVersion { get; set; } = "SQL Server 2019 Express";
}

public sealed record SqlInstanceInfo(string ServerName, string Edition, string ProductVersion)
{
    public override string ToString() => string.IsNullOrWhiteSpace(Edition)
        ? ServerName
        : $"{ServerName} — {Edition} — {ProductVersion}";
}

public sealed record PrerequisiteState(bool Installed, string Detail);
