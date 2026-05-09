using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PhotoSauce.MagicScaler;
using PhotoSauce.NativeCodecs.Libheif;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Picazhu.Core;

namespace Picazhu.Media;

public sealed class HeicDecoderService(IAppPaths appPaths) : IHeicDecoderService
{
    private static readonly object CodecRegistrationLock = new();
    private static readonly object LibheifDecodeLock = new();
    private static bool _codecConfigured;

    private HeicDecoderDiagnostics _diagnostics = new(
        NativeWicDetected: false,
        NativeWicHealthy: false,
        LibheifRegistered: false,
        ActivePath: HeicDecoderPath.Unsupported,
        Summary: "HEIC startup checks not yet completed.",
        LastError: null);

    public bool IsHeicPath(string path)
        => string.Equals(Path.GetExtension(path), ".heic", StringComparison.OrdinalIgnoreCase) ||
           string.Equals(Path.GetExtension(path), ".heif", StringComparison.OrdinalIgnoreCase);

    public async Task InitializeAsync(IEnumerable<string> samplePaths, CancellationToken cancellationToken = default)
    {
        var libheifRegistered = TryRegisterLibheif(out var registrationError);
        var lastError = registrationError;
        var nativeDetected = false;
        var nativeHealthy = false;
        var activePath = libheifRegistered ? HeicDecoderPath.BundledLibheif : HeicDecoderPath.Unsupported;
        var summary = libheifRegistered
            ? "Bundled libheif fallback is ready. Native WIC validation is pending."
            : "No working HEIC decoder is registered yet.";

        foreach (var samplePath in samplePaths.Where(File.Exists))
        {
            cancellationToken.ThrowIfCancellationRequested();
            nativeDetected = true;
            string? fallbackError = null;
            if (TryProbeWithWic(samplePath, out _, out var probeError))
            {
                nativeHealthy = true;
                activePath = HeicDecoderPath.WindowsWic;
                summary = "Windows WIC HEIC decode is healthy. Bundled libheif remains ready as fallback.";
                lastError = null;
                break;
            }

            lastError = probeError ?? lastError;

            if (libheifRegistered && TryProbeWithLibheif(samplePath, out _, out fallbackError))
            {
                activePath = HeicDecoderPath.BundledLibheif;
                summary = "Windows WIC failed HEIC validation. Bundled libheif fallback is active.";
                lastError = probeError;
                break;
            }

            if (fallbackError is not null)
            {
                lastError = $"{lastError} | libheif: {fallbackError}";
            }
        }

        if (!nativeDetected)
        {
            summary = libheifRegistered
                ? "No HEIC sample was available at startup. Bundled libheif is ready and native WIC will be validated on first HEIC decode."
                : "No HEIC sample was available at startup, and bundled libheif failed to register.";
        }

        _diagnostics = new HeicDecoderDiagnostics(nativeDetected, nativeHealthy, libheifRegistered, activePath, summary, lastError);
        await Task.CompletedTask;
    }

    public HeicDecoderDiagnostics GetDiagnostics() => _diagnostics;

