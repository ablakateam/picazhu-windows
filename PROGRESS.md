# PICAZHU Progress Log

This document records the major milestones reached so far, the technical decisions behind them, and the lessons learned while stabilizing the app. It is intended to be updated whenever a meaningful product or reliability checkpoint is achieved.

## Current Status

PICAZHU is now a working local-first Windows desktop media browser with:

- watched-root indexing and SQLite cataloging
- folder-first navigation with breadcrumbs and back history
- pinned folders, recent folders, and saved searches
- a three-pane shell with a virtualized media grid and right-side preview
- dark/light theme infrastructure with a premium dark-first shell
- HEIC support with runtime decoder selection
- quick preview for images and video
- multi-select export of original media files
- iPhone/portable-device import for original DCIM photos and videos
- right-click media actions and mouse-driven multi-selection mode
- live indexing, thumbnail, and export status in the shell
- diagnostics for indexing, thumbnailing, cache state, and HEIC decoder state
- dedicated header-launched settings window instead of preview-tab settings
- optional AI runtime gate with provider readiness and visible status
- OCR-backed AI indexing and OCR-searchable image text
- LM Studio vision-provider integration with explicit provider/model selection
- preview `Tags` tab for selected media, including AI summary and searchable chips
- media-first shell with a vertical command rail, compact header, bottom status strip, and responsive behavior for restored, laptop-sized, and non-maximized windows
- post-redesign QA hardening for folder query boundaries, large-folder scan resilience, UI exception containment, cache maintenance, and AI queue duplicate suppression
- MediaDevices-backed iPhone import with DCIM-only filtering, duplicate skipping, preserved folder layout, progress, cancellation, and automatic add/index after import
- Windows release packaging with native installer, portable zip, checksums, and clean GitHub staging workflow

Published build output:

- `publish/Picazhu.App-win-x64/Picazhu.App.exe`
- `release/PICAZHU-Windows-Setup-0.1.1-alpha.exe`
- `release/PICAZHU-Windows-portable-0.1.1-alpha.zip`

## Milestones

### Milestone 1: Core app foundation

Delivered the initial app structure around the PRD:

- modular solution split into `App`, `Core`, `Data`, `Indexing`, `Media`, `Cache`, `AI`, and `Tests`
- SQLite catalog and settings persistence
- indexing workers for scanning, metadata probing, and thumbnail generation
- watcher-driven rescans
- three-pane desktop shell

Outcome:

- PICAZHU moved from concept/PRD to a runnable local-first desktop app.

### Milestone 2: Media grid virtualization

The original center pane used a non-virtualized wrap surface, which would not scale to large libraries.

Delivered:

- custom `VirtualizingWrapPanel`
- `ListBox` integration for visible-range realization only

Outcome:

- large result sets became practical without realizing every card at once.

### Milestone 3: Folder-first navigation

The first shell pass lacked proper folder workflow polish.

Delivered:

- breadcrumbs
- back navigation
- pinned folders
- recent folders
- saved searches
- keyboard shortcuts for navigation/search flow

Outcome:

- browsing now behaves like a real library browser instead of a flat result viewer.

### Milestone 4: Settings became real

The settings area started as placeholder UI.

Delivered:

- persisted cache limit
- default subfolder setting
- AI global toggle
- Ollama endpoint
- runtime theme setting support

Outcome:

- settings now map to actual persisted product behavior.

### Milestone 5: Build and startup stabilization

Several early failures were environment and event-ordering related.

Delivered:

- fixed a compile error in `VirtualizingWrapPanel`
- discovered that `dotnet build` needed `-m:1` in this environment to surface real compiler failures
- fixed startup crash caused by `SortComboBox_SelectionChanged` firing before `_viewModel` assignment

Outcome:

- build, publish, and launch became repeatable.

### Milestone 6: Premium shell redesign

The early dark theme was readable but still felt like default WPF controls dropped onto dark panels.

