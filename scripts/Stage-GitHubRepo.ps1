param(
    [string]$Destination = ".\release\github-picazhu-windows",
    [switch]$IncludePortableRelease
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$destinationPath = if ([System.IO.Path]::IsPathRooted($Destination)) {
    $Destination
}
else {
    Join-Path $repoRoot $Destination
}

if (Test-Path $destinationPath) {
    Remove-Item -LiteralPath $destinationPath -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $destinationPath | Out-Null

$topLevelFiles = @(
    ".gitattributes",
    ".gitignore",
    "Directory.Build.props",
    "DISTRIBUTION.md",
    "LICENSE.md",
    "Picazhu.sln",
    "PROGRESS.md",
    "README.md",
    "STATUS.md"
)

foreach ($file in $topLevelFiles) {
    Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $destinationPath -Force
}

$directories = @(
    "app",
    "docs",
    "installer",
    "scripts",
    "tools"
)

foreach ($directory in $directories) {
    $source = Join-Path $repoRoot $directory
    $target = Join-Path $destinationPath $directory
    robocopy $source $target /E /XD bin obj /XF *_wpftmp.csproj *.user *.suo *.log | Out-Null
    if ($LASTEXITCODE -gt 7) {
        throw "robocopy failed for $directory with exit code $LASTEXITCODE"
    }
}

if ($IncludePortableRelease) {
    $releaseTarget = Join-Path $destinationPath "release"
    New-Item -ItemType Directory -Force -Path $releaseTarget | Out-Null
    Get-ChildItem -LiteralPath (Join-Path $repoRoot "release") -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^PICAZHU-Windows-(portable|Setup)-.+\.(zip|exe)$' -or $_.Name -eq "SHA256SUMS.txt" } |
        Copy-Item -Destination $releaseTarget -Force
}

Write-Host "Clean GitHub repo staged at:"
Write-Host $destinationPath
Write-Host ""
Write-Host "Review this folder before creating/pushing the new repository."
