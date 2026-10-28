using GenericWeb.DeploymentManager.Models;
using System.Text;

namespace GenericWeb.DeploymentManager.Services;

public sealed record RemoteReadiness(bool Connected, bool IsAdministrator, bool IisInstalled, bool HostingBundleInstalled, string Detail);

public sealed class RemoteDeploymentService
{
    private readonly CommandRunner _runner;
    private readonly PrerequisiteService _prereq;

    public RemoteDeploymentService(CommandRunner runner, PrerequisiteService prereq)
    {
        _runner = runner;
        _prereq = prereq;
    }

    public async Task<RemoteReadiness> CheckReadinessAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        EnsureRemoteProfile(profile);

        var script = """
$ErrorActionPreference='Stop'
$sec=ConvertTo-SecureString $env:GWDM_REMOTE_PASSWORD -AsPlainText -Force
$cred=[pscredential]::new($env:GWDM_REMOTE_USER,$sec)
$opt=New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck
$useSsl=($env:GWDM_REMOTE_SSL -eq '1')
$s=New-PSSession -ComputerName $env:GWDM_REMOTE_HOST -Port ([int]$env:GWDM_REMOTE_PORT) -Credential $cred -UseSSL:$useSsl -SessionOption $opt
try {
  Invoke-Command -Session $s -ScriptBlock {
    $admin=([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    $iis=((Get-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole -ErrorAction SilentlyContinue).State -eq 'Enabled')
    $hosting=Test-Path 'C:\Program Files\IIS\Asp.Net Core Module\V2\aspnetcorev2.dll'
    [pscustomobject]@{Administrator=$admin;IIS=$iis;Hosting=$hosting;Computer=$env:COMPUTERNAME} | ConvertTo-Json -Compress
  }
} finally {
  Remove-PSSession $s -ErrorAction SilentlyContinue
}
""";

        var result = await RunPowerShellAsync(profile, script, ct);
        if (result.ExitCode != 0)
            return new(false, false, false, false, result.Error + result.Output);

