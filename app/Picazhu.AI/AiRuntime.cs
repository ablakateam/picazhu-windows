using System.Collections.Concurrent;
using System.Text.Json;
using Picazhu.Core;
using Picazhu.Cache;
using Picazhu.Media;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Media.Editing;
using Windows.Media.MediaProperties;
using Windows.Media.Ocr;
using Windows.Storage;

namespace Picazhu.AI;

public sealed class AiFeatureGate : IAiFeatureGate
{
    private volatile bool _isEnabled;

    public bool IsEnabled => _isEnabled;
    public event Action<bool>? Changed;

    public void SetEnabled(bool isEnabled)
    {
        if (_isEnabled == isEnabled)
        {
            return;
        }

        _isEnabled = isEnabled;
        Changed?.Invoke(isEnabled);
    }
}

public sealed class AiProviderStatusService(IEnumerable<IAiProvider> providers) : IAiProviderStatusService
{
    public async Task<IReadOnlyList<AiProviderStatusSnapshot>> GetStatusesAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var statuses = new List<AiProviderStatusSnapshot>();
        foreach (var provider in providers.OrderBy(item => item.GetProviderInfo().DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            statuses.Add(await provider.GetStatusAsync(settings, cancellationToken));
        }

        return statuses;
    }
}

public sealed class AiIndexingService(
    ICatalogRepository catalogRepository,
    IAiFeatureGate featureGate,
    IAiProviderStatusService providerStatusService,
    IOcrTextExtractor ocrTextExtractor,
    IHeicDecoderService heicDecoderService,
    IThumbnailCacheService thumbnailCacheService,
    IAppPaths appPaths,
    IEnumerable<IAiProvider> providers) : IAiIndexingService
{
    private readonly object _sync = new();
    private readonly ConcurrentQueue<MediaItem> _queue = new();
    private readonly ConcurrentDictionary<string, byte> _queuedMediaIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _queueSignal = new(0);
    private CancellationTokenSource? _runCts;
    private Task? _workerTask;
    private TaskCompletionSource<bool>? _resumeTcs;
    private AppSettings _settings = new();
    private readonly IReadOnlyDictionary<string, IAiProvider> _providers = providers.ToDictionary(item => item.GetProviderInfo().ProviderId, StringComparer.OrdinalIgnoreCase);
    private AiIndexingProgressSnapshot _progress = DisabledSnapshot();
    private IReadOnlyList<AiProviderStatusSnapshot> _providerStatuses = [];
    private bool _isPaused;
    private int _queuePreparedTotal;
    private int _queuePreparedProcessed;

    public async Task InitializeAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        featureGate.SetEnabled(settings.EnableAiGlobally);
        featureGate.Changed += OnFeatureGateChanged;
        _runCts ??= CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workerTask ??= Task.Run(() => RunQueueWorkerAsync(_runCts.Token), _runCts.Token);
        await RefreshStateAsync(cancellationToken);
        if (featureGate.IsEnabled)
        {
            await SeedPendingMediaAsync(cancellationToken);
        }
    }

    public async Task ApplySettingsAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        featureGate.SetEnabled(settings.EnableAiGlobally);
        await RefreshStateAsync(cancellationToken);
        if (featureGate.IsEnabled)
        {
            await SeedPendingMediaAsync(cancellationToken);
        }
        else
        {
            ClearQueue();
        }
    }

    public async Task RefreshProviderStatusesAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        _settings = settings;
        _providerStatuses = await providerStatusService.GetStatusesAsync(settings, cancellationToken);

        if (featureGate.IsEnabled)
        {
            await RefreshStateAsync(cancellationToken);
            return;
        }

        lock (_sync)
        {
            _progress = DisabledSnapshot();
        }
    }

    public Task QueueMediaAsync(MediaItem mediaItem, CancellationToken cancellationToken = default)
    {
        if (!featureGate.IsEnabled || (mediaItem.MediaKind != MediaKind.Image && mediaItem.MediaKind != MediaKind.Video))
        {
            return Task.CompletedTask;
        }

        if (!_queuedMediaIds.TryAdd(mediaItem.Id, 0))
        {
            return Task.CompletedTask;
        }

        _queue.Enqueue(mediaItem);
        _queueSignal.Release();
        lock (_sync)
        {
            _queuePreparedTotal++;
            var processed = Math.Max(0, _queuePreparedProcessed);
            var total = Math.Max(_queuePreparedTotal, processed);
            var pending = Math.Max(1, total - processed);
            _progress = _progress with
            {
                IsActive = true,
                TotalItems = total,
                PendingItems = pending,
                CompletedItems = processed,
                StatusText = "AI indexing in progress",
                DetailText = $"Queued {mediaItem.FileName} for visual analysis. {processed:N0} of {total:N0} processed."
            };
        }

        return Task.CompletedTask;
    }

    public void PauseProcessing()
    {
        lock (_sync)
        {
            if (!_progress.IsEnabled)
            {
                return;
            }

            _isPaused = true;
            _resumeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _progress = _progress with
            {
                IsPaused = true,
                StatusText = "AI indexing paused",
                DetailText = "Background visual analysis is paused."
            };
        }
    }

    public void ResumeProcessing()
    {
        lock (_sync)
        {
            if (!_progress.IsEnabled)
            {
                return;
            }

            _isPaused = false;
            _resumeTcs?.TrySetResult(true);
            _resumeTcs = null;
            _progress = _progress with
            {
                IsPaused = false,
                StatusText = "AI indexing ready",
                DetailText = BuildReadyDetail(_progress.TotalItems, _progress.PendingItems, _progress.ProviderName)
            };
        }
    }

    public void StopCurrentWork()
    {
        lock (_sync)
        {
            ClearQueue();
            _isPaused = false;
            _resumeTcs?.TrySetResult(true);
            _resumeTcs = null;
            _progress = featureGate.IsEnabled
                ? UnavailableSnapshot(_providerStatuses, _progress.TotalItems)
                : DisabledSnapshot();
        }
    }

    public AiIndexingProgressSnapshot GetProgress()
    {
        lock (_sync)
        {
            return _progress;
        }
    }

    public IReadOnlyList<AiProviderStatusSnapshot> GetProviderStatuses()
    {
        lock (_sync)
        {
            return _providerStatuses;
        }
    }

    private void OnFeatureGateChanged(bool isEnabled)
    {
        lock (_sync)
        {
            _progress = isEnabled ? _progress with { IsEnabled = true } : DisabledSnapshot();
            if (!isEnabled)
            {
                ClearQueue();
            }
        }
    }

    private async Task RefreshStateAsync(CancellationToken cancellationToken)
    {
        if (!featureGate.IsEnabled)
        {
            lock (_sync)
            {
                _providerStatuses = [];
                _progress = DisabledSnapshot();
            }

            return;
        }

        _providerStatuses = await providerStatusService.GetStatusesAsync(_settings, cancellationToken);

        var activeProvider = GetSelectedProviderStatus(_providerStatuses, _settings);
        var aiCounts = await catalogRepository.GetAiAnalysisCountsAsync(cancellationToken);
        var selectedProviderName = ResolveSelectedProviderDisplayName(_providerStatuses, _settings);
        var totalItems = Math.Max(aiCounts.Total, _queuePreparedTotal);
        var processedItems = Math.Max(aiCounts.Completed + aiCounts.Failed, _queuePreparedProcessed);
        var pendingItems = Math.Max(aiCounts.Pending, Math.Max(0, totalItems - processedItems));
        var isActive = pendingItems > 0;
        lock (_sync)
        {
            _progress = new AiIndexingProgressSnapshot(
                true,
                isActive,
                false,
                false,
                false,
                totalItems,
                pendingItems,
                processedItems,
                aiCounts.Failed,
                activeProvider?.DisplayName ?? selectedProviderName,
                activeProvider is null
                    ? (isActive ? "AI indexing running OCR-only" : "AI indexing ready")
                    : (isActive ? "AI indexing in progress" : "AI indexing ready"),
                activeProvider is null
                    ? BuildOcrOnlyDetail(totalItems, processedItems, pendingItems, selectedProviderName)
                    : BuildReadyDetail(totalItems, pendingItems, activeProvider.DisplayName, processedItems));
        }
    }

    private async Task SeedPendingMediaAsync(CancellationToken cancellationToken)
    {
        var mediaItems = (await catalogRepository.QueryMediaAsync(new MediaQuery(
            null,
            true,
            null,
            false,
            false,
            false,
            false,
            false,
            false,
            SortMode.Name,
            0), cancellationToken))
            .Where(item => item.MediaKind is MediaKind.Image or MediaKind.Video)
            .ToList();
        var aiCounts = await catalogRepository.GetAiAnalysisCountsAsync(cancellationToken);
        var processedItems = aiCounts.Completed + aiCounts.Failed;
        var totalItems = Math.Max(mediaItems.Count, aiCounts.Total);
        var pendingItems = Math.Max(aiCounts.Pending, Math.Max(0, totalItems - processedItems));

        lock (_sync)
        {
            _queuePreparedTotal = totalItems;
            _queuePreparedProcessed = processedItems;
            _progress = _progress with
            {
                IsActive = pendingItems > 0,
                IsCounting = false,
                TotalItems = totalItems,
                PendingItems = pendingItems,
                CompletedItems = processedItems,
                StatusText = pendingItems > 0 ? "Preparing AI queue" : "AI indexing ready",
                DetailText = pendingItems > 0
                    ? $"Queueing {pendingItems:N0} eligible media item(s) for visual analysis."
                    : BuildReadyDetail(totalItems, 0, _progress.ProviderName, processedItems)
            };
        }

        foreach (var mediaItem in mediaItems)
        {
            if (!featureGate.IsEnabled)
            {
                break;
            }

            if (_queuedMediaIds.TryAdd(mediaItem.Id, 0))
            {
                _queue.Enqueue(mediaItem);
                _queueSignal.Release();
            }
        }
    }

    private async Task RunQueueWorkerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _queueSignal.WaitAsync(cancellationToken);
            if (!_queue.TryDequeue(out var mediaItem))
            {
                continue;
            }
            _queuedMediaIds.TryRemove(mediaItem.Id, out _);

            await WaitWhilePausedAsync(cancellationToken);
            if (!featureGate.IsEnabled || (mediaItem.MediaKind != MediaKind.Image && mediaItem.MediaKind != MediaKind.Video))
            {
                continue;
            }

            await catalogRepository.EnsureAiAnalysisRecordAsync(mediaItem.Id, cancellationToken);
            var record = await catalogRepository.GetAiAnalysisAsync(mediaItem.Id, cancellationToken);
            if (record?.AnalysisState == AiAnalysisState.Done && !string.IsNullOrWhiteSpace(record.CombinedText))
            {
                continue;
            }

            var startedUtc = DateTimeOffset.UtcNow;
            var selectedProviderStatus = GetSelectedProviderStatus(_providerStatuses, _settings);
            await catalogRepository.UpdateAiAnalysisAsync(new AiAnalysisRecord(
                mediaItem.Id,
                AiAnalysisState.Processing,
                selectedProviderStatus?.ProviderId ?? "windows-ocr",
                selectedProviderStatus?.ActiveModel ?? "Windows OCR",
                null,
                null,
                null,
                null,
                null,
                null,
                record?.QueuedUtc ?? startedUtc,
                startedUtc,
                null,
                startedUtc), cancellationToken);

            string? ocrText = null;
            string? error = null;
            AiAnalysisResult? visionResult = null;
            try
            {
                if (mediaItem.MediaKind == MediaKind.Image)
                {
                    ocrText = await ocrTextExtractor.ExtractTextAsync(mediaItem, cancellationToken);
                }

                if (selectedProviderStatus is not null &&
                    selectedProviderStatus.IsAvailable &&
                    _providers.TryGetValue(selectedProviderStatus.ProviderId, out var provider))
                {
                    if (mediaItem.MediaKind == MediaKind.Image)
                    {
                        var analysisPath = await ResolveVisionInputPathAsync(mediaItem, cancellationToken);
                        visionResult = await provider.DescribeImageAsync(analysisPath, _settings, cancellationToken);
                    }
                    else if (mediaItem.MediaKind == MediaKind.Video)
                    {
                        var framePaths = await ResolveVideoFramePathsAsync(mediaItem, cancellationToken);
                        visionResult = await provider.DescribeVideoFrameSetAsync(framePaths, _settings, cancellationToken);
                    }
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                error = ex.Message;
            }

            if (mediaItem.MediaKind == MediaKind.Video && visionResult is null && string.IsNullOrWhiteSpace(error))
            {
                error = selectedProviderStatus is null || !selectedProviderStatus.IsAvailable
                    ? "Video AI requires an available vision provider."
                    : "Video analysis did not return a usable result.";
            }

            var tagsJson = SerializeStringArray(visionResult?.Tags);
            var objectsJson = SerializeStringArray(visionResult?.Objects);
            var combinedText = string.Join(' ', new[]
                {
                    visionResult?.Caption,
                    visionResult is null ? null : string.Join(' ', visionResult.Tags),
                    visionResult is null ? null : string.Join(' ', visionResult.Objects),
                    ocrText
                }
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => value!.Trim()))
                .Trim();
            var completedUtc = DateTimeOffset.UtcNow;
            await catalogRepository.UpdateAiAnalysisAsync(new AiAnalysisRecord(
                mediaItem.Id,
                error is null ? AiAnalysisState.Done : AiAnalysisState.Failed,
                visionResult is null ? "windows-ocr" : selectedProviderStatus?.ProviderId,
                visionResult?.ProviderModel ?? "Windows OCR",
                visionResult?.Caption,
                tagsJson,
                objectsJson,
                ocrText,
                string.IsNullOrWhiteSpace(combinedText) ? null : combinedText,
                error,
                record?.QueuedUtc ?? startedUtc,
                startedUtc,
                completedUtc,
                completedUtc), cancellationToken);

            var shouldRefresh = false;
            lock (_sync)
            {
                _queuePreparedProcessed++;
                var processed = Math.Max(0, _queuePreparedProcessed);
                var total = Math.Max(_queuePreparedTotal, processed);
                var pending = Math.Max(0, total - processed);
                shouldRefresh = _queuePreparedProcessed % 25 == 0 || _queue.IsEmpty;
                _progress = _progress with
                {
                    IsActive = pending > 0,
                    TotalItems = total,
                    PendingItems = pending,
                    CompletedItems = processed,
                    StatusText = pending > 0 ? "AI indexing in progress" : "AI indexing ready",
                    DetailText = pending > 0
                        ? $"{processed:N0} of {total:N0} eligible media item(s) processed. {pending:N0} remaining."
                        : BuildReadyDetail(total, 0, _progress.ProviderName, processed)
                };
            }

            if (shouldRefresh)
            {
                await RefreshStateAsync(cancellationToken);
            }
        }
    }

    private async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        Task? waitTask = null;
        lock (_sync)
        {
            if (_isPaused)
            {
                _resumeTcs ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _resumeTcs.Task;
            }
        }

        if (waitTask is not null)
        {
            await waitTask.WaitAsync(cancellationToken);
        }
    }

    private async Task<string> ResolveVisionInputPathAsync(MediaItem mediaItem, CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaItem.FullPath))
        {
            return mediaItem.FullPath;
        }

        string sourcePath;
        if (heicDecoderService.IsHeicPath(mediaItem.FullPath))
        {
            var previewKey = thumbnailCacheService.CreateCacheKey(mediaItem.FullPath, mediaItem.ModifiedUtc, mediaItem.SizeBytes, "ai-vision-v3");
            var previewPath = await heicDecoderService.GetPreviewPathAsync(mediaItem.FullPath, previewKey, 1600, 1600, cancellationToken);
            if (!string.IsNullOrWhiteSpace(previewPath) && File.Exists(previewPath))
            {
                sourcePath = previewPath;
            }
            else
            {
                throw new InvalidOperationException($"HEIC image could not be converted into an AI analysis preview: {mediaItem.FileName}");
            }
        }
        else
        {
            sourcePath = mediaItem.FullPath;
        }

        var analysisDir = Path.Combine(appPaths.AiPath, "analysis-cache");
        Directory.CreateDirectory(analysisDir);
        var analysisKey = thumbnailCacheService.CreateCacheKey(mediaItem.FullPath, mediaItem.ModifiedUtc, mediaItem.SizeBytes, "ai-vision-raster-v2");
        var analysisPath = Path.Combine(analysisDir, $"{analysisKey}.jpg");
        if (!File.Exists(analysisPath))
        {
            CreateAnalysisImage(sourcePath, analysisPath, 768, 768);
        }

        return analysisPath;
    }

    private static void CreateAnalysisImage(string inputPath, string outputPath, int maxWidth, int maxHeight)
    {
        using var stream = File.OpenRead(inputPath);
        var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var scale = Math.Min((double)maxWidth / frame.PixelWidth, (double)maxHeight / frame.PixelHeight);
        if (double.IsNaN(scale) || double.IsInfinity(scale) || scale <= 0d)
        {
            scale = 1d;
        }

        BitmapSource source = scale < 1d
            ? new TransformedBitmap(frame, new ScaleTransform(scale, scale))
            : frame;

        var encoder = new JpegBitmapEncoder { QualityLevel = 60 };
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
        using var output = File.Create(outputPath);
        encoder.Save(output);
    }

    private async Task<IReadOnlyList<string>> ResolveVideoFramePathsAsync(MediaItem mediaItem, CancellationToken cancellationToken)
    {
        if (!File.Exists(mediaItem.FullPath))
        {
            return [];
        }

        var frameDir = Path.Combine(appPaths.AiPath, "video-frames");
        Directory.CreateDirectory(frameDir);
        var frameKey = thumbnailCacheService.CreateCacheKey(mediaItem.FullPath, mediaItem.ModifiedUtc, mediaItem.SizeBytes, "ai-video-frames-v1");
        var framePaths = new[]
        {
            Path.Combine(frameDir, $"{frameKey}-1.jpg"),
            Path.Combine(frameDir, $"{frameKey}-2.jpg")
        };

        if (framePaths.All(File.Exists))
        {
            return framePaths;
        }

        var storageFile = await StorageFile.GetFileFromPathAsync(mediaItem.FullPath);
        var clip = await MediaClip.CreateFromFileAsync(storageFile);
        var composition = new MediaComposition();
        composition.Clips.Add(clip);
        var duration = mediaItem.DurationMs is > 0
            ? TimeSpan.FromMilliseconds(mediaItem.DurationMs.Value)
            : clip.OriginalDuration;
        if (duration <= TimeSpan.Zero)
        {
            duration = TimeSpan.FromSeconds(1);
        }

        var timestamps = new[]
        {
            TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.25d),
            TimeSpan.FromMilliseconds(duration.TotalMilliseconds * 0.75d)
        };

        for (var index = 0; index < framePaths.Length; index++)
        {
            if (File.Exists(framePaths[index]))
            {
                continue;
            }

            using var thumbnailStream = await composition.GetThumbnailAsync(timestamps[index], 448, 448, VideoFramePrecision.NearestFrame);
            using var input = thumbnailStream.AsStreamForRead();
            using var output = File.Create(framePaths[index]);
            await input.CopyToAsync(output, cancellationToken);
        }

        return framePaths.Where(File.Exists).ToList();
    }

    private static string? SerializeStringArray(IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            return null;
        }

        return JsonSerializer.Serialize(values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static AiProviderStatusSnapshot? GetSelectedProviderStatus(IReadOnlyList<AiProviderStatusSnapshot> statuses, AppSettings settings)
    {
        if (statuses.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(settings.AiVisionProviderId))
        {
            var selected = statuses.FirstOrDefault(status => string.Equals(status.ProviderId, settings.AiVisionProviderId, StringComparison.OrdinalIgnoreCase));
            return selected?.IsAvailable == true ? selected : null;
        }

        return statuses.FirstOrDefault(status => status.IsAvailable);
    }

    private static string? ResolveSelectedProviderDisplayName(IReadOnlyList<AiProviderStatusSnapshot> statuses, AppSettings settings)
    {
        if (statuses.Count == 0)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(settings.AiVisionProviderId))
        {
            return statuses.FirstOrDefault(status => string.Equals(status.ProviderId, settings.AiVisionProviderId, StringComparison.OrdinalIgnoreCase))?.DisplayName
                   ?? settings.AiVisionProviderId;
        }

        return statuses.FirstOrDefault(status => status.IsAvailable)?.DisplayName;
    }

    private void ClearQueue()
    {
        while (_queue.TryDequeue(out _))
        {
        }
        _queuedMediaIds.Clear();

        while (_queueSignal.CurrentCount > 0)
        {
            _queueSignal.Wait(0);
        }
        _queuePreparedProcessed = 0;
        _queuePreparedTotal = 0;
    }

    private static AiIndexingProgressSnapshot DisabledSnapshot()
        => new(false, false, false, false, false, 0, 0, 0, 0, null, "AI indexing is off", "Enable AI features to allow visual analysis and semantic search.");

    private static AiIndexingProgressSnapshot UnavailableSnapshot(IReadOnlyList<AiProviderStatusSnapshot> statuses, int totalItems)
    {
        var configured = statuses.Where(status => status.IsConfigured).ToList();
        var detail = configured.Count == 0
            ? "No AI provider is configured."
            : string.Join(" | ", configured.Select(status => $"{status.DisplayName}: {status.Summary}"));
        return new AiIndexingProgressSnapshot(true, false, false, true, false, totalItems, 0, 0, 0, null, "AI provider unavailable", detail);
    }

    private static string BuildReadyDetail(int imageCount, int pendingCount, string? providerName, int processedCount = 0)
        => imageCount <= 0
            ? $"No eligible media is currently available for visual indexing. Provider: {providerName ?? "Unknown"}."
            : pendingCount > 0
                ? $"{processedCount:N0} of {imageCount:N0} eligible media item(s) processed. {pendingCount:N0} pending in the AI queue. Provider: {providerName ?? "Unknown"}."
                : $"{imageCount:N0} eligible media item(s) are fully processed for visual analysis. Provider: {providerName ?? "Unknown"}.";

    private static string BuildOcrOnlyDetail(int imageCount, int processedCount, int pendingCount, string? providerName)
        => imageCount <= 0
            ? $"No eligible media is currently available for AI indexing. Provider: {providerName ?? "OCR only"}."
            : pendingCount > 0
                ? $"{processedCount:N0} of {imageCount:N0} eligible media item(s) processed. {pendingCount:N0} remaining. OCR is active for images while visual tagging waits for {providerName ?? "a configured provider"}."
                : $"{imageCount:N0} eligible media item(s) are processed through OCR or visual tagging. Provider state: {providerName ?? "a configured provider"}.";
}

