[CmdletBinding()]
param(
    [switch] $StagePreparedModel,
    [switch] $SkipRuntimeDownload
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..'))
$runtimeManifestPath = Join-Path $PSScriptRoot 'runtime-manifest.json'
$modelManifestPath = Join-Path $PSScriptRoot 'model-manifest.json'
$runtimeManifest = Get-Content -LiteralPath $runtimeManifestPath -Raw |
    ConvertFrom-Json
$modelManifest = Get-Content -LiteralPath $modelManifestPath -Raw |
    ConvertFrom-Json
$artifactRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\local-inference'))

if (-not $artifactRoot.StartsWith(
        $repositoryRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The inference artifact directory escaped the repository.'
}

function Assert-ManifestFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [long] $SizeBytes,
        [Parameter(Mandatory)] [string] $Sha256
    )

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.Length -ne $SizeBytes) {
        throw "Unexpected size for '$Path'."
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $actual,
            $Sha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected SHA-256 for '$Path'."
    }
}

$downloadRoot = Join-Path $artifactRoot 'downloads'
$runtimeRoot = Join-Path $repositoryRoot $runtimeManifest.runtimeRelativePath
New-Item -ItemType Directory -Force -Path $downloadRoot | Out-Null

foreach ($archive in $runtimeManifest.archives) {
    $archivePath = Join-Path $downloadRoot $archive.fileName
    if (-not (Test-Path -LiteralPath $archivePath)) {
        if ($SkipRuntimeDownload) {
            throw "Pinned archive is missing: $archivePath"
        }

        $partialPath = "$archivePath.partial"
        if (Test-Path -LiteralPath $partialPath) {
            throw "Incomplete prior download requires review: $partialPath"
        }

        Write-Host "Downloading pinned $($archive.fileName)..."
        Invoke-WebRequest -Uri $archive.url -OutFile $partialPath
        Assert-ManifestFile -Path $partialPath `
            -SizeBytes $archive.sizeBytes -Sha256 $archive.sha256
        Move-Item -LiteralPath $partialPath -Destination $archivePath
    }

    Assert-ManifestFile -Path $archivePath `
        -SizeBytes $archive.sizeBytes -Sha256 $archive.sha256
}

if (-not (Test-Path -LiteralPath $runtimeRoot)) {
    $temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) `
        ("matasuri-inference-{0}" -f [Guid]::NewGuid().ToString('N'))
    $resolvedTemporaryRoot = [System.IO.Path]::GetFullPath($temporaryRoot)
    $resolvedSystemTemp = [System.IO.Path]::GetFullPath(
        [System.IO.Path]::GetTempPath())
    if (-not $resolvedTemporaryRoot.StartsWith(
            $resolvedSystemTemp,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The extraction directory escaped the system temp directory.'
    }

    try {
        New-Item -ItemType Directory -Path $resolvedTemporaryRoot | Out-Null
        foreach ($archive in $runtimeManifest.archives) {
            Expand-Archive `
                -LiteralPath (Join-Path $downloadRoot $archive.fileName) `
                -DestinationPath $resolvedTemporaryRoot
        }

        New-Item -ItemType Directory -Path $runtimeRoot | Out-Null
        foreach ($file in $runtimeManifest.files) {
            Copy-Item `
                -LiteralPath (Join-Path $resolvedTemporaryRoot $file.name) `
                -Destination (Join-Path $runtimeRoot $file.name)
        }
    }
    finally {
        if (Test-Path -LiteralPath $resolvedTemporaryRoot) {
            Remove-Item -LiteralPath $resolvedTemporaryRoot -Recurse -Force
        }
    }
}

foreach ($file in $runtimeManifest.files) {
    Assert-ManifestFile -Path (Join-Path $runtimeRoot $file.name) `
        -SizeBytes $file.sizeBytes -Sha256 $file.sha256
}

$modelRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'Matasuri\Inference\Models'))
$expectedInferenceRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $env:LOCALAPPDATA 'Matasuri\Inference'))
if (-not $modelRoot.StartsWith(
        $expectedInferenceRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The model directory escaped Matasuri-owned local state.'
}

$modelPath = Join-Path $modelRoot $modelManifest.fileName
if ($StagePreparedModel) {
    $preparedModelPath = Join-Path `
        (Join-Path $artifactRoot 'models') `
        $modelManifest.fileName
    Assert-ManifestFile -Path $preparedModelPath `
        -SizeBytes $modelManifest.sizeBytes -Sha256 $modelManifest.sha256

    $stream = [System.IO.File]::OpenRead($preparedModelPath)
    try {
        $magic = New-Object byte[] 4
        if ($stream.Read($magic, 0, $magic.Length) -ne 4 -or
            [System.Text.Encoding]::ASCII.GetString($magic) -ne 'GGUF') {
            throw 'The verified model seed is not a GGUF file.'
        }
    }
    finally {
        $stream.Dispose()
    }

    $requiresCopy = $true
    if (Test-Path -LiteralPath $modelPath) {
        try {
            Assert-ManifestFile -Path $modelPath `
                -SizeBytes $modelManifest.sizeBytes `
                -Sha256 $modelManifest.sha256
            $requiresCopy = $false
        }
        catch {
            $requiresCopy = $true
        }
    }

    if ($requiresCopy) {
        New-Item -ItemType Directory -Force -Path $modelRoot | Out-Null
        $partialModelPath = "$modelPath.partial"
        if (Test-Path -LiteralPath $partialModelPath) {
            throw "Incomplete prior model staging requires review: $partialModelPath"
        }

        Copy-Item -LiteralPath $preparedModelPath `
            -Destination $partialModelPath
        Assert-ManifestFile -Path $partialModelPath `
            -SizeBytes $modelManifest.sizeBytes `
            -Sha256 $modelManifest.sha256
        Move-Item -LiteralPath $partialModelPath `
            -Destination $modelPath -Force
    }
}

if (Test-Path -LiteralPath $modelPath) {
    Assert-ManifestFile -Path $modelPath `
        -SizeBytes $modelManifest.sizeBytes -Sha256 $modelManifest.sha256
}
else {
    Write-Warning "The app-owned model is not staged. Build it explicitly with Build-QwenModel.ps1, then re-run with -StagePreparedModel."
}

Write-Host "Verified runtime: $runtimeRoot"
Write-Host "App-owned model: $modelPath"
