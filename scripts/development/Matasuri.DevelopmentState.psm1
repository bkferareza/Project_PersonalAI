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

function Get-MatasuriActionRecoveryRoot {
    param(
        [string] $ActionRecoveryRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($ActionRecoveryRoot)) {
        return [System.IO.Path]::GetFullPath($ActionRecoveryRoot)
    }

    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        throw 'LOCALAPPDATA is unavailable; action recovery cannot be resolved.'
    }

    return [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA `
        'Matasuri\ActionRecovery\Startup'))
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

function ConvertTo-BoundedInteger {
    param(
        [object] $Value,
        [long] $Minimum,
        [long] $Maximum,
        [string] $Context
    )

    $parsed = 0L
    if ($null -eq $Value -or -not [long]::TryParse(
            [System.Convert]::ToString(
                $Value,
                [System.Globalization.CultureInfo]::InvariantCulture),
            [ref] $parsed) -or
        $parsed -lt $Minimum -or $parsed -gt $Maximum) {
        throw "Invalid bounded integer in $Context."
    }

    return $parsed
}

function Test-MatasuriSha256 {
    param([string] $Value)

    return $Value -match '^[0-9a-fA-F]{64}$'
}

function Test-MatasuriDirectFileName {
    param([string] $Value)

    return -not [string]::IsNullOrWhiteSpace($Value) -and
        $Value.Length -le 255 -and
        [System.IO.Path]::GetFileName($Value) -eq $Value -and
        $Value -ne '.' -and $Value -ne '..' -and
        $Value.IndexOfAny([System.IO.Path]::GetInvalidFileNameChars()) -lt 0
}

function Test-MatasuriDirectoryChainWithoutReparse {
    param(
        [Parameter(Mandatory)] [string] $Path
    )

    $current = [System.IO.Path]::GetFullPath($Path)
    while (-not (Test-Path -LiteralPath $current -PathType Container)) {
        $parent = [System.IO.Path]::GetDirectoryName($current)
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            return $false
        }
        $current = $parent
    }

    while (-not [string]::IsNullOrWhiteSpace($current)) {
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band
                [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            return $false
        }
        $parent = [System.IO.Path]::GetDirectoryName(
            $current.TrimEnd('\'))
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $current) {
            break
        }
        $current = $parent
    }
    return $true
}

function Get-MatasuriRequiredActionRecoveryFiles {
    param(
        [Parameter(Mandatory)] [string] $ActionStatePath
    )

    $document = Get-Content -LiteralPath $ActionStatePath -Raw `
        -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
    $schema = ConvertTo-BoundedInteger `
        (Get-JsonPropertyValue $document 'SchemaVersion') 1 1 `
        'matasuri-actions-v1.json SchemaVersion'
    if ($schema -ne 1) {
        throw 'Only action outcome schema 1 is supported.'
    }

    $outcomes = @(Get-JsonPropertyValue $document 'Outcomes')
    if ($outcomes.Count -gt 4096) {
        throw 'The action outcome collection exceeds its safe bound.'
    }

    $required = @()
    $seen = @{}
    foreach ($outcome in $outcomes) {
        if ($null -eq $outcome) {
            throw 'The action outcome collection contains a null item.'
        }

        $target = Get-JsonPropertyValue $outcome 'Target'
        if ($null -eq $target) {
            throw 'An action outcome target is missing.'
        }
        $targetKind = ConvertTo-BoundedInteger `
            (Get-JsonPropertyValue $target 'Kind') 1 2 'action target kind'
        if ($targetKind -ne 2) {
            # Registry recovery is completely represented by the action JSON.
            continue
        }

        $result = ConvertTo-BoundedInteger `
            (Get-JsonPropertyValue $outcome 'Result') 0 8 'action result'
        $undoState = ConvertTo-BoundedInteger `
            (Get-JsonPropertyValue $outcome 'UndoState') 0 10 `
            'action undo state'
        $isUnresolved = $result -in @(0, 8) -or
            $undoState -in @(4, 5, 7, 8, 9, 10)

        $recoveryPayload = Get-JsonPropertyValue `
            $outcome 'RecoveryPayload'
        if ($null -eq $recoveryPayload) {
            if ($isUnresolved) {
                throw 'Unresolved folder recovery has no provider payload.'
            }
            continue
        }
        $version = ConvertTo-BoundedInteger `
            (Get-JsonPropertyValue $recoveryPayload 'Version') 1 1 `
            'folder recovery payload version'
        if ($version -ne 1) {
            throw 'Only folder recovery payload version 1 is supported.'
        }
        $providerDataText = [string] (Get-JsonPropertyValue `
            $recoveryPayload 'ProviderData')
        if ([string]::IsNullOrWhiteSpace($providerDataText) -or
            $providerDataText.Length -gt 16384) {
            throw 'Folder recovery provider data is missing or unbounded.'
        }
        $providerData = $providerDataText |
            ConvertFrom-Json -ErrorAction Stop
        if ((Get-JsonPropertyValue $providerData 'Provider') -ne
            'windows-user-startup-folder-v1') {
            throw 'The folder recovery provider is not allowlisted.'
        }

        $fileName = [string] (Get-JsonPropertyValue `
            $providerData 'FileName')
        if (-not (Test-MatasuriDirectFileName $fileName)) {
            throw 'Folder recovery contains an unsafe original file name.'
        }
        $recoveryFileName = [string] (Get-JsonPropertyValue `
            $providerData 'RecoveryFileName')
        if ($recoveryFileName -notmatch
            '^[0-9a-fA-F]{32}\.startup-recovery$') {
            throw 'Folder recovery contains an invalid staging file name.'
        }
        $actionIdText = [string] (Get-JsonPropertyValue `
            $outcome 'ActionId')
        $actionId = [guid]::Empty
        if (-not [guid]::TryParse($actionIdText, [ref] $actionId) -or
            $actionId -eq [guid]::Empty -or
            -not $recoveryFileName.StartsWith(
                $actionId.ToString('N'),
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw 'Folder recovery staging identity does not match its action.'
        }
        $length = ConvertTo-BoundedInteger `
            (Get-JsonPropertyValue $providerData 'FileLength') 0 `
            ([long]::MaxValue) 'folder recovery file length'
        $sha256 = [string] (Get-JsonPropertyValue `
            $providerData 'FileSha256')
        if (-not (Test-MatasuriSha256 $sha256)) {
            throw 'Folder recovery contains an invalid SHA-256 value.'
        }

        if ($isUnresolved) {
            if ($seen.ContainsKey($recoveryFileName)) {
                throw 'Folder recovery staging identities must be unique.'
            }
            $seen[$recoveryFileName] = $true
            $required += [pscustomobject]@{
                ActionId = $actionId.ToString('D')
                Name = $recoveryFileName
                SizeBytes = $length
                Sha256 = $sha256.ToUpperInvariant()
            }
        }
    }

    return @($required)
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

