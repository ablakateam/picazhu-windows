param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "0.1.0-alpha",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$publishDir = Join-Path $repoRoot "publish\Picazhu.App-$Runtime"
$releaseDir = Join-Path $repoRoot "release"
$portableZip = Join-Path $releaseDir "PICAZHU-Windows-portable-$Version.zip"
$checksumsPath = Join-Path $releaseDir "SHA256SUMS.txt"

New-Item -ItemType Directory -Force -Path $releaseDir | Out-Null

Push-Location $repoRoot
try {
    dotnet restore .\Picazhu.sln
    dotnet build .\Picazhu.sln -c $Configuration -m:1
    dotnet test .\Picazhu.sln -c $Configuration --no-build -m:1
    dotnet publish .\app\Picazhu.App\Picazhu.App.csproj -c $Configuration -r $Runtime --self-contained false -m:1 -o $publishDir /p:DebugType=None /p:DebugSymbols=false
    Get-ChildItem -LiteralPath $publishDir -Filter *.pdb -File -ErrorAction SilentlyContinue |
        Remove-Item -Force

    if (Test-Path $portableZip) {
        Remove-Item -LiteralPath $portableZip -Force
    }

    Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $portableZip -CompressionLevel Optimal

    $installerPath = $null
    if (-not $SkipInstaller) {
        $isccCandidates = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
            "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
        )
        $iscc = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $iscc) {
            Write-Warning "Inno Setup 6 was not found. Install it with: winget install --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements"
        }
        else {
            & $iscc (Join-Path $repoRoot "installer\Picazhu.Windows.iss")
            $installerPath = Join-Path $releaseDir "PICAZHU-Windows-Setup-$Version.exe"
        }
    }

    $artifacts = @($portableZip)
    if ($installerPath -and (Test-Path $installerPath)) {
        $artifacts += $installerPath
    }

    $hashLines = foreach ($artifact in $artifacts) {
        $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $artifact
        "$($hash.Hash.ToLowerInvariant())  $(Split-Path $artifact -Leaf)"
    }
    Set-Content -Path $checksumsPath -Value $hashLines -Encoding UTF8

    Write-Host "Release assets created:"
    @($artifacts + $checksumsPath) |
        ForEach-Object { Get-Item -LiteralPath $_ } |
        Select-Object FullName, Length, LastWriteTime |
        Format-Table -AutoSize
}
finally {
    Pop-Location
}
