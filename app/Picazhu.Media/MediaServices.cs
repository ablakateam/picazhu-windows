using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Picazhu.Core;

namespace Picazhu.Media;

public sealed class MediaProbeService(IHeicDecoderService heicDecoderService) : IMediaProbeService
{
    public Task<ProbeResult> ProbeAsync(IndexingWorkItem workItem, CancellationToken cancellationToken = default)
        => Task.FromResult(workItem.MediaKind switch
        {
            MediaKind.Image => ProbeImage(workItem, cancellationToken),
            MediaKind.Video => ProbeVideo(workItem),
            _ => new ProbeResult(null, null, null, null, new MediaMetadata(workItem.FullPath, null, null, null, null, null, null, null, null, null, null), false)
        });

    private ProbeResult ProbeImage(IndexingWorkItem workItem, CancellationToken cancellationToken)
    {
        try
        {
            if (heicDecoderService.IsHeicPath(workItem.FullPath))
            {
                var heicProbe = heicDecoderService.ProbeAsync(workItem.FullPath, cancellationToken).GetAwaiter().GetResult();
                var heicMetadata = ReadImageMetadata(workItem.FullPath);
                var dateTakenHeic = GetBestImageDateTakenUtc(heicMetadata.Ifd0, heicMetadata.IfdSub);
                var locationHeic = heicMetadata.Gps?.GetGeoLocation();

                return new ProbeResult(
                    heicProbe?.Width,
                    heicProbe?.Height,
                    null,
                    heicProbe?.Orientation ?? heicMetadata.Ifd0?.GetInt32(ExifDirectoryBase.TagOrientation),
                    new MediaMetadata(
                        workItem.FullPath,
                        GetTagDescription(heicMetadata.Ifd0, ExifDirectoryBase.TagMake),
                        GetTagDescription(heicMetadata.Ifd0, ExifDirectoryBase.TagModel),
                        dateTakenHeic,
                        locationHeic?.Latitude,
                        locationHeic?.Longitude,
                        "HEIC",
                        null,
                        null,
                        GetColorProfile(heicMetadata.Ifd0, heicMetadata.IfdSub),
                        null),
                    heicProbe is not null);
            }

            using var stream = File.OpenRead(workItem.FullPath);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            var metadata = ReadImageMetadata(workItem.FullPath);
            var dateTaken = GetBestImageDateTakenUtc(metadata.Ifd0, metadata.IfdSub);
            var location = metadata.Gps?.GetGeoLocation();

            return new ProbeResult(
                frame.PixelWidth,
                frame.PixelHeight,
                null,
                metadata.Ifd0?.GetInt32(ExifDirectoryBase.TagOrientation),
                new MediaMetadata(
                    workItem.FullPath,
                    GetTagDescription(metadata.Ifd0, ExifDirectoryBase.TagMake),
                    GetTagDescription(metadata.Ifd0, ExifDirectoryBase.TagModel),
                    dateTaken,
                    location?.Latitude,
                    location?.Longitude,
                    null,
                    null,
                    null,
                    GetColorProfile(metadata.Ifd0, metadata.IfdSub),
                    null),
                true);
        }
        catch
        {
            return new ProbeResult(null, null, null, null, new MediaMetadata(workItem.FullPath, null, null, null, null, null, null, null, null, null, null), false);
        }
    }

    private static ProbeResult ProbeVideo(IndexingWorkItem workItem)
    {
        try
        {
            using var file = TagLib.File.Create(workItem.FullPath);
            var props = file.Properties;
            return new ProbeResult(
                props.VideoWidth > 0 ? props.VideoWidth : null,
                props.VideoHeight > 0 ? props.VideoHeight : null,
                (long)props.Duration.TotalMilliseconds,
                null,
                new MediaMetadata(
                    workItem.FullPath,
                    null,
                    null,
                    null,
                    null,
                    null,
                    props.Codecs.FirstOrDefault()?.Description,
                    props.AudioBitrate > 0 ? props.AudioBitrate : null,
                    null,
                    null,
                    null),
                true);
        }
        catch
        {
            return new ProbeResult(null, null, null, null, new MediaMetadata(workItem.FullPath, null, null, null, null, null, null, null, null, null, null), false);
        }
    }