function Test-MatasuriBackupActionRecovery {
    param(
        [Parameter(Mandatory)] [string] $BackupDirectory,
        [Parameter(Mandatory)] [object] $Manifest,
        [Parameter(Mandatory)] [string] $ActionRecoveryRoot,
        [Parameter(Mandatory)] [object] $Errors
    )

    $declaredValue = Get-JsonPropertyValue `
        $Manifest 'ActionRecoveryFiles'
    if ($null -eq $declaredValue) {
        $declared = @()
    }
    else {
        $declared = @($declaredValue)
    }
    $actionEntry = @((Get-JsonPropertyValue $Manifest 'Files') |
        Where-Object { $_.Name -eq 'matasuri-actions-v1.json' })
    $expected = @()
    if ($actionEntry.Count -eq 1 -and [bool] $actionEntry[0].JsonValid) {
        try {
            $expected = @(Get-MatasuriRequiredActionRecoveryFiles `
                (Join-Path $BackupDirectory 'matasuri-actions-v1.json'))
        }
        catch {
            $Errors.Add("Action recovery metadata is invalid: " +
                $_.Exception.Message)
        }
    }

    if ($declared.Count -ne $expected.Count) {
        $Errors.Add('The action recovery manifest count does not match state.')
    }

    $manifestRoot = [string] (Get-JsonPropertyValue `
        $Manifest 'ActionRecoveryRoot')
    if ([string]::IsNullOrWhiteSpace($manifestRoot)) {
        if ($expected.Count -gt 0 -or $declared.Count -gt 0) {
            $Errors.Add(
                'A backup with folder recovery lacks exact root metadata.')
        }
    }
    else {
        try {
            $manifestRoot = [System.IO.Path]::GetFullPath($manifestRoot)
            if (-not $manifestRoot.Equals(
                    $ActionRecoveryRoot,
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                $Errors.Add(
                    'The action recovery root is not the exact fixed root.')
            }
        }
        catch {
            $Errors.Add('The action recovery root metadata is invalid.')
        }
    }

    $seen = @{}
    foreach ($entry in $declared) {
        if ($null -eq $entry) {
            $Errors.Add('The action recovery manifest contains a null entry.')
            continue
        }
        $name = [string] (Get-JsonPropertyValue $entry 'Name')
        $actionId = [string] (Get-JsonPropertyValue $entry 'ActionId')
        $size = 0L
        try {
            $size = ConvertTo-BoundedInteger `
                (Get-JsonPropertyValue $entry 'SizeBytes') 0 `
                ([long]::MaxValue) 'action recovery manifest size'
        }
        catch {
            $Errors.Add("The action recovery size is invalid: $name")
            continue
        }
        $sha256 = [string] (Get-JsonPropertyValue $entry 'Sha256')
        if ($name -notmatch '^[0-9a-fA-F]{32}\.startup-recovery$' -or
            -not (Test-MatasuriSha256 $sha256) -or
            $seen.ContainsKey($name)) {
            $Errors.Add("The action recovery manifest entry is invalid: $name")
            continue
        }
        $seen[$name] = $true

        $matching = @($expected | Where-Object {
            $_.Name -eq $name -and $_.ActionId -eq $actionId
        })
        if ($matching.Count -ne 1 -or
            $matching[0].SizeBytes -ne $size -or
            $matching[0].Sha256 -ne $sha256.ToUpperInvariant()) {
            $Errors.Add("Action recovery state does not match manifest: $name")
        }

        $path = Join-Path (Join-Path $BackupDirectory 'ActionRecovery') $name
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $Errors.Add("The backed-up action recovery file is missing: $name")
            continue
        }
        $actualSize = (Get-Item -LiteralPath $path).Length
        $actualHash = (Get-FileHash -LiteralPath $path `
            -Algorithm SHA256).Hash
        if ($actualSize -ne $size -or $actualHash -ne $sha256) {
            $Errors.Add("The backed-up action recovery file mismatches: $name")
        }
    }

    $artifactDirectory = Join-Path $BackupDirectory 'ActionRecovery'
    $actualArtifacts = if (Test-Path -LiteralPath $artifactDirectory `
            -PathType Container) {
        @(Get-ChildItem -LiteralPath $artifactDirectory -File)
    }
    else {
        @()
    }
    foreach ($artifact in $actualArtifacts) {
        if (-not $seen.ContainsKey($artifact.Name)) {
            $Errors.Add(
                "The backup contains an unreferenced recovery file: " +
                $artifact.Name)
        }
    }
}

function Test-MatasuriDevelopmentBackup {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $BackupDirectory,
        [switch] $RequireReadableJson,
        [string] $ActionRecoveryRoot
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

        $resolvedActionRecoveryRoot = Get-MatasuriActionRecoveryRoot `
            $ActionRecoveryRoot
        Test-MatasuriBackupActionRecovery `
            -BackupDirectory $BackupDirectory `
            -Manifest $manifest `
            -ActionRecoveryRoot $resolvedActionRecoveryRoot `
            -Errors $errors
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
        [ValidateRange(1, 100)] [int] $RetentionCount,
        [string] $ActionRecoveryRoot
    )

    $validBackups = @()
    foreach ($directory in @(Get-ChildItem -LiteralPath $BackupRoot `
            -Directory -ErrorAction SilentlyContinue)) {
        $validation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $directory.FullName `
            -ActionRecoveryRoot $ActionRecoveryRoot
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
        [switch] $PreserveUnreadableJson,
        [string] $ActionRecoveryRoot
    )

    $stateRoot = [System.IO.Path]::GetFullPath($StateDirectory)
    if (-not (Test-Path -LiteralPath $stateRoot -PathType Container)) {
        throw "The durable state directory does not exist: $stateRoot"
    }

    $resolvedBackupRoot = Get-MatasuriDevelopmentBackupRoot $BackupRoot
    $resolvedActionRecoveryRoot = Get-MatasuriActionRecoveryRoot `
        $ActionRecoveryRoot
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
        $jsonValidity = @{}
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
            $jsonValidity[$name] = $copiedMetadata.JsonValid
        }

        if ($entries.Count -eq 0) {
            throw 'No current allowlisted durable files were found; refusing backup.'
        }

        $actionRecoveryEntries = @()
        if ($jsonValidity.ContainsKey('matasuri-actions-v1.json') -and
            $jsonValidity['matasuri-actions-v1.json']) {
            $requiredRecovery = @(
                Get-MatasuriRequiredActionRecoveryFiles `
                    (Join-Path $staging 'matasuri-actions-v1.json'))
            if ($requiredRecovery.Count -gt 0) {
                if (-not (Test-Path -LiteralPath `
                        $resolvedActionRecoveryRoot -PathType Container) -or
                    -not (Test-MatasuriDirectoryChainWithoutReparse `
                        $resolvedActionRecoveryRoot)) {
                    throw 'The fixed action recovery root is unavailable or unsafe.'
                }
                $recoveryBackupDirectory = Join-Path $staging 'ActionRecovery'
                New-Item -ItemType Directory `
                    -Path $recoveryBackupDirectory | Out-Null
            }
            foreach ($required in $requiredRecovery) {
                $sourceRecovery = Join-Path `
                    $resolvedActionRecoveryRoot $required.Name
                $sourceParent = [System.IO.Path]::GetDirectoryName(
                    [System.IO.Path]::GetFullPath($sourceRecovery))
                if (-not $sourceParent.Equals(
                        $resolvedActionRecoveryRoot.TrimEnd('\'),
                        [System.StringComparison]::OrdinalIgnoreCase) -or
                    -not (Test-Path -LiteralPath $sourceRecovery `
                        -PathType Leaf)) {
                    throw "Required action recovery is missing: " +
                        $required.Name
                }
                $sourceItem = Get-Item -LiteralPath $sourceRecovery
                if (($sourceItem.Attributes -band
                        [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
                    $sourceItem.Length -ne $required.SizeBytes -or
                    (Get-FileHash -LiteralPath $sourceRecovery `
                        -Algorithm SHA256).Hash -ne $required.Sha256) {
                    throw "Required action recovery mismatches: " +
                        $required.Name
                }

                $destinationRecovery = Join-Path `
                    $recoveryBackupDirectory $required.Name
                Copy-Item -LiteralPath $sourceRecovery `
                    -Destination $destinationRecovery -ErrorAction Stop
                if ((Get-Item -LiteralPath $destinationRecovery).Length -ne
                        $required.SizeBytes -or
                    (Get-FileHash -LiteralPath $destinationRecovery `
                        -Algorithm SHA256).Hash -ne $required.Sha256) {
                    throw "Copied action recovery mismatches: " +
                        $required.Name
                }
                $actionRecoveryEntries += [ordered]@{
                    ActionId = $required.ActionId
                    Name = $required.Name
                    SizeBytes = $required.SizeBytes
                    Sha256 = $required.Sha256
                }
            }
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
            ActionRecoveryRoot = $resolvedActionRecoveryRoot
            ActionRecoveryFiles = $actionRecoveryEntries
        }
        $manifest | ConvertTo-Json -Depth 12 |
            Set-Content -LiteralPath (Join-Path $staging 'manifest.json') `
                -Encoding UTF8

        $stagingValidation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $staging `
            -RequireReadableJson:(-not $PreserveUnreadableJson) `
            -ActionRecoveryRoot $resolvedActionRecoveryRoot
        if (-not $stagingValidation.IsValid) {
            throw ('Backup staging validation failed: ' +
                ($stagingValidation.Errors -join '; '))
        }

        Move-Item -LiteralPath $staging -Destination $final
        $finalValidation = Test-MatasuriDevelopmentBackup `
            -BackupDirectory $final `
            -RequireReadableJson:(-not $PreserveUnreadableJson) `
            -ActionRecoveryRoot $resolvedActionRecoveryRoot
        if (-not $finalValidation.IsValid) {
            throw ('Final backup validation failed: ' +
                ($finalValidation.Errors -join '; '))
        }

        Remove-OldMatasuriDevelopmentBackups `
            -BackupRoot $resolvedBackupRoot `
            -RetentionCount $RetentionCount `
            -ActionRecoveryRoot $resolvedActionRecoveryRoot

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
        [string] $SourceCommit = 'unknown',
        [string] $ActionRecoveryRoot
    )

    & $GracefulStopAction | Out-Host

    $backup = New-MatasuriDevelopmentBackup `
        -StateDirectory $StateDirectory `
        -BackupRoot $BackupRoot `
        -SourcePackageIdentity $SourcePackageIdentity `
        -SourceAppVersion $SourceAppVersion `
        -SourceCommit $SourceCommit `
        -RetentionCount 5 `
        -Purpose PreUnregister `
        -ActionRecoveryRoot $ActionRecoveryRoot

    $validation = Test-MatasuriDevelopmentBackup `
        -BackupDirectory $backup.BackupDirectory `
        -RequireReadableJson `
        -ActionRecoveryRoot $ActionRecoveryRoot
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
        [string] $SourceCommit = 'unknown',
        [string] $ActionRecoveryRoot
    )

    $resolvedActionRecoveryRoot = Get-MatasuriActionRecoveryRoot `
        $ActionRecoveryRoot
    $selected = Test-MatasuriDevelopmentBackup `
        -BackupDirectory $BackupDirectory `
        -RequireReadableJson `
        -ActionRecoveryRoot $resolvedActionRecoveryRoot
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
        -PreserveUnreadableJson `
        -ActionRecoveryRoot $resolvedActionRecoveryRoot

    $staged = @()
    $stagedRecovery = @()
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

        if ((Test-Path -LiteralPath $resolvedActionRecoveryRoot `
                -PathType Container) -and
            -not (Test-MatasuriDirectoryChainWithoutReparse `
                $resolvedActionRecoveryRoot)) {
            throw 'The fixed action recovery root has an unsafe reparse chain.'
        }
        $selectedRecoveryEntries = @(Get-JsonPropertyValue `
            $selected.Manifest 'ActionRecoveryFiles')
        foreach ($entry in $selectedRecoveryEntries) {
            $name = [string] $entry.Name
            if (-not (Test-MatasuriDirectFileName $name)) {
                throw "Unsafe action recovery restore name: $name"
            }
            $source = Join-Path `
                (Join-Path $BackupDirectory 'ActionRecovery') $name
            $target = Join-Path $resolvedActionRecoveryRoot $name
            $targetParent = [System.IO.Path]::GetDirectoryName(
                [System.IO.Path]::GetFullPath($target))
            if (-not $targetParent.Equals(
                    $resolvedActionRecoveryRoot.TrimEnd('\'),
                    [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Action recovery restore escaped the fixed root: $name"
            }

            $size = [long] $entry.SizeBytes
            $sha256 = [string] $entry.Sha256
            if (Test-Path -LiteralPath $target -PathType Leaf) {
                $targetItem = Get-Item -LiteralPath $target
                if (($targetItem.Attributes -band
                        [System.IO.FileAttributes]::ReparsePoint) -ne 0 -or
                    $targetItem.Length -ne $size -or
                    (Get-FileHash -LiteralPath $target `
                        -Algorithm SHA256).Hash -ne $sha256) {
                    throw "Action recovery restore conflict: $name"
                }
                $stagedRecovery += [pscustomobject]@{
                    Name = $name
                    StagePath = $null
                    TargetPath = $target
                    SizeBytes = $size
                    Sha256 = $sha256
                    AlreadyPresent = $true
                }
                continue
            }
            if (Test-Path -LiteralPath $target) {
                throw "Action recovery restore destination is occupied: $name"
            }

            if (-not (Test-MatasuriDirectoryChainWithoutReparse `
                    $resolvedActionRecoveryRoot)) {
                throw 'The fixed action recovery root has an unsafe reparse chain.'
            }
            New-Item -ItemType Directory `
                -Path $resolvedActionRecoveryRoot -Force | Out-Null
            $recoveryStagePath = Join-Path $resolvedActionRecoveryRoot `
                ('.matasuri-action-restore-' +
                    [guid]::NewGuid().ToString('N') + '.tmp')
            Copy-Item -LiteralPath $source `
                -Destination $recoveryStagePath -ErrorAction Stop
            if ((Get-Item -LiteralPath $recoveryStagePath).Length -ne $size -or
                (Get-FileHash -LiteralPath $recoveryStagePath `
                    -Algorithm SHA256).Hash -ne $sha256) {
                throw "Action recovery restore staging failed: $name"
            }
            $stagedRecovery += [pscustomobject]@{
                Name = $name
                StagePath = $recoveryStagePath
                TargetPath = $target
                SizeBytes = $size
                Sha256 = $sha256
                AlreadyPresent = $false
            }
        }

        if (-not $PSCmdlet.ShouldProcess(
                $stateRoot,
                "Restore $($staged.Count) durable files and " +
                "$($stagedRecovery.Count) action recovery files")) {
            return [pscustomobject]@{
                Restored = $false
                FileCount = 0
                ActionRecoveryFileCount = 0
                CurrentStateBackup = $currentBackup.BackupDirectory
                SelectedBackup = $selected.BackupDirectory
            }
        }

        foreach ($item in $stagedRecovery) {
            if (-not $item.AlreadyPresent) {
                [System.IO.File]::Move(
                    $item.StagePath,
                    $item.TargetPath)
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

        foreach ($item in $stagedRecovery) {
            if (-not (Test-Path -LiteralPath $item.TargetPath `
                    -PathType Leaf) -or
                (Get-Item -LiteralPath $item.TargetPath).Length -ne
                    $item.SizeBytes -or
                (Get-FileHash -LiteralPath $item.TargetPath `
                    -Algorithm SHA256).Hash -ne $item.Sha256) {
                throw "Restored action recovery validation failed: " +
                    $item.Name
            }
        }

        return [pscustomobject]@{
            Restored = $true
            FileCount = $staged.Count
            ActionRecoveryFileCount = $stagedRecovery.Count
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
        foreach ($item in $stagedRecovery) {
            if ($null -ne $item.StagePath -and
                (Test-Path -LiteralPath $item.StagePath -PathType Leaf)) {
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
