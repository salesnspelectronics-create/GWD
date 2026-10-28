$exe = Join-Path $PSScriptRoot 'DeploymentManager.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    [Console]::Error.WriteLine("DeploymentManager.exe was not found next to this wrapper: $exe")
    exit 9
}
& $exe @args
$deploymentManagerExitCode = $LASTEXITCODE
if ($null -eq $deploymentManagerExitCode) { $deploymentManagerExitCode = 1 }
exit $deploymentManagerExitCode