public sealed class WindowsOcrTextExtractor(
    IHeicDecoderService heicDecoderService,
    IThumbnailCacheService thumbnailCacheService) : IOcrTextExtractor
{
    private const uint MaxAnalysisDimension = 2200;

    public async Task<string?> ExtractTextAsync(MediaItem mediaItem, CancellationToken cancellationToken = default)
    {
        if (mediaItem.MediaKind != MediaKind.Image || !File.Exists(mediaItem.FullPath))
        {
            return null;
        }

        var inputPath = await ResolveInputPathAsync(mediaItem, cancellationToken);
        if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
        {
            return null;
        }

        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
        {
            return null;
        }

        var file = await StorageFile.GetFileFromPathAsync(inputPath);
        using var stream = await file.OpenAsync(FileAccessMode.Read);
        var decoder = await Windows.Graphics.Imaging.BitmapDecoder.CreateAsync(stream);
        var transform = CreateTransform(decoder);
        var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform, ExifOrientationMode.RespectExifOrientation, ColorManagementMode.DoNotColorManage);
        var result = await engine.RecognizeAsync(bitmap);

        var text = string.Join(' ', result.Lines.Select(line => line.Text?.Trim()).Where(line => !string.IsNullOrWhiteSpace(line)));
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    private async Task<string> ResolveInputPathAsync(MediaItem mediaItem, CancellationToken cancellationToken)
    {
        if (!heicDecoderService.IsHeicPath(mediaItem.FullPath))
        {
            return mediaItem.FullPath;
        }

        var previewKey = thumbnailCacheService.CreateCacheKey(mediaItem.FullPath, mediaItem.ModifiedUtc, mediaItem.SizeBytes, "ocr-preview-v1");
        var previewPath = await heicDecoderService.GetPreviewPathAsync(mediaItem.FullPath, previewKey, (int)MaxAnalysisDimension, (int)MaxAnalysisDimension, cancellationToken);
        return string.IsNullOrWhiteSpace(previewPath) ? mediaItem.FullPath : previewPath;
    }

    private static BitmapTransform CreateTransform(Windows.Graphics.Imaging.BitmapDecoder decoder)
    {
        var width = decoder.PixelWidth;
        var height = decoder.PixelHeight;
        var longestEdge = Math.Max(width, height);
        if (longestEdge <= MaxAnalysisDimension || longestEdge == 0)
        {
            return new BitmapTransform();
        }

        var scale = (double)MaxAnalysisDimension / longestEdge;
        return new BitmapTransform
        {
            ScaledWidth = Math.Max(1, (uint)Math.Round(width * scale)),
            ScaledHeight = Math.Max(1, (uint)Math.Round(height * scale))
        };
    }
}
