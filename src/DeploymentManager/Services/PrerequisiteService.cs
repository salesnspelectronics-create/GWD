using System.Diagnostics;

namespace GenericWeb.DeploymentManager.Services;

public sealed record InstallerDescriptor(string Key, string DisplayName, string FileName, string DirectUrl, string OfficialPageUrl);

public sealed class PrerequisiteService
{
    private readonly CommandRunner _runner;
    public PrerequisiteService(CommandRunner runner) => _runner = runner;

    public const string Sql2022ExpressUrl = "https://download.microsoft.com/download/5/1/4/5145fe04-4d30-4b85-b0d1-39533663a2f1/SQL2022-SSEI-Expr.exe";
    public const string Sql2019ExpressUrl = "https://download.microsoft.com/download/7/f/8/7f8a9c43-8c8a-4f7c-9f92-83c18d96b681/SQL2019-SSEI-Expr.exe";
    public const string Sql2022Page = "https://www.microsoft.com/en-us/download/details.aspx?id=104781";
    public const string Sql2019Page = "https://www.microsoft.com/en-us/download/details.aspx?id=101064";
    public const string NodeLtsMsiUrl = "https://nodejs.org/dist/v22.23.2/node-v22.23.2-x64.msi";
    public const string NodeDownloadPage = "https://nodejs.org/en/download";

    public InstallerDescriptor HostingBundle(int major) => new(
        "hosting", $"ASP.NET Core {major} Hosting Bundle", $"dotnet-hosting-{major}.exe",
        $"https://aka.ms/dotnet/{major}.0/dotnet-hosting-win.exe",
        $"https://dotnet.microsoft.com/en-us/download/dotnet/{major}.0");

    public InstallerDescriptor DotNetSdk(int major) => new(
        "sdk", $".NET {major} SDK x64", $"dotnet-sdk-{major}-x64.exe",
        $"https://aka.ms/dotnet/{major}.0/dotnet-sdk-win-x64.exe",
        $"https://dotnet.microsoft.com/en-us/download/dotnet/{major}.0");

    public InstallerDescriptor Node() => new(
        "node", "Node.js 22 LTS x64", "node-v22.23.2-x64.msi", NodeLtsMsiUrl, NodeDownloadPage);

    public InstallerDescriptor Sql(string version) => version.Contains("2019", StringComparison.OrdinalIgnoreCase)
        ? new("sql", "SQL Server 2019 Express", "SQL2019-SSEI-Expr.exe", Sql2019ExpressUrl, Sql2019Page)
        : new("sql", "SQL Server 2022 Express", "SQL2022-SSEI-Expr.exe", Sql2022ExpressUrl, Sql2022Page);

    public string? FindCached(string cacheFolder, InstallerDescriptor package)
    {
        var exact = Path.Combine(cacheFolder, package.FileName);
        if (File.Exists(exact)) return exact;
        if (!Directory.Exists(cacheFolder)) return null;
        var stem = Path.GetFileNameWithoutExtension(package.FileName);
        return Directory.EnumerateFiles(cacheFolder, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(x => Path.GetFileNameWithoutExtension(x).Contains(stem, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<string> DownloadAsync(InstallerDescriptor package, string cacheFolder, IProgress<int>? progress = null, CancellationToken ct = default)
    {
        Directory.CreateDirectory(cacheFolder);
        var destination = Path.Combine(cacheFolder, package.FileName);
        using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        using var response = await http.GetAsync(package.DirectUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        _runner.Report(total is > 0
            ? $"[DOWNLOAD] {package.DisplayName}: server reports {total.Value / 1024d / 1024d:0.0} MB."
            : $"[DOWNLOAD] {package.DisplayName}: download started; total size is not reported by the server.");
        await using var input = await response.Content.ReadAsStreamAsync(ct);
        await using var output = File.Create(destination);
        var buffer = new byte[1024 * 128];
        long done = 0;
        var lastLoggedPercent = -5;
        var lastLoggedUtc = DateTime.UtcNow;
        while (true)
        {
            var read = await input.ReadAsync(buffer, ct);
            if (read <= 0) break;
            await output.WriteAsync(buffer.AsMemory(0, read), ct);
            done += read;
            var percent = total is > 0 ? (int)Math.Clamp(done * 100 / total.Value, 0, 100) : -1;
            if (percent >= 0) progress?.Report(percent);
            var now = DateTime.UtcNow;
            if ((percent >= 0 && percent >= lastLoggedPercent + 5) || (now - lastLoggedUtc) >= TimeSpan.FromSeconds(10))
            {
                var size = done / 1024d / 1024d;
                var detail = percent >= 0 ? $"{percent}% — {size:0.0} MB" : $"{size:0.0} MB received";
                _runner.Report($"[DOWNLOAD] {package.DisplayName}: {detail}");
                if (percent >= 0) lastLoggedPercent = percent;
                lastLoggedUtc = now;
            }
        }
        progress?.Report(100);
        _runner.Report($"[DOWNLOAD] {package.DisplayName}: completed — {done / 1024d / 1024d:0.0} MB saved to {destination}");
        return destination;
    }

    public void OpenOfficialPage(string url)
    {
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    public async Task InstallHostingBundleAsync(string installerPath, CancellationToken ct = default)
    {
        EnsureInstaller(installerPath);
        await _runner.RunCheckedAsync(installerPath, "/install /quiet /norestart", ct: ct);
        await _runner.RunAsync("iisreset.exe", "/restart", ct: ct);
    }

    public async Task InstallDotNetSdkAsync(string installerPath, CancellationToken ct = default)
    {
        EnsureInstaller(installerPath);
        await _runner.RunCheckedAsync(installerPath, "/install /quiet /norestart", ct: ct);
    }

    public async Task InstallNodeAsync(string installerPath, CancellationToken ct = default)
    {
        EnsureInstaller(installerPath);
        await _runner.RunCheckedAsync("msiexec.exe", $"/i \"{installerPath}\" /qn /norestart", ct: ct);
    }

    private static void EnsureInstaller(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Installer file was not found. Download it or choose a local installer first.", path);
    }
}
