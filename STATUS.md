# PICAZHU for Windows Status

## Current Build

- Version: `0.1.1-alpha`
- Platform: Windows 10/11 x64
- Framework: WPF on .NET 8
- Release type: alpha, ready for controlled testing
- Release artifacts: native installer, portable zip, and SHA-256 checksums

## Verified

- Clean Release build.
- Automated test suite passing.
- Published executable starts and responds.
- Inno Setup native installer compiles.
- Portable zip excludes debug `.pdb` files.
- Clean GitHub source staging excludes build output, logs, local databases, screenshots, recordings, and temp files.
- Settings popup layout has been refined so labels, model IDs, provider status, and footer actions remain readable instead of clipping.
- HEIC support includes native WIC detection plus bundled libheif fallback.
- Folder indexing supports recursive folders and ignores AppleDouble sidecars.
- Media export copies selected originals.
- iPhone import detects a connected iPhone, scans camera folders, and displays visual thumbnails.
- AI starts off by default and can be enabled explicitly.

## Implemented

- Folder-first media browser.
- SQLite local catalog.
- Thumbnail cache.
- Metadata extraction.
- HEIC preview and thumbnail fallback.
- Visual media grid.
- Preview/details/tags panel.
- Multi-select export.
- iPhone original-file import.
- Optional OCR and LM Studio vision tagging.
- Responsive media-first Windows shell.
- Windows release packaging scripts and Inno Setup installer definition.

## Known Open Areas

- Embeddings and hybrid semantic ranking are not complete.
- OpenAI/Ollama inference providers are not at feature parity with LM Studio.
- Installer is currently unsigned.
- Installer needs a clean-machine install/uninstall smoke test before broad public release.
- Very large iPhone libraries, locked/untrusted phones, iCloud placeholder files, and unplug-during-import scenarios need more real-device QA.
- Broader HEIC corpus testing is still recommended.

## Recommended Next Release Gates

- End-to-end install/uninstall test on a clean Windows user profile.
- Signed installer build.
- GitHub Release asset upload.
- User-facing screenshots for README.
- Manual smoke-test checklist recorded in release notes.