Delivered:

- shared theme dictionaries
- premium dark-first surface system
- improved typography, spacing, and hierarchy
- redesigned shell, sidebar, toolbar, cards, preview pane, and settings surfaces
- more consistent control templates and interaction states

Outcome:

- PICAZHU now feels closer to a real product rather than a functional prototype.

### Milestone 7: HEIC architecture

Windows-only HEIC support was not reliable enough.

Delivered:

- decoder abstraction with runtime priority:
  - Windows WIC when healthy
  - bundled libheif fallback
  - unsupported placeholder with diagnostics
- startup capability detection
- HEIC-specific diagnostics surfaced in the app
- bundled fallback path using `PhotoSauce.NativeCodecs.Libheif`

Outcome:

- HEIC support no longer depends entirely on Windows codec state.

### Milestone 8: HEIC reliability hardening

The first fallback integration still exposed real-world runtime issues.

Delivered:

- forced libheif registration with replacement behavior
- upgraded to `PhotoSauce.NativeCodecs.Libheif 1.19.5-preview1`
- moved HEIC decoder initialization earlier in startup
- versioned HEIC thumbnail cache keys to replace stale placeholders
- serialized libheif probe/thumbnail decode paths to avoid native concurrency crashes

Outcome:

- HEIC decoding became substantially more resilient on problematic Windows installs.

### Milestone 9: Cleanup and state correctness

Removing watched roots left stale UI state behind.

Delivered:

- watched-root removal now cleans:
  - recent folders
  - pinned folders
  - back-history entries
  - invalid current selection state
- startup normalization removes stale persisted paths from earlier builds

Outcome:

- sidebar/history behavior now stays aligned with the watched-root source of truth.

### Milestone 10: Preview and card behavior fixes

Several behavior bugs surfaced during hands-on testing.

Delivered:

- stopped periodic media refresh from clearing selection a few seconds after click
- added quick preview video playback instead of static video thumbnails only
- removed misleading `Pending` badges from cards
- fixed shell thumbnail generation so video thumbnails are not pre-squashed
- changed media surfaces to preserve aspect ratio instead of aggressively filling/cropping
- versioned video thumbnails to invalidate old distorted cache entries

Outcome:

- selection is stable, quick preview is more useful, and media presentation is closer to correct.

### Milestone 11: Large-library indexing visibility and control

Large-folder imports exposed the need for clearer progress and less intrusive background behavior.

Delivered:

- indexing progress with total file estimate and percent complete
- thumbnail readiness status in the shell
- pause, resume, and stop controls for indexing
- safer diagnostics refresh behavior during large scans
- gallery refresh while indexing so media appears without manual poking

Outcome:

- large imports are now more transparent and controllable.

### Milestone 12: Recursive scan and ignore rules

Real-world libraries required deeper root handling and sidecar-file cleanup.

Delivered:

- per-root recursive scanning behavior persisted in watched roots
- add-folder flow asks whether to scan subfolders
- AppleDouble sidecar files such as `._IMG...` and `._video...` are ignored globally

Outcome:

- library ingestion now better matches real photo/video folder structures.

### Milestone 13: Export and selection workflow

PICAZHU now supports building a working batch of media and copying originals out for downstream use cases like print, share, or delivery.

Delivered:

- gallery multi-selection with standard keyboard selection
- export of selected original files to a chosen folder
- flat export layout with auto-renamed conflicts such as `file (2).jpg`
- right-click context menu for media items
- mouse selection mode:
  - `Select This` enables mouse-selection mode
  - plain clicks add/remove items while the mode is on
  - selected tiles show a visible badge
  - selection/export summary appears in the shell

Outcome:

- PICAZHU is now useful not just for browsing, but for assembling and exporting working media sets.

### Milestone 14: AI control plane and OCR indexing

Phase 2 work began with performance control and safe AI plumbing before adding real model inference.

