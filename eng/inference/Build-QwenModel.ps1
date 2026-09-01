[CmdletBinding()]
param(
    [switch] $SkipSourceDownload,
    [switch] $SkipDependencyInstall
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot '..\..'))
$artifactRoot = [System.IO.Path]::GetFullPath(
    (Join-Path $repositoryRoot 'artifacts\local-inference'))
if (-not $artifactRoot.StartsWith(
        $repositoryRoot,
        [StringComparison]::OrdinalIgnoreCase)) {
    throw 'The inference artifact directory escaped the repository.'
}

$runtimeManifestPath = Join-Path $PSScriptRoot 'runtime-manifest.json'
$modelManifestPath = Join-Path $PSScriptRoot 'model-manifest.json'
$runtimeManifest = Get-Content -LiteralPath $runtimeManifestPath -Raw |
    ConvertFrom-Json
$modelManifest = Get-Content -LiteralPath $modelManifestPath -Raw |
    ConvertFrom-Json

function Assert-ManifestFile {
    param(
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [long] $SizeBytes,
        [Parameter(Mandatory)] [string] $Sha256
    )

    $item = Get-Item -LiteralPath $Path -ErrorAction Stop
    if ($item.Length -ne $SizeBytes) {
        throw "Unexpected size for '$Path': $($item.Length)."
    }

    $actual = (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
    if (-not [string]::Equals(
            $actual,
            $Sha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Unexpected SHA-256 for '$Path': $actual."
    }
}

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string] $Executable,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    & $Executable @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "'$Executable' exited with code $LASTEXITCODE."
    }
}

