[CmdletBinding()]
param(
    [switch]$Remove
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runPath = 'SOFTWARE\Microsoft\Windows\CurrentVersion\Run'
$fixtureName = 'MTSR-DEV Startup Fixture'
$fixtureData = '%SystemRoot%\System32\where.exe "__matasuri_development_fixture_never_matches__"'
$fixtureKind = [Microsoft.Win32.RegistryValueKind]::ExpandString

$runKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey(
    $runPath,
    $true)
if ($null -eq $runKey) {
    throw 'The fixed current-user Run key is unavailable.'
}

try {
    $matchingNames = @($runKey.GetValueNames() | Where-Object {
        [string]::Equals(
            $_,
            $fixtureName,
            [StringComparison]::OrdinalIgnoreCase)
    })
    if ($matchingNames.Count -gt 1) {
        throw 'The development fixture identity is ambiguous.'
    }

    $actualName = if ($matchingNames.Count -eq 1) {
        $matchingNames[0]
    }
    else {
        $null
    }

    if ($Remove) {
        if ($null -eq $actualName) {
            [pscustomobject]@{
                Name = $fixtureName
                State = 'Absent'
            }
            return
        }

        $actualKind = $runKey.GetValueKind($actualName)
        $actualData = $runKey.GetValue(
            $actualName,
            $null,
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if (-not [string]::Equals(
                $actualName,
                $fixtureName,
                [StringComparison]::Ordinal) -or
            $actualKind -ne $fixtureKind -or
            -not [string]::Equals(
                [string]$actualData,
                $fixtureData,
                [StringComparison]::Ordinal)) {
            throw 'The fixture changed externally; cleanup refused to delete it.'
        }

        $runKey.DeleteValue($fixtureName, $true)
        [pscustomobject]@{
            Name = $fixtureName
            State = 'Removed'
        }
        return
    }

    if ($null -ne $actualName) {
        $actualKind = $runKey.GetValueKind($actualName)
        $actualData = $runKey.GetValue(
            $actualName,
            $null,
            [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        if (-not [string]::Equals(
                $actualName,
                $fixtureName,
                [StringComparison]::Ordinal) -or
            $actualKind -ne $fixtureKind -or
            -not [string]::Equals(
                [string]$actualData,
                $fixtureData,
                [StringComparison]::Ordinal)) {
            throw 'A conflicting value already occupies the fixture identity.'
        }
    }
    else {
        $runKey.SetValue($fixtureName, $fixtureData, $fixtureKind)
    }

    [pscustomobject]@{
        Name = $fixtureName
        State = 'Enabled'
        Provider = 'HKCU Run'
        ValueKind = 'Expandable string'
    }
}
finally {
    $runKey.Dispose()
}