Delivered:

- real AI runtime kill switch instead of a passive saved checkbox
- visible AI status in the shell and settings
- provider readiness checks for LM Studio, Ollama, and OpenAI
- dedicated durable AI analysis queue and persisted `ai_analysis` records
- Windows OCR extraction as the first real AI worker
- OCR-backed searchability for text visible inside images

Outcome:

- AI can now be fully disabled for fastest browsing mode, and OCR-based analysis/search works without hidden background ambiguity.

### Milestone 15: Tags tab and AI visibility

As AI work started becoming real, the app needed a visible way to inspect what was actually being extracted.

Delivered:

- `Tags` tab beside `Details` and `Diagnostics`
- image-only AI tag surface
- OCR-derived text chips
- placeholder buckets for `Objects`, `Scene`, and `Logos`
- clickable chips that push terms directly into search

Outcome:

- AI output is now inspectable inside the app, which makes semantic-search iteration much easier to debug and refine.

### Milestone 16: LM Studio vision integration

The initial AI indexing path was OCR-only even when LM Studio was configured.

Delivered:

- explicit active vision provider selection
- explicit active model selection in settings
- real LM Studio image-analysis calls through its OpenAI-compatible API
- strict requirement for a vision-capable local model before visual tagging is considered available
- OCR preserved as a separate pass and merged with vision output
- HEIC-aware vision input path using generated previews when needed

Outcome:

- PICAZHU now has a real local-model vision path instead of only OCR and provider health checks.

### Milestone 17: Settings relocation and AI settings hardening

The original preview-rail `Settings` tab no longer matched the role settings now play in the product.

Delivered:

- moved settings out of the preview rail into a dedicated header-launched window
- operational AI/provider controls in the settings window
- `Test Connections` support against current unsaved form values
- fixed provider/model selection glitches by:
  - replacing weak editable bindings with deterministic selection controls
  - auto-picking the first detected LM Studio vision model when appropriate
  - pausing the main shell refresh loop while the settings window is open

Outcome:

- settings are now a first-class operational surface, and AI provider/model configuration is materially more stable.

### Milestone 18: LM Studio context-window fix

Once real LM Studio vision requests started hitting `qwen/qwen2.5-vl-7b`, image analysis still failed on whole folders even though provider/model configuration was correct.

Delivered:

- correlated PICAZHU request behavior with LM Studio developer logs
- identified the actual failure mode:
  - `request (4096 tokens) exceeds the available context size (4096 tokens)`
- reduced LM Studio request size by:
  - removing the extra system prompt
  - shortening the user instruction
  - capping output tokens
- reduced image payload size by:
  - normalizing analysis images into compact JPEGs
  - lowering analysis resolution to `768 x 768`
  - lowering JPEG quality for analysis copies
- hardened vision input so:
  - HEIC uses generated previews first
  - non-JPEG originals are rasterized before upload
- corrected AI UI truthfulness so images with no AI row are no longer blindly shown as `Pending`

Outcome:

- LM Studio local vision indexing now fits the model’s context budget and begins processing real folders successfully instead of failing the whole batch.

### Milestone 19: First video AI indexing slice

With image AI stable, the next step was extending the same local-model workflow to videos.

Delivered:

- AI queue now accepts both images and videos
- video frame extraction using Windows media composition thumbnails
- compact representative frame set generation for videos
- LM Studio frame-set analysis path through `DescribeVideoFrameSetAsync(...)`
- current-view AI progress and summaries now count videos as eligible media
- `Tags` tab is now available for selected videos as well as images
- video AI no longer silently lands in `Done` without a usable result; failures surface explicitly

Outcome:

- PICAZHU now has a real first-pass local AI path for videos, not just images, using representative frames and the same LM Studio provider pipeline.

### Milestone 20: Video tags visibility and LM Studio payload hardening

