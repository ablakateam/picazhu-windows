using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Picazhu.Core;

namespace Picazhu.Indexing;

public sealed class IndexingService(
    ICatalogRepository catalogRepository,
    IMediaProbeService mediaProbeService,
    IThumbnailGenerator thumbnailGenerator,
    IThumbnailCacheService thumbnailCacheService,
    IAiIndexingService aiIndexingService,
    ILogger<IndexingService> logger) : IIndexingService
{
    private readonly Channel<WatchedRoot> _scanQueue = Channel.CreateBounded<WatchedRoot>(32);
    private readonly Channel<IndexingWorkItem> _metadataQueue = Channel.CreateBounded<IndexingWorkItem>(512);
    private readonly Channel<ThumbnailRequest> _thumbQueue = Channel.CreateBounded<ThumbnailRequest>(512);
    private readonly ConcurrentDictionary<string, FileSystemWatcher> _watchers = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _debounce = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Task> _workers = [];
    private readonly object _progressLock = new();
    private readonly object _pauseLock = new();
    private CancellationTokenSource? _runCts;
    private CancellationTokenSource _activeWorkCts = new();
    private TaskCompletionSource<bool>? _resumeTcs;
    private bool _isPaused;
    private IndexingProgressSnapshot _progress = new(false, false, null, null, false, 0, 0, 0, null);

    public int WatcherCount => _watchers.Count;
    public int ScanQueueDepth { get; private set; }
    public int MetadataQueueDepth { get; private set; }
    public int ThumbnailQueueDepth { get; private set; }
    public IndexingProgressSnapshot GetProgress()
    {
        lock (_progressLock)
        {
            return _progress;
        }
    }

    public void PauseProcessing()
    {
        lock (_pauseLock)
        {
            if (_isPaused)
            {
                return;
            }

            _isPaused = true;
            _resumeTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        var progress = GetProgress();
        SetProgress(progress with { IsPaused = true });
    }

    public void ResumeProcessing()
    {
        TaskCompletionSource<bool>? resumeTcs;
        lock (_pauseLock)
        {
            if (!_isPaused)
            {
                return;
            }

            _isPaused = false;
            resumeTcs = _resumeTcs;
            _resumeTcs = null;
        }

        resumeTcs?.TrySetResult(true);
        var progress = GetProgress();
        SetProgress(progress with { IsPaused = false });
    }

    public void StopCurrentWork()
    {
        ResumeProcessing();

        var currentCts = Interlocked.Exchange(ref _activeWorkCts, new CancellationTokenSource());
        currentCts.Cancel();
        currentCts.Dispose();

        ClearPendingWork(_scanQueue, depth => ScanQueueDepth = depth);
        ClearPendingWork(_metadataQueue, depth => MetadataQueueDepth = depth);
        ClearPendingWork(_thumbQueue, depth => ThumbnailQueueDepth = depth);
        SetProgress(new IndexingProgressSnapshot(false, false, null, null, false, 0, 0, 0, null));
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_runCts is not null)
        {
            return;
        }

        _runCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _workers.Add(Task.Run(() => RunScanWorkerAsync(_runCts.Token), _runCts.Token));
        _workers.Add(Task.Run(() => RunMetadataWorkerAsync(_runCts.Token), _runCts.Token));
        _workers.Add(Task.Run(() => RunMetadataWorkerAsync(_runCts.Token), _runCts.Token));
        _workers.Add(Task.Run(() => RunThumbWorkerAsync(_runCts.Token), _runCts.Token));
        _workers.Add(Task.Run(() => RunThumbWorkerAsync(_runCts.Token), _runCts.Token));
        await WatchRootsAsync(cancellationToken);
        await RescanAllAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_runCts is null)
        {
            return;
        }

        _runCts.Cancel();
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        try
        {
            await Task.WhenAll(_workers);
        }
        catch (OperationCanceledException)
        {
        }
    }

    public async Task RescanAllAsync(CancellationToken cancellationToken = default)
    {
        foreach (var root in await catalogRepository.GetWatchedRootsAsync(cancellationToken))
        {
            await RescanRootAsync(root, cancellationToken);
        }
    }

    public async Task RescanRootAsync(WatchedRoot root, CancellationToken cancellationToken = default)
    {
        await _scanQueue.Writer.WriteAsync(root, cancellationToken);
        ScanQueueDepth++;
    }

    public async Task WatchRootsAsync(CancellationToken cancellationToken = default)
    {
        foreach (var watcher in _watchers.Values)
        {
            watcher.Dispose();
        }

        _watchers.Clear();
        foreach (var root in await catalogRepository.GetWatchedRootsAsync(cancellationToken))
        {
            if (!Directory.Exists(root.Path))
            {
                continue;
            }

            var watcher = new FileSystemWatcher(root.Path)
            {
                IncludeSubdirectories = root.IncludeSubfolders,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
            };

            FileSystemEventHandler handler = (_, _) => ScheduleRescan(root);
            RenamedEventHandler rename = (_, _) => ScheduleRescan(root);
            watcher.Created += handler;
            watcher.Changed += handler;
            watcher.Deleted += handler;
            watcher.Renamed += rename;
            watcher.Error += (_, args) =>
            {
                logger.LogWarning(args.GetException(), "Watcher error for {Root}", root.Path);
                ScheduleRescan(root);
            };
            watcher.EnableRaisingEvents = true;
            _watchers[root.Id] = watcher;
        }
    }

    public async Task QueueThumbnailAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
    {
        await _thumbQueue.Writer.WriteAsync(request, cancellationToken);
        ThumbnailQueueDepth++;
    }

    private void ScheduleRescan(WatchedRoot root)
    {
        var cts = new CancellationTokenSource();
        _debounce.AddOrUpdate(root.Id, cts, (_, existing) =>
        {
            existing.Cancel();
            existing.Dispose();
            return cts;
        });

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cts.Token);
                await RescanRootAsync(root, cts.Token);
            }
            catch (OperationCanceledException)
            {
            }
        }, cts.Token);
    }

    private async Task RunScanWorkerAsync(CancellationToken cancellationToken)
    {
        await foreach (var root in _scanQueue.Reader.ReadAllAsync(cancellationToken))
        {
            ScanQueueDepth = Math.Max(0, ScanQueueDepth - 1);
            if (!Directory.Exists(root.Path))
            {
                logger.LogWarning("Skipping unavailable root {Root}", root.Path);
                continue;
            }

            var seenFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenMedia = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _activeWorkCts.Token);
            var workerToken = workerCts.Token;
            await catalogRepository.MarkRootScanStartedAsync(root.Id, workerToken);

            try
            {
                var normalizedRootPath = Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var startedUtc = DateTimeOffset.UtcNow;
                SetProgress(new IndexingProgressSnapshot(
                    true,
                    _isPaused,
                    root.DisplayName,
                    root.Path,
                    true,
                    0,
                    0,
                    0,
                    startedUtc));

                var totalFiles = CountIndexableFiles(normalizedRootPath, root.IncludeSubfolders, workerToken);
                SetProgress(new IndexingProgressSnapshot(
                    true,
                    _isPaused,
                    root.DisplayName,
                    normalizedRootPath,
                    false,
                    totalFiles,
                    0,
                    0,
                    startedUtc));

                var filesIndexed = 0;
                var filesAddedThisRun = 0;
                foreach (var directory in EnumerateDirectoriesForScan(normalizedRootPath, root.IncludeSubfolders, workerToken))
                {
                    workerToken.ThrowIfCancellationRequested();
                    await WaitWhilePausedAsync(workerToken);
                    var fullDirectory = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    seenFolders.Add(fullDirectory);
                    var info = new DirectoryInfo(fullDirectory);
                    var folderId = StableId(fullDirectory);
                    var parent = info.Parent?.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                    await catalogRepository.UpsertFolderAsync(new FolderEntry(
                        folderId,
                        root.Id,
                        parent is null || !IsPathWithinRoot(parent, normalizedRootPath) ? null : StableId(parent),
                        fullDirectory,
                        info.Name,
                        Path.GetRelativePath(normalizedRootPath, fullDirectory),
                        0,
                        0,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow), workerToken);

                    foreach (var filePath in EnumerateFilesForScan(fullDirectory, workerToken))
                    {
                        workerToken.ThrowIfCancellationRequested();
                        await WaitWhilePausedAsync(workerToken);
                        if (MediaSupport.ShouldIgnoreFile(filePath))
                        {
                            continue;
                        }

                        var file = new FileInfo(filePath);
                        if (!MediaSupport.ShouldIndexFile(file.FullName))
                        {
                            continue;
                        }

                        var mediaKind = MediaSupport.GetMediaKind(file.Extension);

                        seenMedia.Add(file.FullName);
                        filesIndexed++;
                        filesAddedThisRun++;
                        var mediaItem = new MediaItem(
                            StableId(file.FullName),
                            folderId,
                            file.FullName,
                            file.Name,
                            file.Extension,
                            mediaKind,
                            null,
                            file.Exists ? file.Length : 0,
                            ToDateTimeOffset(file.CreationTimeUtc),
                            ToDateTimeOffset(file.LastWriteTimeUtc),
                            DateTimeOffset.UtcNow,
                            null,
                            null,
                            null,
                            null,
                            file.Attributes.HasFlag(FileAttributes.Hidden),
                            true,
                            MetadataState.Pending,
                            ThumbState.Pending,
                            null,
                            FileSignatures.CreateQuickHash(file.FullName, file.Exists ? file.Length : 0, ToDateTimeOffset(file.LastWriteTimeUtc)),
                            null,
                            null,
                            DateTimeOffset.UtcNow,
                            DateTimeOffset.UtcNow);
                        await catalogRepository.UpsertMediaItemAsync(mediaItem, workerToken);
                        await aiIndexingService.QueueMediaAsync(mediaItem, workerToken);

                        await _metadataQueue.Writer.WriteAsync(new IndexingWorkItem(
                            root.Id, root.Path, file.FullName, file.Name, file.Extension, mediaKind,
                            file.Exists ? file.Length : 0, ToDateTimeOffset(file.CreationTimeUtc), ToDateTimeOffset(file.LastWriteTimeUtc), DateTimeOffset.UtcNow, folderId), workerToken);
                        MetadataQueueDepth++;

                        await QueueThumbnailAsync(new ThumbnailRequest(mediaItem.Id, file.FullName, mediaItem.ModifiedUtc, mediaItem.SizeBytes, mediaItem.MediaKind, 360, 240), workerToken);

                        SetProgress(new IndexingProgressSnapshot(
                            true,
                            _isPaused,
                            root.DisplayName,
                            root.Path,
                            false,
                            totalFiles,
                            filesIndexed,
                            filesAddedThisRun,
                            startedUtc));
                    }
                }

                await catalogRepository.PruneMissingAsync(root.Id, seenFolders, seenMedia, workerToken);
                await catalogRepository.MarkRootScanCompletedAsync(root.Id, workerToken);
                SetProgress(new IndexingProgressSnapshot(false, false, root.DisplayName, root.Path, false, totalFiles, filesIndexed, filesAddedThisRun, startedUtc));
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                SetProgress(new IndexingProgressSnapshot(false, false, root.DisplayName, root.Path, false, 0, 0, 0, null));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Root scan failed for {Root}", root.Path);
                SetProgress(new IndexingProgressSnapshot(false, false, root.DisplayName, root.Path, false, 0, 0, 0, null));
            }
        }
    }

    private async Task RunMetadataWorkerAsync(CancellationToken cancellationToken)
    {
        await foreach (var workItem in _metadataQueue.Reader.ReadAllAsync(cancellationToken))
        {
            MetadataQueueDepth = Math.Max(0, MetadataQueueDepth - 1);
            try
            {
                using var workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _activeWorkCts.Token);
                var workerToken = workerCts.Token;
                await WaitWhilePausedAsync(workerToken);
                var result = await mediaProbeService.ProbeAsync(workItem, workerToken);
                await catalogRepository.UpdateMediaProbeAsync(StableId(workItem.FullPath), result with
                {
                    Metadata = result.Metadata with { MediaItemId = StableId(workItem.FullPath) }
                }, workerToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Probe failed for {Path}", workItem.FullPath);
            }
        }
    }

    private async Task RunThumbWorkerAsync(CancellationToken cancellationToken)
    {
        await foreach (var request in _thumbQueue.Reader.ReadAllAsync(cancellationToken))
        {
            ThumbnailQueueDepth = Math.Max(0, ThumbnailQueueDepth - 1);
            try
            {
                using var workerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _activeWorkCts.Token);
                var workerToken = workerCts.Token;
                await WaitWhilePausedAsync(workerToken);
                var generated = await thumbnailGenerator.GenerateAsync(request, workerToken);
                if (generated is null)
                {
                    await catalogRepository.UpdateMediaThumbnailStateAsync(request.MediaItemId, ThumbState.Failed, null, null, workerToken);
                    continue;
                }

                await catalogRepository.UpsertThumbnailAsync(generated, workerToken);
                await catalogRepository.UpdateMediaThumbnailStateAsync(request.MediaItemId, ThumbState.Done, generated.CacheKey, generated.RelativeCachePath, workerToken);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Thumb generation failed for {Path}", request.FullPath);
            }
        }

        await thumbnailCacheService.CleanupAsync(2L * 1024 * 1024 * 1024, cancellationToken);
    }

    private static string StableId(string value)
        => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..32];

    private static DateTimeOffset? ToDateTimeOffset(DateTime dateTime)
        => dateTime == DateTime.MinValue ? null : new DateTimeOffset(dateTime, TimeSpan.Zero);

    private void SetProgress(IndexingProgressSnapshot snapshot)
    {
        lock (_progressLock)
        {
            _progress = snapshot;
        }
    }

    private int CountIndexableFiles(string rootPath, bool includeSubfolders, CancellationToken cancellationToken)
    {
        var total = 0;
        foreach (var directory in EnumerateDirectoriesForScan(rootPath, includeSubfolders, cancellationToken))
        {
            foreach (var filePath in EnumerateFilesForScan(directory, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (MediaSupport.ShouldIndexFile(filePath))
                {
                    total++;
                }
            }
        }

        return total;
    }

    private IEnumerable<string> EnumerateDirectoriesForScan(string rootPath, bool includeSubfolders, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            var isRoot = string.Equals(
                Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase);
            if (!isRoot && ShouldSkipDirectory(current))
            {
                continue;
            }

            yield return current;

            if (!includeSubfolders)
            {
                continue;
            }

            string[] children;
            try
            {
                children = Directory.EnumerateDirectories(current).ToArray();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException or PathTooLongException)
            {
                logger.LogWarning(ex, "Skipping inaccessible directory {Directory}", current);
                continue;
            }

            for (var index = children.Length - 1; index >= 0; index--)
            {
                pending.Push(children[index]);
            }
        }
    }

    private IEnumerable<string> EnumerateFilesForScan(string directory, CancellationToken cancellationToken)
    {
        string[] files;
        try
        {
            files = Directory.EnumerateFiles(directory).ToArray();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException or PathTooLongException)
        {
            logger.LogWarning(ex, "Skipping inaccessible files in {Directory}", directory);
            yield break;
        }

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return file;
        }
    }

    private bool ShouldSkipDirectory(string directory)
    {
        try
        {
            return new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or DirectoryNotFoundException or PathTooLongException)
        {
            logger.LogWarning(ex, "Skipping inaccessible directory {Directory}", directory);
            return true;
        }
    }

    private static bool IsPathWithinRoot(string path, string rootPath)
    {
        var normalizedPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(normalizedPath, normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               normalizedPath.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async Task WaitWhilePausedAsync(CancellationToken cancellationToken)
    {
        Task waitTask;
        lock (_pauseLock)
        {
            if (!_isPaused)
            {
                return;
            }

            _resumeTcs ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            waitTask = _resumeTcs.Task;
        }

        await waitTask.WaitAsync(cancellationToken);
    }

    private static void ClearPendingWork<T>(Channel<T> channel, Action<int> setDepth)
    {
        while (channel.Reader.TryRead(out _))
        {
        }

        setDepth(0);
    }
}