        var text = result.Output;
        var admin = text.Contains("\"Administrator\":true", StringComparison.OrdinalIgnoreCase);
        var iis = text.Contains("\"IIS\":true", StringComparison.OrdinalIgnoreCase);
        var hosting = text.Contains("\"Hosting\":true", StringComparison.OrdinalIgnoreCase);
        return new(true, admin, iis, hosting, $"Remote connected. Administrator={admin}; IIS={iis}; ASP.NET Hosting Bundle={hosting}.");
    }

    public async Task BootstrapAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        var readiness = await CheckReadinessAsync(profile, ct);
        if (!readiness.Connected)
            throw new InvalidOperationException("Remote connection failed. " + readiness.Detail);
        if (!readiness.IsAdministrator)
            throw new InvalidOperationException("Remote credential must have local Administrator rights.");

        var installerCache = Path.Combine(profile.DeploymentRepositoryFolder, "Installers");
        Directory.CreateDirectory(installerCache);

        var installers = new List<(InstallerDescriptor Package, bool Needed)>
        {
            (_prereq.HostingBundle(profile.TargetFrameworkMajor), !readiness.HostingBundleInstalled)
        };

        foreach (var item in installers.Where(x => x.Needed))
        {
            var local = _prereq.FindCached(installerCache, item.Package)
                ?? await _prereq.DownloadAsync(item.Package, installerCache, ct: ct);

            await CopyInstallerAndExecuteAsync(
                profile,
                local,
                item.Package.Key == "hosting" ? "/install /quiet /norestart" : "/quiet /norestart",
                ct);
        }

        if (!readiness.IisInstalled)
        {
            var script = """
$ErrorActionPreference='Stop'
$sec=ConvertTo-SecureString $env:GWDM_REMOTE_PASSWORD -AsPlainText -Force
$cred=[pscredential]::new($env:GWDM_REMOTE_USER,$sec)
$opt=New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck
$s=New-PSSession -ComputerName $env:GWDM_REMOTE_HOST -Port ([int]$env:GWDM_REMOTE_PORT) -Credential $cred -UseSSL:($env:GWDM_REMOTE_SSL -eq '1') -SessionOption $opt
try {
  Invoke-Command -Session $s -ScriptBlock {
    Enable-WindowsOptionalFeature -Online -FeatureName IIS-WebServerRole,IIS-WebServer,IIS-ManagementConsole -All -NoRestart | Out-Null
  }
} finally {
  Remove-PSSession $s -ErrorAction SilentlyContinue
}
""";

            var result = await RunPowerShellAsync(profile, script, ct);
            if (result.ExitCode != 0)
                throw new InvalidOperationException("Remote IIS enable failed. " + result.Error + result.Output);
        }
    }

    public async Task DeployPublishAsync(DeploymentProfile profile, string localPublish, CancellationToken ct = default)
    {
        EnsureRemoteProfile(profile);
        var remoteStage = $"C:\\ProgramData\\GenericWebDeploymentManager\\Staging\\{Guid.NewGuid():N}";

        var script = $$"""
$ErrorActionPreference='Stop'
$sec=ConvertTo-SecureString $env:GWDM_REMOTE_PASSWORD -AsPlainText -Force
$cred=[pscredential]::new($env:GWDM_REMOTE_USER,$sec)
$opt=New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck
$s=New-PSSession -ComputerName $env:GWDM_REMOTE_HOST -Port ([int]$env:GWDM_REMOTE_PORT) -Credential $cred -UseSSL:($env:GWDM_REMOTE_SSL -eq '1') -SessionOption $opt
try {
  Invoke-Command -Session $s -ScriptBlock { param($p) New-Item -ItemType Directory -Force -Path $p | Out-Null } -ArgumentList '{{Ps(remoteStage)}}'
  Copy-Item -Path '{{Ps(Path.Combine(localPublish, "*"))}}' -Destination '{{Ps(remoteStage)}}' -ToSession $s -Recurse -Force
  Invoke-Command -Session $s -ScriptBlock {
    param($stage,$target,$site,$pool,$port)
    Import-Module WebAdministration
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $backup=$target + '.backup-' + (Get-Date -Format 'yyyyMMdd-HHmmss')
    if(Test-Path $target) { Copy-Item $target $backup -Recurse -Force }
    if(Test-Path "IIS:\Sites\$site") { Stop-Website $site -ErrorAction SilentlyContinue }
    if(Test-Path "IIS:\AppPools\$pool") {
      Stop-WebAppPool $pool -ErrorAction SilentlyContinue
    } else {
      New-WebAppPool $pool | Out-Null
    }
    Remove-Item (Join-Path $target '*') -Recurse -Force -ErrorAction SilentlyContinue
    Copy-Item (Join-Path $stage '*') $target -Recurse -Force
    if(Test-Path "IIS:\Sites\$site") {
      Set-ItemProperty "IIS:\Sites\$site" -Name physicalPath -Value $target
    } else {
      New-Website -Name $site -PhysicalPath $target -Port $port -ApplicationPool $pool | Out-Null
    }
    Start-WebAppPool $pool
    Start-Website $site
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
  } -ArgumentList '{{Ps(remoteStage)}}','{{Ps(profile.HostingRoot)}}','{{Ps(profile.SiteName)}}','{{Ps(profile.AppPoolName)}}',{{profile.HttpPort}}
} finally {
  Remove-PSSession $s -ErrorAction SilentlyContinue
}
""";

        var result = await RunPowerShellAsync(profile, script, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Remote IIS deployment failed. " + result.Error + result.Output);
    }

    public async Task<(bool Ok, string Detail)> HealthCheckAsync(DeploymentProfile profile, CancellationToken ct = default)
    {
        var host = string.IsNullOrWhiteSpace(profile.HostHeader) ? profile.TargetHost : profile.HostHeader;
        var url = profile.HttpPort == 80
            ? $"http://{host}{profile.HealthPath}"
            : $"http://{host}:{profile.HttpPort}{profile.HealthPath}";

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            using var response = await http.GetAsync(url, ct);
            return (response.IsSuccessStatusCode, $"{url} => {(int)response.StatusCode} {response.ReasonPhrase}");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return (false, url + " => " + ex.Message);
        }
    }

    private async Task CopyInstallerAndExecuteAsync(DeploymentProfile profile, string localInstaller, string arguments, CancellationToken ct)
    {
        var remote = "C:\\ProgramData\\GenericWebDeploymentManager\\Installers\\" + Path.GetFileName(localInstaller);

        var script = $$"""
$ErrorActionPreference='Stop'
$sec=ConvertTo-SecureString $env:GWDM_REMOTE_PASSWORD -AsPlainText -Force
$cred=[pscredential]::new($env:GWDM_REMOTE_USER,$sec)
$opt=New-PSSessionOption -SkipCACheck -SkipCNCheck -SkipRevocationCheck
$s=New-PSSession -ComputerName $env:GWDM_REMOTE_HOST -Port ([int]$env:GWDM_REMOTE_PORT) -Credential $cred -UseSSL:($env:GWDM_REMOTE_SSL -eq '1') -SessionOption $opt
try {
  Invoke-Command -Session $s -ScriptBlock {
    New-Item -ItemType Directory -Force -Path 'C:\ProgramData\GenericWebDeploymentManager\Installers' | Out-Null
  }
  Copy-Item '{{Ps(localInstaller)}}' '{{Ps(remote)}}' -ToSession $s -Force
  Invoke-Command -Session $s -ScriptBlock {
    param($p,$a)
    $x=Start-Process -FilePath $p -ArgumentList $a -Wait -PassThru
    if($x.ExitCode -notin 0,3010,1641) { throw "Installer exit code $($x.ExitCode)" }
  } -ArgumentList '{{Ps(remote)}}','{{Ps(arguments)}}'
} finally {
  Remove-PSSession $s -ErrorAction SilentlyContinue
}
""";

        var result = await RunPowerShellAsync(profile, script, ct);
        if (result.ExitCode != 0)
            throw new InvalidOperationException("Remote prerequisite install failed. " + result.Error + result.Output);
    }

    private async Task<CommandResult> RunPowerShellAsync(DeploymentProfile profile, string script, CancellationToken ct)
    {
        var dir = Path.Combine(Path.GetTempPath(), "GenericWebDeploymentManager");
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, Guid.NewGuid().ToString("N") + ".ps1");
        await File.WriteAllTextAsync(file, script, Encoding.UTF8, ct);

        var processEnvironment = new Dictionary<string, string?>
        {
            ["GWDM_REMOTE_PASSWORD"] = profile.RemotePassword,
            ["GWDM_REMOTE_USER"] = profile.RemoteUserName,
            ["GWDM_REMOTE_HOST"] = profile.TargetHost,
            ["GWDM_REMOTE_PORT"] = profile.RemoteWinRmPort.ToString(),
            ["GWDM_REMOTE_SSL"] = profile.RemoteUseSsl ? "1" : "0"
        };

        try
        {
            return await _runner.RunAsync(
                "powershell.exe",
                $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{file}\"",
                ct: ct,
                timeout: TimeSpan.FromMinutes(30),
                environment: processEnvironment);
        }
        finally
        {
            try { File.Delete(file); }
            catch (Exception ex) { InternalDiagnostics.Record(ex, "Delete temporary remote PowerShell script"); }
        }
    }

    private static string Ps(string value) => value.Replace("'", "''");

    private static void EnsureRemoteProfile(DeploymentProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.TargetHost))
            throw new InvalidOperationException("Remote host/IP is required.");
        if (string.IsNullOrWhiteSpace(profile.RemoteUserName))
            throw new InvalidOperationException("Remote username is required.");
        if (string.IsNullOrEmpty(profile.RemotePassword))
            throw new InvalidOperationException("Remote password is required for this session.");
    }
}