The first video AI pass exposed two practical gaps: LM Studio requests could exceed the active model context window, and successful video analysis still did not always surface useful output in the `Tags` tab.

Delivered:

- LM Studio request-size hardening:
  - shorter prompt
  - capped output tokens
  - normalized compact JPEG analysis images
  - reduced analysis resolution and quality for local VLM safety
- app-side truthfulness improvements for AI state and progress visibility
- `Tags` tab now shows an `AI Summary` block from the stored caption
- caption-derived fallback scene chips so videos still surface useful semantic cues when the model does not return richer grouped tags

Outcome:

- LM Studio video analysis is now materially more reliable, and selected videos can show meaningful semantic output even when the response is sparse.

### Milestone 21: Responsive shell and settings reflow

The shell was still effectively designed for full-screen usage. At reduced window sizes, critical controls and panels could become clipped or difficult to reach.

Delivered:

- lower supported minimum window sizes for the main shell and settings window
- adaptive header compression with reduced chrome and wrapped status cards
- responsive body layout tiers:
  - wide keeps the three-pane shell
  - medium tightens rails and hides lower-value copy
  - compact moves the preview rail below the gallery instead of clipping it on the right
- responsive settings layout using wrapping cards instead of a rigid two-column form

Outcome:

- PICAZHU is now materially more usable when restored, on smaller laptop screens, and in non-maximized layouts.

### Milestone 22: Media-first command rail shell

User testing showed that the header still consumed too much vertical space and left large unused areas, especially at full-screen where the gallery should be the dominant working surface.

Delivered:

- moved primary library actions into a slim vertical command rail:
  - add folder
  - remove folder
  - rescan
  - rebuild
  - back
  - pin
  - settings
- compressed the header into search, sorting, saved search, AI toggle, and filters
- moved indexing, thumbnail, and AI progress/status from large header cards into a compact bottom status strip
- tightened responsive layout tiers so the gallery retains more horizontal and vertical space before preview reflow happens
- corrected the left command rail fit after visual testing so the logo and Add Folder button no longer clip at the edge
- preserved existing command handlers, AI controls, indexing controls, and settings behavior while changing the shell structure

Verification:

- `dotnet build .\Picazhu.sln -c Release -m:1`
- `dotnet test .\Picazhu.sln -c Release --no-build -m:1`
- `dotnet publish .\app\Picazhu.App\Picazhu.App.csproj -c Release -r win-x64 --self-contained false -m:1 -o .\publish\Picazhu.App-win-x64`
- launched published `Picazhu.App.exe`; process reported `Responding=True`
- after the rail clipping correction, build and test were rerun successfully, the real workspace publish folder was updated, and the app relaunched with `Responding=True`

Outcome:

- the main window now behaves more like a media workstation: commands live on the side, status lives at the bottom, and the gallery/preview area owns substantially more of the usable screen.
- the left command rail is now visually safe for testing: primary actions remain centered and reachable instead of being cut off.

### Milestone 23: Senior QA audit and reliability hardening

A full post-redesign audit focused on issues that could pass normal build/test but fail under real user behavior: large folder trees, path-boundary edge cases, background refresh errors, settings save failures, cache cleanup, and duplicate AI queue work.

Delivered:

- fixed recursive folder search boundaries so a query for `Photos` no longer accidentally includes sibling folders such as `Photos2`
- added a regression test for the sibling-prefix folder bug
- made large-folder scanning more defensive:
  - inaccessible folders are skipped and logged instead of aborting the whole scan
  - inaccessible file lists are skipped per directory
  - child reparse-point folders are skipped to avoid junction/symlink loops
- fixed responsive shell initialization so the first wide-mode layout pass cannot be skipped because of enum default state
- contained background refresh exceptions so a transient diagnostics/indexing error updates status instead of crashing the dispatcher
- added a WPF dispatcher exception safety net so unexpected UI errors are reported and handled rather than immediately tearing down the app
- hardened settings save and connection-test actions with explicit error messages instead of unhandled async event exceptions
- hardened thumbnail cache size/cleanup enumeration against bad cache files or inaccessible cache directories
- added AI queue duplicate suppression so startup seeding and live indexing do not repeatedly enqueue the same media item

