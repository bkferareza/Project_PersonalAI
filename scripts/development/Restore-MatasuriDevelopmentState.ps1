[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $WorkspaceRoot,
    [Parameter(Mandatory)] [string] $ManifestPath,
    [Parameter(Mandatory)] [string] $BackupDirectory,
    [string] $StateDirectory,
    [string] $BackupRoot,
    [switch] $Relaunch
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

$restoreArguments = @{
    BackupDirectory = $BackupDirectory
    StateDirectory = $context.StateDirectory
    BackupRoot = $BackupRoot
    SourcePackageIdentity = $context.PackageFullName
    SourceAppVersion = $context.Version
    SourceCommit = $commit
    Confirm = $false
}
if ($WhatIfPreference) {
    $restoreArguments.WhatIf = $true
}
$result = Restore-MatasuriDevelopmentState @restoreArguments

$resident = $null
if ($result.Restored -and $Relaunch) {
    $resident = Start-MatasuriDevelopmentInstance `
        -ExpectedExecutable $context.ExpectedExecutable `
        -AppUserModelId $context.AppUserModelId
}

[pscustomobject]@{
    Restored = $result.Restored
    RestoredFileCount = $result.FileCount
    SelectedBackup = $result.SelectedBackup
    PreRestoreBackup = $result.CurrentStateBackup
    RelaunchedProcessId = if ($null -eq $resident) {
        $null
    }
    else {
        $resident.ProcessId
    }
}
