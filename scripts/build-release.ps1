<#
.SYNOPSIS
    Automated Release and Packaging Script for NJ Player (Windows x64).
.DESCRIPTION
    1. Compiles self-contained x64 Release build using dotnet publish.
    2. Bundles third-party notices, docs, and external binaries (if available in libs/).
    3. Builds portable ZIP with portable.flag enabled.
    4. Computes SHA-256 checksums for all release artifacts.
#>

param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $PSScriptRoot
$ArtifactsDir = Join-Path $Root "artifacts"
$PublishDir = Join-Path $ArtifactsDir "publish\win-x64"
$PortableDir = Join-Path $ArtifactsDir "NJPlayer-$Version-portable"
$ZipOutput = Join-Path $ArtifactsDir "NJPlayer-$Version-win-x64-portable.zip"
$ChecksumsFile = Join-Path $ArtifactsDir "SHA256SUMS.txt"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host "   NJ Player v$Version - Release Packaging Script" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. Clean output directories
if (Test-Path $ArtifactsDir) {
    Write-Host "[1/5] Cleaning artifacts directory..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $ArtifactsDir
}
New-Item -ItemType Directory -Path $PublishDir -Force | Out-Null

# 2. Publish self-contained executable
Write-Host "[2/5] Running dotnet publish (win-x64, self-contained)..." -ForegroundColor Yellow
$projectPath = Join-Path $Root "src\NJPlayer.App\NJPlayer.App.csproj"
& dotnet publish $projectPath `
    -c $Configuration `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $PublishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "dotnet publish failed with exit code $LASTEXITCODE"
}

# 3. Copy documentation and licenses
Write-Host "[3/5] Bundling notices and docs..." -ForegroundColor Yellow
Copy-Item (Join-Path $Root "THIRD-PARTY-NOTICES.txt") -Destination $PublishDir -Force
Copy-Item (Join-Path $Root "README.md") -Destination $PublishDir -Force

$libsDir = Join-Path $Root "libs"
if (Test-Path $libsDir) {
    Get-ChildItem -Path $libsDir -File | ForEach-Object {
        if ($_.Extension -in @(".dll", ".exe")) {
            Write-Host "  Bundling binary: $($_.Name)" -ForegroundColor DarkGray
            Copy-Item $_.FullName -Destination $PublishDir -Force
        }
    }
}

# 4. Create Portable Edition
Write-Host "[4/5] Creating portable ZIP archive..." -ForegroundColor Yellow
Copy-Item -Path $PublishDir -Destination $PortableDir -Recurse -Force
# Create portable.flag inside portable root
New-Item -ItemType File -Path (Join-Path $PortableDir "portable.flag") -Force | Out-Null

if (Test-Path $ZipOutput) { Remove-Item -Force $ZipOutput }
Compress-Archive -Path "$PortableDir\*" -DestinationPath $ZipOutput -CompressionLevel Optimal
Remove-Item -Recurse -Force $PortableDir

# 5. Calculate SHA-256 Checksums
Write-Host "[5/5] Generating SHA-256 checksums..." -ForegroundColor Yellow
$hashes = @()
Get-ChildItem -Path $ArtifactsDir -File | Where-Object { $_.Name -ne "SHA256SUMS.txt" } | ForEach-Object {
    $hash = Get-FileHash -Path $_.FullName -Algorithm SHA256
    $line = "$($hash.Hash)  $($_.Name)"
    $hashes += $line
    Write-Host "  $line" -ForegroundColor Green
}
$hashes | Out-File -FilePath $ChecksumsFile -Encoding utf8

Write-Host "`nRelease build completed successfully!" -ForegroundColor Green
Write-Host "Output Directory: $ArtifactsDir" -ForegroundColor Cyan