Verification:

- `dotnet build .\Picazhu.sln -c Release -m:1`
- `dotnet test .\Picazhu.sln -c Release --no-build -m:1`
- regression count increased from 21 to 22 tests

Outcome:

- the app is more resilient under real-world photo libraries, especially large folders with protected directories, similarly named folder paths, and repeated background AI/indexing activity.

### Milestone 24: iPhone DCIM import workflow

PICAZHU needed a PC-friendly path for importing original photos and videos directly from a connected iPhone without forcing users through Windows Photos first.

Delivered:

- added `IPhoneImportService` contracts for portable-device discovery, DCIM media listing, import progress, and import results
- integrated `MediaDevices 1.10.0` as the Windows MTP/WPD backend
- added DCIM-only media filtering so imports ignore non-camera folders and AppleDouble sidecars such as `._IMG_0001.HEIC`
- added real-device support for newer iPhone WPD layouts that expose camera folders directly as `\Internal Storage\YYYYMM_a` instead of `\Internal Storage\DCIM`
- preserved iPhone DCIM folder layout under the destination folder
- skipped exact duplicates when destination file size and modified timestamp match
- auto-renamed destination conflicts when a same-name file is not an exact duplicate
- copied original files through a temporary `.picazhu-importing` file before final move to avoid partial-file final paths
- added a dedicated `Import from iPhone` command-rail entry and import window with device refresh, device scan, media selection, destination browsing, transfer progress, cancel action, and post-import add/index
- redesigned the import picker from a filename list into a visual card grid with larger thumbnails, selection badges, kind badges, file size/date, and preview-loading progress
- added background iPhone thumbnail fetching through Windows portable-device thumbnails, cached under PICAZHU temp storage
- added targeted regression coverage for DCIM filtering, path preservation, sanitization, and duplicate detection

Verification:

- `dotnet build .\Picazhu.sln -c Release -m:1`
- `dotnet test .\Picazhu.sln -c Release --no-build -m:1`
- regression count increased from 22 to 35 tests after the real-device iPhone folder-layout fix
- service probe against the connected iPhone found 124 media items and successfully downloaded a video thumbnail through the new thumbnail path

Outcome:

- users can now connect an iPhone, browse importable DCIM media inside PICAZHU, copy selected originals to a chosen local folder, and have that destination automatically indexed as part of the library.
- the import flow now matches the product identity better: choosing iPhone media is visual and tile-based, not a long filename-only list.

### Milestone 25: Windows release packaging and GitHub staging

PICAZHU needed a clean Windows distribution path before creating a public Windows repository.

Delivered:

- added release metadata to the Windows app project for `0.1.0-alpha`
- added a proprietary `LICENSE.md`
- replaced the developer README with a polished public Windows README inspired by the existing PICAZHU product positioning
- added `DISTRIBUTION.md` and `STATUS.md` for release workflow, gates, and known open areas
- added an Inno Setup script for a native Windows installer
- added a release build script that restores, builds, tests, publishes, removes debug symbols, creates a portable zip, compiles the installer, and writes SHA-256 checksums
- added a GitHub staging script that copies source/docs/scripts into a clean folder while excluding build output, logs, local databases, screenshots, recordings, temp files, and debug artifacts
- created release assets:
  - `release/PICAZHU-Windows-Setup-0.1.0-alpha.exe`
  - `release/PICAZHU-Windows-portable-0.1.0-alpha.zip`
  - `release/SHA256SUMS.txt`
- staged a clean repo package under `release/github-picazhu-windows`

Verification:

