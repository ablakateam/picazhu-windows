namespace Picazhu.Core;

public enum MediaKind { Image, Video, Other }
public enum MetadataState { Pending, Done, Failed }
public enum ThumbState { Pending, Done, Failed }
public enum AiAnalysisState { Pending, Processing, Done, Failed }
public enum SortMode { Name, ModifiedDate, CreatedDate, Size, Duration, Type }
public enum HeicDecoderPath { Unsupported, WindowsWic, BundledLibheif }

public sealed record WatchedRoot(
    string Id,
    string Path,
    string DisplayName,
    bool IncludeSubfolders,
    bool IsEnabled,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    DateTimeOffset? LastScanStartedUtc,
    DateTimeOffset? LastScanCompletedUtc);

public sealed record FolderEntry(
    string Id,
    string RootId,
    string? ParentFolderId,
    string FullPath,
    string Name,
    string RelativePath,
    int ItemCount,
    int SubfolderCount,
    DateTimeOffset LastSeenUtc,
    DateTimeOffset UpdatedUtc);

public sealed record MediaItem(
    string Id,
    string FolderId,
    string FullPath,
    string FileName,
    string Extension,
    MediaKind MediaKind,
    string? MimeType,
    long SizeBytes,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset LastSeenUtc,
    int? Width,
    int? Height,
    long? DurationMs,
    int? Orientation,
    bool IsHidden,
    bool IsSupported,
    MetadataState MetadataState,
    ThumbState ThumbState,
    string? FileSignature,
    string? QuickHash,
    string? ThumbnailRelativePath,
    string? ThumbnailCacheKey,
    DateTimeOffset CreatedRowUtc,
    DateTimeOffset UpdatedRowUtc);

public sealed record MediaMetadata(
    string MediaItemId,
    string? CameraMake,
    string? CameraModel,
    DateTimeOffset? DateTakenUtc,
    double? GpsLat,
    double? GpsLon,
    string? Codec,
    int? Bitrate,
    double? FrameRate,
    string? ColorProfile,
    string? NotesJson);

public sealed record ThumbnailRecord(
    string MediaItemId,
    string CacheKey,
    string RelativeCachePath,
    int Width,
    int Height,
    string Format,
    long BytesSize,
    DateTimeOffset GeneratedUtc,
    DateTimeOffset LastAccessedUtc);

public sealed record MediaQuery(
    string? FolderPath,
    bool IncludeSubfolders,
    string? SearchText,
    bool ImagesOnly,
    bool VideosOnly,
    bool RecentOnly,
    bool LargeFilesOnly,
    bool PortraitOnly,
    bool LandscapeOnly,
    SortMode SortMode,
    int Limit = 500);

public sealed record SearchQuery(
    IReadOnlyList<string> IncludeTerms,
    IReadOnlyList<string> ExcludeTerms,
    string? ExactPhrase,
    string? FolderTerm);

public sealed record DiagnosticsSnapshot(
    int WatchedRoots,
    int FolderCount,
    int MediaCount,
    int FailedMetadataItems,
    int FailedThumbItems,
    int PendingMetadataItems,
    int PendingThumbItems,
    long ThumbnailCacheBytes,
    int WatcherCount,
    int ScanQueueDepth,
    int MetadataQueueDepth,
    int ThumbnailQueueDepth,
    DateTimeOffset? LastScanCompletedUtc);

public sealed record IndexingProgressSnapshot(
    bool IsActive,
    bool IsPaused,
    string? RootName,
    string? RootPath,
    bool IsCounting,
    int TotalFiles,
    int FilesIndexed,
    int FilesAddedThisRun,
    DateTimeOffset? StartedUtc);

public sealed record AiProviderStatusSnapshot(
    string ProviderId,
    string DisplayName,
    bool IsConfigured,
    bool IsAvailable,
    bool SupportsVision,
    bool SupportsEmbeddings,
    string Summary,
    string? ActiveModel = null,
    bool ActiveModelSupportsVision = false,
    IReadOnlyList<string>? AvailableModels = null);

public sealed record AiIndexingProgressSnapshot(
    bool IsEnabled,
    bool IsActive,
    bool IsPaused,
    bool IsUnavailable,
    bool IsCounting,
    int TotalItems,
    int PendingItems,
    int CompletedItems,
    int FailedItems,
    string? ProviderName,
    string StatusText,
    string DetailText);

public sealed record AiAnalysisRecord(
    string MediaItemId,
    AiAnalysisState AnalysisState,
    string? ProviderId,
    string? ProviderModel,
    string? Caption,
    string? TagsJson,
    string? ObjectsJson,
    string? OcrText,
    string? CombinedText,
    string? ErrorText,
    DateTimeOffset? QueuedUtc,
    DateTimeOffset? StartedUtc,
    DateTimeOffset? CompletedUtc,
    DateTimeOffset UpdatedUtc);

