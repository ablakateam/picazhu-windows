# PICAZHU for Windows Status

## Current Build

- Version: `0.1.2-alpha`
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
- Microsoft Defender scan found no threats in the generated `0.1.2-alpha` release folder.
- Clean GitHub source staging excludes build output, logs, local databases, screenshots, recordings, and temp files.
- GitHub Wiki is initialized and published with status, roadmap, release process, AI/performance notes, troubleshooting, and milestone pages.
- Settings popup layout has been refined so labels, model IDs, provider status, and footer actions remain readable instead of clipping.
- Settings now opens immediately; provider connection checks run after the dialog is visible instead of blocking the window.
- Light and dark themes now share a tested semantic resource contract, with no hard-coded color literals in non-theme app XAML.
- Tooltips, context menus, progress bars, folder tree items, and expanders now use PICAZHU theme styling instead of WPF defaults.
- Theme switching now removes stale light/dark dictionaries by normalized URI matching, and button icons inherit button foregrounds while standalone icons use theme text/accent colors.
- Theme-owned brushes, gradients, and shadows now live in the theme dictionaries and are referenced dynamically so runtime theme changes repaint existing windows consistently.
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
- Optional OCR and vision tagging through LM Studio, Ollama, Ollama Cloud, or OpenAI.
- Responsive media-first Windows shell.
- Windows release packaging scripts and Inno Setup installer definition.

## Known Open Areas

- Embeddings and hybrid semantic ranking are not complete.
- OpenAI, Ollama, and Ollama Cloud provider paths need broader live QA against real user-owned models, endpoints, and API keys.
- Installer is currently unsigned.
- Installer needs a clean-machine install/uninstall smoke test before broad public release.
- Very large iPhone libraries, locked/untrusted phones, iCloud placeholder files, and unplug-during-import scenarios need more real-device QA.
- Broader HEIC corpus testing is still recommended.

## Recommended Next Release Gates

- End-to-end install/uninstall test on a clean Windows user profile.
- Signed installer build.
- Keep GitHub Wiki, README, `STATUS.md`, `PROGRESS.md`, and `DISTRIBUTION.md` synchronized after meaningful changes.
- Manual smoke-test checklist recorded in release notes.
