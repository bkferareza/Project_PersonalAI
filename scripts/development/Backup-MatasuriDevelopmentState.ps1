[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $WorkspaceRoot,
    [Parameter(Mandatory)] [string] $ManifestPath,
    [string] $StateDirectory,
    [string] $BackupRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $PSScriptRoot `
    'Matasuri.DevelopmentState.psm1') -Force
Import-Module (Join-Path $PSScriptRoot `
    'Matasuri.DevelopmentPackage.psm1') -Force

$workspace = [System.IO.Path]::GetFullPath($WorkspaceRoot)
$context = Get-MatasuriDevelopmentPackageContext `
    -ManifestPath $ManifestPath `
    -StateDirectory $StateDirectory

Stop-MatasuriDevelopmentInstanceGracefully `
    -ExpectedExecutable $context.ExpectedExecutable | Out-Null

$commit = 'unknown'
$gitOutput = @(& git -C $workspace rev-parse HEAD 2>$null)
if ($LASTEXITCODE -eq 0 -and $gitOutput.Count -gt 0) {
    $commit = ([string] $gitOutput[-1]).Trim()
}

$backup = New-MatasuriDevelopmentBackup `
    -StateDirectory $context.StateDirectory `
    -BackupRoot $BackupRoot `
    -SourcePackageIdentity $context.PackageFullName `
    -SourceAppVersion $context.Version `
    -SourceCommit $commit `
    -RetentionCount 5 `
    -Purpose Manual

[pscustomobject]@{
    IsValid = $backup.IsValid
    BackupDirectory = $backup.BackupDirectory
    DurableFileCount = @($backup.Manifest.Files).Count
}