- `.\scripts\Build-WindowsRelease.ps1`
- automated tests passed: 35 of 35
- published executable smoke test launched `PICAZHU` and reported `Responding = True`
- installer compiled with Inno Setup 6.7.1
- portable zip audit found no `.pdb` debug files
- staged repo audit found no `bin`, `obj`, logs, local databases, screenshots, recordings, temp files, or debug symbols

Outcome:

- the Windows app is ready to be reviewed as a new `picazhu-windows` GitHub repository, with binary release assets prepared separately for GitHub Releases instead of being committed to the source branch.

### Milestone 26: Settings popup readability and release-pipeline hardening

The settings popup still had layout risk from fixed-width cards and compact footer actions. Long LM Studio model names, provider status text, and action labels could become partially visible on smaller windows or different display scaling.

Delivered:

- rebuilt `SettingsWindow.xaml` into a full-width scrollable settings surface instead of fixed-width `WrapPanel` cards
- added clear Workspace and AI Providers sections with label/control rows
- added a local settings toggle style with a visible checked state instead of reusing filter-chip styling
- made long provider/model/status text wrap safely
- added a selected-model summary under the LM Studio model dropdown so long model IDs remain readable
- expanded footer actions and added clear copy explaining that Close discards unsaved edits and Save applies immediately
- preserved the existing premium dark-first visual language while reducing clipping risk
- hardened `Stage-GitHubRepo.ps1` so it preserves an existing `.git` folder when refreshing the staged GitHub working tree
- bumped the Windows alpha version to `0.1.1-alpha`

Verification:

- `dotnet build .\Picazhu.sln -c Release -m:1`
- `dotnet test .\Picazhu.sln -c Release --no-build -m:1`
- `dotnet publish .\app\Picazhu.App\Picazhu.App.csproj -c Release -r win-x64 --self-contained false -m:1 -o .\publish\Picazhu.App-win-x64 /p:DebugType=None /p:DebugSymbols=false`
- published executable smoke test launched `PICAZHU` and reported `Responding = True`
- `.\scripts\Build-WindowsRelease.ps1`
- Microsoft Defender custom scan found no threats in the `0.1.1-alpha` installer or portable zip

Outcome:

- the settings popup is more robust for real user display scaling and restored-window sizes, especially around AI configuration and provider diagnostics.

## Lessons Learned

### 1. WPF event ordering matters at startup

XAML event handlers can fire during initialization before constructor setup is finished. Assign view-model state before `InitializeComponent()` when handlers depend on it.

### 2. Serial builds exposed hidden failures

In this environment, parallel MSBuild output sometimes hid the actionable compiler error. `dotnet build -m:1` became the reliable diagnostic path.

### 3. Native codec fallback needs defensive isolation

HEIC fallback is not just a package-install problem. Native decoder paths can crash under concurrency, and capability detection must validate real decode behavior, not just registration presence.

### 4. Cache versioning is part of correctness

When thumbnail generation logic changes, stale cached output can make a fix look like it failed. Cache profile versioning was necessary for HEIC and video thumbnail corrections.

### 5. “Readable dark theme” is not the same as “premium UI”

Simply recoloring controls produced a dark app, but not a polished one. Good spacing, fewer bordered boxes, better hierarchy, and more intentional surfaces mattered more than adding more decoration.

### 6. Real screenshots found the highest-value bugs

The most important issues were found through user screenshots and live app testing:

- unreadable controls
- startup crash
- stale recent folders
- HEIC failure modes
- selection dropping
- distorted thumbnails
- quick preview gaps

### 7. Background refresh must respect UI state

Refreshing diagnostics is safe; refreshing the bound media list on a timer is not. That kind of polling can break selection and make the app feel unstable.

### 8. WPF selection behavior is easiest when you use its native modes

Trying to simulate additive mouse selection on top of `Extended` mode caused repeated event-order and `SelectedItem` conflicts. Switching to native `SelectionMode.Multiple` while mouse-selection mode is active produced the stable behavior.

