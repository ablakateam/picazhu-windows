using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using Picazhu.Core;

namespace Picazhu.App;

public sealed partial class PhoneImportViewModel(
    MainViewModel mainViewModel,
    IPhoneImportService phoneImportService,
    IAppPaths appPaths) : ObservableObject
{
    private CancellationTokenSource? _importCancellationSource;
    private CancellationTokenSource? _thumbnailCancellationSource;

    public ObservableCollection<PhoneDeviceOptionViewModel> Devices { get; } = [];
    public ObservableCollection<PhoneMediaOptionViewModel> MediaItems { get; } = [];

    [ObservableProperty] private PhoneDeviceOptionViewModel? selectedDevice;
    [ObservableProperty] private string destinationFolder = CreateDefaultDestinationFolder();
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private bool isImporting;
    [ObservableProperty] private double progressPercent;
    [ObservableProperty] private double thumbnailProgressPercent;
    [ObservableProperty] private bool isLoadingThumbnails;
    [ObservableProperty] private string statusText = "Connect your iPhone, unlock it, and tap Trust if prompted.";
    [ObservableProperty] private string detailText = "PICAZHU imports original DCIM photo and video files, then indexes the destination folder.";
    [ObservableProperty] private string mediaSummaryText = "No device scanned yet.";
    [ObservableProperty] private string thumbnailProgressText = "Preview thumbnails will load after scan.";

    public int SelectedCount => MediaItems.Count(item => item.IsSelected);
    public int TotalMediaCount => MediaItems.Count;
    public bool HasDevices => Devices.Count > 0;
    public bool HasMediaItems => MediaItems.Count > 0;
    public bool CanRefresh => !IsBusy;
    public bool CanScanDevice => !IsBusy && SelectedDevice is not null;
    public bool CanImport => !IsBusy && SelectedCount > 0 && !string.IsNullOrWhiteSpace(DestinationFolder);
    public bool CanCancel => IsImporting;
    public bool HasThumbnailProgress => IsLoadingThumbnails || ThumbnailProgressPercent > 0;
    public string SelectedSizeText => FormatBytes(MediaItems.Where(item => item.IsSelected).Sum(item => item.Model.SizeBytes));
    public string ImportButtonText => IsImporting ? "Importing..." : $"Import {SelectedCount:N0} Selected";

    public async Task RefreshDevicesAsync()
    {
        if (IsBusy)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            StatusText = "Looking for connected iPhone and portable devices...";
            DetailText = "If the iPhone does not appear, unlock it and approve Trust This Computer.";
            Devices.Clear();
            ClearMediaItems();

            var devices = await phoneImportService.GetDevicesAsync();
            foreach (var device in devices)
            {
                Devices.Add(new PhoneDeviceOptionViewModel(device));
            }

            SelectedDevice = Devices.FirstOrDefault(device => device.IsLikelyIPhone) ?? Devices.FirstOrDefault();
            StatusText = Devices.Count == 0
                ? "No iPhone or portable device detected."
                : $"{Devices.Count:N0} portable device(s) detected.";
            DetailText = Devices.Count == 0
                ? "Connect the iPhone with USB, unlock it, then press Refresh."
                : "Select a device and scan DCIM to load importable photos and videos.";
            RaiseState();
        });
    }

    public async Task LoadSelectedDeviceMediaAsync()
    {
        if (SelectedDevice is null || IsBusy)
        {
            return;
        }

        await RunBusyAsync(async () =>
        {
            StatusText = $"Scanning {SelectedDevice.DisplayName} DCIM...";
            DetailText = "Only supported photo and video originals are listed. AppleDouble cached sidecars are ignored.";
            ClearMediaItems();

            var items = await phoneImportService.GetDeviceMediaAsync(SelectedDevice.DeviceId);
            foreach (var item in items)
            {
                var option = new PhoneMediaOptionViewModel(item) { IsSelected = true };
                option.PropertyChanged += MediaItem_PropertyChanged;
                MediaItems.Add(option);
            }

            StatusText = MediaItems.Count == 0
                ? "No importable DCIM media found on this device."
                : $"{MediaItems.Count:N0} importable DCIM item(s) found.";
            DetailText = MediaItems.Count == 0
                ? "The iPhone may be locked, not trusted, or empty. Unlock it and scan again."
                : "Review the selection, choose a destination, then import originals.";
            UpdateMediaSummary();
            RaiseState();
            StartThumbnailLoading();
        });
    }

    public async Task<PhoneImportResult?> ImportSelectedAsync()
    {
        if (!CanImport)
        {
            return null;
        }

        var selectedItems = MediaItems
            .Where(item => item.IsSelected)
            .Select(item => item.Model)
            .ToList();
        if (SelectedDevice is null || selectedItems.Count == 0)
        {
            return null;
        }

        _importCancellationSource?.Dispose();
        _importCancellationSource = new CancellationTokenSource();
        IsBusy = true;
        IsImporting = true;
        ProgressPercent = 0;
        RaiseState();

        try
        {
            var progress = new Progress<PhoneImportProgressSnapshot>(UpdateProgress);
            var result = await phoneImportService.ImportAsync(
                new PhoneImportRequest(SelectedDevice.DeviceId, selectedItems, DestinationFolder, PreserveDcimFolders: true),
                progress,
                _importCancellationSource.Token);

            StatusText = result.FailedCount > 0
                ? "Import completed with errors."
                : "Import completed.";
            DetailText = $"Copied {result.CopiedCount:N0}, skipped {result.SkippedCount:N0}, renamed {result.RenamedCount:N0}, failed {result.FailedCount:N0}.";

            if (Directory.Exists(result.DestinationFolder) && (result.CopiedCount > 0 || result.SkippedCount > 0))
            {
                StatusText = "Adding imported folder to PICAZHU...";
                DetailText = "The destination is being added as a watched root with subfolders enabled.";
                await mainViewModel.AddFolderAsync(result.DestinationFolder, includeSubfolders: true);
                await mainViewModel.TrySelectFolderByPathAsync(result.DestinationFolder);
                StatusText = result.FailedCount > 0
                    ? "Import indexed with errors."
                    : "Import indexed and ready.";
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            StatusText = "Import canceled.";
            DetailText = "Partial temporary files were cleaned up. Already copied originals remain in the destination folder.";
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
        {
            StatusText = "iPhone import failed.";
            DetailText = ex.Message;
            return null;
        }
        finally
        {
            IsImporting = false;
            IsBusy = false;
            _importCancellationSource?.Dispose();
            _importCancellationSource = null;
            RaiseState();
        }
    }

    public void CancelImport() => _importCancellationSource?.Cancel();

    public void SelectAllMedia()
    {
        foreach (var item in MediaItems)
        {
            item.IsSelected = true;
        }

        UpdateMediaSummary();
        RaiseState();
    }

    public void SelectNoMedia()
    {
        foreach (var item in MediaItems)
        {
            item.IsSelected = false;
        }

        UpdateMediaSummary();
        RaiseState();
    }

    partial void OnSelectedDeviceChanged(PhoneDeviceOptionViewModel? value)
    {
        ClearMediaItems();
        RaiseState();
    }

    partial void OnDestinationFolderChanged(string value) => RaiseState();

    partial void OnIsBusyChanged(bool value) => RaiseState();

    partial void OnIsImportingChanged(bool value) => RaiseState();

    partial void OnIsLoadingThumbnailsChanged(bool value) => RaiseState();

    private async Task RunBusyAsync(Func<Task> action)
    {
        IsBusy = true;
        RaiseState();
        try
        {
            await action();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
        {
            StatusText = "iPhone import action failed.";
            DetailText = ex.Message;
        }
        finally
        {
            IsBusy = false;
            RaiseState();
        }
    }

    private void UpdateProgress(PhoneImportProgressSnapshot progress)
    {
        ProgressPercent = progress.TotalItems <= 0
            ? 0
            : Math.Clamp((double)progress.CompletedItems / progress.TotalItems * 100d, 0d, 100d);
        StatusText = progress.IsRunning
            ? $"Importing {progress.CompletedItems:N0} of {progress.TotalItems:N0}"
            : "Import transfer finished.";
        DetailText = progress.CurrentFileName is null
            ? $"Copied {progress.CopiedItems:N0}, skipped {progress.SkippedItems:N0}, renamed {progress.RenamedItems:N0}, failed {progress.FailedItems:N0}."
            : $"{progress.CurrentFileName} · copied {progress.CopiedItems:N0}, skipped {progress.SkippedItems:N0}, failed {progress.FailedItems:N0}.";
    }

    private void MediaItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PhoneMediaOptionViewModel.IsSelected))
        {
            UpdateMediaSummary();
            RaiseState();
        }
    }

    private void ClearMediaItems()
    {
        CancelThumbnailLoading();
        foreach (var item in MediaItems)
        {
            item.PropertyChanged -= MediaItem_PropertyChanged;
        }

        MediaItems.Clear();
        IsLoadingThumbnails = false;
        ThumbnailProgressPercent = 0;
        ThumbnailProgressText = "Preview thumbnails will load after scan.";
        UpdateMediaSummary();
    }

    private void StartThumbnailLoading()
    {
        CancelThumbnailLoading();
        if (MediaItems.Count == 0)
        {
            return;
        }

        _thumbnailCancellationSource = new CancellationTokenSource();
        var token = _thumbnailCancellationSource.Token;
        var snapshot = MediaItems.ToList();
        IsLoadingThumbnails = true;
        ThumbnailProgressPercent = 0;
        ThumbnailProgressText = $"Loading previews for {snapshot.Count:N0} item(s)...";
        RaiseState();
        _ = LoadThumbnailsAsync(snapshot, token);
    }

    private void CancelThumbnailLoading()
    {
        if (_thumbnailCancellationSource is null)
        {
            return;
        }

        _thumbnailCancellationSource.Cancel();
        _thumbnailCancellationSource.Dispose();
        _thumbnailCancellationSource = null;
    }

    private async Task LoadThumbnailsAsync(IReadOnlyList<PhoneMediaOptionViewModel> items, CancellationToken cancellationToken)
    {
        var completed = 0;
        var loaded = 0;
        var total = items.Count;

        try
        {
            foreach (var item in items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var thumbnailPath = CreateThumbnailPath(item.Model);
                var hasThumbnail = File.Exists(thumbnailPath) ||
                                   await phoneImportService.TryDownloadThumbnailAsync(item.Model, thumbnailPath, cancellationToken);

                if (hasThumbnail)
                {
                    var source = LoadThumbnailSource(thumbnailPath);
                    if (source is not null)
                    {
                        loaded++;
                        await Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            item.ThumbnailSource = source;
                            item.ThumbnailPath = thumbnailPath;
                        });
                    }
                }

                completed++;
                if (completed % 8 == 0 || completed == total)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ThumbnailProgressPercent = total <= 0 ? 0 : Math.Clamp((double)completed / total * 100d, 0d, 100d);
                        ThumbnailProgressText = completed == total
                            ? $"Loaded {loaded:N0} preview thumbnail(s)."
                            : $"Loading previews {completed:N0} of {total:N0}...";
                    });
                }
            }
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or InvalidOperationException or COMException)
        {
            await Application.Current.Dispatcher.InvokeAsync(() => ThumbnailProgressText = $"Preview loading stopped: {ex.Message}");
        }
        finally
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    IsLoadingThumbnails = false;
                    RaiseState();
                });
            }
        }
    }

    private string CreateThumbnailPath(PhoneMediaItem item)
    {
        var cacheFolder = Path.Combine(appPaths.TempPath, "iphone-import-thumbs");
        Directory.CreateDirectory(cacheFolder);
        var keyInput = $"{item.DeviceId}|{item.DevicePath}|{item.SizeBytes}|{item.ModifiedUtc?.ToUnixTimeMilliseconds() ?? 0}";
        var keyBytes = SHA256.HashData(Encoding.UTF8.GetBytes(keyInput));
        var key = Convert.ToHexString(keyBytes).ToLowerInvariant();
        return Path.Combine(cacheFolder, $"{key}.jpg");
    }

    private static BitmapSource? LoadThumbnailSource(string thumbnailPath)
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

    private void UpdateMediaSummary()
    {
        var imageCount = MediaItems.Count(item => item.Model.MediaKind == MediaKind.Image);
        var videoCount = MediaItems.Count(item => item.Model.MediaKind == MediaKind.Video);
        MediaSummaryText = MediaItems.Count == 0
            ? "No media loaded."
            : $"{SelectedCount:N0} selected of {MediaItems.Count:N0} · {imageCount:N0} images · {videoCount:N0} videos · {SelectedSizeText}";
    }

    private void RaiseState()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(TotalMediaCount));
        OnPropertyChanged(nameof(HasDevices));
        OnPropertyChanged(nameof(HasMediaItems));
        OnPropertyChanged(nameof(CanRefresh));
        OnPropertyChanged(nameof(CanScanDevice));
        OnPropertyChanged(nameof(CanImport));
        OnPropertyChanged(nameof(CanCancel));
        OnPropertyChanged(nameof(HasThumbnailProgress));
        OnPropertyChanged(nameof(SelectedSizeText));
        OnPropertyChanged(nameof(ImportButtonText));
    }

    private static string CreateDefaultDestinationFolder()
    {
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        return Path.Combine(pictures, "PICAZHU iPhone Imports", DateTime.Now.ToString("yyyy-MM-dd HHmm"));
    }

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
}

