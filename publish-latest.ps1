param(
    [string]$DestinationRoot,
    [ValidateSet('Release', 'Debug')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$sourceRoot = $PSScriptRoot
[xml]$versionProps = Get-Content (Join-Path $sourceRoot 'Directory.Build.props')
$version = [string]$versionProps.Project.PropertyGroup.DeploymentManagerVersion
$major = ($version -split '\.')[0]

if (-not $DestinationRoot) {
    $resolver = Join-Path $sourceRoot 'resolve-release-root.ps1'
    $resolved = & $resolver -Create | ConvertFrom-Json
    $DestinationRoot = [string]$resolved.Path
    Write-Host "Release output resolved from: $($resolved.Source)"
} else {
    $DestinationRoot = [IO.Path]::GetFullPath($DestinationRoot)
    New-Item -ItemType Directory -Path $DestinationRoot -Force | Out-Null
}

$project = Join-Path $sourceRoot 'src\DeploymentManager\DeploymentManager.csproj'
$releaseRoot = Join-Path $DestinationRoot ("releases\$version")
$latest = Join-Path $DestinationRoot 'latest'
$staging = Join-Path $DestinationRoot ('.staging-v' + $major + '-' + [Guid]::NewGuid().ToString('N'))

try {
    New-Item -ItemType Directory -Path $staging -Force | Out-Null
    & dotnet restore $project -r win-x64 -p:NuGetAudit=false
    if ($LASTEXITCODE -ne 0) { throw "dotnet restore failed with exit code $LASTEXITCODE." }
    & dotnet publish $project -c $Configuration -r win-x64 --no-restore --self-contained false -p:NuGetAudit=false -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o $staging
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

    foreach ($name in @('README-CLI.md', 'invoke-deployment-manager.ps1', "CHANGELOG-V$major.md", "TEST-REPORT-V$major.md")) {
        $source = Join-Path $sourceRoot $name
        if (Test-Path $source) { Copy-Item -LiteralPath $source -Destination $staging -Force }
    }

    $commit = $null
    try {
        $candidate = (& git -C $sourceRoot rev-parse HEAD 2>$null | Select-Object -First 1)
        if ($LASTEXITCODE -eq 0 -and $candidate) { $commit = $candidate.Trim() }
    } catch { }

    [ordered]@{ version=$version; buildDateUtc=[DateTimeOffset]::UtcNow.ToString('O'); commit=$commit } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $staging 'deployment-manager.version.json') -Encoding utf8

    New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
    Get-ChildItem -LiteralPath $staging | Copy-Item -Destination $releaseRoot -Recurse -Force
    New-Item -ItemType Directory -Path $latest -Force | Out-Null
    Get-ChildItem -LiteralPath $releaseRoot | Copy-Item -Destination $latest -Recurse -Force

    $gui = Join-Path $latest 'GenericWeb.DeploymentManager.exe'
    if (-not (Test-Path -LiteralPath $gui)) {
        throw "Published GUI executable was not found at '$gui'. Previous root EXEs were not replaced."
    }
    Copy-Item -LiteralPath $gui -Destination (Join-Path $DestinationRoot "DeploymentManager-V$major.exe") -Force
    Copy-Item -LiteralPath $gui -Destination (Join-Path $DestinationRoot 'DeploymentManager.exe') -Force

    Write-Host "Deployment Manager $version published successfully."
    Write-Host "Latest EXE: $(Join-Path $DestinationRoot 'DeploymentManager.exe')"
    Write-Host "Versioned EXE: $(Join-Path $DestinationRoot "DeploymentManager-V$major.exe")"
    Write-Host "Destination: $DestinationRoot"
}
finally {
    if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
}
