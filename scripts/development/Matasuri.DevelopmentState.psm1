Set-StrictMode -Version Latest

$script:DurableFileNames = @(
    'learning-state.json',
    'learning-activity.json',
    'matasuri-history-v1.json',
    'health-history-v1.json',
    'electricity-rate-v1.json',
    'matasuri-actions-v1.json'
)

function Get-MatasuriDurableFileNames {
    [CmdletBinding()]
    param()

    return $script:DurableFileNames.Clone()
}

function Get-MatasuriDevelopmentBackupRoot {
    [CmdletBinding()]
    param(
        [string] $BackupRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($BackupRoot)) {
        return [System.IO.Path]::GetFullPath($BackupRoot)
    }

    if (-not [string]::IsNullOrWhiteSpace(
            $env:MATASURI_DEVELOPMENT_BACKUP_ROOT)) {
        return [System.IO.Path]::GetFullPath(
            $env:MATASURI_DEVELOPMENT_BACKUP_ROOT)
    }

    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw 'LOCALAPPDATA is unavailable; specify -BackupRoot.'
    }

    return Join-Path $env:LOCALAPPDATA 'Matasuri\DevelopmentBackups'
}

function Get-JsonPropertyValue {
    param(
        [Parameter(Mandatory)] [object] $InputObject,
        [Parameter(Mandatory)] [string] $Name
    )

    $property = $InputObject.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }

    return $property.Value
}

function ConvertTo-SchemaVersion {
    param(
        [object] $Value,
        [string] $Context
    )

    if ($null -eq $Value) {
        return $null
    }

    $parsed = 0L
    if (-not [long]::TryParse(
            [System.Convert]::ToString(
                $Value,
                [System.Globalization.CultureInfo]::InvariantCulture),
            [ref] $parsed) -or $parsed -lt 1 -or $parsed -gt [int]::MaxValue) {
        throw "Invalid schema version in $Context."
    }

    return [int] $parsed
}

