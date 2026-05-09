# PICAZHU for Windows Distribution

This document describes the intended Windows release workflow.

## Release Shape

PICAZHU for Windows should be distributed as:

- GitHub repository: source code, docs, installer script, build scripts.
- GitHub Release assets:
  - `PICAZHU-Windows-Setup-0.1.0-alpha.exe`
  - `PICAZHU-Windows-portable-0.1.0-alpha.zip`
  - `SHA256SUMS.txt`

Do not commit generated binaries, logs, screenshots, recordings, temp files, local databases, or app cache data to the source repository.

## Build Requirements

- Windows 11 recommended.
- .NET 8 SDK.
- Inno Setup 6 for `.exe` installer builds.

Install Inno Setup:

```powershell
winget install --id JRSoftware.InnoSetup --accept-package-agreements --accept-source-agreements
```

## Build Commands

```powershell
dotnet restore .\Picazhu.sln
dotnet build .\Picazhu.sln -c Release -m:1
dotnet test .\Picazhu.sln -c Release --no-build -m:1
dotnet publish .\app\Picazhu.App\Picazhu.App.csproj -c Release -r win-x64 --self-contained false -m:1 -o .\publish\Picazhu.App-win-x64 /p:DebugType=None /p:DebugSymbols=false
```

Build release assets:

```powershell
.\scripts\Build-WindowsRelease.ps1
```

Stage a clean source tree for a new GitHub repository:

```powershell
.\scripts\Stage-GitHubRepo.ps1
```

Use `-IncludePortableRelease` only when you want the generated release files copied beside the staged source for review or manual upload. Release assets should be uploaded to GitHub Releases, not committed to the source branch.

## Release Checklist

- Confirm `dotnet build` succeeds.
- Confirm `dotnet test` succeeds.
- Confirm published `Picazhu.App.exe` launches.
- Confirm HEIC preview works.
- Confirm iPhone import can scan and show thumbnails.
- Confirm original-file export works.
- Confirm AI defaults off on first launch.
- Confirm no sensitive or local-only files are included in the staged GitHub repo.
- Confirm the portable zip does not contain `.pdb` debug files.
- Upload installer, portable zip, and checksums to GitHub Releases.

## Signing

The current installer script is unsigned. For public distribution, sign both:

- `Picazhu.App.exe`
- `PICAZHU-Windows-Setup-*.exe`

Use a Windows code-signing certificate before broad public release to reduce SmartScreen friction.