public sealed record AppSettings
{
    public List<string> PinnedFolderPaths { get; init; } = [];
    public List<string> RecentFolderPaths { get; init; } = [];
    public List<SavedSearch> SavedSearches { get; init; } = [];
    public int ThumbnailCacheLimitMb { get; init; } = 2048;
    public bool ShowSubfoldersByDefault { get; init; } = true;
    public string ThemeMode { get; init; } = "Dark";
    public bool EnableAiGlobally { get; init; }
    public string AiVisionProviderId { get; init; } = "lmstudio";
    public string? OpenAiApiKeyPlaceholder { get; init; }
    public string? OpenAiVisionModel { get; init; }
    public string? OllamaEndpoint { get; init; } = "http://localhost:11434";
    public string? OllamaVisionModel { get; init; }
    public string? OllamaCloudEndpoint { get; init; } = "https://ollama.com";
    public string? OllamaCloudApiKeyPlaceholder { get; init; }
    public string? OllamaCloudVisionModel { get; init; }
    public string? LmStudioEndpoint { get; init; } = "http://localhost:1234/v1";
    public string? LmStudioVisionModel { get; init; }
}

public sealed record SavedSearch(string Name, string QueryText, SortMode SortMode);

public sealed record IndexingWorkItem(
    string RootId,
    string RootPath,
    string FullPath,
    string FileName,
    string Extension,
    MediaKind MediaKind,
    long SizeBytes,
    DateTimeOffset? CreatedUtc,
    DateTimeOffset? ModifiedUtc,
    DateTimeOffset LastSeenUtc,
    string FolderId);

public sealed record ProbeResult(
    int? Width,
    int? Height,
    long? DurationMs,
    int? Orientation,
    MediaMetadata Metadata,
    bool IsSupported);

public sealed record ThumbnailRequest(
    string MediaItemId,
    string FullPath,
    DateTimeOffset? ModifiedUtc,
    long SizeBytes,
    MediaKind MediaKind,
    int TargetWidth,
    int TargetHeight);

public sealed record QuickPreviewResult(
    string FullPath,
    MediaKind MediaKind,
    string? ThumbnailPath,
    string? DisplayText);

public sealed record ExportSourceItem(
    string MediaItemId,
    string FullPath,
    string FileName,
    long SizeBytes);

public sealed record ExportRequest(
    IReadOnlyList<ExportSourceItem> Items,
    string DestinationFolder);

public sealed record ExportProgressSnapshot(
    bool IsRunning,
    int TotalItems,
    int CompletedItems,
    int CopiedItems,
    int RenamedItems,
    int FailedItems,
    string? CurrentFileName);

public sealed record ExportResult(
    int RequestedCount,
    int CopiedCount,
    int RenamedCount,
    int FailedCount,
    IReadOnlyList<string> Errors);

public sealed record PhoneDeviceInfo(
    string DeviceId,
    string DisplayName,
    string? Manufacturer,
    bool IsLikelyIPhone,
    bool IsConnected);

public sealed record PhoneMediaItem(
    string DeviceId,
    string DevicePath,
    string RelativePath,
    string FileName,
    string Extension,
    MediaKind MediaKind,
    long SizeBytes,
    DateTimeOffset? ModifiedUtc);

public sealed record PhoneImportRequest(
    string DeviceId,
    IReadOnlyList<PhoneMediaItem> Items,
    string DestinationFolder,
    bool PreserveDcimFolders = true);

public sealed record PhoneImportProgressSnapshot(
    bool IsRunning,
    int TotalItems,
    int CompletedItems,
    int CopiedItems,
    int SkippedItems,
    int RenamedItems,
    int FailedItems,
    string? CurrentFileName);

public sealed record PhoneImportResult(
    int RequestedCount,
    int CopiedCount,
    int SkippedCount,
    int RenamedCount,
    int FailedCount,
    IReadOnlyList<string> Errors,
    string DestinationFolder);

public sealed record HeicDecoderDiagnostics(
    bool NativeWicDetected,
    bool NativeWicHealthy,
    bool LibheifRegistered,
    HeicDecoderPath ActivePath,
    string Summary,
    string? LastError);

public sealed record AiProviderInfo(
    string ProviderId,
    string DisplayName,
    bool RequiresRemoteAccess,
    bool SupportsVision,
    bool SupportsEmbeddings);

public sealed record AiAnalysisResult(
    string? Caption,
    IReadOnlyList<string> Tags,
    IReadOnlyList<string> Objects,
    string? OcrText,
    string ProviderModel,
    DateTimeOffset AnalyzedUtc);

public interface IAppPaths
{
    string RootPath { get; }
    string DatabasePath { get; }
    string ThumbsPath { get; }
    string LogsPath { get; }
    string TempPath { get; }
    string AiPath { get; }
}