    private static (ExifIfd0Directory? Ifd0, ExifSubIfdDirectory? IfdSub, GpsDirectory? Gps) ReadImageMetadata(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            return (
                directories.OfType<ExifIfd0Directory>().FirstOrDefault(),
                directories.OfType<ExifSubIfdDirectory>().FirstOrDefault(),
                directories.OfType<GpsDirectory>().FirstOrDefault());
        }
        catch
        {
            return (null, null, null);
        }
    }

    private static DateTimeOffset? GetBestImageDateTakenUtc(ExifIfd0Directory? ifd0, ExifSubIfdDirectory? ifdSub)
    {
        var localDate =
            ifdSub?.GetDateTime(ExifDirectoryBase.TagDateTimeOriginal) ??
            ifdSub?.GetDateTime(ExifDirectoryBase.TagDateTimeDigitized) ??
            ifdSub?.GetDateTime(ExifDirectoryBase.TagDateTime) ??
            ifd0?.GetDateTime(ExifDirectoryBase.TagDateTime);

        return localDate is null
            ? null
            : new DateTimeOffset(DateTime.SpecifyKind(localDate.Value, DateTimeKind.Local)).ToUniversalTime();
    }

    private static string? GetTagDescription(MetadataExtractor.Directory? directory, int tagType)
    {
        var value = directory?.GetDescription(tagType);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? GetColorProfile(ExifIfd0Directory? ifd0, ExifSubIfdDirectory? ifdSub)
        => GetTagDescription(ifdSub, ExifDirectoryBase.TagColorSpace) ??
           GetTagDescription(ifd0, ExifDirectoryBase.TagColorSpace);
}

