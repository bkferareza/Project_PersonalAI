[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $RepositoryRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

Import-Module (Join-Path $RepositoryRoot `
    'scripts\development\Matasuri.DevelopmentState.psm1') -Force
$packageModule = Import-Module (Join-Path $RepositoryRoot `
    'scripts\development\Matasuri.DevelopmentPackage.psm1') `
    -Force -PassThru

function Assert-True {
    param(
        [Parameter(Mandatory)] [bool] $Condition,
        [Parameter(Mandatory)] [string] $Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Write-LearningState {
    param(
        [Parameter(Mandatory)] [string] $Directory,
        [Parameter(Mandatory)] [int] $SchemaVersion,
        [Parameter(Mandatory)] [int] $Marker
    )

    New-Item -ItemType Directory -Path $Directory -Force | Out-Null
    [ordered]@{
        SchemaVersion = $SchemaVersion
        Marker = $Marker
    } | ConvertTo-Json | Set-Content -LiteralPath `
        (Join-Path $Directory 'learning-state.json') -Encoding UTF8
}

function Invoke-Test {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [scriptblock] $Body
    )

    & $Body
    Write-Output "PASS $Name"
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
    ('matasuri-development-state-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null

try {
    Invoke-Test 'graceful stop accepts an already stopped resident' {
        & $packageModule {
            Assert-ExactMatasuriDevelopmentProcesses `
                -Processes @() `
                -ExpectedExecutable 'C:\Matasuri\Machine.App.exe'
        }
    }

    Invoke-Test 'normal debug launch preserves package registration' {
        $launchPath = Join-Path $RepositoryRoot '.vscode\launch.json'
        $launch = Get-Content -LiteralPath $launchPath -Raw |
            ConvertFrom-Json
        $configuration = @($launch.configurations)[0]
        Assert-True `
            ($null -eq $configuration.PSObject.Properties['postDebugTask']) `
            'Normal debug launch still has a destructive postDebugTask.'

        $serialized = $configuration | ConvertTo-Json -Depth 10
        Assert-True `
            ($serialized -notmatch '(?i)unregister|remove-appxpackage|--clean') `
            'Normal debug launch contains a destructive deployment operation.'
    }

    Invoke-Test 'guard orders stop, verified backup, then destruction' {
        $state = Join-Path $testRoot 'ordered-state'
        $backups = Join-Path $testRoot 'ordered-backups'
        Write-LearningState $state 4 11
        $events = New-Object System.Collections.Generic.List[string]
        $stop = { $events.Add('stop') }.GetNewClosure()
        $destroy = {
            param($verified)
            Assert-True $verified.IsValid 'Destructive action saw invalid backup.'
            $events.Add('destroy')
        }.GetNewClosure()

        $result = Invoke-MatasuriGuardedDestructiveOperation `
            -StateDirectory $state `
            -BackupRoot $backups `
            -GracefulStopAction $stop `
            -DestructiveAction $destroy

        Assert-True $result.IsValid 'The guarded backup is invalid.'
        Assert-True (($events -join ',') -eq 'stop,destroy') `
            'Guarded operation ordering is incorrect.'
        Assert-True (Test-Path -LiteralPath `
            (Join-Path $result.BackupDirectory 'manifest.json')) `
            'The manifest was not created before destruction.'
    }

    Invoke-Test 'invalid durable JSON prevents destructive operation' {
        $state = Join-Path $testRoot 'invalid-state'
        $backups = Join-Path $testRoot 'invalid-backups'
        New-Item -ItemType Directory -Path $state | Out-Null
        Set-Content -LiteralPath (Join-Path $state 'learning-state.json') `
            -Value '{ invalid' -Encoding UTF8
        $destroyed = $false
        $stop = { }
        $destroy = { $destroyed = $true }.GetNewClosure()
        $threw = $false
        try {
            Invoke-MatasuriGuardedDestructiveOperation `
                -StateDirectory $state `
                -BackupRoot $backups `
                -GracefulStopAction $stop `
                -DestructiveAction $destroy | Out-Null
        }
        catch {
            $threw = $true
        }

        Assert-True $threw 'Invalid JSON did not abort the guard.'
        Assert-True (-not $destroyed) `
            'Destructive action ran after backup validation failed.'
    }

    Invoke-Test 'manifest checksum detects a changed copy' {
        $state = Join-Path $testRoot 'checksum-state'
        $backups = Join-Path $testRoot 'checksum-backups'
        Write-LearningState $state 4 21
        $backup = New-MatasuriDevelopmentBackup `
            -StateDirectory $state `
            -BackupRoot $backups
        Add-Content -LiteralPath `
            (Join-Path $backup.BackupDirectory 'learning-state.json') `
            -Value ' '
        $validation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $backup.BackupDirectory `
            -RequireReadableJson

        Assert-True (-not $validation.IsValid) `
            'A modified backup passed checksum validation.'
        Assert-True (($validation.Errors -join ' ') -match 'checksum|size') `
            'The modified copy did not report checksum or size failure.'
    }

    Invoke-Test 'backup retention keeps five successful snapshots' {
        $state = Join-Path $testRoot 'retention-state'
        $backups = Join-Path $testRoot 'retention-backups'
        for ($index = 1; $index -le 7; $index++) {
            Write-LearningState $state 4 $index
            New-MatasuriDevelopmentBackup `
                -StateDirectory $state `
                -BackupRoot $backups `
                -RetentionCount 5 | Out-Null
        }

        $validCount = 0
        foreach ($directory in @(Get-ChildItem -LiteralPath $backups `
                -Directory)) {
            if ((Test-MatasuriDevelopmentBackup `
                    -BackupDirectory $directory.FullName).IsValid) {
                $validCount++
            }
        }
        Assert-True ($validCount -eq 5) `
            "Expected five retained backups; found $validCount."
    }

    Invoke-Test 'restore refuses an older schema over newer current state' {
        $source = Join-Path $testRoot 'downgrade-source'
        $current = Join-Path $testRoot 'downgrade-current'
        $backups = Join-Path $testRoot 'downgrade-backups'
        Write-LearningState $source 3 31
        $selected = New-MatasuriDevelopmentBackup `
            -StateDirectory $source `
            -BackupRoot $backups
        Write-LearningState $current 4 41

        $threw = $false
        try {
            Restore-MatasuriDevelopmentState `
                -BackupDirectory $selected.BackupDirectory `
                -StateDirectory $current `
                -BackupRoot $backups `
                -Confirm:$false | Out-Null
        }
        catch {
            $threw = $_.Exception.Message -match 'downgrade'
        }
        $currentDocument = Get-Content -LiteralPath `
            (Join-Path $current 'learning-state.json') -Raw |
            ConvertFrom-Json
        Assert-True $threw 'An older schema was not rejected as a downgrade.'
        Assert-True ($currentDocument.SchemaVersion -eq 4) `
            'The newer current state was overwritten.'
        Assert-True ($currentDocument.Marker -eq 41) `
            'Current state content changed after rejected restore.'
    }

    Invoke-Test 'explicit restore snapshots current state and validates result' {
        $source = Join-Path $testRoot 'restore-source'
        $current = Join-Path $testRoot 'restore-current'
        $backups = Join-Path $testRoot 'restore-backups'
        Write-LearningState $source 4 51
        $selected = New-MatasuriDevelopmentBackup `
            -StateDirectory $source `
            -BackupRoot $backups
        Write-LearningState $current 4 61

        $restored = Restore-MatasuriDevelopmentState `
            -BackupDirectory $selected.BackupDirectory `
            -StateDirectory $current `
            -BackupRoot $backups `
            -Confirm:$false
        $document = Get-Content -LiteralPath `
            (Join-Path $current 'learning-state.json') -Raw |
            ConvertFrom-Json
        $preRestoreDocument = Get-Content -LiteralPath `
            (Join-Path $restored.CurrentStateBackup 'learning-state.json') `
            -Raw | ConvertFrom-Json

        Assert-True $restored.Restored 'Explicit restore did not complete.'
        Assert-True ($document.Marker -eq 51) `
            'Selected backup content was not restored.'
        Assert-True ($preRestoreDocument.Marker -eq 61) `
            'Current state was not backed up before restore.'
        Assert-True (-not (Get-ChildItem -LiteralPath $current `
            -Filter '.matasuri-restore-*' -File)) `
            'Restore staging files were left behind.'
    }

    Invoke-Test 'backup copies only allowlisted durable files' {
        $state = Join-Path $testRoot 'allowlist-state'
        $backups = Join-Path $testRoot 'allowlist-backups'
        Write-LearningState $state 4 71
        Set-Content -LiteralPath (Join-Path $state 'unrelated.log') `
            -Value 'do not copy'
        $backup = New-MatasuriDevelopmentBackup `
            -StateDirectory $state `
            -BackupRoot $backups

        Assert-True (-not (Test-Path -LiteralPath `
            (Join-Path $backup.BackupDirectory 'unrelated.log'))) `
            'A non-allowlisted file was copied.'
    }
}
finally {
    $fullTestRoot = [System.IO.Path]::GetFullPath($testRoot)
    $tempRoot = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath())
    if ($fullTestRoot.StartsWith(
            $tempRoot,
            [System.StringComparison]::OrdinalIgnoreCase) -and
        (Split-Path -Leaf $fullTestRoot).StartsWith(
            'matasuri-development-state-tests-')) {
        Remove-Item -LiteralPath $fullTestRoot -Recurse -Force
    }
}