    public Task<(int Width, int Height, int? Orientation)?> ProbeAsync(string path, CancellationToken cancellationToken = default)
    {
        if (!IsHeicPath(path) || !File.Exists(path))
        {
            return Task.FromResult<(int Width, int Height, int? Orientation)?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (TryProbeWithWic(path, out var result, out _))
        {
            PromoteActivePath(HeicDecoderPath.WindowsWic, "Windows WIC HEIC decode is active.");
            return Task.FromResult<(int Width, int Height, int? Orientation)?>(result);
        }

        if (TryProbeWithLibheif(path, out result, out var libheifError))
        {
            PromoteActivePath(HeicDecoderPath.BundledLibheif, "Bundled libheif fallback is active for HEIC decode.");
            return Task.FromResult<(int Width, int Height, int? Orientation)?>(result);
        }

        RememberFailure(libheifError ?? "No working HEIC decoder could read this file.");
        return Task.FromResult<(int Width, int Height, int? Orientation)?>(null);
    }

    public Task<bool> TryCreateThumbnailAsync(string inputPath, string outputPath, int width, int height, CancellationToken cancellationToken = default)
    {
        if (!IsHeicPath(inputPath))
        {
            return Task.FromResult(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        try
        {
            if (TryCreateThumbnailWithWic(inputPath, outputPath, width, height))
            {
                PromoteActivePath(HeicDecoderPath.WindowsWic, "Windows WIC HEIC decode is active.");
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            RememberFailure($"WIC thumbnail decode failed: {ex.Message}");
        }

        try
        {
            if (TryCreateThumbnailWithLibheif(inputPath, outputPath, width, height))
            {
                PromoteActivePath(HeicDecoderPath.BundledLibheif, "Bundled libheif fallback is active for HEIC decode.");
                return Task.FromResult(true);
            }
        }
        catch (Exception ex)
        {
            RememberFailure($"libheif thumbnail decode failed: {ex.Message}");
        }

        SaveUnsupportedPlaceholder(outputPath, width, height, "HEIC", "Decoder unavailable");
        return Task.FromResult(true);
    }

    public Task<string?> GetPreviewPathAsync(string inputPath, string cacheKey, int width, int height, CancellationToken cancellationToken = default)
    {
        if (!IsHeicPath(inputPath))
        {
            return Task.FromResult<string?>(inputPath);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var previewFolder = Path.Combine(appPaths.TempPath, "preview-heic");
        System.IO.Directory.CreateDirectory(previewFolder);
        var previewPath = Path.Combine(previewFolder, $"{cacheKey}-{width}x{height}.jpg");

        if (File.Exists(previewPath))
        {
            return Task.FromResult<string?>(previewPath);
        }

        try
        {
            if (TryCreateThumbnailWithWic(inputPath, previewPath, width, height))
            {
                PromoteActivePath(HeicDecoderPath.WindowsWic, "Windows WIC HEIC decode is active.");
                return Task.FromResult<string?>(previewPath);
            }
        }
        catch (Exception ex)
        {
            RememberFailure($"WIC preview decode failed: {ex.Message}");
        }

        try
        {
            if (TryCreateThumbnailWithLibheif(inputPath, previewPath, width, height))
            {
                PromoteActivePath(HeicDecoderPath.BundledLibheif, "Bundled libheif fallback is active for HEIC decode.");
                return Task.FromResult<string?>(previewPath);
            }
        }
        catch (Exception ex)
        {
            RememberFailure($"libheif preview decode failed: {ex.Message}");
        }

        TryDeletePath(previewPath);
        return Task.FromResult<string?>(null);
    }

    private static bool TryProbeWithWic(string path, out (int Width, int Height, int? Orientation) result, out string? error)
    {
        try
        {
            using var stream = File.OpenRead(path);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            var frame = decoder.Frames[0];
            result = (frame.PixelWidth, frame.PixelHeight, ReadOrientation(path));
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            result = default;
            error = ex.Message;
            return false;
        }
    }

    private static bool TryProbeWithLibheif(string path, out (int Width, int Height, int? Orientation) result, out string? error)
    {
        try
        {
            lock (LibheifDecodeLock)
            {
                var info = ImageFileInfo.Load(path);
                if (!info.Frames.Any())
                {
                    result = default;
                    error = "No image frames were reported by libheif.";
                    return false;
                }

                var frame = info.Frames[0];
                result = (frame.Width, frame.Height, ReadOrientation(path));
                error = null;
                return true;
            }
        }
        catch (Exception ex)
        {
            result = default;
            error = ex.Message;
            return false;
        }
    }

    private static bool TryCreateThumbnailWithWic(string inputPath, string outputPath, int width, int height)
    {
        using var stream = File.OpenRead(inputPath);
        var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames[0];
        var scale = Math.Min((double)width / frame.PixelWidth, (double)height / frame.PixelHeight);
        BitmapSource transformed = scale < 1d
            ? new TransformedBitmap(frame, new ScaleTransform(scale, scale))
            : frame;

        var encoder = new JpegBitmapEncoder { QualityLevel = 86 };
        encoder.Frames.Add(BitmapFrame.Create(transformed));
        using var output = File.Create(outputPath);
        encoder.Save(output);
        return true;
    }

    private static bool TryCreateThumbnailWithLibheif(string inputPath, string outputPath, int width, int height)
    {
        var settings = new ProcessImageSettings
        {
            Width = width,
            Height = height,
            ResizeMode = CropScaleMode.Max,
            EncoderOptions = new JpegEncoderOptions(86, ChromaSubsampleMode.Subsample420, false)
        };

        settings.TrySetEncoderFormat("image/jpeg");
        lock (LibheifDecodeLock)
        {
            MagicImageProcessor.ProcessImage(inputPath, outputPath, settings);
        }

        return true;
    }

    private static int? ReadOrientation(string path)
    {
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(path);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            return ifd0?.GetInt32(ExifDirectoryBase.TagOrientation);
        }
        catch
        {
            return null;
        }
    }

    private static void SaveUnsupportedPlaceholder(string outputPath, int width, int height, string title, string subtitle)
    {
        var safeWidth = Math.Max(320, width);
        var safeHeight = Math.Max(220, height);
        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawRectangle(new LinearGradientBrush(
                (Color)ColorConverter.ConvertFromString("#18202B"),
                (Color)ColorConverter.ConvertFromString("#0F151D"),
                90), null, new Rect(0, 0, safeWidth, safeHeight));

            var frameRect = new Rect(22, 22, safeWidth - 44, safeHeight - 44);
            context.DrawRoundedRectangle(
                new SolidColorBrush((Color)ColorConverter.ConvertFromString("#202C39")),
                new Pen(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#314255")), 1),
                frameRect,
                18,
                18);

            DrawText(context, title, 28, FontWeights.SemiBold, Brushes.White, new Point(36, 46));
            DrawText(context, subtitle, 14, FontWeights.Normal, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#B1BEC9")), new Point(36, 84));
            DrawText(context, "PICAZHU could not decode this file on this machine.", 13, FontWeights.Normal, new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8E9EAE")), new Point(36, safeHeight - 50));
        }

        var bitmap = new RenderTargetBitmap(safeWidth, safeHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
        bitmap.Render(visual);

        var encoder = new JpegBitmapEncoder { QualityLevel = 84 };
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(outputPath);
        encoder.Save(output);
    }

    private static void DrawText(DrawingContext context, string text, double fontSize, FontWeight weight, Brush brush, Point origin)
    {
        var formatted = new FormattedText(
            text,
            System.Globalization.CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface(new FontFamily("Segoe UI Variable Display"), FontStyles.Normal, weight, FontStretches.Normal),
            fontSize,
            brush,
            1.0);
        context.DrawText(formatted, origin);
    }

    private static bool TryRegisterLibheif(out string? error)
    {
        lock (CodecRegistrationLock)
        {
            if (_codecConfigured)
            {
                error = null;
                return true;
            }

            try
            {
                CodecManager.Configure(codecs => codecs.UseLibheif(true));
                _codecConfigured = true;
                error = null;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }

    private static void TryDeletePath(string path)
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

    private void PromoteActivePath(HeicDecoderPath path, string summary)
    {
        _diagnostics = _diagnostics with
        {
            ActivePath = path,
            NativeWicHealthy = _diagnostics.NativeWicHealthy || path == HeicDecoderPath.WindowsWic,
            Summary = summary
        };
    }

    private void RememberFailure(string message)
    {
        _diagnostics = _diagnostics with
        {
            Summary = "PICAZHU could not decode at least one HEIC file and fell back to placeholder rendering.",
            LastError = message,
            ActivePath = _diagnostics.LibheifRegistered ? HeicDecoderPath.BundledLibheif : HeicDecoderPath.Unsupported
        };
    }
}