function Read-MatasuriDurableJsonMetadata {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $FileName,
        [switch] $AllowUnreadableJson
    )

    try {
        $raw = Get-Content -LiteralPath $Path -Raw -ErrorAction Stop
        if ([string]::IsNullOrWhiteSpace($raw)) {
            throw 'The file is empty.'
        }

        $document = $raw | ConvertFrom-Json -ErrorAction Stop
        if ($null -eq $document) {
            throw 'The JSON document is null.'
        }

        $schemaVersion = ConvertTo-SchemaVersion `
            (Get-JsonPropertyValue $document 'SchemaVersion') $FileName

        if ($null -eq $schemaVersion -and
            $FileName -eq 'learning-activity.json') {
            $eventSchemas = @()
            $events = Get-JsonPropertyValue $document 'Events'
            foreach ($event in @($events)) {
                if ($null -ne $event) {
                    $eventSchema = ConvertTo-SchemaVersion `
                        (Get-JsonPropertyValue $event 'SchemaVersion') `
                        $FileName
                    if ($null -ne $eventSchema) {
                        $eventSchemas += $eventSchema
                    }
                }
            }
            if ($eventSchemas.Count -gt 0) {
                $schemaVersion = ($eventSchemas |
                    Measure-Object -Maximum).Maximum
            }
        }

        if ($null -eq $schemaVersion -and
            $FileName -eq 'electricity-rate-v1.json') {
            $rateSchemas = @()
            $rates = Get-JsonPropertyValue $document 'Rates'
            foreach ($rate in @($rates)) {
                if ($null -ne $rate) {
                    $rateSchema = ConvertTo-SchemaVersion `
                        (Get-JsonPropertyValue $rate 'SchemaVersion') `
                        $FileName
                    if ($null -ne $rateSchema) {
                        $rateSchemas += $rateSchema
                    }
                }
            }
            if ($rateSchemas.Count -gt 0) {
                $schemaVersion = ($rateSchemas |
                    Measure-Object -Maximum).Maximum
            }
        }

        if ($FileName -in @(
                'learning-state.json',
                'matasuri-history-v1.json',
                'health-history-v1.json',
                'matasuri-actions-v1.json') -and
            $null -eq $schemaVersion) {
            throw "The required SchemaVersion is missing from $FileName."
        }

        return [pscustomobject]@{
            JsonValid = $true
            SchemaVersion = $schemaVersion
            Error = $null
        }
    }
    catch {
        if (-not $AllowUnreadableJson) {
            throw "Invalid durable JSON '$FileName': $($_.Exception.Message)"
        }

        return [pscustomobject]@{
            JsonValid = $false
            SchemaVersion = $null
            Error = $_.Exception.Message
        }
    }
}

function Test-MatasuriSchemaCompatibility {
    param(
        [Parameter(Mandatory)] [string] $FileName,
        [AllowNull()] [object] $SchemaVersion
    )

    if ($null -eq $SchemaVersion) {
        return $true
    }

    $version = [int] $SchemaVersion
    switch ($FileName) {
        'learning-state.json' {
            return $version -ge 1 -and $version -le 4
        }
        'learning-activity.json' {
            return $version -ge 1 -and $version -le 4
        }
        'matasuri-history-v1.json' { return $version -eq 1 }
        'health-history-v1.json' { return $version -eq 1 }
        'electricity-rate-v1.json' { return $version -eq 1 }
        'matasuri-actions-v1.json' { return $version -eq 1 }
        default { return $false }
    }
}

function Test-MatasuriDevelopmentBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $BackupDirectory,
        [switch] $RequireReadableJson
    )

    $errors = New-Object System.Collections.Generic.List[string]
    $manifest = $null
    $manifestPath = Join-Path $BackupDirectory 'manifest.json'

    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        $errors.Add('manifest.json is missing.')
    }
    else {
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw `
                -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
        }
        catch {
            $errors.Add("manifest.json is invalid: $($_.Exception.Message)")
        }
    }

    if ($null -ne $manifest) {
        if ((Get-JsonPropertyValue $manifest 'FormatVersion') -ne 1) {
            $errors.Add('Unsupported backup manifest format.')
        }

        $entries = @(Get-JsonPropertyValue $manifest 'Files')
        if ($entries.Count -eq 0) {
            $errors.Add('The backup contains no durable files.')
        }

        $seen = @{}
        foreach ($entry in $entries) {
            if ($null -eq $entry) {
                $errors.Add('The manifest contains an empty file entry.')
                continue
            }

            $name = [string] (Get-JsonPropertyValue $entry 'Name')
            if ($name -notin $script:DurableFileNames) {
                $errors.Add("The manifest contains a non-allowlisted file: $name")
                continue
            }
            if ($seen.ContainsKey($name)) {
                $errors.Add("The manifest contains a duplicate file: $name")
                continue
            }
            $seen[$name] = $true

            $path = Join-Path $BackupDirectory $name
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                $errors.Add("The backup file is missing: $name")
                continue
            }

            $expectedSize = [long] (Get-JsonPropertyValue $entry 'SizeBytes')
            $actualSize = (Get-Item -LiteralPath $path).Length
            if ($actualSize -ne $expectedSize) {
                $errors.Add("The backup file size does not match: $name")
            }

            $expectedHash = [string] (Get-JsonPropertyValue $entry 'Sha256')
            $actualHash = (Get-FileHash -LiteralPath $path `
                -Algorithm SHA256).Hash
            if ($actualHash -ne $expectedHash) {
                $errors.Add("The backup checksum does not match: $name")
            }

            try {
                $metadata = Read-MatasuriDurableJsonMetadata `
                    -Path $path `
                    -FileName $name `
                    -AllowUnreadableJson
                $declaredJsonValid = [bool] (Get-JsonPropertyValue `
                    $entry 'JsonValid')
                if ($metadata.JsonValid -ne $declaredJsonValid) {
                    $errors.Add("The JSON validation state does not match: $name")
                }
                if ($RequireReadableJson -and -not $metadata.JsonValid) {
                    $errors.Add("The durable JSON is unreadable: $name")
                }

                $declaredSchema = Get-JsonPropertyValue $entry 'SchemaVersion'
                if ($null -eq $declaredSchema) {
                    if ($null -ne $metadata.SchemaVersion) {
                        $errors.Add("The schema metadata does not match: $name")
                    }
                }
                elseif ([int] $declaredSchema -ne $metadata.SchemaVersion) {
                    $errors.Add("The schema metadata does not match: $name")
                }

                if ($metadata.JsonValid -and
                    -not (Test-MatasuriSchemaCompatibility `
                        -FileName $name `
                        -SchemaVersion $metadata.SchemaVersion)) {
                    $errors.Add("The schema is incompatible: $name")
                }
            }
            catch {
                $errors.Add("The backup file cannot be validated: $name")
            }
        }
    }

    return [pscustomobject]@{
        IsValid = $errors.Count -eq 0
        Errors = @($errors)
        Manifest = $manifest
        BackupDirectory = [System.IO.Path]::GetFullPath($BackupDirectory)
    }
}

function Remove-OldMatasuriDevelopmentBackups {
    param(
        [Parameter(Mandatory)] [string] $BackupRoot,
        [ValidateRange(1, 100)] [int] $RetentionCount
    )

    $validBackups = @()
    foreach ($directory in @(Get-ChildItem -LiteralPath $BackupRoot `
            -Directory -ErrorAction SilentlyContinue)) {
        $validation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $directory.FullName
        if ($validation.IsValid) {
            $createdAt = [datetimeoffset]::MinValue
            [datetimeoffset]::TryParse(
                [string] (Get-JsonPropertyValue `
                    $validation.Manifest 'CreatedAtUtc'),
                [ref] $createdAt) | Out-Null
            $validBackups += [pscustomobject]@{
                Directory = $directory.FullName
                CreatedAt = $createdAt
            }
        }
    }

    $expired = @($validBackups |
        Sort-Object CreatedAt -Descending |
        Select-Object -Skip $RetentionCount)
    foreach ($backup in $expired) {
        Remove-Item -LiteralPath $backup.Directory -Recurse -Force
    }
}

function New-MatasuriDevelopmentBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $StateDirectory,
        [string] $BackupRoot,
        [string] $SourcePackageIdentity = 'unknown',
        [string] $SourceAppVersion = 'unknown',
        [string] $SourceCommit = 'unknown',
        [ValidateRange(1, 100)] [int] $RetentionCount = 5,
        [ValidateSet('PreUnregister', 'PreRestore', 'Manual')]
        [string] $Purpose = 'Manual',
        [switch] $PreserveUnreadableJson
    )

    $stateRoot = [System.IO.Path]::GetFullPath($StateDirectory)
    if (-not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
        throw "The durable state directory does not exist: $stateRoot"
    }

    $resolvedBackupRoot = Get-MatasuriDevelopmentBackupRoot $BackupRoot
    New-Item -ItemType Directory -Path $resolvedBackupRoot -Force |
        Out-Null

    $timestamp = [datetimeoffset]::UtcNow.ToString(
        'yyyyMMddTHHmmssfffffffZ',
        [System.Globalization.CultureInfo]::InvariantCulture)
    $suffix = [guid]::NewGuid().ToString('N')
    $staging = Join-Path $resolvedBackupRoot ".staging-$timestamp-$suffix"
    $final = Join-Path $resolvedBackupRoot "$timestamp-$suffix"
    New-Item -ItemType Directory -Path $staging | Out-Null

    try {
        $entries = @()
        foreach ($name in $script:DurableFileNames) {
            $source = Join-Path $stateRoot $name
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
                continue
            }

            $sourceMetadata = Read-MatasuriDurableJsonMetadata `
                -Path $source `
                -FileName $name `
                -AllowUnreadableJson:$PreserveUnreadableJson
            $sourceItem = Get-Item -LiteralPath $source
            $sourceSize = $sourceItem.Length
            $sourceHash = (Get-FileHash -LiteralPath $source `
                -Algorithm SHA256).Hash
            $destination = Join-Path $staging $name
            Copy-Item -LiteralPath $source -Destination $destination `
                -ErrorAction Stop
            $copiedMetadata = Read-MatasuriDurableJsonMetadata `
                -Path $destination `
                -FileName $name `
                -AllowUnreadableJson:$PreserveUnreadableJson

            if ($sourceMetadata.JsonValid -ne $copiedMetadata.JsonValid -or
                $sourceMetadata.SchemaVersion -ne
                    $copiedMetadata.SchemaVersion) {
                throw "Copied metadata validation failed for $name."
            }
            $destinationSize = (Get-Item -LiteralPath $destination).Length
            $destinationHash = (Get-FileHash -LiteralPath $destination `
                -Algorithm SHA256).Hash
            if ($sourceSize -ne $destinationSize -or
                $sourceHash -ne $destinationHash) {
                throw "Copied checksum validation failed for $name."
            }

            $entries += [ordered]@{
                Name = $name
                SizeBytes = $destinationSize
                Sha256 = $destinationHash
                JsonValid = $copiedMetadata.JsonValid
                SchemaVersion = $copiedMetadata.SchemaVersion
            }
        }

        if ($entries.Count -eq 0) {
            throw 'No current allowlisted durable files were found; refusing backup.'
        }

        $manifest = [ordered]@{
            FormatVersion = 1
            CreatedAtUtc = [datetimeoffset]::UtcNow.ToString('O')
            Purpose = $Purpose
            SourcePackageIdentity = $SourcePackageIdentity
            SourceAppVersion = $SourceAppVersion
            SourceCommit = $SourceCommit
            DurableFileAllowlist = $script:DurableFileNames
            Files = $entries
        }
        $manifest | ConvertTo-Json -Depth 12 |
            Set-Content -LiteralPath (Join-Path $staging 'manifest.json') `
                -Encoding UTF8

        $stagingValidation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $staging `
            -RequireReadableJson:(-not $PreserveUnreadableJson)
        if (-not $stagingValidation.IsValid) {
            throw ('Backup staging validation failed: ' +
                ($stagingValidation.Errors -join '; '))
        }

        Move-Item -LiteralPath $staging -Destination $final
        $finalValidation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $final `
            -RequireReadableJson:(-not $PreserveUnreadableJson)
        if (-not $finalValidation.IsValid) {
            throw ('Final backup validation failed: ' +
                ($finalValidation.Errors -join '; '))
        }

        Remove-OldMatasuriDevelopmentBackups `
            -BackupRoot $resolvedBackupRoot `
            -RetentionCount $RetentionCount

        return $finalValidation
    }
    finally {
        if (Test-Path -LiteralPath $staging -PathType Container) {
            $fullStaging = [System.IO.Path]::GetFullPath($staging)
            $fullRoot = [System.IO.Path]::GetFullPath(
                $resolvedBackupRoot).TrimEnd('\') + '\'
            if ($fullStaging.StartsWith(
                    $fullRoot,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                Remove-Item -LiteralPath $fullStaging -Recurse -Force
            }
        }
    }
}

function Invoke-MatasuriGuardedDestructiveOperation {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $StateDirectory,
        [string] $BackupRoot,
        [Parameter(Mandatory)] [scriptblock] $GracefulStopAction,
        [Parameter(Mandatory)] [scriptblock] $DestructiveAction,
        [string] $SourcePackageIdentity = 'unknown',
        [string] $SourceAppVersion = 'unknown',
        [string] $SourceCommit = 'unknown'
    )

    & $GracefulStopAction | Out-Host

    $backup = New-MatasuriDevelopmentBackup `
        -StateDirectory $StateDirectory `
        -BackupRoot $BackupRoot `
        -SourcePackageIdentity $SourcePackageIdentity `
        -SourceAppVersion $SourceAppVersion `
        -SourceCommit $SourceCommit `
        -RetentionCount 5 `
        -Purpose PreUnregister

    $validation = Test-MatasuriDevelopmentBackup `
        -BackupDirectory $backup.BackupDirectory `
        -RequireReadableJson
    if (-not $validation.IsValid) {
        throw ('Verified backup is mandatory; destructive operation aborted: ' +
            ($validation.Errors -join '; '))
    }

    & $DestructiveAction $validation | Out-Host
    return $validation
}

function Restore-MatasuriDevelopmentState {
    [CmdletBinding(SupportsShouldProcess)]
    param(
        [Parameter(Mandatory)] [string] $BackupDirectory,
        [Parameter(Mandatory)] [string] $StateDirectory,
        [string] $BackupRoot,
        [string] $SourcePackageIdentity = 'unknown',
        [string] $SourceAppVersion = 'unknown',
        [string] $SourceCommit = 'unknown'
    )

    $selected = Test-MatasuriDevelopmentBackup `
        -BackupDirectory $BackupDirectory `
        -RequireReadableJson
    if (-not $selected.IsValid) {
        throw ('Selected backup is invalid: ' +
            ($selected.Errors -join '; '))
    }

    $stateRoot = [System.IO.Path]::GetFullPath($StateDirectory)
    if (-not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
        throw "The durable state directory does not exist: $stateRoot"
    }

    $currentBackup = New-MatasuriDevelopmentBackup `
        -StateDirectory $stateRoot `
        -BackupRoot $BackupRoot `
        -SourcePackageIdentity $SourcePackageIdentity `
        -SourceAppVersion $SourceAppVersion `
        -SourceCommit $SourceCommit `
        -RetentionCount 5 `
        -Purpose PreRestore `
        -PreserveUnreadableJson

    $staged = @()
    try {
        foreach ($entry in @($selected.Manifest.Files)) {
            $name = [string] $entry.Name
            $source = Join-Path $BackupDirectory $name
            $target = Join-Path $stateRoot $name

            $backupMetadata = Read-MatasuriDurableJsonMetadata `
                -Path $source `
                -FileName $name
            if (-not (Test-MatasuriSchemaCompatibility `
                    -FileName $name `
                    -SchemaVersion $backupMetadata.SchemaVersion)) {
                throw "The selected backup has an incompatible schema: $name"
            }

            if (Test-Path -LiteralPath $target -PathType Leaf) {
                $currentMetadata = Read-MatasuriDurableJsonMetadata `
                    -Path $target `
                    -FileName $name `
                    -AllowUnreadableJson
                if ($currentMetadata.JsonValid -and
                    $null -ne $currentMetadata.SchemaVersion -and
                    $null -ne $backupMetadata.SchemaVersion) {
                    if ($currentMetadata.SchemaVersion -gt
                        $backupMetadata.SchemaVersion) {
                        throw "Restore would downgrade newer state: $name"
                    }
                    if ($currentMetadata.SchemaVersion -lt
                        $backupMetadata.SchemaVersion) {
                        throw "Restore schema is newer than current state: $name"
                    }
                }
            }

            $stagePath = Join-Path $stateRoot `
                ('.matasuri-restore-' + [guid]::NewGuid().ToString('N') +
                    '-' + $name)
            Copy-Item -LiteralPath $source -Destination $stagePath
            $stageHash = (Get-FileHash -LiteralPath $stagePath `
                -Algorithm SHA256).Hash
            if ($stageHash -ne [string] $entry.Sha256) {
                throw "Restore staging checksum failed: $name"
            }
            Read-MatasuriDurableJsonMetadata `
                -Path $stagePath `
                -FileName $name | Out-Null
            $staged += [pscustomobject]@{
                Name = $name
                StagePath = $stagePath
                TargetPath = $target
                Sha256 = [string] $entry.Sha256
            }
        }

        if (-not $PSCmdlet.ShouldProcess(
                $stateRoot,
                "Restore $($staged.Count) validated durable files")) {
            return [pscustomobject]@{
                Restored = $false
                CurrentStateBackup = $currentBackup.BackupDirectory
                SelectedBackup = $selected.BackupDirectory
            }
        }

        foreach ($item in $staged) {
            if (Test-Path -LiteralPath $item.TargetPath -PathType Leaf) {
                $rollbackPath = $item.TargetPath + '.restore-rollback'
                [System.IO.File]::Replace(
                    $item.StagePath,
                    $item.TargetPath,
                    $rollbackPath,
                    $true)
                Remove-Item -LiteralPath $rollbackPath -Force
            }
            else {
                [System.IO.File]::Move(
                    $item.StagePath,
                    $item.TargetPath)
            }
        }

        foreach ($item in $staged) {
            $actualHash = (Get-FileHash -LiteralPath $item.TargetPath `
                -Algorithm SHA256).Hash
            if ($actualHash -ne $item.Sha256) {
                throw "Restored checksum validation failed: $($item.Name)"
            }
            Read-MatasuriDurableJsonMetadata `
                -Path $item.TargetPath `
                -FileName $item.Name | Out-Null
        }

        return [pscustomobject]@{
            Restored = $true
            FileCount = $staged.Count
            CurrentStateBackup = $currentBackup.BackupDirectory
            SelectedBackup = $selected.BackupDirectory
        }
    }
    finally {
        foreach ($item in $staged) {
            if (Test-Path -LiteralPath $item.StagePath -PathType Leaf) {
                Remove-Item -LiteralPath $item.StagePath -Force
            }
        }
    }
}

Export-ModuleMember -Function @(
    'Get-MatasuriDurableFileNames',
    'Get-MatasuriDevelopmentBackupRoot',
    'New-MatasuriDevelopmentBackup',
    'Test-MatasuriDevelopmentBackup',
    'Invoke-MatasuriGuardedDestructiveOperation',
    'Restore-MatasuriDevelopmentState'
)
