Set-StrictMode -Version Latest

function Get-MatasuriDevelopmentPackageContext {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $ManifestPath,
        [string] $StateDirectory
    )

    $resolvedManifest = [System.IO.Path]::GetFullPath($ManifestPath)
    if (-not (Test-Path -LiteralPath $resolvedManifest -PathType Leaf)) {
        throw "The package manifest does not exist: $resolvedManifest"
    }

    [xml] $manifest = Get-Content -LiteralPath $resolvedManifest -Raw
    $identity = $manifest.Package.Identity
    if ($null -eq $identity -or
        [string]::IsNullOrWhiteSpace([string] $identity.Name)) {
        throw 'The package manifest has no usable Identity.'
    }

    $packages = @(Get-AppxPackage -Name ([string] $identity.Name))
    if ($packages.Count -ne 1) {
        throw "Expected one registered Matasuri package; found $($packages.Count)."
    }

    $package = $packages[0]
    if ([string] $package.Publisher -ne [string] $identity.Publisher) {
        throw 'The registered package publisher does not match the manifest.'
    }

    $resolvedState = $StateDirectory
    if ([string]::IsNullOrWhiteSpace($resolvedState)) {
        if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
            throw 'LOCALAPPDATA is unavailable; specify -StateDirectory.'
        }
        $resolvedState = Join-Path $env:LOCALAPPDATA `
            "Packages\$($package.PackageFamilyName)\LocalCache\Local\Machine"
    }
    $resolvedState = [System.IO.Path]::GetFullPath($resolvedState)

    $runtimeRoot = Split-Path -Parent $resolvedManifest
    $expectedExecutable = [System.IO.Path]::GetFullPath(
        (Join-Path $runtimeRoot 'AppX\Machine.App.exe'))

    return [pscustomobject]@{
        IdentityName = [string] $identity.Name
        Publisher = [string] $identity.Publisher
        PackageFullName = [string] $package.PackageFullName
        PackageFamilyName = [string] $package.PackageFamilyName
        Version = [string] $package.Version
        StateDirectory = $resolvedState
        ExpectedExecutable = $expectedExecutable
        AppUserModelId = "$($package.PackageFamilyName)!App"
        ManifestPath = $resolvedManifest
    }
}

function Get-MatasuriDevelopmentProcesses {
    [CmdletBinding()]
    param()

    $processes = @(Get-CimInstance Win32_Process `
        -Filter "Name = 'Machine.App.exe'" -ErrorAction Stop)
    return @($processes | ForEach-Object {
        [pscustomobject]@{
            ProcessId = [int] $_.ProcessId
            ExecutablePath = [string] $_.ExecutablePath
            CommandLine = [string] $_.CommandLine
        }
    })
}

function Assert-ExactMatasuriDevelopmentProcesses {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $Processes,
        [Parameter(Mandatory)] [string] $ExpectedExecutable,
        [ValidateRange(0, 1)] [int] $MaximumCount = 1
    )

    $expected = [System.IO.Path]::GetFullPath($ExpectedExecutable)
    foreach ($process in $Processes) {
        if ([string]::IsNullOrWhiteSpace($process.ExecutablePath)) {
            throw "Cannot verify Machine.App PID $($process.ProcessId); aborting."
        }
        $actual = [System.IO.Path]::GetFullPath($process.ExecutablePath)
        if (-not $actual.Equals(
                $expected,
                [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Unrelated Machine.App PID $($process.ProcessId) is running; aborting."
        }
    }

    if ($Processes.Count -gt $MaximumCount) {
        throw "Expected at most $MaximumCount Matasuri instance; found $($Processes.Count)."
    }
}

function Stop-MatasuriDevelopmentInstanceGracefully {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $ExpectedExecutable,
        [ValidateRange(1, 120)] [int] $TimeoutSeconds = 30
    )

    $before = @(Get-MatasuriDevelopmentProcesses)
    Assert-ExactMatasuriDevelopmentProcesses `
        -Processes $before `
        -ExpectedExecutable $ExpectedExecutable

    if ($before.Count -eq 0) {
        return [pscustomobject]@{
            WasRunning = $false
            ProcessId = $null
            GracefulShutdownVerified = $true
        }
    }

    $resident = $before[0]
    Start-Process -FilePath 'matasuri-dev://shutdown'

    $deadline = [datetimeoffset]::UtcNow.AddSeconds($TimeoutSeconds)
    while ([datetimeoffset]::UtcNow -lt $deadline) {
        if ($null -eq (Get-Process -Id $resident.ProcessId `
                -ErrorAction SilentlyContinue)) {
            break
        }
        Start-Sleep -Milliseconds 100
    }

    if ($null -ne (Get-Process -Id $resident.ProcessId `
            -ErrorAction SilentlyContinue)) {
        throw "Matasuri PID $($resident.ProcessId) did not exit gracefully."
    }

    $after = @(Get-MatasuriDevelopmentProcesses)
    Assert-ExactMatasuriDevelopmentProcesses `
        -Processes $after `
        -ExpectedExecutable $ExpectedExecutable `
        -MaximumCount 0

    return [pscustomobject]@{
        WasRunning = $true
        ProcessId = $resident.ProcessId
        GracefulShutdownVerified = $true
    }
}

function Start-MatasuriDevelopmentInstance {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)] [string] $ExpectedExecutable,
        [Parameter(Mandatory)] [string] $AppUserModelId,
        [ValidateRange(1, 120)] [int] $TimeoutSeconds = 30
    )

    $before = @(Get-MatasuriDevelopmentProcesses)
    Assert-ExactMatasuriDevelopmentProcesses `
        -Processes $before `
        -ExpectedExecutable $ExpectedExecutable `
        -MaximumCount 0

    Start-Process -FilePath 'explorer.exe' `
        -ArgumentList @("shell:AppsFolder\$AppUserModelId")

    $deadline = [datetimeoffset]::UtcNow.AddSeconds($TimeoutSeconds)
    do {
        Start-Sleep -Milliseconds 100
        $after = @(Get-MatasuriDevelopmentProcesses)
        Assert-ExactMatasuriDevelopmentProcesses `
            -Processes $after `
            -ExpectedExecutable $ExpectedExecutable
    } while ($after.Count -eq 0 -and
        [datetimeoffset]::UtcNow -lt $deadline)

    if ($after.Count -ne 1) {
        throw 'Matasuri did not relaunch as exactly one verified instance.'
    }

    return $after[0]
}

Export-ModuleMember -Function @(
    'Get-MatasuriDevelopmentPackageContext',
    'Get-MatasuriDevelopmentProcesses',
    'Stop-MatasuriDevelopmentInstanceGracefully',
    'Start-MatasuriDevelopmentInstance'
)
