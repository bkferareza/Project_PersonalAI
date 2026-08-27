[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $WorkspaceRoot,
    [Parameter(Mandatory)] [string] $ManifestPath,
    [string] $StateDirectory,
    [string] $BackupRoot,
    [switch] $ConfirmDestructiveUnregister
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $ConfirmDestructiveUnregister) {
    throw ('This task removes package-local data. Pass ' +
        '-ConfirmDestructiveUnregister to use the guarded path explicitly.')
}

Import-Module (Join-Path $PSScriptRoot `
    'Matasuri.DevelopmentState.psm1') -Force
Import-Module (Join-Path $PSScriptRoot `
    'Matasuri.DevelopmentPackage.psm1') -Force

$workspace = [System.IO.Path]::GetFullPath($WorkspaceRoot)
$context = Get-MatasuriDevelopmentPackageContext `
    -ManifestPath $ManifestPath `
    -StateDirectory $StateDirectory

if (-not (Test-Path -LiteralPath $context.StateDirectory `
        -PathType Container)) {
    throw "Matasuri durable state is unavailable: $($context.StateDirectory)"
}
if (-not (Test-Path -LiteralPath $context.ExpectedExecutable `
        -PathType Leaf)) {
    throw "The expected Debug executable is unavailable: $($context.ExpectedExecutable)"
}

$project = Join-Path $workspace 'src\Machine.App\Machine.App.csproj'
$cliOutput = @(& dotnet msbuild $project `
    -getProperty:WinAppCliPath `
    -property:Configuration=Debug `
    -property:RuntimeIdentifier=win-x64)
if ($LASTEXITCODE -ne 0) {
    throw "Could not resolve WinAppCliPath (exit $LASTEXITCODE)."
}
$cli = [string] ($cliOutput | Select-Object -Last 1)
$cli = $cli.Trim()
if (-not (Test-Path -LiteralPath $cli -PathType Leaf)) {
    throw "The project WinApp CLI was not found: $cli"
}

$commit = 'unknown'
$gitOutput = @(& git -C $workspace rev-parse HEAD 2>$null)
if ($LASTEXITCODE -eq 0 -and $gitOutput.Count -gt 0) {
    $commit = ([string] $gitOutput[-1]).Trim()
}

$shutdownAction = {
    Stop-MatasuriDevelopmentInstanceGracefully `
        -ExpectedExecutable $context.ExpectedExecutable | Out-Null
}.GetNewClosure()

$unregisterAction = {
    param($verifiedBackup)

    if (-not $verifiedBackup.IsValid) {
        throw 'Unregister cannot run without a valid backup.'
    }
    & $cli unregister --manifest $context.ManifestPath
    if ($LASTEXITCODE -ne 0) {
        throw "WinApp unregister failed (exit $LASTEXITCODE)."
    }
}.GetNewClosure()

$result = Invoke-MatasuriGuardedDestructiveOperation `
    -StateDirectory $context.StateDirectory `
    -BackupRoot $BackupRoot `
    -GracefulStopAction $shutdownAction `
    -DestructiveAction $unregisterAction `
    -SourcePackageIdentity $context.PackageFullName `
    -SourceAppVersion $context.Version `
    -SourceCommit $commit

[pscustomobject]@{
    UnregisteredPackage = $context.PackageFullName
    VerifiedBackup = $result.BackupDirectory
    DurableFileCount = @($result.Manifest.Files).Count
    ActionRecoveryFileCount = @(
        $result.Manifest.ActionRecoveryFiles).Count
}