function Ensure-PinnedRepository {
    param(
        [Parameter(Mandatory)] [string] $Url,
        [Parameter(Mandatory)] [string] $Path,
        [Parameter(Mandatory)] [string] $Revision,
        [switch] $SkipLargeFiles
    )

    $gitDirectory = Join-Path $Path '.git'
    if (-not (Test-Path -LiteralPath $gitDirectory)) {
        if (Test-Path -LiteralPath $Path) {
            throw "The expected source path is not a Git repository: $Path"
        }
        if ($SkipSourceDownload) {
            throw "Pinned source repository is missing: $Path"
        }

        New-Item -ItemType Directory -Force `
            -Path (Split-Path -Parent $Path) | Out-Null
        $previousLfsSetting = $env:GIT_LFS_SKIP_SMUDGE
        try {
            if ($SkipLargeFiles) {
                $env:GIT_LFS_SKIP_SMUDGE = '1'
            }
            Invoke-Checked -Executable 'git' -Arguments @(
                'clone', '--filter=blob:none', '--no-checkout', $Url, $Path)
        }
        finally {
            $env:GIT_LFS_SKIP_SMUDGE = $previousLfsSetting
        }
    }

    $currentRevision = (& git -C $Path rev-parse HEAD 2>$null)
    if ($LASTEXITCODE -ne 0 -or $currentRevision -ne $Revision) {
        if ($SkipSourceDownload) {
            throw "Source repository is not at pinned revision ${Revision}: $Path"
        }

        Invoke-Checked -Executable 'git' -Arguments @(
            '-C', $Path, 'fetch', '--depth', '1', 'origin', $Revision)
        $previousLfsSetting = $env:GIT_LFS_SKIP_SMUDGE
        try {
            if ($SkipLargeFiles) {
                $env:GIT_LFS_SKIP_SMUDGE = '1'
            }
            Invoke-Checked -Executable 'git' -Arguments @(
                '-C', $Path, 'checkout', '--detach', $Revision)
        }
        finally {
            $env:GIT_LFS_SKIP_SMUDGE = $previousLfsSetting
        }
    }

    $verifiedRevision = (& git -C $Path rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0 -or $verifiedRevision -ne $Revision) {
        throw "Unable to verify pinned revision $Revision at $Path."
    }
}

function Test-GitLfsPointer {
    param([Parameter(Mandatory)] [string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        return $false
    }

    $item = Get-Item -LiteralPath $Path
    if ($item.Length -gt 1024) {
        return $false
    }

    return (Get-Content -LiteralPath $Path -TotalCount 1) -eq
        'version https://git-lfs.github.com/spec/v1'
}

$sourceRoot = Join-Path $artifactRoot 'source'
$llamaSourceRoot = Join-Path $sourceRoot 'llama.cpp'
$qwenSourceRoot = Join-Path $sourceRoot 'Qwen3.5-4B'
Ensure-PinnedRepository `
    -Url $runtimeManifest.sourceUrl `
    -Path $llamaSourceRoot `
    -Revision $runtimeManifest.sourceCommit
Ensure-PinnedRepository `
    -Url $modelManifest.upstreamSource `
    -Path $qwenSourceRoot `
    -Revision $modelManifest.upstreamRevision `
    -SkipLargeFiles

foreach ($sourceArtifact in $modelManifest.sourceArtifacts) {
    $sourcePath = Join-Path $qwenSourceRoot $sourceArtifact.fileName
    $requiresDownload = -not (Test-Path -LiteralPath $sourcePath)
    if (-not $requiresDownload) {
        try {
            Assert-ManifestFile -Path $sourcePath `
                -SizeBytes $sourceArtifact.sizeBytes `
                -Sha256 $sourceArtifact.sha256
            continue
        }
        catch {
            if (-not (Test-GitLfsPointer -Path $sourcePath)) {
                throw
            }
            $requiresDownload = $true
        }
    }

    if ($requiresDownload) {
        if ($SkipSourceDownload) {
            throw "Pinned Qwen source artifact is missing: $sourcePath"
        }

        $partialPath = "$sourcePath.partial"
        if (Test-Path -LiteralPath $partialPath) {
            throw "Incomplete prior download requires review: $partialPath"
        }

        $sourceUrl = '{0}/resolve/{1}/{2}?download=true' -f `
            $modelManifest.upstreamSource,
            $modelManifest.upstreamRevision,
            $sourceArtifact.fileName
        Write-Host "Downloading pinned $($sourceArtifact.fileName)..."
        Invoke-WebRequest -Uri $sourceUrl -OutFile $partialPath
        Assert-ManifestFile -Path $partialPath `
            -SizeBytes $sourceArtifact.sizeBytes `
            -Sha256 $sourceArtifact.sha256
        Move-Item -LiteralPath $partialPath -Destination $sourcePath -Force
    }
}

$prepareArguments = @()
if ($SkipSourceDownload) {
    $prepareArguments += '-SkipRuntimeDownload'
}
Invoke-Checked `
    -Executable 'powershell.exe' `
    -Arguments (@(
        '-NoProfile', '-ExecutionPolicy', 'Bypass',
        '-File', (Join-Path $PSScriptRoot 'Prepare-LocalInference.ps1')) +
        $prepareArguments)

$downloadRoot = Join-Path $artifactRoot 'downloads'
$toolRuntimeRoot = Join-Path `
    (Join-Path $artifactRoot 'tools') `
    (Join-Path $runtimeManifest.releaseTag 'cuda12-x64')
$quantizerPath = Join-Path $toolRuntimeRoot 'llama-quantize.exe'
if (-not (Test-Path -LiteralPath $toolRuntimeRoot)) {
    $partialToolRoot = "$toolRuntimeRoot.partial-$([Guid]::NewGuid().ToString('N'))"
    $resolvedPartialToolRoot = [System.IO.Path]::GetFullPath($partialToolRoot)
    if (-not $resolvedPartialToolRoot.StartsWith(
            $artifactRoot,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw 'The quantization-tool staging directory escaped the artifact root.'
    }

    try {
        New-Item -ItemType Directory -Force -Path $resolvedPartialToolRoot |
            Out-Null
        foreach ($archive in $runtimeManifest.archives) {
            Expand-Archive `
                -LiteralPath (Join-Path $downloadRoot $archive.fileName) `
                -DestinationPath $resolvedPartialToolRoot `
                -Force
        }
        Assert-ManifestFile `
            -Path (Join-Path $resolvedPartialToolRoot 'llama-quantize.exe') `
            -SizeBytes $modelManifest.conversion.quantizerSizeBytes `
            -Sha256 $modelManifest.conversion.quantizerSha256
        New-Item -ItemType Directory -Force `
            -Path (Split-Path -Parent $toolRuntimeRoot) | Out-Null
        Move-Item -LiteralPath $resolvedPartialToolRoot `
            -Destination $toolRuntimeRoot
    }
    finally {
        if (Test-Path -LiteralPath $resolvedPartialToolRoot) {
            Remove-Item -LiteralPath $resolvedPartialToolRoot -Recurse -Force
        }
    }
}

Assert-ManifestFile -Path $quantizerPath `
    -SizeBytes $modelManifest.conversion.quantizerSizeBytes `
    -Sha256 $modelManifest.conversion.quantizerSha256

$venvRoot = Join-Path $artifactRoot 'tools\python\.venv'
$venvPython = Join-Path $venvRoot 'Scripts\python.exe'
if (-not (Test-Path -LiteralPath $venvPython)) {
    if ($SkipDependencyInstall) {
        throw "Python build environment is missing: $venvRoot"
    }

    $systemPython = (Get-Command python.exe -ErrorAction Stop).Source
    Invoke-Checked -Executable $systemPython `
        -Arguments @('-m', 'venv', $venvRoot)
}

if (-not $SkipDependencyInstall) {
    Invoke-Checked -Executable $venvPython -Arguments @(
        '-m', 'pip', 'install', '--disable-pip-version-check',
        '-r', (Join-Path $PSScriptRoot 'requirements-model-build.txt'))
}

$modelOutputRoot = Join-Path $artifactRoot 'models'
New-Item -ItemType Directory -Force -Path $modelOutputRoot | Out-Null
$bf16Path = Join-Path `
    $modelOutputRoot `
    $modelManifest.conversion.intermediateFileName
$quantizedPath = Join-Path $modelOutputRoot $modelManifest.fileName

if (Test-Path -LiteralPath $quantizedPath) {
    Assert-ManifestFile -Path $quantizedPath `
        -SizeBytes $modelManifest.sizeBytes `
        -Sha256 $modelManifest.sha256
}
else {
    if (Test-Path -LiteralPath $bf16Path) {
        Assert-ManifestFile -Path $bf16Path `
            -SizeBytes $modelManifest.conversion.intermediateSizeBytes `
            -Sha256 $modelManifest.conversion.intermediateSha256
    }
    else {
        Invoke-Checked -Executable $venvPython -Arguments @(
            (Join-Path $llamaSourceRoot 'convert_hf_to_gguf.py'),
            $qwenSourceRoot,
            '--outfile', $bf16Path,
            '--outtype', 'bf16')
        Assert-ManifestFile -Path $bf16Path `
            -SizeBytes $modelManifest.conversion.intermediateSizeBytes `
            -Sha256 $modelManifest.conversion.intermediateSha256
    }

    $partialQuantizedPath = "$quantizedPath.partial"
    if (Test-Path -LiteralPath $partialQuantizedPath) {
        throw "Incomplete prior quantization requires review: $partialQuantizedPath"
    }
    Invoke-Checked -Executable $quantizerPath -Arguments @(
        $bf16Path, $partialQuantizedPath, 'Q4_K_M')
    Assert-ManifestFile -Path $partialQuantizedPath `
        -SizeBytes $modelManifest.sizeBytes `
        -Sha256 $modelManifest.sha256
    Move-Item -LiteralPath $partialQuantizedPath `
        -Destination $quantizedPath
}

Write-Host "Verified pinned Qwen model: $quantizedPath"