public interface ISettingsService
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface ICatalogRepository
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WatchedRoot>> GetWatchedRootsAsync(CancellationToken cancellationToken = default);
    Task<WatchedRoot> AddWatchedRootAsync(string path, bool includeSubfolders, CancellationToken cancellationToken = default);
    Task RemoveWatchedRootAsync(string id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<FolderEntry>> GetFoldersAsync(string rootId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MediaItem>> QueryMediaAsync(MediaQuery query, CancellationToken cancellationToken = default);
    Task<MediaItem?> GetMediaItemAsync(string mediaItemId, CancellationToken cancellationToken = default);
    Task<MediaMetadata?> GetMediaMetadataAsync(string mediaItemId, CancellationToken cancellationToken = default);
    Task UpsertFolderAsync(FolderEntry folder, CancellationToken cancellationToken = default);
    Task UpsertMediaItemAsync(MediaItem item, CancellationToken cancellationToken = default);
    Task UpsertMediaMetadataAsync(MediaMetadata metadata, CancellationToken cancellationToken = default);
    Task UpsertThumbnailAsync(ThumbnailRecord thumbnail, CancellationToken cancellationToken = default);
    Task UpdateMediaProbeAsync(string mediaItemId, ProbeResult result, CancellationToken cancellationToken = default);
    Task UpdateMediaThumbnailStateAsync(string mediaItemId, ThumbState thumbState, string? cacheKey, string? relativePath, CancellationToken cancellationToken = default);
    Task PruneMissingAsync(string rootId, IReadOnlySet<string> seenFolders, IReadOnlySet<string> seenMedia, CancellationToken cancellationToken = default);
    Task<DiagnosticsSnapshot> GetDiagnosticsAsync(int watcherCount, int scanQueueDepth, int metadataQueueDepth, int thumbQueueDepth, long thumbCacheBytes, CancellationToken cancellationToken = default);
    Task MarkRootScanStartedAsync(string rootId, CancellationToken cancellationToken = default);
    Task MarkRootScanCompletedAsync(string rootId, CancellationToken cancellationToken = default);
    Task RebuildCatalogAsync(CancellationToken cancellationToken = default);
    Task EnsureAiAnalysisRecordAsync(string mediaItemId, CancellationToken cancellationToken = default);
    Task<AiAnalysisRecord?> GetAiAnalysisAsync(string mediaItemId, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, AiAnalysisState>> GetAiAnalysisStatesAsync(IReadOnlyList<string> mediaItemIds, CancellationToken cancellationToken = default);
    Task UpdateAiAnalysisAsync(AiAnalysisRecord record, CancellationToken cancellationToken = default);
    Task<(int Total, int Pending, int Completed, int Failed)> GetAiAnalysisCountsAsync(CancellationToken cancellationToken = default);
}

public interface IThumbnailCacheService
{
    string GetRelativeThumbnailPath(string cacheKey);
    string GetAbsoluteThumbnailPath(string cacheKey);
    string CreateCacheKey(string fullPath, DateTimeOffset? modifiedUtc, long sizeBytes, string profileVersion);
    Task<long> GetCacheSizeBytesAsync(CancellationToken cancellationToken = default);
    Task EnsureCacheFoldersAsync(CancellationToken cancellationToken = default);
    Task CleanupAsync(long maxBytes, CancellationToken cancellationToken = default);
}

public interface IMediaProbeService
{
    Task<ProbeResult> ProbeAsync(IndexingWorkItem workItem, CancellationToken cancellationToken = default);
}

public interface IThumbnailGenerator
{
    Task<ThumbnailRecord?> GenerateAsync(ThumbnailRequest request, CancellationToken cancellationToken = default);
}

public interface IIndexingService
{
    Task StartAsync(CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
    Task RescanAllAsync(CancellationToken cancellationToken = default);
    Task RescanRootAsync(WatchedRoot root, CancellationToken cancellationToken = default);
    Task WatchRootsAsync(CancellationToken cancellationToken = default);
    void PauseProcessing();
    void ResumeProcessing();
    void StopCurrentWork();
    IndexingProgressSnapshot GetProgress();
}

public interface IQuickPreviewService
{
    Task<QuickPreviewResult> GetPreviewAsync(MediaItem item, CancellationToken cancellationToken = default);
}

public interface IExportService
{
    Task<ExportResult> ExportOriginalsAsync(ExportRequest request, IProgress<ExportProgressSnapshot>? progress = null, CancellationToken cancellationToken = default);
}

public interface IPhoneImportService
{
    Task<IReadOnlyList<PhoneDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<PhoneMediaItem>> GetDeviceMediaAsync(string deviceId, CancellationToken cancellationToken = default);
    Task<bool> TryDownloadThumbnailAsync(PhoneMediaItem item, string outputPath, CancellationToken cancellationToken = default);
    Task<PhoneImportResult> ImportAsync(PhoneImportRequest request, IProgress<PhoneImportProgressSnapshot>? progress = null, CancellationToken cancellationToken = default);
}

public interface IHeicDecoderService
{
    bool IsHeicPath(string path);
    Task InitializeAsync(IEnumerable<string> samplePaths, CancellationToken cancellationToken = default);
    HeicDecoderDiagnostics GetDiagnostics();
    Task<(int Width, int Height, int? Orientation)?> ProbeAsync(string path, CancellationToken cancellationToken = default);
    Task<bool> TryCreateThumbnailAsync(string inputPath, string outputPath, int width, int height, CancellationToken cancellationToken = default);
    Task<string?> GetPreviewPathAsync(string inputPath, string cacheKey, int width, int height, CancellationToken cancellationToken = default);
}

public interface IAiProvider
{
    AiProviderInfo GetProviderInfo();
    Task<AiProviderStatusSnapshot> GetStatusAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task<AiAnalysisResult?> DescribeImageAsync(string imagePath, AppSettings settings, CancellationToken cancellationToken = default);
    Task<AiAnalysisResult?> DescribeVideoFrameSetAsync(IReadOnlyList<string> framePaths, AppSettings settings, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<float>> CreateTextEmbeddingsAsync(string text, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> RerankResultsAsync(string query, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default);
}

public interface IAiFeatureGate
{
    bool IsEnabled { get; }
    event Action<bool>? Changed;
    void SetEnabled(bool isEnabled);
}

public interface IAiProviderStatusService
{
    Task<IReadOnlyList<AiProviderStatusSnapshot>> GetStatusesAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface IOcrTextExtractor
{
    Task<string?> ExtractTextAsync(MediaItem mediaItem, CancellationToken cancellationToken = default);
}

public interface IAiIndexingService
{
    Task InitializeAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task RefreshProviderStatusesAsync(AppSettings settings, CancellationToken cancellationToken = default);
    Task QueueMediaAsync(MediaItem mediaItem, CancellationToken cancellationToken = default);
    void PauseProcessing();
    void ResumeProcessing();
    void StopCurrentWork();
    AiIndexingProgressSnapshot GetProgress();
    IReadOnlyList<AiProviderStatusSnapshot> GetProviderStatuses();
}

public static class MediaSupport
{
    public static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".jpg", ".jpeg", ".png", ".webp", ".gif", ".bmp", ".tif", ".tiff", ".heic", ".heif" };

    public static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".mp4", ".mov", ".avi", ".mkv", ".m4v", ".wmv", ".webm" };

    public static bool ShouldIgnoreFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.StartsWith("._", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldIndexFile(string path)
        => !ShouldIgnoreFile(path) && GetMediaKind(Path.GetExtension(path)) != MediaKind.Other;

    public static MediaKind GetMediaKind(string extension) =>
        ImageExtensions.Contains(extension) ? MediaKind.Image :
        VideoExtensions.Contains(extension) ? MediaKind.Video :
        MediaKind.Other;
}

public static class SearchQueryParser
{
    public static SearchQuery Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new SearchQuery([], [], null, null);
        }

        var includes = new List<string>();
        var excludes = new List<string>();
        string? phrase = null;
        string? folder = null;

        foreach (var token in Tokenize(input))
        {
            if (token.StartsWith("-", StringComparison.Ordinal) && token.Length > 1)
            {
                excludes.Add(token[1..]);
            }
            else if (token.StartsWith("folder:", StringComparison.OrdinalIgnoreCase))
            {
                folder = token["folder:".Length..];
            }
            else if (token.Contains(' '))
            {
                phrase = token;
            }
            else
            {
                includes.Add(token);
            }
        }

        return new SearchQuery(includes, excludes, phrase, folder);
    }

    private static IEnumerable<string> Tokenize(string value)
    {
        var chars = new List<char>();
        var inQuotes = false;

        foreach (var c in value)
        {
            if (c == '"')
            {
                if (inQuotes && chars.Count > 0)
                {
                    yield return new string(chars.ToArray());
                    chars.Clear();
                }

                inQuotes = !inQuotes;
                continue;
            }

            if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (chars.Count > 0)
                {
                    yield return new string(chars.ToArray());
                    chars.Clear();
                }

                continue;
            }

            chars.Add(c);
        }

        if (chars.Count > 0)
        {
            yield return new string(chars.ToArray());
        }
    }
}

public static class FileSignatures
{
    public static string CreateQuickHash(string fullPath, long sizeBytes, DateTimeOffset? modifiedUtc)
        => $"{fullPath.ToUpperInvariant()}|{sizeBytes}|{modifiedUtc?.ToUnixTimeSeconds() ?? 0}";
}