### 9. Screen recordings can expose interaction bugs that screenshots cannot

The multi-select mouse bug was much easier to diagnose with a short recording because the failure depended on click order and control event timing, not just a static UI state.

### 10. Shared live view-model state can destabilize settings UX

Binding a settings dialog directly to a live shell view-model is convenient, but background refreshes can mutate the same collections and selections while the user is interacting with them. Pausing live refresh during settings interaction was necessary to stop provider/model controls from glitching.

### 11. AI readiness is not the same as AI usefulness

Provider connectivity alone was not enough. The app needed:

- a real kill switch
- explicit provider/model selection
- a vision-capable model check
- visible AI output in the `Tags` tab

before the AI feature set became trustworthy for semantic-search development.

### 12. Local multimodal models are constrained by request size, not just model choice

The LM Studio failure was not caused by a wrong model, wrong endpoint, or broken auth. The model was correct, but the combined prompt plus base64 image payload still exhausted the available context window. For local VLMs, payload size must be treated as a first-class engineering constraint.

### 13. Video AI is best added as a representative-frame slice first

Trying to jump directly from still-image analysis to full temporal reasoning would have been too large and risky for this phase. Representative-frame analysis delivers real value quickly while leaving room for later motion-aware improvements.

### 14. Desktop responsiveness needs explicit layout tiers

WPF does not become responsive just because controls are inside grids. Fixed columns, wide hero headers, and rigid multi-column dialogs still behave like a full-screen-only app unless the layout is intentionally reflowed for narrower widths.

### 15. Media apps should give the media surface priority

Large headers and status cards made PICAZHU feel like the shell was competing with the content. Moving persistent commands to a vertical rail and progress to a bottom strip gives the gallery more space while keeping controls discoverable.

### 16. Prefix path matching is dangerous for media libraries

Filesystem filtering must use exact-or-child matching, not raw `LIKE 'path%'` or naive `StartsWith(root)`. Otherwise folders with similar names, such as `Photos` and `Photos2`, can bleed into the same result set.

### 17. Large-folder scans must be fault-tolerant per directory

One inaccessible subfolder should not fail the whole root scan. Real user libraries can contain protected folders, cloud placeholders, stale paths, and junctions, so scanning needs per-directory exception isolation.

### 18. Phone import needs its own safety layer

Treating a connected iPhone like a normal filesystem is risky. MTP paths, trust prompts, locked-device errors, duplicate handling, and partial transfers all need explicit defensive handling before the workflow feels dependable.

### 19. iPhone storage shape changes across Windows/WPD stacks

The same iPhone camera library may not appear as `Internal Storage\DCIM`. On the tested device, Windows exposed folders such as `Internal Storage\202605_a`, so camera import must detect supported media-bearing camera folders, not only one hard-coded DCIM path.

## Known Open Areas

These areas are improved but still not “finished”:

- premium polish pass on remaining controls and visual empty/loading states
- deeper HEIC validation against a broader real-world sample set
- rebuild flow robustness when `catalog.db` is locked
- richer preview experience for video playback controls
- additional polish around export workflow UX and batch operations
- stronger automated coverage beyond the current small test set
- embeddings and hybrid semantic ranking are not implemented yet
- richer video AI:
  - better frame sampling strategy
  - optional more-than-two-frame analysis
  - stronger action/activity summaries
- OpenAI and Ollama provider inference paths are still scaffolding compared to the new LM Studio path
- iPhone import still needs real-device QA across locked, untrusted, low-storage, iCloud-placeholder, and very large DCIM scenarios

## How To Update This Log

Add a new milestone entry when one of these happens:

- a meaningful product capability is completed
- a major reliability bug is fixed
- a technical architecture decision changes future work
- a recurring lesson is learned from debugging or user testing

Each new entry should record:

- what changed
- why it mattered
- the outcome for users or future development
