using System.IO;
using System.Windows;
using System.Windows.Controls;
using Ookii.Dialogs.Wpf;
using Picazhu.Core;

namespace Picazhu.App;

public partial class PhoneImportWindow : Window
{
    private readonly PhoneImportViewModel _viewModel;
    private bool _isLoadingDeviceMedia;

    public PhoneImportWindow(MainViewModel mainViewModel, IPhoneImportService phoneImportService, IAppPaths appPaths)
    {
        _viewModel = new PhoneImportViewModel(mainViewModel, phoneImportService, appPaths);
        InitializeComponent();
        DataContext = _viewModel;
    }

    public async Task InitializeAsync() => await _viewModel.RefreshDevicesAsync();

    private async void RefreshDevices_Click(object sender, RoutedEventArgs e)
        => await _viewModel.RefreshDevicesAsync();

    private async void ScanDevice_Click(object sender, RoutedEventArgs e)
        => await _viewModel.LoadSelectedDeviceMediaAsync();

    private async void DeviceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isLoadingDeviceMedia || _viewModel.SelectedDevice is null)
        {
            return;
        }

        _isLoadingDeviceMedia = true;
        try
        {
            await _viewModel.LoadSelectedDeviceMediaAsync();
        }
        finally
        {
            _isLoadingDeviceMedia = false;
        }
    }

    private void BrowseDestination_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new VistaFolderBrowserDialog
        {
            Description = "Choose where PICAZHU should copy iPhone originals.",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(_viewModel.DestinationFolder)
                ? _viewModel.DestinationFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };

        if (dialog.ShowDialog(this) == true)
        {
            _viewModel.DestinationFolder = dialog.SelectedPath;
        }
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => _viewModel.SelectAllMedia();

    private void SelectNone_Click(object sender, RoutedEventArgs e) => _viewModel.SelectNoMedia();

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        var result = await _viewModel.ImportSelectedAsync();
        if (result is null)
        {
            return;
        }

        var summary = $"Copied {result.CopiedCount:N0} file(s).\nSkipped duplicates {result.SkippedCount:N0}.\nRenamed {result.RenamedCount:N0}.\nFailed {result.FailedCount:N0}.";
        if (result.Errors.Count > 0)
        {
            summary += $"\n\nFirst error:\n{result.Errors[0]}";
        }

        MessageBox.Show(
            this,
            summary,
            "PICAZHU iPhone Import",
            MessageBoxButton.OK,
            result.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private void CancelImport_Click(object sender, RoutedEventArgs e) => _viewModel.CancelImport();

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
