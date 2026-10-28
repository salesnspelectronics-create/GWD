param(
    [switch]$Create,
    [switch]$PathOnly
)

$ErrorActionPreference = 'Stop'

function Resolve-SettingsPath {
    if ($env:GWDM_APPDATA_ROOT) {
        return (Join-Path ([IO.Path]::GetFullPath($env:GWDM_APPDATA_ROOT)) 'settings.json')
    }
    return (Join-Path $env:ProgramData 'NSP\DeploymentManager\settings.json')
}

$source = 'fallback'
$root = $null

if ($env:GWDM_MANAGER_ROOT) {
    $root = $env:GWDM_MANAGER_ROOT
    $source = 'GWDM_MANAGER_ROOT'
} else {
    $settingsPath = Resolve-SettingsPath
    if (Test-Path -LiteralPath $settingsPath) {
        try {
            $settings = Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
            if ($settings.ManagerReleaseRoot) {
                $root = [string]$settings.ManagerReleaseRoot
                $source = $settingsPath
            }
        } catch {
            Write-Error "Could not read Deployment Manager settings from '$settingsPath': $($_.Exception.Message)"
        }
    }
}

if (-not $root) {
    $root = Join-Path $PSScriptRoot 'release-output'
}

$root = [IO.Path]::GetFullPath($root)
if ($Create) {
    New-Item -ItemType Directory -Path $root -Force | Out-Null
    $probe = Join-Path $root ('.gwd-write-test-' + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        [IO.File]::WriteAllText($probe, 'ok')
    } finally {
        if (Test-Path -LiteralPath $probe) { Remove-Item -LiteralPath $probe -Force }
    }
}

if ($PathOnly) {
    Write-Output $root
} else {
    [pscustomobject]@{
        Path = $root
        Source = $source
    } | ConvertTo-Json -Compress
}
