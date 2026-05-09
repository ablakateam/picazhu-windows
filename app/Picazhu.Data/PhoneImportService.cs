using MediaDevices;
using Picazhu.Core;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Picazhu.Data;

[SupportedOSPlatform("windows7.0")]
public sealed class PhoneImportService : IPhoneImportService
{
    private const string RootPath = @"\";

    public Task<IReadOnlyList<PhoneDeviceInfo>> GetDevicesAsync(CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<PhoneDeviceInfo>>(() =>
        {
            if (!OperatingSystem.IsWindows())
            {
                return [];
            }

            cancellationToken.ThrowIfCancellationRequested();
            var devices = MediaDevice.GetDevices().ToList();
            try
            {
                return devices
                    .Select(CreateDeviceInfo)
                    .OrderByDescending(device => device.IsLikelyIPhone)
                    .ThenBy(device => device.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            finally
            {
                foreach (var device in devices)
                {
                    device.Dispose();
                }
            }
        }, cancellationToken);

    public Task<IReadOnlyList<PhoneMediaItem>> GetDeviceMediaAsync(string deviceId, CancellationToken cancellationToken = default)
        => Task.Run<IReadOnlyList<PhoneMediaItem>>(() =>
        {
            if (string.IsNullOrWhiteSpace(deviceId))
            {
                throw new ArgumentException("Device id is required.", nameof(deviceId));
            }

            using var device = FindDeviceOrThrow(deviceId);
            ConnectForRead(device);

            try
            {
                var media = new List<PhoneMediaItem>();
                foreach (var cameraRoot in FindCameraRoots(device, cancellationToken))
                {
                    foreach (var devicePath in EnumerateFilesDepthFirst(device, cameraRoot, cancellationToken))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!PhoneImportPath.IsSupportedPhoneMediaPath(devicePath))
                        {
                            continue;
                        }

                        var fileInfo = TryGetFileInfo(device, devicePath);
                        var fileName = fileInfo?.Name;
                        if (string.IsNullOrWhiteSpace(fileName))
                        {
                            fileName = Path.GetFileName(devicePath);
                        }

                        var extension = Path.GetExtension(fileName);
                        var modifiedUtc = ToUtc(fileInfo?.LastWriteTime ?? fileInfo?.DateAuthored ?? fileInfo?.CreationTime);
                        media.Add(new PhoneMediaItem(
                            deviceId,
                            devicePath,
                            PhoneImportPath.CreateDestinationRelativePath(devicePath, preserveDcimFolders: true),
                            fileName,
                            extension,
                            MediaSupport.GetMediaKind(extension),
                            SafeLength(fileInfo),
                            modifiedUtc));
                    }
                }

                return media
                    .GroupBy(item => item.DevicePath, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .OrderByDescending(item => item.ModifiedUtc)
                    .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            finally
            {
                DisconnectQuietly(device);
            }
        }, cancellationToken);

    public Task<bool> TryDownloadThumbnailAsync(PhoneMediaItem item, string outputPath, CancellationToken cancellationToken = default)
        => Task.Run(() =>
        {
            if (string.IsNullOrWhiteSpace(item.DeviceId) || string.IsNullOrWhiteSpace(item.DevicePath) || string.IsNullOrWhiteSpace(outputPath))
            {
                return false;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
            using var device = FindDeviceOrThrow(item.DeviceId);
            ConnectForRead(device);

            var tempPath = $"{outputPath}.picazhu-thumb";
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                TryDelete(tempPath);
                using (var stream = File.Create(tempPath))
                {
                    device.DownloadThumbnail(item.DevicePath, stream);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var tempFile = new FileInfo(tempPath);
                if (!tempFile.Exists || tempFile.Length == 0)
                {
                    TryDelete(tempPath);
                    return false;
                }

                File.Move(tempPath, outputPath, overwrite: true);
                return true;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
            {
                TryDelete(tempPath);
                return false;
            }
            finally
            {
                DisconnectQuietly(device);
            }
        }, cancellationToken);

    public Task<PhoneImportResult> ImportAsync(PhoneImportRequest request, IProgress<PhoneImportProgressSnapshot>? progress = null, CancellationToken cancellationToken = default)
        => Task.Run(() => ImportCore(request, progress, cancellationToken), cancellationToken);

    private static PhoneImportResult ImportCore(PhoneImportRequest request, IProgress<PhoneImportProgressSnapshot>? progress, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.DeviceId))
        {
            throw new ArgumentException("Device id is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.DestinationFolder))
        {
            throw new ArgumentException("Destination folder is required.", nameof(request));
        }

        Directory.CreateDirectory(request.DestinationFolder);

        using var device = FindDeviceOrThrow(request.DeviceId);
        ConnectForRead(device);

        var copiedCount = 0;
        var skippedCount = 0;
        var renamedCount = 0;
        var failedCount = 0;
        var errors = new List<string>();
        var totalItems = request.Items.Count;

        try
        {
            for (var index = 0; index < totalItems; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = request.Items[index];
                progress?.Report(new PhoneImportProgressSnapshot(true, totalItems, index, copiedCount, skippedCount, renamedCount, failedCount, item.FileName));

                try
                {
                    var destinationRelativePath = request.PreserveDcimFolders
                        ? PhoneImportPath.CreateDestinationRelativePath(item.DevicePath, preserveDcimFolders: true)
                        : PhoneImportPath.SanitizeSegment(item.FileName);
                    var destinationPath = Path.Combine(request.DestinationFolder, destinationRelativePath);
                    var destinationFolder = Path.GetDirectoryName(destinationPath) ?? request.DestinationFolder;
                    Directory.CreateDirectory(destinationFolder);

                    if (PhoneImportPath.IsExactDuplicate(destinationPath, item.SizeBytes, item.ModifiedUtc))
                    {
                        skippedCount++;
                        progress?.Report(new PhoneImportProgressSnapshot(true, totalItems, index + 1, copiedCount, skippedCount, renamedCount, failedCount, item.FileName));
                        continue;
                    }

                    if (File.Exists(destinationPath))
                    {
                        destinationPath = ExportService.GetUniqueDestinationPath(destinationFolder, Path.GetFileName(destinationPath), out var renamed);
                        if (renamed)
                        {
                            renamedCount++;
                        }
                    }

                    DownloadToFile(device, item, destinationPath, cancellationToken);
                    copiedCount++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
                {
                    failedCount++;
                    errors.Add($"{item.FileName}: {ex.Message}");
                }

                progress?.Report(new PhoneImportProgressSnapshot(true, totalItems, index + 1, copiedCount, skippedCount, renamedCount, failedCount, item.FileName));
            }
        }
        finally
        {
            DisconnectQuietly(device);
        }

        progress?.Report(new PhoneImportProgressSnapshot(false, totalItems, totalItems, copiedCount, skippedCount, renamedCount, failedCount, null));
        return new PhoneImportResult(totalItems, copiedCount, skippedCount, renamedCount, failedCount, errors, request.DestinationFolder);
    }

    private static void DownloadToFile(MediaDevice device, PhoneMediaItem item, string destinationPath, CancellationToken cancellationToken)
    {
        var tempPath = $"{destinationPath}.picazhu-importing";
        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            using (var stream = File.Create(tempPath))
            {
                device.DownloadFile(item.DevicePath, stream);
            }

            if (item.ModifiedUtc is not null)
            {
                File.SetLastWriteTimeUtc(tempPath, item.ModifiedUtc.Value.UtcDateTime);
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(tempPath, destinationPath, overwrite: false);
        }
        catch
        {
            TryDelete(tempPath);
            throw;
        }
    }

    private static IEnumerable<string> FindCameraRoots(MediaDevice device, CancellationToken cancellationToken)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownCandidates = new[]
        {
            @"\Internal Storage\DCIM",
            @"\DCIM"
        };

        foreach (var candidate in knownCandidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (seen.Add(candidate) && SafeDirectoryExists(device, candidate))
            {
                yield return candidate;
            }
        }

        var isAppleDevice = IsLikelyAppleDevice(device);
        foreach (var topLevel in SafeEnumerateDirectories(device, RootPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (IsDcimDirectory(topLevel) && seen.Add(topLevel))
            {
                yield return topLevel;
                continue;
            }

            foreach (var child in SafeEnumerateDirectories(device, topLevel))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((IsDcimDirectory(child) || (isAppleDevice && IsLikelyIPhoneCameraDirectory(child, device))) && seen.Add(child))
                {
                    yield return child;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateFilesDepthFirst(MediaDevice device, string rootPath, CancellationToken cancellationToken)
    {
        var stack = new Stack<string>();
        stack.Push(rootPath);
        var seenDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = stack.Pop();
            if (!seenDirectories.Add(current))
            {
                continue;
            }

            foreach (var file in SafeEnumerateFiles(device, current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return file;
            }

            foreach (var directory in SafeEnumerateDirectories(device, current).Reverse())
            {
                cancellationToken.ThrowIfCancellationRequested();
                stack.Push(directory);
            }
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(MediaDevice device, string path)
    {
        try
        {
            return device.EnumerateFiles(path).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
        {
            return [];
        }
    }

    private static IEnumerable<string> SafeEnumerateDirectories(MediaDevice device, string path)
    {
        try
        {
            return device.EnumerateDirectories(path).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
        {
            return [];
        }
    }

    private static bool SafeDirectoryExists(MediaDevice device, string path)
    {
        try
        {
            return device.DirectoryExists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static MediaFileInfo? TryGetFileInfo(MediaDevice device, string path)
    {
        try
        {
            return device.GetFileInfo(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
        {
            return null;
        }
    }

    private static MediaDevice FindDeviceOrThrow(string deviceId)
    {
        var device = MediaDevice.GetDevices()
            .FirstOrDefault(item => string.Equals(item.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));

        return device ?? throw new InvalidOperationException("The selected iPhone or portable device is no longer connected.");
    }

    private static void ConnectForRead(MediaDevice device)
    {
        if (!device.IsConnected)
        {
            device.Connect(MediaDeviceAccess.GenericRead, MediaDeviceShare.Read, enableCache: true);
        }
    }

    private static void DisconnectQuietly(MediaDevice device)
    {
        try
        {
            if (device.IsConnected)
            {
                device.Disconnect();
            }
        }
        catch
        {
            // The device may already be gone. Import results should carry item-level failures.
        }
    }

    private static PhoneDeviceInfo CreateDeviceInfo(MediaDevice device)
    {
        var displayName = SafeRead(() => device.FriendlyName)
                          ?? SafeRead(() => device.Description)
                          ?? SafeRead(() => device.Model)
                          ?? "Portable device";
        var manufacturer = SafeRead(() => device.Manufacturer);
        var model = SafeRead(() => device.Model);
        var description = SafeRead(() => device.Description);
        var isLikelyIPhone = ContainsApplePhoneSignal(displayName) ||
                             ContainsApplePhoneSignal(manufacturer) ||
                             ContainsApplePhoneSignal(model) ||
                             ContainsApplePhoneSignal(description);

        return new PhoneDeviceInfo(
            device.DeviceId,
            displayName,
            manufacturer,
            isLikelyIPhone,
            device.IsConnected);
    }

    private static bool IsLikelyAppleDevice(MediaDevice device)
    {
        var displayName = SafeRead(() => device.FriendlyName);
        var manufacturer = SafeRead(() => device.Manufacturer);
        var model = SafeRead(() => device.Model);
        var description = SafeRead(() => device.Description);

        return ContainsApplePhoneSignal(displayName) ||
               ContainsApplePhoneSignal(manufacturer) ||
               ContainsApplePhoneSignal(model) ||
               ContainsApplePhoneSignal(description);
    }

    private static bool ContainsApplePhoneSignal(string? value)
        => !string.IsNullOrWhiteSpace(value) &&
           (value.Contains("iphone", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("apple", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("ipad", StringComparison.OrdinalIgnoreCase));

    private static bool IsDcimDirectory(string path)
        => string.Equals(Path.GetFileName(path.TrimEnd('\\', '/')), "DCIM", StringComparison.OrdinalIgnoreCase);

    private static bool IsLikelyIPhoneCameraDirectory(string path, MediaDevice device)
    {
        var name = Path.GetFileName(path.TrimEnd('\\', '/'));
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d{6}_[A-Za-z]$", System.Text.RegularExpressions.RegexOptions.CultureInvariant) ||
            System.Text.RegularExpressions.Regex.IsMatch(name, @"^\d{3}(APPLE|CLOUD)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant))
        {
            return true;
        }

        // Newer iPhone/WPD stacks can expose camera-month folders directly under Internal Storage.
        return SafeEnumerateFiles(device, path).Take(24).Any(PhoneImportPath.IsSupportedPhoneMediaPath);
    }

    private static DateTimeOffset? ToUtc(DateTime? dateTime)
    {
        if (dateTime is null || dateTime.Value == default)
        {
            return null;
        }

        return dateTime.Value.Kind switch
        {
            DateTimeKind.Utc => new DateTimeOffset(dateTime.Value),
            DateTimeKind.Local => new DateTimeOffset(dateTime.Value).ToUniversalTime(),
            _ => new DateTimeOffset(DateTime.SpecifyKind(dateTime.Value, DateTimeKind.Local)).ToUniversalTime()
        };
    }

    private static long SafeLength(MediaFileInfo? fileInfo)
    {
        if (fileInfo is null)
        {
            return 0;
        }

        return fileInfo.Length > long.MaxValue ? long.MaxValue : (long)fileInfo.Length;
    }

    private static string? SafeRead(Func<string?> read)
    {
        try
        {
            return read();
        }
        catch
        {
            return null;
        }
    }

    private static void TryDelete(string path)
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
            // Best effort cleanup only.
        }
    }
}