public sealed class PhoneDeviceOptionViewModel(PhoneDeviceInfo model)
{
    public PhoneDeviceInfo Model { get; } = model;
    public string DeviceId => Model.DeviceId;
    public string DisplayName => Model.DisplayName;
    public bool IsLikelyIPhone => Model.IsLikelyIPhone;
    public string ManufacturerText => string.IsNullOrWhiteSpace(Model.Manufacturer) ? "Unknown manufacturer" : Model.Manufacturer;
    public string TrustHint => Model.IsLikelyIPhone ? "Apple device candidate" : "Portable media device";
}

public sealed partial class PhoneMediaOptionViewModel(PhoneMediaItem model) : ObservableObject
{
    [ObservableProperty] private bool isSelected;
    [ObservableProperty] private BitmapSource? thumbnailSource;
    [ObservableProperty] private string? thumbnailPath;

    public PhoneMediaItem Model { get; } = model;
    public string FileName => Model.FileName;
    public string RelativePath => Model.RelativePath;
    public string KindLabel => Model.MediaKind == MediaKind.Video ? "Video" : "Image";
    public string IconGlyph => Model.MediaKind == MediaKind.Video ? "\uE714" : "\uEB9F";
    public string SizeText => PhoneImportViewModelFormat.FormatBytes(Model.SizeBytes);
    public string ModifiedText => Model.ModifiedUtc?.ToLocalTime().ToString("g") ?? "Date unknown";
    public bool HasThumbnail => ThumbnailSource is not null;

    partial void OnThumbnailSourceChanged(BitmapSource? value) => OnPropertyChanged(nameof(HasThumbnail));
}

internal static class PhoneImportViewModelFormat
{
    public static string FormatBytes(long bytes)
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
}