public sealed class ThumbnailGenerator(IThumbnailCacheService thumbnailCacheService, IHeicDecoderService heicDecoderService) : IThumbnailGenerator
{
    public Task<ThumbnailRecord?> GenerateAsync(ThumbnailRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var profileVersion = request.MediaKind switch
            {
                MediaKind.Image when heicDecoderService.IsHeicPath(request.FullPath) => "heic-v2",
                MediaKind.Video => "video-v2",
                _ => "v1"
            };
            var cacheKey = thumbnailCacheService.CreateCacheKey(request.FullPath, request.ModifiedUtc, request.SizeBytes, profileVersion);
            var relativePath = thumbnailCacheService.GetRelativeThumbnailPath(cacheKey);
            var absolutePath = thumbnailCacheService.GetAbsoluteThumbnailPath(cacheKey);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

            if (File.Exists(absolutePath) && !IsValidThumbnailFile(absolutePath))
            {
                TryDeleteFile(absolutePath);
            }

            if (!File.Exists(absolutePath))
            {
                if (request.MediaKind == MediaKind.Image)
                {
                    if (heicDecoderService.IsHeicPath(request.FullPath))
                    {
                        var created = heicDecoderService.TryCreateThumbnailAsync(request.FullPath, absolutePath, request.TargetWidth, request.TargetHeight, cancellationToken).GetAwaiter().GetResult();
                        if (!created)
                        {
                            return Task.FromResult<ThumbnailRecord?>(null);
                        }
                    }
                    else
                    {
                        SaveImageThumbnail(request.FullPath, absolutePath, request.TargetWidth, request.TargetHeight);
                    }
                }
                else if (request.MediaKind == MediaKind.Video)
                {
                    SaveShellThumbnail(request.FullPath, absolutePath, request.TargetWidth, request.TargetHeight);
                }
                else
                {
                    return Task.FromResult<ThumbnailRecord?>(null);
                }
            }

            if (!IsValidThumbnailFile(absolutePath))
            {
                TryDeleteFile(absolutePath);
                return Task.FromResult<ThumbnailRecord?>(null);
            }

            var fileInfo = new FileInfo(absolutePath);
            return Task.FromResult<ThumbnailRecord?>(new ThumbnailRecord(
                request.MediaItemId,
                cacheKey,
                relativePath,
                request.TargetWidth,
                request.TargetHeight,
                "jpg",
                fileInfo.Exists ? fileInfo.Length : 0,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));
        }
        catch
        {
            return Task.FromResult<ThumbnailRecord?>(null);
        }
    }

    public static bool IsValidThumbnailFile(string? thumbnailPath)
    {
        if (string.IsNullOrWhiteSpace(thumbnailPath) || !File.Exists(thumbnailPath))
        {
            return false;
        }

        try
        {
            var fileInfo = new FileInfo(thumbnailPath);
            if (!fileInfo.Exists || fileInfo.Length <= 0)
            {
                return false;
            }

            using var stream = File.Open(thumbnailPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            return frame.PixelWidth > 0 && frame.PixelHeight > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    private static void SaveImageThumbnail(string inputPath, string outputPath, int width, int height)
    {
        using var stream = File.OpenRead(inputPath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var scale = Math.Min((double)width / frame.PixelWidth, (double)height / frame.PixelHeight);
        BitmapSource transformed = scale < 1d
            ? new TransformedBitmap(frame, new ScaleTransform(scale, scale))
            : frame;

        var encoder = new JpegBitmapEncoder { QualityLevel = 82 };
        encoder.Frames.Add(BitmapFrame.Create(transformed));
        using var output = File.Create(outputPath);
        encoder.Save(output);
    }

    private static void SaveShellThumbnail(string inputPath, string outputPath, int width, int height)
    {
        var hBitmap = IntPtr.Zero;
        try
        {
            var item = ShellItemFactory.Create(inputPath);
            item.GetImage(new NativeSize(width, height), ShellImageOptions.ResizeToFit | ShellImageOptions.ThumbnailOnly | ShellImageOptions.BiggerSizeOk, out hBitmap);
            var source = Imaging.CreateBitmapSourceFromHBitmap(hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = File.Create(outputPath);
            encoder.Save(output);
        }
        finally
        {
            if (hBitmap != IntPtr.Zero)
            {
                DeleteObject(hBitmap);
            }
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);
}

public sealed class QuickPreviewService(IAppPaths appPaths, IHeicDecoderService heicDecoderService, IThumbnailCacheService thumbnailCacheService) : IQuickPreviewService
{
    public async Task<QuickPreviewResult> GetPreviewAsync(MediaItem item, CancellationToken cancellationToken = default)
    {
        var thumbPath = string.IsNullOrWhiteSpace(item.ThumbnailRelativePath)
            ? null
            : Path.Combine(appPaths.ThumbsPath, item.ThumbnailRelativePath);

        if (item.MediaKind == MediaKind.Image && heicDecoderService.IsHeicPath(item.FullPath))
        {
            var previewKey = thumbnailCacheService.CreateCacheKey(item.FullPath, item.ModifiedUtc, item.SizeBytes, "heic-preview-v3");
            var previewPath = await heicDecoderService.GetPreviewPathAsync(item.FullPath, previewKey, 1600, 1200, cancellationToken);
            return new QuickPreviewResult(item.FullPath, item.MediaKind, previewPath ?? thumbPath, item.FileName);
        }

        return new QuickPreviewResult(item.FullPath, item.MediaKind, thumbPath, item.FileName);
    }
}

internal static class ShellItemFactory
{
    private static readonly Guid ShellItemImageFactoryGuid = new("bcc18b79-ba16-442f-80c4-8a59c30c463b");

    public static IShellItemImageFactory Create(string path)
    {
        SHCreateItemFromParsingName(path, IntPtr.Zero, ShellItemImageFactoryGuid, out var result);
        return result;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(
        [MarshalAs(UnmanagedType.LPWStr)] string path,
        IntPtr pbc,
        [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory shellItem);
}

[ComImport]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b")]
internal interface IShellItemImageFactory
{
    void GetImage(NativeSize size, ShellImageOptions flags, out IntPtr hBitmap);
}

[StructLayout(LayoutKind.Sequential)]
internal readonly struct NativeSize(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
}

[Flags]
internal enum ShellImageOptions
{
    ResizeToFit = 0,
    BiggerSizeOk = 0x1,
    ThumbnailOnly = 0x8
}
