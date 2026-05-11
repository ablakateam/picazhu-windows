using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Picazhu.AI;
using Picazhu.Cache;
using Picazhu.Core;
using Picazhu.Indexing;
using Picazhu.Media;

namespace Picazhu.App;

public partial class MainViewModel(
    ICatalogRepository catalogRepository,
    ISettingsService settingsService,
    IAppPaths appPaths,
    IQuickPreviewService quickPreviewService,
    IExportService exportService,
    IHeicDecoderService heicDecoderService,
    IMediaProbeService mediaProbeService,
    IndexingService indexingService,
    IAiIndexingService aiIndexingService) : ObservableObject
{
    private const int MediaQueryLimit = 50000;
    private AppSettings _settings = new();
    private readonly List<string> _folderHistory = [];
    private bool _suppressHistoryPush;
    private DiagnosticsSnapshot? _lastDiagnosticsSnapshot;
    private AiIndexingProgressSnapshot? _lastAiProgressSnapshot;
    private bool _isRepairingInvalidThumbs;
    private MediaMetadata? _selectedMediaMetadata;

    public ObservableCollection<FolderNodeViewModel> Roots { get; } = [];
    public ObservableCollection<BreadcrumbItemViewModel> Breadcrumbs { get; } = [];
    public ObservableCollection<MediaTileViewModel> MediaItems { get; } = [];
    public ObservableCollection<MediaTileViewModel> SelectedMediaItems { get; } = [];
    public ObservableCollection<AiTagChipViewModel> ObjectTags { get; } = [];
    public ObservableCollection<AiTagChipViewModel> SceneTags { get; } = [];
    public ObservableCollection<AiTagChipViewModel> LogoTags { get; } = [];
    public ObservableCollection<AiTagChipViewModel> TextTags { get; } = [];
    public ObservableCollection<string> LmStudioAvailableModels { get; } = [];
    public ObservableCollection<string> OllamaAvailableModels { get; } = [];
    public ObservableCollection<string> OllamaCloudAvailableModels { get; } = [];
    public ObservableCollection<string> OpenAiAvailableModels { get; } = [];
    public ObservableCollection<string> PinnedFolders { get; } = [];
    public ObservableCollection<string> RecentFolders { get; } = [];
    public ObservableCollection<SavedSearch> SavedSearches { get; } = [];

    [ObservableProperty] private FolderNodeViewModel? selectedFolder;
    [ObservableProperty] private MediaTileViewModel? selectedMedia;
    [ObservableProperty] private string searchText = string.Empty;
    [ObservableProperty] private bool includeSubfolders = true;
    [ObservableProperty] private bool filterImagesOnly;
    [ObservableProperty] private bool filterVideosOnly;
    [ObservableProperty] private bool filterRecent;
    [ObservableProperty] private bool filterLarge;
    [ObservableProperty] private bool filterPortrait;
    [ObservableProperty] private bool filterLandscape;
    [ObservableProperty] private SortMode sortMode = SortMode.ModifiedDate;
    [ObservableProperty] private string statusText = "Ready";
    [ObservableProperty] private string diagnosticsText = "Loading diagnostics...";
    [ObservableProperty] private string selectedMetadataText = "Select an item to view details.";
    [ObservableProperty] private bool canGoBack;
    [ObservableProperty] private int thumbnailCacheLimitMb;
    [ObservableProperty] private string themeMode = "Dark";
    [ObservableProperty] private bool enableAiGlobally;
    [ObservableProperty] private string aiVisionProviderId = "lmstudio";
    [ObservableProperty] private string openAiApiKeyPlaceholder = string.Empty;
    [ObservableProperty] private string openAiVisionModel = string.Empty;
    [ObservableProperty] private string ollamaEndpoint = "http://localhost:11434";
    [ObservableProperty] private string ollamaVisionModel = string.Empty;
    [ObservableProperty] private string ollamaCloudEndpoint = "https://ollama.com";
    [ObservableProperty] private string ollamaCloudApiKeyPlaceholder = string.Empty;
    [ObservableProperty] private string ollamaCloudVisionModel = string.Empty;
    [ObservableProperty] private string lmStudioEndpoint = "http://localhost:1234/v1";
    [ObservableProperty] private string lmStudioVisionModel = string.Empty;
    [ObservableProperty] private bool isIndexingActive;
    [ObservableProperty] private bool isIndexingCounting;
    [ObservableProperty] private bool isIndexingPaused;
    [ObservableProperty] private double indexingProgressPercent;
    [ObservableProperty] private string indexingProgressText = "Idle";
    [ObservableProperty] private string indexingDetailText = "Waiting for the next scan.";
    [ObservableProperty] private string indexingPauseButtonText = "Pause";
    [ObservableProperty] private bool isThumbnailProcessingActive;
    [ObservableProperty] private double thumbnailProgressPercent;
    [ObservableProperty] private string thumbnailProgressText = "Thumbnails idle";
    [ObservableProperty] private string thumbnailDetailText = "No thumbnail work in progress.";
    [ObservableProperty] private bool isAiIndexingActive;
    [ObservableProperty] private bool isAiIndexingPaused;
    [ObservableProperty] private bool isAiIndexingUnavailable;
    [ObservableProperty] private double aiIndexingProgressPercent;
    [ObservableProperty] private string aiIndexingProgressText = "AI indexing is off";
    [ObservableProperty] private string aiIndexingDetailText = "Enable AI features to allow visual analysis and semantic search.";
    [ObservableProperty] private string aiProviderStatusText = "LM Studio: Not configured | Ollama: Not configured | Ollama Cloud: Not configured | OpenAI: Not configured";
    [ObservableProperty] private bool isExporting;
    [ObservableProperty] private string exportProgressText = "No export in progress.";
    [ObservableProperty] private string exportDetailText = "Select media to export originals.";
    [ObservableProperty] private string selectionSummaryText = "No items selected";
    [ObservableProperty] private string selectionDetailText = "Select images or videos to export originals.";
    [ObservableProperty] private string visibleAiSummaryText = "Visual index is off";
    [ObservableProperty] private string visibleAiDetailText = "Enable AI to analyze visible media.";
    [ObservableProperty] private double visibleAiProgressPercent;
    [ObservableProperty] private string selectedAiCaption = string.Empty;
    [ObservableProperty] private bool isMouseSelectionMode;
    [ObservableProperty] private string mouseSelectionModeText = "Mouse selection off";
    [ObservableProperty] private string settingsStatusText = "Settings ready.";

    public string SourceOfTruthMessage => "PICAZHU indexes your files; it does not move them.";
    public bool HasMediaItems => MediaItems.Count > 0;
    public bool HasSelectedMedia => SelectedMedia is not null;
    public bool HasAnySelectedMedia => SelectedMediaItems.Count > 0;
    public bool HasMultipleSelectedMedia => SelectedMediaItems.Count > 1;
    public bool ShowSinglePreview => SelectedMediaItems.Count == 1 && SelectedMedia is not null;
    public bool ShowTagsTab => ShowSinglePreview;
    public bool HasSelectedAiCaption => !string.IsNullOrWhiteSpace(SelectedAiCaption);
    public bool HasRoots => Roots.Count > 0;
    public bool HasDeterministicIndexingProgress => IsIndexingActive && !IsIndexingCounting && IndexingProgressPercent > 0;
    public bool HasIndexingControls => IsIndexingActive || IsIndexingPaused;
    public bool HasThumbnailProgress => IsThumbnailProcessingActive || ThumbnailProgressPercent > 0;
    public bool HasAiIndexingControls => IsAiIndexingActive || IsAiIndexingPaused;
    public bool HasAiProgress => EnableAiGlobally && (IsAiIndexingActive || IsAiIndexingPaused || IsAiIndexingUnavailable || AiIndexingProgressPercent > 0);
    public bool ShowAiStatus => true;
    public bool CanExportSelected => HasAnySelectedMedia && !IsExporting;
    public bool ShowMouseSelectionBadge => IsMouseSelectionMode;
    public bool ShowVisibleAiStatus => EnableAiGlobally && HasMediaItems;
    public string AiToggleButtonText => EnableAiGlobally ? "AI On" : "AI Off";
    public string AiToggleHintText => EnableAiGlobally ? "Background analysis enabled" : "Fast mode active";
    public string ActiveAiModelSummary => AiVisionProviderId switch
    {
        "lmstudio" => string.IsNullOrWhiteSpace(LmStudioVisionModel) ? "No LM Studio model selected" : LmStudioVisionModel,
        "ollama" => string.IsNullOrWhiteSpace(OllamaVisionModel) ? "No Ollama model selected" : OllamaVisionModel,
        "ollama-cloud" => string.IsNullOrWhiteSpace(OllamaCloudVisionModel) ? "No Ollama Cloud model selected" : OllamaCloudVisionModel,
        "openai" => string.IsNullOrWhiteSpace(OpenAiVisionModel) ? "No OpenAI model selected" : OpenAiVisionModel,
        _ => "No model selected"
    };
    public bool CanOpenInMaps => ShowSinglePreview &&
                                 _selectedMediaMetadata?.GpsLat is not null &&
                                 _selectedMediaMetadata.GpsLon is not null;
    public bool HasObjectTags => ObjectTags.Count > 0;
    public bool HasSceneTags => SceneTags.Count > 0;
    public bool HasLogoTags => LogoTags.Count > 0;
    public bool HasTextTags => TextTags.Count > 0;

    public event Action? MediaItemsRefreshed;

    public async Task LoadAsync()
    {
        _settings = await settingsService.LoadAsync();
        if (_settings.EnableAiGlobally)
        {
            _settings = _settings with { EnableAiGlobally = false };
            await settingsService.SaveAsync(_settings);
            SettingsStatusText = "AI starts off by default. Enable it explicitly when you want background analysis.";
        }

        IncludeSubfolders = _settings.ShowSubfoldersByDefault;
        ThumbnailCacheLimitMb = _settings.ThumbnailCacheLimitMb;
        ThemeMode = string.IsNullOrWhiteSpace(_settings.ThemeMode) ? "Dark" : _settings.ThemeMode;
        EnableAiGlobally = _settings.EnableAiGlobally;
        AiVisionProviderId = NormalizeAiVisionProviderId(_settings.AiVisionProviderId);
        OpenAiApiKeyPlaceholder = _settings.OpenAiApiKeyPlaceholder ?? string.Empty;
        OpenAiVisionModel = _settings.OpenAiVisionModel ?? string.Empty;
        OllamaEndpoint = _settings.OllamaEndpoint ?? "http://localhost:11434";
        OllamaVisionModel = _settings.OllamaVisionModel ?? string.Empty;
        OllamaCloudEndpoint = _settings.OllamaCloudEndpoint ?? "https://ollama.com";
        OllamaCloudApiKeyPlaceholder = _settings.OllamaCloudApiKeyPlaceholder ?? string.Empty;
        OllamaCloudVisionModel = _settings.OllamaCloudVisionModel ?? string.Empty;
        LmStudioEndpoint = _settings.LmStudioEndpoint ?? "http://localhost:1234/v1";
        LmStudioVisionModel = _settings.LmStudioVisionModel ?? string.Empty;
        await aiIndexingService.InitializeAsync(_settings);
        await RefreshFoldersAsync();
        await RefreshMediaAsync(refreshDiagnostics: false);
        await RefreshDiagnosticsAsync();
    }

    public async Task RefreshFoldersAsync()
    {
        var selectedPath = SelectedFolder?.FullPath;
        Roots.Clear();
        var watchedRoots = await catalogRepository.GetWatchedRootsAsync();
        foreach (var root in watchedRoots)
        {
            var folders = await catalogRepository.GetFoldersAsync(root.Id);
            Roots.Add(FolderNodeViewModel.BuildTree(root, folders));
        }

        await NormalizeStoredPathsAsync(watchedRoots);
        if (!string.IsNullOrWhiteSpace(selectedPath))
        {
            SelectedFolder = FindFolderByPath(selectedPath) ?? Roots.FirstOrDefault();
        }
        else
        {
            SelectedFolder ??= Roots.FirstOrDefault();
        }

        OnPropertyChanged(nameof(HasRoots));
        UpdateBreadcrumbs();
    }

    public async Task RefreshMediaAsync(bool refreshDiagnostics = true)
    {
        var selectedIds = SelectedMediaItems.Select(item => item.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var activeSelectedId = SelectedMedia?.Id;
        MediaItems.Clear();
        var query = new MediaQuery(
            SelectedFolder?.FullPath,
            IncludeSubfolders,
            SearchText,
            FilterImagesOnly,
            FilterVideosOnly,
            FilterRecent,
            FilterLarge,
            FilterPortrait,
            FilterLandscape,
            SortMode,
            MediaQueryLimit);

        var mediaModels = await catalogRepository.QueryMediaAsync(query);
        var aiStates = await catalogRepository.GetAiAnalysisStatesAsync(mediaModels.Select(item => item.Id).ToList());
        foreach (var item in mediaModels)
        {
            AiAnalysisState? aiState = aiStates.TryGetValue(item.Id, out var state)
                ? state
                : null;
            MediaItems.Add(new MediaTileViewModel(item, appPaths, aiState));
        }

        var rehydratedSelection = selectedIds.Count == 0
            ? []
            : MediaItems.Where(item => selectedIds.Contains(item.Id)).ToList();
        Replace(SelectedMediaItems, rehydratedSelection);
        if (SelectedMediaItems.Count == 0)
        {
            SelectedMedia = null;
        }
        else if (!string.IsNullOrWhiteSpace(activeSelectedId) && SelectedMediaItems.Any(item => string.Equals(item.Id, activeSelectedId, StringComparison.OrdinalIgnoreCase)))
        {
            SelectedMedia = SelectedMediaItems.First(item => string.Equals(item.Id, activeSelectedId, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            SelectedMedia = SelectedMediaItems.Last();
        }

        UpdateSelectionState();
        UpdateVisibleAiStatus();
        await RepairInvalidCurrentViewThumbnailsAsync();
        MediaItemsRefreshed?.Invoke();

        StatusText = MediaItems.Count >= MediaQueryLimit
            ? $"{MediaItems.Count}+ item(s)"
            : $"{MediaItems.Count} item(s)";
        OnPropertyChanged(nameof(HasMediaItems));
        OnPropertyChanged(nameof(HasSelectedMedia));

        if (refreshDiagnostics)
        {
            await RefreshDiagnosticsAsync();
        }
    }

    public async Task RefreshDiagnosticsAsync()
    {
        var diagnostics = await GetDiagnosticsSnapshotAsync();
        _lastDiagnosticsSnapshot = diagnostics;
        UpdateIndexingProgress(indexingService.GetProgress());
        UpdateThumbnailProgress(diagnostics);
        UpdateAiProgress(aiIndexingService.GetProgress(), aiIndexingService.GetProviderStatuses());
        _lastAiProgressSnapshot = aiIndexingService.GetProgress();
        UpdateDiagnosticsText(diagnostics);
    }

    public async Task RefreshIndexingUiAsync()
    {
        var diagnostics = await GetDiagnosticsSnapshotAsync();
        var previous = _lastDiagnosticsSnapshot;
        _lastDiagnosticsSnapshot = diagnostics;
        UpdateIndexingProgress(indexingService.GetProgress());
        UpdateThumbnailProgress(diagnostics);
        var aiProgress = aiIndexingService.GetProgress();
        var previousAi = _lastAiProgressSnapshot;
        _lastAiProgressSnapshot = aiProgress;
        UpdateAiProgress(aiProgress, aiIndexingService.GetProviderStatuses());
        UpdateDiagnosticsText(diagnostics);

        if (previous is null)
        {
            return;
        }

        var mediaChanged =
            diagnostics.MediaCount != previous.MediaCount ||
            diagnostics.PendingMetadataItems != previous.PendingMetadataItems ||
            diagnostics.PendingThumbItems != previous.PendingThumbItems ||
            diagnostics.FailedMetadataItems != previous.FailedMetadataItems ||
            diagnostics.FailedThumbItems != previous.FailedThumbItems;

        var foldersChanged =
            diagnostics.FolderCount != previous.FolderCount ||
            diagnostics.WatchedRoots != previous.WatchedRoots;
        var currentViewHasUnresolvedThumbs = MediaItems.Any(item => !item.IsThumbnailReady && item.ThumbState != ThumbState.Failed);

        if (foldersChanged)
        {
            await RefreshFoldersAsync();
        }

        if ((mediaChanged || foldersChanged || (currentViewHasUnresolvedThumbs && (diagnostics.PendingThumbItems > 0 || diagnostics.ThumbnailQueueDepth > 0))) && SelectedFolder is not null)
        {
            await RefreshMediaAsync(refreshDiagnostics: false);
        }

        var aiChanged = previousAi is null ||
                        aiProgress.PendingItems != previousAi.PendingItems ||
                        aiProgress.CompletedItems != previousAi.CompletedItems ||
                        aiProgress.FailedItems != previousAi.FailedItems ||
                        aiProgress.IsActive != previousAi.IsActive;

        if (aiChanged && SelectedFolder is not null)
        {
            await RefreshMediaAsync(refreshDiagnostics: false);
        }
    }

    public Task ToggleIndexingPauseAsync()
    {
        if (IsIndexingPaused)
        {
            indexingService.ResumeProcessing();
        }
        else
        {
            indexingService.PauseProcessing();
        }

        UpdateIndexingProgress(indexingService.GetProgress());
        return Task.CompletedTask;
    }

    public Task StopIndexingAsync()
    {
        indexingService.StopCurrentWork();
        UpdateIndexingProgress(indexingService.GetProgress());
        return Task.CompletedTask;
    }

    private async Task<DiagnosticsSnapshot> GetDiagnosticsSnapshotAsync()
    {
        var cacheBytes = await new ThumbnailCacheService(appPaths).GetCacheSizeBytesAsync();
        return await catalogRepository.GetDiagnosticsAsync(indexingService.WatcherCount, indexingService.ScanQueueDepth, indexingService.MetadataQueueDepth, indexingService.ThumbnailQueueDepth, cacheBytes);
    }

    private void UpdateDiagnosticsText(DiagnosticsSnapshot diagnostics)
    {
        var heic = heicDecoderService.GetDiagnostics();
        var aiProgress = aiIndexingService.GetProgress();
        DiagnosticsText = $"Roots {diagnostics.WatchedRoots} | Folders {diagnostics.FolderCount} | Media {diagnostics.MediaCount}\n" +
                          $"Pending metadata {diagnostics.PendingMetadataItems} | Pending thumbs {diagnostics.PendingThumbItems}\n" +
                          $"Failed metadata {diagnostics.FailedMetadataItems} | Failed thumbs {diagnostics.FailedThumbItems}\n" +
                          $"Watchers {diagnostics.WatcherCount} | Scan queue {diagnostics.ScanQueueDepth} | Metadata queue {diagnostics.MetadataQueueDepth} | Thumb queue {diagnostics.ThumbnailQueueDepth}\n" +
                          $"AI status {aiProgress.StatusText}\n" +
                          $"AI detail {aiProgress.DetailText}\n" +
                          $"AI providers {AiProviderStatusText}\n" +
                          $"Thumb cache {diagnostics.ThumbnailCacheBytes / 1024d / 1024d:F1} MB | Last scan {diagnostics.LastScanCompletedUtc?.ToLocalTime():g}\n" +
                          $"HEIC native WIC {(heic.NativeWicDetected ? (heic.NativeWicHealthy ? "healthy" : "detected but failing") : "not yet validated")}\n" +
                          $"HEIC libheif fallback {(heic.LibheifRegistered ? "ready" : "not available")} | active path {heic.ActivePath}\n" +
                          $"HEIC status {heic.Summary}\n" +
                          $"HEIC last error {heic.LastError ?? "none"}";
    }

    private void UpdateIndexingProgress(IndexingProgressSnapshot progress)
    {
        IsIndexingActive = progress.IsActive;
        IsIndexingPaused = progress.IsPaused;
        IsIndexingCounting = progress.IsActive && progress.IsCounting;
        IndexingPauseButtonText = progress.IsPaused ? "Resume" : "Pause";
        OnPropertyChanged(nameof(HasIndexingControls));

        if (!progress.IsActive && !progress.IsPaused)
        {
            IndexingProgressPercent = 0;
            IndexingProgressText = "Idle";
            IndexingDetailText = "Waiting for the next scan.";
            OnPropertyChanged(nameof(HasDeterministicIndexingProgress));
            return;
        }

        if (progress.IsPaused)
        {
            IndexingProgressPercent = progress.TotalFiles <= 0
                ? 0
                : Math.Clamp((double)progress.FilesIndexed / progress.TotalFiles * 100d, 0d, 100d);
            IndexingProgressText = $"Indexing paused for {progress.RootName ?? "library"}";
            IndexingDetailText = progress.TotalFiles > 0
                ? $"{progress.FilesIndexed:N0} of {progress.TotalFiles:N0} files indexed before pause."
                : "Background indexing is paused.";
            OnPropertyChanged(nameof(HasDeterministicIndexingProgress));
            return;
        }

        if (progress.IsCounting)
        {
            IndexingProgressPercent = 0;
            IndexingProgressText = $"Counting files in {progress.RootName ?? "library"}";
            IndexingDetailText = "Preparing an indexing estimate for this root.";
            OnPropertyChanged(nameof(HasDeterministicIndexingProgress));
            return;
        }

        var percent = progress.TotalFiles <= 0
            ? 0
            : Math.Clamp((double)progress.FilesIndexed / progress.TotalFiles * 100d, 0d, 100d);
        IndexingProgressPercent = percent;
        IndexingProgressText = $"Indexing {progress.RootName ?? "library"}";
        IndexingDetailText = $"{progress.FilesIndexed:N0} of {progress.TotalFiles:N0} files indexed ({percent:F0}%)";
        OnPropertyChanged(nameof(HasDeterministicIndexingProgress));
    }

    private void UpdateThumbnailProgress(DiagnosticsSnapshot diagnostics)
    {
        var viewTotal = MediaItems.Count;
        var viewReady = MediaItems.Count(item => item.IsThumbnailReady);
        var viewFailed = MediaItems.Count(item => item.ThumbState == ThumbState.Failed);
        var viewPending = Math.Max(0, viewTotal - viewReady - viewFailed);
        var libraryReady = Math.Max(0, diagnostics.MediaCount - diagnostics.PendingThumbItems - diagnostics.FailedThumbItems);
        var libraryTotal = Math.Max(diagnostics.MediaCount, libraryReady);

        if (libraryTotal <= 0 && viewTotal <= 0)
        {
            IsThumbnailProcessingActive = false;
            ThumbnailProgressPercent = 0;
            ThumbnailProgressText = "Thumbnails idle";
            ThumbnailDetailText = "No media has been indexed yet.";
            OnPropertyChanged(nameof(HasThumbnailProgress));
            return;
        }

        ThumbnailProgressPercent = viewTotal <= 0
            ? Math.Clamp(libraryTotal == 0 ? 0d : (double)libraryReady / libraryTotal * 100d, 0d, 100d)
            : Math.Clamp((double)viewReady / viewTotal * 100d, 0d, 100d);
        IsThumbnailProcessingActive = diagnostics.PendingThumbItems > 0 || diagnostics.ThumbnailQueueDepth > 0;

        if (IsThumbnailProcessingActive)
        {
            ThumbnailProgressText = viewTotal > 0
                ? $"View thumbnails {viewReady:N0} of {viewTotal:N0} ready"
                : "Rendering thumbnails";
            ThumbnailDetailText = $"Library thumbnails {libraryReady:N0} of {libraryTotal:N0} ready, {diagnostics.PendingThumbItems:N0} pending";
        }
        else if (viewFailed > 0 || diagnostics.FailedThumbItems > 0)
        {
            ThumbnailProgressText = viewTotal > 0
                ? $"View thumbnails {viewReady:N0} of {viewTotal:N0} ready"
                : "Thumbnail pass completed";
            ThumbnailDetailText = $"Library thumbnails {libraryReady:N0} of {libraryTotal:N0} ready, {diagnostics.FailedThumbItems:N0} failed";
        }
        else
        {
            ThumbnailProgressText = viewTotal > 0
                ? $"View thumbnails {viewReady:N0} of {viewTotal:N0} ready"
                : "All thumbnails ready";
            ThumbnailDetailText = $"Library thumbnails {libraryReady:N0} of {libraryTotal:N0} ready.";
        }

        if (viewPending > 0 && !IsThumbnailProcessingActive)
        {
            ThumbnailDetailText += $" {viewPending:N0} in view still need regeneration.";
        }

        OnPropertyChanged(nameof(HasThumbnailProgress));
    }

    private void UpdateAiProgress(AiIndexingProgressSnapshot progress, IReadOnlyList<AiProviderStatusSnapshot> statuses)
    {
        IsAiIndexingActive = progress.IsActive;
        IsAiIndexingPaused = progress.IsPaused;
        IsAiIndexingUnavailable = progress.IsUnavailable;
        AiIndexingProgressText = progress.StatusText;
        AiIndexingDetailText = progress.DetailText;
        AiIndexingProgressPercent = progress.TotalItems <= 0
            ? 0
            : Math.Clamp((double)progress.CompletedItems / progress.TotalItems * 100d, 0d, 100d);
        AiProviderStatusText = statuses.Count == 0
            ? "LM Studio: Not configured | Ollama: Not configured | Ollama Cloud: Not configured | OpenAI: Not configured"
            : string.Join(" | ", statuses.Select(status => $"{status.DisplayName}: {status.Summary}"));
        var lmStudioModels = statuses.FirstOrDefault(status => status.ProviderId == "lmstudio")?.AvailableModels ?? [];
        var ollamaModels = statuses.FirstOrDefault(status => status.ProviderId == "ollama")?.AvailableModels ?? [];
        var ollamaCloudModels = statuses.FirstOrDefault(status => status.ProviderId == "ollama-cloud")?.AvailableModels ?? [];
        var openAiModels = statuses.FirstOrDefault(status => status.ProviderId == "openai")?.AvailableModels ?? AiProviderModelCatalog.DefaultOpenAiVisionModels;
        Replace(LmStudioAvailableModels, lmStudioModels);
        Replace(OllamaAvailableModels, ollamaModels);
        Replace(OllamaCloudAvailableModels, ollamaCloudModels);
        Replace(OpenAiAvailableModels, openAiModels);

        if (string.IsNullOrWhiteSpace(LmStudioVisionModel))
        {
            var detectedVisionModel = lmStudioModels.FirstOrDefault(AiProviderModelCatalog.IsVisionModelId);
            if (!string.IsNullOrWhiteSpace(detectedVisionModel))
            {
                LmStudioVisionModel = detectedVisionModel;
            }
        }

        if (string.IsNullOrWhiteSpace(OllamaVisionModel))
        {
            var detectedVisionModel = ollamaModels.FirstOrDefault(AiProviderModelCatalog.IsVisionModelId);
            if (!string.IsNullOrWhiteSpace(detectedVisionModel))
            {
                OllamaVisionModel = detectedVisionModel;
            }
        }

        if (string.IsNullOrWhiteSpace(OllamaCloudVisionModel))
        {
            var detectedVisionModel = ollamaCloudModels.FirstOrDefault(AiProviderModelCatalog.IsVisionModelId);
            if (!string.IsNullOrWhiteSpace(detectedVisionModel))
            {
                OllamaCloudVisionModel = detectedVisionModel;
            }
        }

        if (string.IsNullOrWhiteSpace(OpenAiVisionModel))
        {
            OpenAiVisionModel = openAiModels.FirstOrDefault(AiProviderModelCatalog.IsOpenAiVisionModelId) ?? AiProviderModelCatalog.DefaultOpenAiVisionModel;
        }

        OnPropertyChanged(nameof(ActiveAiModelSummary));
        OnPropertyChanged(nameof(HasAiIndexingControls));
        OnPropertyChanged(nameof(HasAiProgress));
        OnPropertyChanged(nameof(ShowAiStatus));
    }

    private void UpdateVisibleAiStatus()
    {
        var eligibleImages = MediaItems.ToList();
        if (!EnableAiGlobally)
        {
            VisibleAiProgressPercent = 0;
            VisibleAiSummaryText = "Visual index is off";
            VisibleAiDetailText = "Enable AI to analyze visible media.";
            OnPropertyChanged(nameof(ShowVisibleAiStatus));
            return;
        }

        if (eligibleImages.Count == 0)
        {
            VisibleAiProgressPercent = 0;
            VisibleAiSummaryText = "This view has no eligible media";
            VisibleAiDetailText = "Add images or videos to start the visual index in this view.";
            OnPropertyChanged(nameof(ShowVisibleAiStatus));
            return;
        }

        var done = eligibleImages.Count(item => item.AiState == AiAnalysisState.Done);
        var processing = eligibleImages.Count(item => item.AiState is AiAnalysisState.Pending or AiAnalysisState.Processing);
        var failed = eligibleImages.Count(item => item.AiState == AiAnalysisState.Failed);
        var notQueued = eligibleImages.Count(item => item.AiState is null);
        var started = done + processing + failed;
        VisibleAiProgressPercent = Math.Clamp((double)done / eligibleImages.Count * 100d, 0d, 100d);
        VisibleAiSummaryText = $"Visual index: {done:N0} of {eligibleImages.Count:N0} visible media item(s) analyzed";
        VisibleAiDetailText = processing > 0
            ? $"{processing:N0} visible media item(s) are pending or processing. {failed:N0} failed. {notQueued:N0} not queued yet."
            : notQueued > 0
                ? $"{started:N0} visible media item(s) have AI records. {notQueued:N0} have not been queued yet."
            : failed > 0
                ? $"{failed:N0} visible media item(s) failed analysis."
                : "All visible media in this view is analyzed.";
        OnPropertyChanged(nameof(ShowVisibleAiStatus));
    }

    private async Task RepairInvalidCurrentViewThumbnailsAsync()
    {
        if (_isRepairingInvalidThumbs)
        {
            return;
        }

        var invalidItems = MediaItems
            .Where(item => item.ThumbState == ThumbState.Done && !item.IsThumbnailReady)
            .Select(item => item.Model)
            .ToList();

        if (invalidItems.Count == 0)
        {
            return;
        }

        _isRepairingInvalidThumbs = true;
        try
        {
            foreach (var item in invalidItems)
            {
                await catalogRepository.UpdateMediaThumbnailStateAsync(item.Id, ThumbState.Pending, null, null);
                await indexingService.QueueThumbnailAsync(new ThumbnailRequest(
                    item.Id,
                    item.FullPath,
                    item.ModifiedUtc,
                    item.SizeBytes,
                    item.MediaKind,
                    360,
                    240));
            }
        }
        finally
        {
            _isRepairingInvalidThumbs = false;
        }
    }

    public async Task AddFolderAsync(string folderPath, bool includeSubfolders)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
        {
            return;
        }

        await catalogRepository.AddWatchedRootAsync(folderPath, includeSubfolders);
        await indexingService.WatchRootsAsync();
        await RefreshFoldersAsync();
        await indexingService.RescanAllAsync();
        await RefreshMediaAsync();
    }

    public async Task RemoveSelectedRootAsync()
    {
        if (SelectedFolder?.RootId is null || string.IsNullOrWhiteSpace(SelectedFolder.FullPath))
        {
            return;
        }

        var rootId = SelectedFolder.RootId;
        var roots = await catalogRepository.GetWatchedRootsAsync();
        var root = roots.FirstOrDefault(item => string.Equals(item.Id, rootId, StringComparison.OrdinalIgnoreCase));
        var rootPath = root?.Path ?? SelectedFolder.FullPath;

        indexingService.StopCurrentWork();
        await catalogRepository.RemoveWatchedRootAsync(rootId);
        RemovePathsInsideRoot(rootPath);
        await settingsService.SaveAsync(_settings);
        await indexingService.WatchRootsAsync();
        SelectedFolder = null;
        UpdateCanGoBack();
        await RefreshFoldersAsync();
        await RefreshMediaAsync();
    }

    public async Task RescanAsync()
    {
        await indexingService.RescanAllAsync();
        StatusText = "Rescan queued";
    }

    public async Task RebuildAsync()
    {
        var roots = await catalogRepository.GetWatchedRootsAsync();
        var rootDefinitions = roots.Select(root => new { root.Path, root.IncludeSubfolders }).ToList();
        await catalogRepository.RebuildCatalogAsync();
        foreach (var root in rootDefinitions)
        {
            await catalogRepository.AddWatchedRootAsync(root.Path, root.IncludeSubfolders);
        }

        await indexingService.WatchRootsAsync();
        await RefreshFoldersAsync();
        await indexingService.RescanAllAsync();
        await RefreshMediaAsync();
    }

    public async Task SelectFolderAsync(FolderNodeViewModel folder)
    {
        if (!_suppressHistoryPush && SelectedFolder is not null && !string.Equals(SelectedFolder.FullPath, folder.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            _folderHistory.Add(SelectedFolder.FullPath);
            UpdateCanGoBack();
        }

        SelectedFolder = folder;
        PushRecent(folder.FullPath);
        UpdateBreadcrumbs();
        await RefreshMediaAsync();
    }

    public async Task<bool> TrySelectFolderByPathAsync(string fullPath)
    {
        var folder = FindFolderByPath(fullPath);
        if (folder is null)
        {
            await NormalizeStoredPathsAsync();
            return false;
        }

        await SelectFolderAsync(folder);
        return true;
    }

    public async Task NavigateToBreadcrumbAsync(BreadcrumbItemViewModel breadcrumb)
    {
        var target = FindFolderByPath(breadcrumb.FullPath);
        if (target is not null)
        {
            await SelectFolderAsync(target);
        }
    }

    public async Task GoBackAsync()
    {
        while (_folderHistory.Count > 0)
        {
            var path = _folderHistory[^1];
            _folderHistory.RemoveAt(_folderHistory.Count - 1);
            var folder = FindFolderByPath(path);
            if (folder is null)
            {
                continue;
            }

            _suppressHistoryPush = true;
            try
            {
                await SelectFolderAsync(folder);
            }
            finally
            {
                _suppressHistoryPush = false;
            }

            break;
        }

        UpdateCanGoBack();
    }

    public async Task SelectSavedSearchAsync(SavedSearch search)
    {
        SearchText = search.QueryText;
        SortMode = search.SortMode;
        await RefreshMediaAsync();
    }

    public async Task SaveCurrentSearchAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return;
        }

        var updated = _settings.SavedSearches.ToList();
        updated.RemoveAll(item => string.Equals(item.QueryText, SearchText, StringComparison.OrdinalIgnoreCase));
        updated.Insert(0, new SavedSearch(SearchText, SearchText, SortMode));
        _settings = _settings with { SavedSearches = updated.Take(12).ToList() };
        await settingsService.SaveAsync(_settings);
        LoadSettingsCollections();
    }

    public async Task TogglePinSelectedFolderAsync()
    {
        if (SelectedFolder is null)
        {
            return;
        }

        var pinned = _settings.PinnedFolderPaths.ToList();
        if (pinned.Contains(SelectedFolder.FullPath, StringComparer.OrdinalIgnoreCase))
        {
            pinned.RemoveAll(path => string.Equals(path, SelectedFolder.FullPath, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            pinned.Insert(0, SelectedFolder.FullPath);
        }

        _settings = _settings with { PinnedFolderPaths = pinned.Take(20).ToList() };
        await settingsService.SaveAsync(_settings);
        LoadSettingsCollections();
    }

    public async Task UpdateSettingsAsync()
    {
        await ApplySettingsSnapshotAsync(BuildPendingSettings());
    }

    public async Task RefreshAiProviderStatusAsync()
    {
        var pendingSettings = BuildPendingSettings();
        ApplySettingsToUi(pendingSettings);
        await aiIndexingService.RefreshProviderStatusesAsync(pendingSettings);
        await RefreshDiagnosticsAsync();
        SettingsStatusText = "Provider readiness refreshed from current form values.";
    }

    public async Task ToggleAiFeaturesAsync()
    {
        EnableAiGlobally = !EnableAiGlobally;
        await UpdateSettingsAsync();
        StatusText = EnableAiGlobally ? "AI features enabled" : "AI features disabled";
        SettingsStatusText = EnableAiGlobally
            ? "AI is on. OCR and future visual analysis can run in the background."
            : "AI is off. PICAZHU is back in its lightest mode.";
    }

    private AppSettings BuildPendingSettings()
    {
        return _settings with
        {
            ThumbnailCacheLimitMb = Math.Max(256, ThumbnailCacheLimitMb),
            ShowSubfoldersByDefault = IncludeSubfolders,
            ThemeMode = string.IsNullOrWhiteSpace(ThemeMode) ? "Dark" : ThemeMode,
            EnableAiGlobally = EnableAiGlobally,
            AiVisionProviderId = NormalizeAiVisionProviderId(AiVisionProviderId),
            OpenAiApiKeyPlaceholder = string.IsNullOrWhiteSpace(OpenAiApiKeyPlaceholder) ? null : OpenAiApiKeyPlaceholder.Trim(),
            OpenAiVisionModel = string.IsNullOrWhiteSpace(OpenAiVisionModel) ? null : OpenAiVisionModel.Trim(),
            OllamaEndpoint = string.IsNullOrWhiteSpace(OllamaEndpoint) ? "http://localhost:11434" : OllamaEndpoint.Trim(),
            OllamaVisionModel = string.IsNullOrWhiteSpace(OllamaVisionModel) ? null : OllamaVisionModel.Trim(),
            OllamaCloudEndpoint = string.IsNullOrWhiteSpace(OllamaCloudEndpoint) ? "https://ollama.com" : OllamaCloudEndpoint.Trim(),
            OllamaCloudApiKeyPlaceholder = string.IsNullOrWhiteSpace(OllamaCloudApiKeyPlaceholder) ? null : OllamaCloudApiKeyPlaceholder.Trim(),
            OllamaCloudVisionModel = string.IsNullOrWhiteSpace(OllamaCloudVisionModel) ? null : OllamaCloudVisionModel.Trim(),
            LmStudioEndpoint = string.IsNullOrWhiteSpace(LmStudioEndpoint) ? "http://localhost:1234/v1" : LmStudioEndpoint.Trim(),
            LmStudioVisionModel = string.IsNullOrWhiteSpace(LmStudioVisionModel) ? null : LmStudioVisionModel.Trim()
        };
    }

    private void ApplySettingsToUi(AppSettings settings)
    {
        ThumbnailCacheLimitMb = settings.ThumbnailCacheLimitMb;
        ThemeMode = settings.ThemeMode;
        EnableAiGlobally = settings.EnableAiGlobally;
        AiVisionProviderId = NormalizeAiVisionProviderId(settings.AiVisionProviderId);
        OpenAiApiKeyPlaceholder = settings.OpenAiApiKeyPlaceholder ?? string.Empty;
        OpenAiVisionModel = settings.OpenAiVisionModel ?? string.Empty;
        OllamaEndpoint = settings.OllamaEndpoint ?? "http://localhost:11434";
        OllamaVisionModel = settings.OllamaVisionModel ?? string.Empty;
        OllamaCloudEndpoint = settings.OllamaCloudEndpoint ?? "https://ollama.com";
        OllamaCloudApiKeyPlaceholder = settings.OllamaCloudApiKeyPlaceholder ?? string.Empty;
        OllamaCloudVisionModel = settings.OllamaCloudVisionModel ?? string.Empty;
        LmStudioEndpoint = settings.LmStudioEndpoint ?? "http://localhost:1234/v1";
        LmStudioVisionModel = settings.LmStudioVisionModel ?? string.Empty;
        OnPropertyChanged(nameof(ActiveAiModelSummary));
    }

    partial void OnEnableAiGloballyChanged(bool value)
    {
        OnPropertyChanged(nameof(AiToggleButtonText));
        OnPropertyChanged(nameof(AiToggleHintText));
    }

    partial void OnAiVisionProviderIdChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));
    partial void OnOpenAiVisionModelChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));
    partial void OnOllamaVisionModelChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));
    partial void OnOllamaCloudVisionModelChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));
    partial void OnLmStudioVisionModelChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));

    public async Task UpdateSelectionAsync(IReadOnlyList<MediaTileViewModel> selectedItems)
    {
        Replace(SelectedMediaItems, selectedItems.DistinctBy(item => item.Id));

        if (SelectedMediaItems.Count == 0)
        {
            SelectedMedia = null;
        }
        else if (SelectedMediaItems.Count == 1)
        {
            SelectedMedia = SelectedMediaItems[0];
        }
        else if (SelectedMedia is null || !SelectedMediaItems.Any(item => item.Id == SelectedMedia.Id))
        {
            SelectedMedia = SelectedMediaItems.Last();
        }

        UpdateSelectionState();
        await LoadSelectedMetadataAsync();
    }

    public async Task SetActiveSelectionAsync(MediaTileViewModel? item)
    {
        if (item is null)
        {
            SelectedMedia = SelectedMediaItems.LastOrDefault();
        }
        else if (SelectedMediaItems.Any(selected => selected.Id == item.Id))
        {
            SelectedMedia = item;
        }

        UpdateSelectionState();
        await LoadSelectedMetadataAsync();
    }

    public void ClearSelection()
    {
        Replace(SelectedMediaItems, []);
        SelectedMedia = null;
        IsMouseSelectionMode = false;
        UpdateSelectionState();
        SelectedMetadataText = "Select an item to view details.";
    }

    public void SetMouseSelectionMode(bool isEnabled)
    {
        IsMouseSelectionMode = isEnabled;
        MouseSelectionModeText = isEnabled ? "Mouse selection on" : "Mouse selection off";
        OnPropertyChanged(nameof(ShowMouseSelectionBadge));
    }

    public async Task<ExportResult?> ExportSelectedAsync(string destinationFolder)
    {
        if (!HasAnySelectedMedia || string.IsNullOrWhiteSpace(destinationFolder))
        {
            return null;
        }

        IsExporting = true;
        OnPropertyChanged(nameof(CanExportSelected));
        ExportProgressText = "Preparing export";
        ExportDetailText = $"Copying {SelectedMediaItems.Count:N0} original file(s).";

        try
        {
            var progress = new Progress<ExportProgressSnapshot>(snapshot =>
            {
                IsExporting = snapshot.IsRunning;
                ExportProgressText = snapshot.IsRunning
                    ? $"Exporting {snapshot.CompletedItems:N0} of {snapshot.TotalItems:N0}"
                    : "Export complete";
                ExportDetailText = snapshot.IsRunning
                    ? $"{snapshot.CopiedItems:N0} copied, {snapshot.RenamedItems:N0} renamed, {snapshot.FailedItems:N0} failed. Current: {snapshot.CurrentFileName ?? "working"}"
                    : $"{snapshot.CopiedItems:N0} copied, {snapshot.RenamedItems:N0} renamed, {snapshot.FailedItems:N0} failed.";
                OnPropertyChanged(nameof(CanExportSelected));
            });

            var result = await exportService.ExportOriginalsAsync(new ExportRequest(
                SelectedMediaItems.Select(item => new ExportSourceItem(item.Id, item.FullPath, item.FileName, item.Model.SizeBytes)).ToList(),
                destinationFolder),
                progress);

            ExportProgressText = "Export complete";
            ExportDetailText = $"{result.CopiedCount:N0} copied, {result.RenamedCount:N0} renamed, {result.FailedCount:N0} failed.";
            return result;
        }
        finally
        {
            IsExporting = false;
            OnPropertyChanged(nameof(CanExportSelected));
        }
    }

    public async Task OpenSelectedAsync()
    {
        if (SelectedMedia is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(SelectedMedia.FullPath) { UseShellExecute = true });
        await Task.CompletedTask;
    }

    public Task RevealSelectedInFolderAsync()
    {
        if (SelectedMedia is null || !File.Exists(SelectedMedia.FullPath))
        {
            return Task.CompletedTask;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{SelectedMedia.FullPath}\"")
        {
            UseShellExecute = true
        });

        return Task.CompletedTask;
    }

    public Task OpenSelectedInMapsAsync()
    {
        if (!CanOpenInMaps || _selectedMediaMetadata?.GpsLat is null || _selectedMediaMetadata.GpsLon is null)
        {
            return Task.CompletedTask;
        }

        var latitude = _selectedMediaMetadata.GpsLat.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        var longitude = _selectedMediaMetadata.GpsLon.Value.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
        var url = $"https://www.bing.com/maps?cp={latitude}~{longitude}&lvl=16&style=r";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        return Task.CompletedTask;
    }

    public async Task<QuickPreviewResult?> GetQuickPreviewAsync()
    {
        if (SelectedMedia is null)
        {
            return null;
        }

        return await quickPreviewService.GetPreviewAsync(SelectedMedia.Model);
    }

    public async Task ApplyTagSearchAsync(string? term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return;
        }

        SearchText = term.Trim();
        await RefreshMediaAsync();
    }

    public async Task LoadSelectedMetadataAsync()
    {
        if (SelectedMediaItems.Count > 1)
        {
            _selectedMediaMetadata = null;
            ResetSelectedTags();
            var imageCount = SelectedMediaItems.Count(item => !item.IsVideo);
            var videoCount = SelectedMediaItems.Count(item => item.IsVideo);
            var totalBytes = SelectedMediaItems.Sum(item => item.Model.SizeBytes);
            SelectedMetadataText =
                $"{SelectedMediaItems.Count:N0} items selected\n" +
                $"Images: {imageCount:N0} | Videos: {videoCount:N0}\n" +
                $"Total size: {FormatBytes(totalBytes)}\n" +
                "Export Selected copies the original files to a folder you choose.";
            OnPropertyChanged(nameof(HasSelectedMedia));
            OnPropertyChanged(nameof(CanOpenInMaps));
            return;
        }

        if (SelectedMedia is null)
        {
            _selectedMediaMetadata = null;
            ResetSelectedTags();
            SelectedMetadataText = "Select an item to view details.";
            OnPropertyChanged(nameof(HasSelectedMedia));
            OnPropertyChanged(nameof(CanOpenInMaps));
            return;
        }

        var mediaItem = await catalogRepository.GetMediaItemAsync(SelectedMedia.Id) ?? SelectedMedia.Model;
        var metadata = await catalogRepository.GetMediaMetadataAsync(mediaItem.Id);

        if (ShouldReprobeMetadata(mediaItem, metadata))
        {
            var reprobed = await ReprobeMetadataAsync(mediaItem);
            mediaItem = reprobed.MediaItem;
            metadata = reprobed.Metadata;
        }

        _selectedMediaMetadata = metadata;
        var aiAnalysis = await catalogRepository.GetAiAnalysisAsync(mediaItem.Id);
        LoadSelectedTags(mediaItem, aiAnalysis);
        SelectedMetadataText = AppendAiAnalysisText(BuildSelectedMetadataText(mediaItem, metadata), aiAnalysis);
        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(CanOpenInMaps));
    }

    private async Task<(MediaItem MediaItem, MediaMetadata? Metadata)> ReprobeMetadataAsync(MediaItem mediaItem)
    {
        if (!File.Exists(mediaItem.FullPath))
        {
            return (mediaItem, await catalogRepository.GetMediaMetadataAsync(mediaItem.Id));
        }

        var reprobeResult = await mediaProbeService.ProbeAsync(new IndexingWorkItem(
            string.Empty,
            SelectedFolder?.FullPath ?? Path.GetDirectoryName(mediaItem.FullPath) ?? string.Empty,
            mediaItem.FullPath,
            mediaItem.FileName,
            mediaItem.Extension,
            mediaItem.MediaKind,
            mediaItem.SizeBytes,
            mediaItem.CreatedUtc,
            mediaItem.ModifiedUtc,
            mediaItem.LastSeenUtc,
            mediaItem.FolderId));

        await catalogRepository.UpdateMediaProbeAsync(mediaItem.Id, reprobeResult with
        {
            Metadata = reprobeResult.Metadata with { MediaItemId = mediaItem.Id }
        });

        var refreshedMediaItem = await catalogRepository.GetMediaItemAsync(mediaItem.Id) ?? mediaItem;
        var refreshedMetadata = await catalogRepository.GetMediaMetadataAsync(mediaItem.Id);
        return (refreshedMediaItem, refreshedMetadata);
    }

    private static bool ShouldReprobeMetadata(MediaItem mediaItem, MediaMetadata? metadata)
    {
        if (!File.Exists(mediaItem.FullPath))
        {
            return false;
        }

        if (mediaItem.MetadataState != MetadataState.Done)
        {
            return true;
        }

        if (metadata is null)
        {
            return true;
        }

        if (mediaItem.MediaKind == MediaKind.Image &&
            mediaItem.Width is null &&
            mediaItem.Height is null &&
            string.IsNullOrWhiteSpace(metadata.CameraMake) &&
            string.IsNullOrWhiteSpace(metadata.CameraModel) &&
            metadata.DateTakenUtc is null &&
            string.IsNullOrWhiteSpace(metadata.ColorProfile))
        {
            return true;
        }

        if (mediaItem.MediaKind == MediaKind.Video &&
            mediaItem.Width is null &&
            mediaItem.Height is null &&
            mediaItem.DurationMs is null &&
            string.IsNullOrWhiteSpace(metadata.Codec))
        {
            return true;
        }

        return false;
    }

    private static string BuildSelectedMetadataText(MediaItem mediaItem, MediaMetadata? metadata)
    {
        var lines = new List<string>
        {
            mediaItem.FileName,
            $"{mediaItem.MediaKind} | {FormatBytes(mediaItem.SizeBytes)}",
            $"Path: {mediaItem.FullPath}",
            $"Type: {(mediaItem.MimeType ?? mediaItem.Extension.TrimStart('.').ToUpperInvariant())}",
            $"Resolution: {FormatResolution(mediaItem.Width, mediaItem.Height)}"
        };

        if (mediaItem.MediaKind == MediaKind.Video)
        {
            lines.Add($"Duration: {FormatDuration(mediaItem.DurationMs)}");
        }

        lines.Add($"Original capture date: {FormatDate(metadata?.DateTakenUtc)}");
        lines.Add($"File created: {FormatDate(mediaItem.CreatedUtc)}");
        lines.Add($"File modified: {FormatDate(mediaItem.ModifiedUtc)}");
        lines.Add($"Camera: {FormatCamera(metadata)}");
        lines.Add($"Codec: {FormatValue(metadata?.Codec)}");
        lines.Add($"Orientation: {FormatOrientation(mediaItem.Orientation)}");
        lines.Add($"Color profile: {FormatValue(metadata?.ColorProfile)}");

        if (metadata?.Bitrate is not null)
        {
            lines.Add($"Bitrate: {metadata.Bitrate:N0} kbps");
        }

        if (metadata?.FrameRate is not null)
        {
            lines.Add($"Frame rate: {metadata.FrameRate:0.##} fps");
        }

        if (metadata?.GpsLat is not null && metadata.GpsLon is not null)
        {
            lines.Add($"GPS: {metadata.GpsLat:0.######}, {metadata.GpsLon:0.######}");
        }

        lines.Add($"Metadata status: {mediaItem.MetadataState}");
        return string.Join(Environment.NewLine, lines);
    }

    private static string AppendAiAnalysisText(string baseText, AiAnalysisRecord? aiAnalysis)
    {
        if (aiAnalysis is null)
        {
            return $"{baseText}{Environment.NewLine}AI analysis: Not queued";
        }

        var lines = new List<string>
        {
            baseText,
            $"AI analysis: {aiAnalysis.AnalysisState}"
        };

        if (!string.IsNullOrWhiteSpace(aiAnalysis.ProviderModel))
        {
            lines.Add($"AI engine: {aiAnalysis.ProviderModel}");
        }

        if (!string.IsNullOrWhiteSpace(aiAnalysis.OcrText))
        {
            lines.Add($"OCR text: {aiAnalysis.OcrText}");
        }

        if (!string.IsNullOrWhiteSpace(aiAnalysis.ErrorText))
        {
            lines.Add($"AI error: {aiAnalysis.ErrorText}");
        }

        if (aiAnalysis.CompletedUtc is not null)
        {
            lines.Add($"AI completed: {FormatDate(aiAnalysis.CompletedUtc)}");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string FormatResolution(int? width, int? height)
        => width is > 0 && height is > 0 ? $"{width} x {height}" : "Unknown";

    private static string FormatDuration(long? durationMs)
        => durationMs is > 0 ? TimeSpan.FromMilliseconds(durationMs.Value).ToString(@"hh\:mm\:ss") : "Not available";

    private static string FormatDate(DateTimeOffset? value)
        => value?.ToLocalTime().ToString("yyyy-MM-dd h:mm tt") ?? "Unknown";

    private static string FormatValue(string? value)
        => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();

    private static string FormatCamera(MediaMetadata? metadata)
    {
        var parts = new[] { metadata?.CameraMake, metadata?.CameraModel }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return parts.Count == 0 ? "Unknown" : string.Join(" ", parts);
    }

    private static string FormatOrientation(int? orientation) => orientation switch
    {
        1 => "Normal",
        2 => "Mirrored horizontal",
        3 => "Rotated 180",
        4 => "Mirrored vertical",
        5 => "Mirrored horizontal, rotated 270 CW",
        6 => "Rotated 90 CW",
        7 => "Mirrored horizontal, rotated 90 CW",
        8 => "Rotated 270 CW",
        _ => "Unknown"
    };

    private void LoadSelectedTags(MediaItem mediaItem, AiAnalysisRecord? aiAnalysis)
    {
        if (mediaItem.MediaKind != MediaKind.Image && mediaItem.MediaKind != MediaKind.Video)
        {
            ResetSelectedTags();
            return;
        }

        var parsedObjects = ParseAiTagList(aiAnalysis?.ObjectsJson);
        var parsedTags = ParseAiTagList(aiAnalysis?.TagsJson);
        var captionChips = TokenizeCaptionTerms(aiAnalysis?.Caption);
        SelectedAiCaption = aiAnalysis?.Caption?.Trim() ?? string.Empty;

        Replace(ObjectTags, parsedObjects);
        Replace(SceneTags, []);
        Replace(LogoTags, []);
        Replace(TextTags, TokenizeOcrTerms(aiAnalysis?.OcrText));

        if (SceneTags.Count == 0 && parsedTags.Count > 0)
        {
            Replace(SceneTags, parsedTags
                .Where(tag => !parsedObjects.Any(existing => string.Equals(existing.Label, tag.Label, StringComparison.OrdinalIgnoreCase)))
                .Take(12)
                .ToList());
        }

        if (SceneTags.Count == 0 && captionChips.Count > 0)
        {
            Replace(SceneTags, captionChips
                .Where(tag => !parsedObjects.Any(existing => string.Equals(existing.Label, tag.Label, StringComparison.OrdinalIgnoreCase)))
                .Take(12)
                .ToList());
        }

        RaiseTagStateChanged();
    }

    private void ResetSelectedTags()
    {
        SelectedAiCaption = string.Empty;
        Replace(ObjectTags, []);
        Replace(SceneTags, []);
        Replace(LogoTags, []);
        Replace(TextTags, []);
        RaiseTagStateChanged();
    }

    private void RaiseTagStateChanged()
    {
        OnPropertyChanged(nameof(ShowTagsTab));
        OnPropertyChanged(nameof(HasSelectedAiCaption));
        OnPropertyChanged(nameof(HasObjectTags));
        OnPropertyChanged(nameof(HasSceneTags));
        OnPropertyChanged(nameof(HasLogoTags));
        OnPropertyChanged(nameof(HasTextTags));
    }

    private static IReadOnlyList<AiTagChipViewModel> ParseAiTagList(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                return document.RootElement
                    .EnumerateArray()
                    .SelectMany(ToChipLabels)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Take(16)
                    .Select(label => new AiTagChipViewModel(label))
                    .ToList();
            }
        }
        catch
        {
            // Ignore malformed AI payloads and fall back to empty groups.
        }

        return [];
    }

    private static IEnumerable<string> ToChipLabels(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                {
                    var value = element.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        yield return value.Trim();
                    }

                    break;
                }
            case JsonValueKind.Object:
                {
                    foreach (var propertyName in new[] { "label", "name", "text", "value", "tag" })
                    {
                        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
                        {
                            var value = property.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                yield return value.Trim();
                                yield break;
                            }
                        }
                    }

                    break;
                }
        }
    }

    private static IReadOnlyList<AiTagChipViewModel> TokenizeOcrTerms(string? ocrText)
    {
        if (string.IsNullOrWhiteSpace(ocrText))
        {
            return [];
        }

        return Regex.Matches(ocrText, @"[\p{L}\p{N}][\p{L}\p{N}\-'.&]{1,}")
            .Select(match => match.Value.Trim())
            .Where(value => value.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(20)
            .Select(value => new AiTagChipViewModel(value))
            .ToList();
    }

    private static IReadOnlyList<AiTagChipViewModel> TokenizeCaptionTerms(string? caption)
    {
        if (string.IsNullOrWhiteSpace(caption))
        {
            return [];
        }

        var stopWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "the", "and", "with", "from", "into", "over", "under", "near", "this", "that",
            "these", "those", "video", "image", "photo", "scene", "clip", "showing", "shows",
            "view", "visible", "appears", "appearing", "likely", "possibly", "inside", "outside",
            "people", "person", "something", "background", "foreground"
        };

        return Regex.Matches(caption, @"[\p{L}\p{N}][\p{L}\p{N}\-']{2,}")
            .Select(match => match.Value.Trim())
            .Where(value => !stopWords.Contains(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .Select(value => new AiTagChipViewModel(value))
            .ToList();
    }

    private void UpdateSelectionState()
    {
        var selectedCount = SelectedMediaItems.Count;
        var imageCount = SelectedMediaItems.Count(item => !item.IsVideo);
        var videoCount = SelectedMediaItems.Count(item => item.IsVideo);
        var totalBytes = SelectedMediaItems.Sum(item => item.Model.SizeBytes);

        SelectionSummaryText = selectedCount switch
        {
            0 => "No items selected",
            1 => "1 item selected",
            _ => $"{selectedCount:N0} items selected"
        };

        SelectionDetailText = selectedCount == 0
            ? "Select images or videos to export originals."
            : $"{imageCount:N0} images, {videoCount:N0} videos · {FormatBytes(totalBytes)}";

        if (selectedCount == 0 && IsMouseSelectionMode)
        {
            SetMouseSelectionMode(false);
        }

        OnPropertyChanged(nameof(HasAnySelectedMedia));
        OnPropertyChanged(nameof(HasMultipleSelectedMedia));
        OnPropertyChanged(nameof(ShowSinglePreview));
        OnPropertyChanged(nameof(ShowTagsTab));
        OnPropertyChanged(nameof(CanExportSelected));
        OnPropertyChanged(nameof(HasSelectedMedia));
        OnPropertyChanged(nameof(ShowMouseSelectionBadge));
        OnPropertyChanged(nameof(CanOpenInMaps));
    }

    private void LoadSettingsCollections()
    {
        Replace(PinnedFolders, _settings.PinnedFolderPaths);
        Replace(RecentFolders, _settings.RecentFolderPaths);
        Replace(SavedSearches, _settings.SavedSearches);
    }

    private void PushRecent(string fullPath)
    {
        var recent = _settings.RecentFolderPaths.ToList();
        recent.RemoveAll(path => string.Equals(path, fullPath, StringComparison.OrdinalIgnoreCase));
        recent.Insert(0, fullPath);
        _settings = _settings with { RecentFolderPaths = recent.Take(20).ToList() };
        _ = settingsService.SaveAsync(_settings);
        Replace(RecentFolders, _settings.RecentFolderPaths);
    }

    private async Task NormalizeStoredPathsAsync(IReadOnlyList<WatchedRoot>? watchedRoots = null)
    {
        watchedRoots ??= await catalogRepository.GetWatchedRootsAsync();
        var rootPaths = watchedRoots
            .Select(item => NormalizePath(item.Path))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToList();

        var cleanedPinned = FilterPathsToExistingRoots(_settings.PinnedFolderPaths, rootPaths);
        var cleanedRecent = FilterPathsToExistingRoots(_settings.RecentFolderPaths, rootPaths);
        _folderHistory.RemoveAll(path => !IsUnderAnyRoot(path, rootPaths));

        if (SelectedFolder is not null && !IsUnderAnyRoot(SelectedFolder.FullPath, rootPaths))
        {
            SelectedFolder = null;
        }

        var changed = cleanedPinned.Count != _settings.PinnedFolderPaths.Count ||
                      cleanedRecent.Count != _settings.RecentFolderPaths.Count;

        if (changed)
        {
            _settings = _settings with
            {
                PinnedFolderPaths = cleanedPinned,
                RecentFolderPaths = cleanedRecent
            };
            await settingsService.SaveAsync(_settings);
        }
        else
        {
            _settings = _settings with
            {
                PinnedFolderPaths = cleanedPinned,
                RecentFolderPaths = cleanedRecent
            };
        }

        LoadSettingsCollections();
        UpdateCanGoBack();
    }

    private void RemovePathsInsideRoot(string rootPath)
    {
        var cleanedPinned = RemovePathsInsideRoot(_settings.PinnedFolderPaths, rootPath);
        var cleanedRecent = RemovePathsInsideRoot(_settings.RecentFolderPaths, rootPath);
        _folderHistory.RemoveAll(path => IsPathWithinRoot(path, rootPath));

        if (SelectedFolder is not null && IsPathWithinRoot(SelectedFolder.FullPath, rootPath))
        {
            SelectedFolder = null;
        }

        if (SelectedMedia is not null && IsPathWithinRoot(SelectedMedia.FullPath, rootPath))
        {
            SelectedMedia = null;
        }

        if (SelectedMediaItems.Count > 0)
        {
            Replace(SelectedMediaItems, SelectedMediaItems.Where(item => !IsPathWithinRoot(item.FullPath, rootPath)).ToList());
            UpdateSelectionState();
        }

        _settings = _settings with
        {
            PinnedFolderPaths = cleanedPinned,
            RecentFolderPaths = cleanedRecent
        };

        LoadSettingsCollections();
    }

    private static List<string> RemovePathsInsideRoot(IEnumerable<string> paths, string rootPath)
        => paths.Where(path => !IsPathWithinRoot(path, rootPath)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static List<string> FilterPathsToExistingRoots(IEnumerable<string> paths, IReadOnlyList<string> rootPaths)
        => paths.Where(path => IsUnderAnyRoot(path, rootPaths)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static bool IsUnderAnyRoot(string? path, IReadOnlyList<string> rootPaths)
        => !string.IsNullOrWhiteSpace(path) && rootPaths.Any(rootPath => IsPathWithinRoot(path, rootPath));

    private static bool IsPathWithinRoot(string? path, string? rootPath)
    {
        var normalizedPath = NormalizePath(path);
        var normalizedRoot = NormalizePath(rootPath);
        if (string.IsNullOrWhiteSpace(normalizedPath) || string.IsNullOrWhiteSpace(normalizedRoot))
        {
            return false;
        }

        if (string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return string.Empty;
        }

        return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private void UpdateBreadcrumbs()
    {
        Replace(Breadcrumbs, BuildBreadcrumbs(SelectedFolder));
    }

    private static IReadOnlyList<BreadcrumbItemViewModel> BuildBreadcrumbs(FolderNodeViewModel? folder)
    {
        if (folder is null || string.IsNullOrWhiteSpace(folder.FullPath))
        {
            return [];
        }

        var segments = folder.FullPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var items = new List<BreadcrumbItemViewModel>();

        if (segments.Length == 0)
        {
            return items;
        }

        var currentPath = folder.FullPath.StartsWith(Path.DirectorySeparatorChar)
            ? Path.DirectorySeparatorChar.ToString()
            : string.Empty;

        for (var index = 0; index < segments.Length; index++)
        {
            currentPath = string.IsNullOrEmpty(currentPath)
                ? segments[index]
                : Path.Combine(currentPath, segments[index]);

            items.Add(new BreadcrumbItemViewModel(
                index == 0 && folder.FullPath.Contains(':') ? $"{segments[index]}{Path.DirectorySeparatorChar}" : segments[index],
                currentPath));
        }

        return items;
    }

    private FolderNodeViewModel? FindFolderByPath(string fullPath)
    {
        foreach (var root in Roots)
        {
            var found = FindFolderByPath(root, fullPath);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private static FolderNodeViewModel? FindFolderByPath(FolderNodeViewModel node, string fullPath)
    {
        if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
        {
            return node;
        }

        foreach (var child in node.Children)
        {
            var found = FindFolderByPath(child, fullPath);
            if (found is not null)
            {
                return found;
            }
        }

        return null;
    }

    private void UpdateCanGoBack() => CanGoBack = _folderHistory.Count > 0;

    private static string FormatBytes(long bytes)
    {
        string[] suffixes = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        var suffixIndex = 0;
        while (value >= 1024 && suffixIndex < suffixes.Length - 1)
        {
            value /= 1024;
            suffixIndex++;
        }

        return $"{value:0.#} {suffixes[suffixIndex]}";
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }

    public AppSettings CreateSettingsSnapshot() => BuildPendingSettings();

    public async Task ApplySettingsSnapshotAsync(AppSettings settings)
    {
        _settings = settings with { AiVisionProviderId = NormalizeAiVisionProviderId(settings.AiVisionProviderId) };
        ApplySettingsToUi(_settings);
        await settingsService.SaveAsync(_settings);
        ThemeManager.ApplyTheme(_settings.ThemeMode);
        await aiIndexingService.ApplySettingsAsync(_settings);
        await new ThumbnailCacheService(appPaths).CleanupAsync((long)_settings.ThumbnailCacheLimitMb * 1024 * 1024);
        await RefreshDiagnosticsAsync();
        SettingsStatusText = "Settings saved and applied.";
    }

    private static string NormalizeAiVisionProviderId(string? providerId)
    {
        var value = providerId?.Trim().ToLowerInvariant();
        return value is "lmstudio" or "ollama" or "ollama-cloud" or "openai"
            ? value
            : "lmstudio";
    }
}

public sealed record AiTagChipViewModel(string Label);

public sealed class FolderNodeViewModel
{
    public string RootId { get; init; } = string.Empty;
    public string FullPath { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public ObservableCollection<FolderNodeViewModel> Children { get; } = [];

    public static FolderNodeViewModel BuildTree(WatchedRoot root, IReadOnlyList<FolderEntry> folders)
    {
        var nodes = folders.ToDictionary(
            item => item.FullPath,
            item => new FolderNodeViewModel { RootId = root.Id, FullPath = item.FullPath, Name = string.IsNullOrWhiteSpace(item.Name) ? root.DisplayName : item.Name });

        var rootNode = nodes.TryGetValue(root.Path, out var existing)
            ? existing
            : new FolderNodeViewModel { RootId = root.Id, FullPath = root.Path, Name = root.DisplayName };

        foreach (var folder in folders.OrderBy(item => item.FullPath))
        {
            if (folder.FullPath == root.Path)
            {
                continue;
            }

            var parentPath = Path.GetDirectoryName(folder.FullPath)?.TrimEnd(Path.DirectorySeparatorChar);
            if (parentPath is not null && nodes.TryGetValue(parentPath, out var parent))
            {
                parent.Children.Add(nodes[folder.FullPath]);
            }
        }

        return rootNode;
    }
}

public sealed record BreadcrumbItemViewModel(string Label, string FullPath);

public sealed class MediaTileViewModel
{
    public MediaTileViewModel(MediaItem model, IAppPaths appPaths, AiAnalysisState? aiAnalysisState = null)
    {
        Model = model;
        AiState = aiAnalysisState;
        Id = model.Id;
        FullPath = model.FullPath;
        FileName = model.FileName;
        MediaKindLabel = model.MediaKind.ToString();
        ThumbnailPath = string.IsNullOrWhiteSpace(model.ThumbnailRelativePath) ? null : Path.Combine(appPaths.ThumbsPath, model.ThumbnailRelativePath);
        ThumbnailSource = LoadThumbnailSource(ThumbnailPath);
        SizeText = $"{model.SizeBytes / 1024d / 1024d:F1} MB";
        DimensionsText = model.Width is null || model.Height is null ? "Metadata pending" : $"{model.Width} x {model.Height}";
        DurationText = model.DurationMs is null ? string.Empty : TimeSpan.FromMilliseconds(model.DurationMs.Value).ToString(@"hh\:mm\:ss");
        HasDurationBadge = model.MediaKind == MediaKind.Video && model.DurationMs is not null;
        IsVideo = model.MediaKind == MediaKind.Video;
    }

    public MediaItem Model { get; }
    public string Id { get; }
    public string FullPath { get; }
    public string FileName { get; }
    public string MediaKindLabel { get; }
    public string? ThumbnailPath { get; }
    public BitmapSource? ThumbnailSource { get; }
    public string SizeText { get; }
    public string DimensionsText { get; }
    public string DurationText { get; }
    public bool HasDurationBadge { get; }
    public bool IsVideo { get; }
    public AiAnalysisState? AiState { get; }
    public ThumbState ThumbState => Model.ThumbState;
    public bool IsThumbnailReady => ThumbnailSource is not null;
    public bool ShowAiPendingBadge => AiState is Core.AiAnalysisState.Pending or Core.AiAnalysisState.Processing;
    public bool ShowAiReadyBadge => AiState == Core.AiAnalysisState.Done;
    public bool ShowAiFailedBadge => AiState == Core.AiAnalysisState.Failed;

    private static BitmapSource? LoadThumbnailSource(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
        {
            return null;
        }

        try
        {
            using var stream = File.Open(thumbnailPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            frame.Freeze();
            return frame;
        }
        catch
        {
            return null;
        }
    }
}
