using Picazhu.Core;

namespace Picazhu.Data;

public static class PhoneImportPath
{
    private static readonly char[] DeviceSeparators = ['\\', '/'];

    public static bool IsSupportedDcimMediaPath(string devicePath)
        => IsUnderDcim(devicePath) && MediaSupport.ShouldIndexFile(devicePath);

    public static bool IsSupportedPhoneMediaPath(string devicePath)
        => MediaSupport.ShouldIndexFile(devicePath);

    public static bool IsUnderDcim(string devicePath)
        => SplitDevicePath(devicePath).Any(segment => string.Equals(segment, "DCIM", StringComparison.OrdinalIgnoreCase));

    public static string CreateDestinationRelativePath(string devicePath, bool preserveDcimFolders)
    {
        var segments = SplitDevicePath(devicePath);
        if (segments.Length == 0)
        {
            return "imported-media";
        }

        if (!preserveDcimFolders)
        {
            return SanitizeSegment(segments[^1]);
        }

        var dcimIndex = Array.FindIndex(segments, segment => string.Equals(segment, "DCIM", StringComparison.OrdinalIgnoreCase));
        string[] relativeSegments;
        if (dcimIndex >= 0)
        {
            relativeSegments = segments[dcimIndex..];
        }
        else
        {
            var internalStorageIndex = Array.FindIndex(segments, segment => string.Equals(segment, "Internal Storage", StringComparison.OrdinalIgnoreCase));
            relativeSegments = internalStorageIndex >= 0 && internalStorageIndex < segments.Length - 1
                ? segments[(internalStorageIndex + 1)..]
                : [segments[^1]];
        }

        return Path.Combine(relativeSegments.Select(SanitizeSegment).Where(segment => !string.IsNullOrWhiteSpace(segment)).ToArray());
    }

    public static bool IsExactDuplicate(string destinationPath, long sourceSizeBytes, DateTimeOffset? sourceModifiedUtc)
    {
        if (!File.Exists(destinationPath))
        {
            return false;
        }

        var destination = new FileInfo(destinationPath);
        if (destination.Length != sourceSizeBytes)
        {
            return false;
        }

        if (sourceModifiedUtc is null)
        {
            return true;
        }

        var delta = destination.LastWriteTimeUtc - sourceModifiedUtc.Value.UtcDateTime;
        return Math.Abs(delta.TotalSeconds) <= 2;
    }

    public static string SanitizeSegment(string segment)
    {
        var trimmed = string.IsNullOrWhiteSpace(segment) ? "untitled" : segment.Trim();
        foreach (var invalid in Path.GetInvalidFileNameChars())
        {
            trimmed = trimmed.Replace(invalid, '_');
        }

        trimmed = trimmed.Replace("..", "_", StringComparison.Ordinal);
        return string.IsNullOrWhiteSpace(trimmed) ? "untitled" : trimmed;
    }

    private static string[] SplitDevicePath(string devicePath)
        => (devicePath ?? string.Empty)
            .Split(DeviceSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
