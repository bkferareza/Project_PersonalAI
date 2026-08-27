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

function Write-ActionStateWithFolderRecovery {
    param(
        [Parameter(Mandatory)] [string] $StateDirectory,
        [Parameter(Mandatory)] [string] $ActionRecoveryRoot,
        [Parameter(Mandatory)] [guid] $ActionId,
        [Parameter(Mandatory)] [byte[]] $Content,
        [switch] $DoNotWriteRecoveryFile,
        [string] $Provider = 'windows-user-startup-folder-v1'
    )

    New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null
    New-Item -ItemType Directory -Path $ActionRecoveryRoot -Force | Out-Null
    $recoveryName = $ActionId.ToString('N') + '.startup-recovery'
    $recoveryPath = Join-Path $ActionRecoveryRoot $recoveryName
    if (-not $DoNotWriteRecoveryFile) {
        [System.IO.File]::WriteAllBytes($recoveryPath, $Content)
    }
    $hasher = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $hasher.ComputeHash($Content)
    }
    finally {
        $hasher.Dispose()
    }
    $sha256 = [System.BitConverter]::ToString($hashBytes).Replace('-', '')
    $providerData = [ordered]@{
        Provider = $Provider
        FileName = 'Agent.lnk'
        FileLength = $Content.Length
        FileSha256 = $sha256.ToLowerInvariant()
        RecoveryFileName = $recoveryName
    } | ConvertTo-Json -Compress
    $state = [ordered]@{
        SchemaVersion = 1
        Outcomes = @(
            [ordered]@{
                ActionId = $ActionId.ToString('D')
                Target = [ordered]@{ Kind = 2 }
                Result = 1
                UndoState = 4
                RecoveryPayload = [ordered]@{
                    Version = 1
                    ProviderData = $providerData
                }
            }
        )
    }
    $state | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath `
        (Join-Path $StateDirectory 'matasuri-actions-v1.json') `
        -Encoding UTF8
    return [pscustomobject]@{
        Name = $recoveryName
        Path = $recoveryPath
        SizeBytes = $Content.Length
        Sha256 = $sha256
    }
}

