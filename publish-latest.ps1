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

    # A same-version republish must not merge obsolete files into an existing release.
    # Build the replacement beside the destination and retain the old directory until
    # the new directory has been installed successfully.
    $nextRelease = Join-Path $DestinationRoot ('.release-next-' + [Guid]::NewGuid().ToString('N'))
    $previousRelease = Join-Path $DestinationRoot ('.release-previous-' + [Guid]::NewGuid().ToString('N'))
    $previousReleaseMoved = $false
    try {
        New-Item -ItemType Directory -Path $nextRelease -Force | Out-Null
        Get-ChildItem -LiteralPath $staging | Copy-Item -Destination $nextRelease -Recurse -Force -ErrorAction Stop
        if (-not (Test-Path -LiteralPath (Join-Path $nextRelease 'GenericWeb.DeploymentManager.exe') -PathType Leaf)) {
            throw 'Versioned release replacement is missing the GUI executable.'
        }
        if (Test-Path -LiteralPath $releaseRoot) {
            Move-Item -LiteralPath $releaseRoot -Destination $previousRelease -ErrorAction Stop
            $previousReleaseMoved = $true
        }
        try {
            Move-Item -LiteralPath $nextRelease -Destination $releaseRoot -ErrorAction Stop
        } catch {
            if ($previousReleaseMoved) {
                Move-Item -LiteralPath $previousRelease -Destination $releaseRoot -ErrorAction Stop
                $previousReleaseMoved = $false
            }
            throw
        }
    } finally {
        if (Test-Path -LiteralPath $nextRelease) { Remove-Item -LiteralPath $nextRelease -Recurse -Force }
    }
    if ($previousReleaseMoved) {
        try { Remove-Item -LiteralPath $previousRelease -Recurse -Force -ErrorAction Stop }
        catch { Write-Warning "Previous versioned release retained at '$previousRelease': $($_.Exception.Message)" }
    }
    # Never merge releases into latest: removed DLLs/config files from older builds
    # must not survive into the next release.
    $nextLatest = Join-Path $DestinationRoot ('.latest-next-' + [Guid]::NewGuid().ToString('N'))
    $previousLatest = Join-Path $DestinationRoot ('.latest-previous-' + [Guid]::NewGuid().ToString('N'))
    $previousMoved = $false
    try {
        New-Item -ItemType Directory -Path $nextLatest -Force | Out-Null
        Get-ChildItem -LiteralPath $releaseRoot | Copy-Item -Destination $nextLatest -Recurse -Force
        $candidateGui = Join-Path $nextLatest 'GenericWeb.DeploymentManager.exe'
        if (-not (Test-Path -LiteralPath $candidateGui -PathType Leaf)) {
            throw "Published GUI executable was not found at '$candidateGui'. Previous latest was preserved."
        }
        if (Test-Path -LiteralPath $latest) {
            Move-Item -LiteralPath $latest -Destination $previousLatest -ErrorAction Stop
            $previousMoved = $true
        }
        try {
            Move-Item -LiteralPath $nextLatest -Destination $latest -ErrorAction Stop
        } catch {
            if ($previousMoved) {
                Move-Item -LiteralPath $previousLatest -Destination $latest -ErrorAction Stop
                $previousMoved = $false
            }
            throw
        }
    } finally {
        if (Test-Path -LiteralPath $nextLatest) { Remove-Item -LiteralPath $nextLatest -Recurse -Force }
    }
    # Keep the previous latest if its cleanup fails; it is never merged into the new release.
    if ($previousMoved) {
        try { Remove-Item -LiteralPath $previousLatest -Recurse -Force -ErrorAction Stop }
        catch { Write-Warning "Previous latest retained at '$previousLatest': $($_.Exception.Message)" }
    }

    $gui = Join-Path $latest 'GenericWeb.DeploymentManager.exe'
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
