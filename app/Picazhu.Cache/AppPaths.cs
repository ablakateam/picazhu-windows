using System.Security.Cryptography;
using System.Text;
using Picazhu.Core;

namespace Picazhu.Cache;

public sealed class AppPaths : IAppPaths
{
    public AppPaths()
    {
        RootPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Picazhu");
        DatabasePath = Path.Combine(RootPath, "db", "catalog.db");
        ThumbsPath = Path.Combine(RootPath, "thumbs");
        LogsPath = Path.Combine(RootPath, "logs");
        TempPath = Path.Combine(RootPath, "temp");
        AiPath = Path.Combine(RootPath, "ai");

        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath)!);
        Directory.CreateDirectory(ThumbsPath);
        Directory.CreateDirectory(LogsPath);
        Directory.CreateDirectory(TempPath);
        Directory.CreateDirectory(Path.Combine(AiPath, "embeddings"));
        Directory.CreateDirectory(Path.Combine(AiPath, "provider-cache"));
    }

    public string RootPath { get; }
    public string DatabasePath { get; }
    public string ThumbsPath { get; }
    public string LogsPath { get; }
    public string TempPath { get; }
    public string AiPath { get; }
}

public sealed class ThumbnailCacheService(IAppPaths appPaths) : IThumbnailCacheService
{
    public string GetRelativeThumbnailPath(string cacheKey)
        => Path.Combine(cacheKey[..2], $"{cacheKey}.jpg");

    public string GetAbsoluteThumbnailPath(string cacheKey)
        => Path.Combine(appPaths.ThumbsPath, GetRelativeThumbnailPath(cacheKey));

    public string CreateCacheKey(string fullPath, DateTimeOffset? modifiedUtc, long sizeBytes, string profileVersion)
    {
        var input = $"{fullPath}|{modifiedUtc?.ToUnixTimeMilliseconds() ?? 0}|{sizeBytes}|{profileVersion}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public Task EnsureCacheFoldersAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(appPaths.ThumbsPath);
        return Task.CompletedTask;
    }

    public Task<long> GetCacheSizeBytesAsync(CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            if (!Directory.Exists(appPaths.ThumbsPath))
            {
                return 0L;
            }

            long size = 0;
            foreach (var path in EnumerateCacheFiles(appPaths.ThumbsPath, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var file = new FileInfo(path);
                    if (file.Exists)
                    {
                        size += file.Length;
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
                {
                }
            }

            return size;
        }, cancellationToken);

    public Task CleanupAsync(long maxBytes, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            if (!Directory.Exists(appPaths.ThumbsPath))
            {
                return;
            }

            var files = EnumerateCacheFiles(appPaths.ThumbsPath, cancellationToken)
                .Select(path =>
                {
                    try
                    {
                        return new FileInfo(path);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
                    {
                        return null;
                    }
                })
                .Where(file => file is not null && file.Exists)
                .OrderBy(file => file!.LastAccessTimeUtc)
                .ToList();

            var currentSize = files.Sum(file => file!.Length);
            foreach (var file in files)
            {
                if (currentSize <= maxBytes)
                {
                    break;
                }

                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    currentSize -= file!.Length;
                    file.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
                {
                }
            }
        }, cancellationToken);

    private static IEnumerable<string> EnumerateCacheFiles(string rootPath, CancellationToken cancellationToken)
    {
        var pending = new Stack<string>();
        pending.Push(rootPath);

        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();

            string[] files;
            try
            {
                files = Directory.GetFiles(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException or PathTooLongException)
            {
                continue;
            }

            foreach (var directory in directories)
            {
                pending.Push(directory);
            }
        }
    }
}