function Write-RegistryActionState {
    param(
        [Parameter(Mandatory)] [string] $StateDirectory,
        [Parameter(Mandatory)] [guid] $ActionId
    )

    New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null
    [ordered]@{
        SchemaVersion = 1
        Outcomes = @(
            [ordered]@{
                ActionId = $ActionId.ToString('D')
                Target = [ordered]@{ Kind = 1 }
                Result = 1
                UndoState = 4
                RecoveryPayload = [ordered]@{
                    Version = 1
                    ProviderData = '{ registry recovery stays in JSON }'
                }
            }
        )
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath `
        (Join-Path $StateDirectory 'matasuri-actions-v1.json') `
        -Encoding UTF8
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

    Invoke-Test 'backup validation accepts pre-action manifest format' {
        $state = Join-Path $testRoot 'legacy-manifest-state'
        $backups = Join-Path $testRoot 'legacy-manifest-backups'
        $recoveryRoot = Join-Path $testRoot 'legacy-manifest-recovery'
        Write-LearningState $state 4 80
        $backup = New-MatasuriDevelopmentBackup `
            -StateDirectory $state `
            -BackupRoot $backups `
            -ActionRecoveryRoot $recoveryRoot
        $manifestPath = Join-Path $backup.BackupDirectory 'manifest.json'
        $manifest = Get-Content -LiteralPath $manifestPath -Raw |
            ConvertFrom-Json
        $manifest.PSObject.Properties.Remove('ActionRecoveryRoot')
        $manifest.PSObject.Properties.Remove('ActionRecoveryFiles')
        $manifest | ConvertTo-Json -Depth 12 |
            Set-Content -LiteralPath $manifestPath -Encoding UTF8

        $validation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $backup.BackupDirectory `
            -RequireReadableJson `
            -ActionRecoveryRoot $recoveryRoot
        Assert-True $validation.IsValid `
            'A valid pre-action backup manifest was rejected.'
    }

    Invoke-Test 'backup binds unresolved folder outcome to exact staged file' {
        $state = Join-Path $testRoot 'action-state'
        $backups = Join-Path $testRoot 'action-backups'
        $recoveryRoot = Join-Path $testRoot 'action-recovery'
        Write-LearningState $state 4 81
        $artifact = Write-ActionStateWithFolderRecovery `
            -StateDirectory $state `
            -ActionRecoveryRoot $recoveryRoot `
            -ActionId ([guid]::NewGuid()) `
            -Content ([byte[]](1, 3, 5, 7, 9))

        $backup = New-MatasuriDevelopmentBackup `
            -StateDirectory $state `
            -BackupRoot $backups `
            -ActionRecoveryRoot $recoveryRoot
        $manifestEntry = Assert-True `
            (@($backup.Manifest.ActionRecoveryFiles).Count -eq 1) `
            'The action recovery manifest entry is missing.'
        $copied = Join-Path `
            (Join-Path $backup.BackupDirectory 'ActionRecovery') `
            $artifact.Name
        Assert-True (Test-Path -LiteralPath $copied -PathType Leaf) `
            'The referenced action recovery file was not copied.'
        Assert-True (
            (Get-FileHash -LiteralPath $copied -Algorithm SHA256).Hash -eq
                $artifact.Sha256) `
            'The copied action recovery checksum changed.'
        $validation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $backup.BackupDirectory `
            -RequireReadableJson `
            -ActionRecoveryRoot $recoveryRoot
        Assert-True $validation.IsValid `
            'The complete action recovery backup did not validate.'
        Assert-True (Test-Path -LiteralPath $artifact.Path) `
            'Backup moved or deleted the live staged recovery file.'
    }

    Invoke-Test 'missing or mismatched folder recovery blocks destruction' {
        foreach ($mode in @('missing', 'mismatch')) {
            $state = Join-Path $testRoot "guard-action-$mode-state"
            $backups = Join-Path $testRoot "guard-action-$mode-backups"
            $recoveryRoot = Join-Path $testRoot "guard-action-$mode-root"
            Write-LearningState $state 4 82
            $artifact = Write-ActionStateWithFolderRecovery `
                -StateDirectory $state `
                -ActionRecoveryRoot $recoveryRoot `
                -ActionId ([guid]::NewGuid()) `
                -Content ([byte[]](2, 4, 6, 8)) `
                -DoNotWriteRecoveryFile:($mode -eq 'missing')
            if ($mode -eq 'mismatch') {
                [System.IO.File]::WriteAllBytes(
                    $artifact.Path, [byte[]](9, 9, 9, 9))
            }
            $operation = [pscustomobject]@{ Destroyed = $false }
            $destroy = {
                param($verified)
                $operation.Destroyed = $true
            }.GetNewClosure()
            $threw = $false
            try {
                Invoke-MatasuriGuardedDestructiveOperation `
                    -StateDirectory $state `
                    -BackupRoot $backups `
                    -ActionRecoveryRoot $recoveryRoot `
                    -GracefulStopAction { } `
                    -DestructiveAction $destroy | Out-Null
            }
            catch {
                $threw = $true
            }
            Assert-True $threw `
                "The $mode recovery condition did not abort the guard."
            Assert-True (-not $operation.Destroyed) `
                "Destruction ran with $mode action recovery."
        }
    }

    Invoke-Test 'folder recovery copies follow five-backup retention' {
        $state = Join-Path $testRoot 'retained-action-state'
        $backups = Join-Path $testRoot 'retained-action-backups'
        $recoveryRoot = Join-Path $testRoot 'retained-action-root'
        $artifact = Write-ActionStateWithFolderRecovery `
            -StateDirectory $state `
            -ActionRecoveryRoot $recoveryRoot `
            -ActionId ([guid]::NewGuid()) `
            -Content ([byte[]](41, 42, 43))
        for ($index = 1; $index -le 7; $index++) {
            Write-LearningState $state 4 (90 + $index)
            New-MatasuriDevelopmentBackup `
                -StateDirectory $state `
                -BackupRoot $backups `
                -ActionRecoveryRoot $recoveryRoot `
                -RetentionCount 5 | Out-Null
        }

        $retained = @(Get-ChildItem -LiteralPath $backups -Directory)
        Assert-True ($retained.Count -eq 5) `
            "Expected five recovery backups; found $($retained.Count)."
        foreach ($directory in $retained) {
            Assert-True (Test-Path -LiteralPath (Join-Path `
                (Join-Path $directory.FullName 'ActionRecovery') `
                $artifact.Name) -PathType Leaf) `
                'A retained backup lost its action recovery copy.'
        }
    }

    Invoke-Test 'recovery copy checksum tampering invalidates backup' {
        $state = Join-Path $testRoot 'tamper-action-state'
        $backups = Join-Path $testRoot 'tamper-action-backups'
        $recoveryRoot = Join-Path $testRoot 'tamper-action-root'
        Write-LearningState $state 4 83
        $artifact = Write-ActionStateWithFolderRecovery `
            -StateDirectory $state `
            -ActionRecoveryRoot $recoveryRoot `
            -ActionId ([guid]::NewGuid()) `
            -Content ([byte[]](11, 12, 13))
        $backup = New-MatasuriDevelopmentBackup `
            -StateDirectory $state `
            -BackupRoot $backups `
            -ActionRecoveryRoot $recoveryRoot
        Add-Content -LiteralPath (Join-Path `
            (Join-Path $backup.BackupDirectory 'ActionRecovery') `
            $artifact.Name) -Value 'tamper'

        $validation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $backup.BackupDirectory `
            -RequireReadableJson `
            -ActionRecoveryRoot $recoveryRoot
        Assert-True (-not $validation.IsValid) `
            'A modified action recovery copy passed validation.'
    }

    Invoke-Test 'registry outcome backup requires no external artifact' {
        $state = Join-Path $testRoot 'registry-action-state'
        $backups = Join-Path $testRoot 'registry-action-backups'
        $recoveryRoot = Join-Path $testRoot 'registry-action-root'
        Write-LearningState $state 4 84
        Write-RegistryActionState $state ([guid]::NewGuid())

        $backup = New-MatasuriDevelopmentBackup `
            -StateDirectory $state `
            -BackupRoot $backups `
            -ActionRecoveryRoot $recoveryRoot

        Assert-True $backup.IsValid `
            'Registry-only action outcome did not back up as JSON.'
        Assert-True `
            (@($backup.Manifest.ActionRecoveryFiles).Count -eq 0) `
            'Registry recovery incorrectly required an external artifact.'
    }

    Invoke-Test 'restore recreates absent recovery and accepts exact existing' {
        $source = Join-Path $testRoot 'restore-action-source'
        $current = Join-Path $testRoot 'restore-action-current'
        $backups = Join-Path $testRoot 'restore-action-backups'
        $recoveryRoot = Join-Path $testRoot 'restore-action-root'
        Write-LearningState $source 4 85
        $artifact = Write-ActionStateWithFolderRecovery `
            -StateDirectory $source `
            -ActionRecoveryRoot $recoveryRoot `
            -ActionId ([guid]::NewGuid()) `
            -Content ([byte[]](21, 22, 23, 24))
        $selected = New-MatasuriDevelopmentBackup `
            -StateDirectory $source `
            -BackupRoot $backups `
            -ActionRecoveryRoot $recoveryRoot
        Remove-Item -LiteralPath $artifact.Path
        Write-LearningState $current 4 86

        $first = Restore-MatasuriDevelopmentState `
            -BackupDirectory $selected.BackupDirectory `
            -StateDirectory $current `
            -BackupRoot $backups `
            -ActionRecoveryRoot $recoveryRoot `
            -Confirm:$false
        Assert-True $first.Restored 'Action recovery restore did not complete.'
        Assert-True ($first.ActionRecoveryFileCount -eq 1) `
            'The restored action recovery count is incorrect.'
        Assert-True (Test-Path -LiteralPath $artifact.Path -PathType Leaf) `
            'The absent action recovery file was not restored.'

        $second = Restore-MatasuriDevelopmentState `
            -BackupDirectory $selected.BackupDirectory `
            -StateDirectory $current `
            -BackupRoot $backups `
            -ActionRecoveryRoot $recoveryRoot `
            -Confirm:$false
        Assert-True $second.Restored `
            'An exact existing action recovery file was not accepted.'
    }

    Invoke-Test 'restore refuses conflicting external recovery without overwrite' {
        $source = Join-Path $testRoot 'conflict-action-source'
        $current = Join-Path $testRoot 'conflict-action-current'
        $backups = Join-Path $testRoot 'conflict-action-backups'
        $recoveryRoot = Join-Path $testRoot 'conflict-action-root'
        Write-LearningState $source 4 87
        $artifact = Write-ActionStateWithFolderRecovery `
            -StateDirectory $source `
            -ActionRecoveryRoot $recoveryRoot `
            -ActionId ([guid]::NewGuid()) `
            -Content ([byte[]](31, 32, 33))
        $selected = New-MatasuriDevelopmentBackup `
            -StateDirectory $source `
            -BackupRoot $backups `
            -ActionRecoveryRoot $recoveryRoot
        [System.IO.File]::WriteAllBytes(
            $artifact.Path, [byte[]](99, 98, 97))
        Write-LearningState $current 4 88
        $threw = $false
        try {
            Restore-MatasuriDevelopmentState `
                -BackupDirectory $selected.BackupDirectory `
                -StateDirectory $current `
                -BackupRoot $backups `
                -ActionRecoveryRoot $recoveryRoot `
                -Confirm:$false | Out-Null
        }
        catch {
            $threw = $_.Exception.Message -match 'conflict'
        }

        Assert-True $threw `
            'Conflicting external action recovery was not refused.'
        Assert-True (
            ([System.IO.File]::ReadAllBytes($artifact.Path) -join ',') -eq
                '99,98,97') `
            'Conflicting action recovery was overwritten.'
        $currentState = Get-Content -LiteralPath `
            (Join-Path $current 'learning-state.json') -Raw |
            ConvertFrom-Json
        Assert-True ($currentState.Marker -eq 88) `
            'Durable state changed before recovery conflict rejection.'
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
