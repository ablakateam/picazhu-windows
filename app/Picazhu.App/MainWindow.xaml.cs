using System.IO;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Ookii.Dialogs.Wpf;
using Picazhu.Core;

namespace Picazhu.App;

public partial class MainWindow : Window
{
    private enum ShellLayoutMode
    {
        Wide,
        Medium,
        Compact,
        Stacked
    }

    private readonly MainViewModel _viewModel;
    private readonly IPhoneImportService _phoneImportService;
    private readonly IAppPaths _appPaths;
    private readonly IAiProviderStatusService _aiProviderStatusService;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _refreshDiagnosticsInFlight;
    private bool _syncingMediaSelection;
    private ShellLayoutMode? _shellLayoutMode;

    public MainWindow(MainViewModel viewModel, IPhoneImportService phoneImportService, IAppPaths appPaths, IAiProviderStatusService aiProviderStatusService)
    {
        _viewModel = viewModel;
        _phoneImportService = phoneImportService;
        _appPaths = appPaths;
        _aiProviderStatusService = aiProviderStatusService;
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.MediaItemsRefreshed += RestoreMediaSelectionFromViewModel;
        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _refreshTimer.Tick += async (_, _) =>
        {
            if (_refreshDiagnosticsInFlight)
            {
                return;
            }

            _refreshDiagnosticsInFlight = true;
            try
            {
                await _viewModel.RefreshIndexingUiAsync();
            }
            catch (Exception ex)
            {
                _viewModel.StatusText = $"Background refresh failed: {ex.Message}";
            }
            finally
            {
                _refreshDiagnosticsInFlight = false;
            }
        };
    }

    public async Task InitializeAsync()
    {
        await _viewModel.LoadAsync();
        ApplyMouseSelectionMode();
        ApplyResponsiveLayout();
        _refreshTimer.Start();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private async void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new VistaFolderBrowserDialog { Description = "Select a folder to index in PICAZHU." };
        if (dialog.ShowDialog(this) == true)
        {
            var includeSubfolders = MessageBox.Show(
                this,
                "Scan subfolders too?\n\nChoose Yes to index nested folders inside this root.",
                "PICAZHU",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question) == MessageBoxResult.Yes;
            await _viewModel.AddFolderAsync(dialog.SelectedPath, includeSubfolders);
        }
    }

    private async void FolderTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is FolderNodeViewModel folder)
        {
            await _viewModel.SelectFolderAsync(folder);
        }
    }

    private async void Filters_Changed(object sender, RoutedEventArgs e) => await _viewModel.RefreshMediaAsync();
    private async void Rescan_Click(object sender, RoutedEventArgs e) => await _viewModel.RescanAsync();
    private async void Rebuild_Click(object sender, RoutedEventArgs e) => await _viewModel.RebuildAsync();
    private async void RemoveFolder_Click(object sender, RoutedEventArgs e) => await _viewModel.RemoveSelectedRootAsync();
    private async void ImportFromIPhone_Click(object sender, RoutedEventArgs e)
    {
        var wasRunning = _refreshTimer.IsEnabled;
        if (wasRunning)
        {
            _refreshTimer.Stop();
        }

        try
        {
            var window = new PhoneImportWindow(_viewModel, _phoneImportService, _appPaths) { Owner = this };
            await window.InitializeAsync();
            window.ShowDialog();
        }
        finally
        {
            if (wasRunning)
            {
                _refreshTimer.Start();
            }
        }
    }

    private async void Back_Click(object sender, RoutedEventArgs e) => await _viewModel.GoBackAsync();
    private async void PinFolder_Click(object sender, RoutedEventArgs e) => await _viewModel.TogglePinSelectedFolderAsync();
    private async void SaveSearch_Click(object sender, RoutedEventArgs e) => await _viewModel.SaveCurrentSearchAsync();
    private async void ToggleAiFeatures_Click(object sender, RoutedEventArgs e) => await _viewModel.ToggleAiFeaturesAsync();
    private void OpenSettings_Click(object sender, RoutedEventArgs e)
    {
        var wasRunning = _refreshTimer.IsEnabled;
        if (wasRunning)
        {
            _refreshTimer.Stop();
        }

        try
        {
            var window = new SettingsWindow(_viewModel, _aiProviderStatusService) { Owner = this };
            window.ShowDialog();
        }
        finally
        {
            if (wasRunning)
            {
                _refreshTimer.Start();
            }
        }
    }
    private async void OpenSelected_Click(object sender, RoutedEventArgs e) => await _viewModel.OpenSelectedAsync();
    private async void RevealSelected_Click(object sender, RoutedEventArgs e) => await _viewModel.RevealSelectedInFolderAsync();
    private async void OpenInMaps_Click(object sender, RoutedEventArgs e) => await _viewModel.OpenSelectedInMapsAsync();
    private async void ExportSelected_Click(object sender, RoutedEventArgs e) => await ExportSelectedAsync();
    private async void PauseIndexing_Click(object sender, RoutedEventArgs e) => await _viewModel.ToggleIndexingPauseAsync();
    private async void StopIndexing_Click(object sender, RoutedEventArgs e) => await _viewModel.StopIndexingAsync();
    private async void MediaList_DoubleClick(object sender, MouseButtonEventArgs e) => await ShowQuickPreviewAsync();
    private async void QuickPreview_Click(object sender, RoutedEventArgs e) => await ShowQuickPreviewAsync();
    private async void ClearSelection_Click(object sender, RoutedEventArgs e) => await ClearSelectionAsync();
    private async void SelectAllVisible_Click(object sender, RoutedEventArgs e) => await SelectAllVisibleAsync();
    private async void TagChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string term })
        {
            await _viewModel.ApplyTagSearchAsync(term);
        }
    }

    private async void MediaList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingMediaSelection)
        {
            return;
        }

        await _viewModel.UpdateSelectionAsync(MediaList.SelectedItems.Cast<MediaTileViewModel>().ToList());
        await UpdatePreviewAsync();
    }

    private async void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            await _viewModel.RefreshMediaAsync();
        }
    }

    private async void Breadcrumb_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is BreadcrumbItemViewModel breadcrumb)
        {
            await _viewModel.NavigateToBreadcrumbAsync(breadcrumb);
        }
    }

    private async void SortComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox comboBox)
        {
            return;
        }

        _viewModel.SortMode = comboBox.SelectedIndex switch
        {
            0 => Core.SortMode.ModifiedDate,
            1 => Core.SortMode.Name,
            2 => Core.SortMode.CreatedDate,
            3 => Core.SortMode.Size,
            4 => Core.SortMode.Duration,
            5 => Core.SortMode.Type,
            _ => Core.SortMode.ModifiedDate
        };
        await _viewModel.RefreshMediaAsync();
    }

    private Task UpdatePreviewAsync()
    {
        if (!_viewModel.ShowSinglePreview)
        {
            PreviewImage.Source = null;
            return Task.CompletedTask;
        }

        var selected = _viewModel.SelectedMedia;
        if (selected?.ThumbnailSource is not null)
        {
            PreviewImage.Source = selected.ThumbnailSource;
            return Task.CompletedTask;
        }

        var path = selected?.ThumbnailPath;
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            try
            {
                using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                frame.Freeze();
                PreviewImage.Source = frame;
            }
            catch
            {
                PreviewImage.Source = null;
            }
        }
        else
        {
            PreviewImage.Source = null;
        }

        return Task.CompletedTask;
    }

    private async Task ShowQuickPreviewAsync()
    {
        if (!_viewModel.ShowSinglePreview)
        {
            return;
        }

        var preview = await _viewModel.GetQuickPreviewAsync();
        if (preview is null)
        {
            return;
        }

        var window = new PreviewWindow(preview) { Owner = this };
        window.ShowDialog();
    }

    private async void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        else if (e.Key == Key.A && Keyboard.Modifiers == ModifierKeys.Control)
        {
            await SelectAllVisibleAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            await ClearSelectionAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Space)
        {
            await ShowQuickPreviewAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Back || (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt))
        {
            await _viewModel.GoBackAsync();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            await _viewModel.OpenSelectedAsync();
            e.Handled = true;
        }
    }

    private async void PinnedFolders_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is string path)
        {
            await _viewModel.TrySelectFolderByPathAsync(path);
        }
    }

    private async void RecentFolders_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is string path)
        {
            await _viewModel.TrySelectFolderByPathAsync(path);
        }
    }

    private async void SavedSearches_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is Core.SavedSearch search)
        {
            await _viewModel.SelectSavedSearchAsync(search);
        }
    }

    private async void MediaListItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem listBoxItem || listBoxItem.DataContext is not MediaTileViewModel item)
        {
            return;
        }

        if (_viewModel.IsMouseSelectionMode)
        {
            e.Handled = true;
            if (!listBoxItem.IsSelected)
            {
                await AddItemToSelectionAsync(item);
            }
            else if (_viewModel.SelectedMedia != item)
            {
                listBoxItem.Focus();
                await _viewModel.SetActiveSelectionAsync(item);
                await UpdatePreviewAsync();
            }

            return;
        }

        if (!listBoxItem.IsSelected)
        {
            e.Handled = true;
            await SelectOnlyItemAsync(item);
        }
        else if (_viewModel.SelectedMedia != item)
        {
            e.Handled = true;
            await _viewModel.UpdateSelectionAsync(MediaList.SelectedItems.Cast<MediaTileViewModel>().ToList());
            await UpdatePreviewAsync();
        }
    }

    private void MediaListItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBoxItem listBoxItem || listBoxItem.DataContext is not MediaTileViewModel item)
        {
            return;
        }

        var selectedCount = MediaList.SelectedItems.Count;
        var canUseSingleItemActions = selectedCount == 1 && listBoxItem.IsSelected;

        var menu = new ContextMenu();
        if (selectedCount > 1)
        {
            menu.Items.Add(new MenuItem
            {
                Header = $"{selectedCount:N0} items selected",
                IsEnabled = false
            });
            menu.Items.Add(new Separator());
        }

        menu.Items.Add(CreateContextMenuItem(listBoxItem.IsSelected ? "Remove From Selection" : "Add To Selection", MediaContextSelectToggle_Click, item));
        menu.Items.Add(CreateContextMenuItem("Select This", MediaContextSelectThis_Click, item));
        menu.Items.Add(CreateContextMenuItem("Select Only This", MediaContextSelectOnly_Click, item));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateContextMenuItem("Open Original", MediaContextOpen_Click, item, canUseSingleItemActions));
        menu.Items.Add(CreateContextMenuItem("Show In Folder", MediaContextReveal_Click, item, canUseSingleItemActions));
        menu.Items.Add(CreateContextMenuItem("Open In Maps", MediaContextMaps_Click, item, canUseSingleItemActions && _viewModel.CanOpenInMaps));
        menu.Items.Add(CreateContextMenuItem("Quick Preview", MediaContextPreview_Click, item, canUseSingleItemActions));
        menu.Items.Add(new Separator());
        menu.Items.Add(CreateContextMenuItem("Export Selected", MediaContextExport_Click, item, _viewModel.CanExportSelected));
        menu.Items.Add(CreateContextMenuItem(_viewModel.IsMouseSelectionMode ? "Turn Mouse Selection Off" : "Turn Mouse Selection On", MediaContextMouseMode_Click, item));
        menu.Items.Add(CreateContextMenuItem("Clear Selection", MediaContextClear_Click, item, _viewModel.HasAnySelectedMedia));

        listBoxItem.ContextMenu = menu;
    }

    private void RestoreMediaSelectionFromViewModel()
    {
        Dispatcher.InvokeAsync(() =>
        {
            _syncingMediaSelection = true;
            try
            {
                MediaList.SelectedItems.Clear();
                foreach (var item in _viewModel.SelectedMediaItems.Where(item => MediaList.Items.Contains(item)))
                {
                    MediaList.SelectedItems.Add(item);
                }

                if (_viewModel.SelectedMedia is not null && MediaList.Items.Contains(_viewModel.SelectedMedia))
                {
                    (MediaList.ItemContainerGenerator.ContainerFromItem(_viewModel.SelectedMedia) as ListBoxItem)?.Focus();
                }
            }
            finally
            {
                _syncingMediaSelection = false;
            }
        }, DispatcherPriority.Background);
    }

    private async Task SelectAllVisibleAsync()
    {
        _syncingMediaSelection = true;
        try
        {
            MediaList.SelectAll();
        }
        finally
        {
            _syncingMediaSelection = false;
        }

        await _viewModel.UpdateSelectionAsync(MediaList.SelectedItems.Cast<MediaTileViewModel>().ToList());
        await UpdatePreviewAsync();
    }

    private async Task ClearSelectionAsync()
    {
        _syncingMediaSelection = true;
        try
        {
            foreach (var selectedItem in MediaList.SelectedItems.Cast<object>().ToList())
            {
                if (MediaList.ItemContainerGenerator.ContainerFromItem(selectedItem) is ListBoxItem selectedContainer)
                {
                    selectedContainer.IsSelected = false;
                }
            }
        }
        finally
        {
            _syncingMediaSelection = false;
        }

        _viewModel.ClearSelection();
        await UpdatePreviewAsync();
    }

    private async Task ExportSelectedAsync()
    {
        if (!_viewModel.CanExportSelected)
        {
            return;
        }

        var dialog = new VistaFolderBrowserDialog { Description = "Choose a folder where PICAZHU should copy the selected original files." };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var result = await _viewModel.ExportSelectedAsync(dialog.SelectedPath);
        if (result is null)
        {
            return;
        }

        var summary = $"Copied {result.CopiedCount:N0} file(s).\nRenamed {result.RenamedCount:N0} file(s).\nFailed {result.FailedCount:N0} file(s).";
        if (result.Errors.Count > 0)
        {
            summary += $"\n\nFirst error:\n{result.Errors[0]}";
        }

        MessageBox.Show(this, summary, "PICAZHU Export", MessageBoxButton.OK, result.FailedCount > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
    }

    private static MenuItem CreateContextMenuItem(string header, RoutedEventHandler clickHandler, MediaTileViewModel item, bool isEnabled = true)
    {
        var menuItem = new MenuItem
        {
            Header = header,
            Tag = item,
            IsEnabled = isEnabled
        };
        menuItem.Click += clickHandler;
        return menuItem;
    }

    private async void MediaContextSelectToggle_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTaggedMediaItem(sender, out var item))
        {
            return;
        }

        await ToggleItemSelectionAsync(item);
    }

    private async void MediaContextSelectThis_Click(object sender, RoutedEventArgs e)
    {
        if (!TryGetTaggedMediaItem(sender, out var item))
        {
            return;
        }

        _viewModel.SetMouseSelectionMode(true);
        await AddItemToSelectionAsync(item);
    }

    private async void MediaContextSelectOnly_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTaggedMediaItem(sender, out var item))
        {
            _viewModel.SetMouseSelectionMode(false);
            await SelectOnlyItemAsync(item);
        }
    }

    private async void MediaContextOpen_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTaggedMediaItem(sender, out var item))
        {
            await SelectOnlyItemAsync(item);
            await _viewModel.OpenSelectedAsync();
        }
    }

    private async void MediaContextReveal_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTaggedMediaItem(sender, out var item))
        {
            await SelectOnlyItemAsync(item);
            await _viewModel.RevealSelectedInFolderAsync();
        }
    }

    private async void MediaContextPreview_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTaggedMediaItem(sender, out var item))
        {
            await SelectOnlyItemAsync(item);
            await ShowQuickPreviewAsync();
        }
    }

    private async void MediaContextMaps_Click(object sender, RoutedEventArgs e)
    {
        if (TryGetTaggedMediaItem(sender, out var item))
        {
            await SelectOnlyItemAsync(item);
            await _viewModel.OpenSelectedInMapsAsync();
        }
    }

    private async void MediaContextExport_Click(object sender, RoutedEventArgs e)
        => await ExportSelectedAsync();

    private void MediaContextMouseMode_Click(object sender, RoutedEventArgs e)
        => _viewModel.SetMouseSelectionMode(!_viewModel.IsMouseSelectionMode);

    private async void MediaContextClear_Click(object sender, RoutedEventArgs e)
        => await ClearSelectionAsync();

    private async Task SelectOnlyItemAsync(MediaTileViewModel item)
    {
        _viewModel.SetMouseSelectionMode(false);
        _syncingMediaSelection = true;
        try
        {
            foreach (var selectedItem in MediaList.SelectedItems.Cast<object>().ToList())
            {
                if (MediaList.ItemContainerGenerator.ContainerFromItem(selectedItem) is ListBoxItem selectedContainer)
                {
                    selectedContainer.IsSelected = false;
                }
            }

            if (TryGetContainer(item) is ListBoxItem container)
            {
                container.IsSelected = true;
                container.Focus();
            }
        }
        finally
        {
            _syncingMediaSelection = false;
        }

        await _viewModel.UpdateSelectionAsync([item]);
        await _viewModel.SetActiveSelectionAsync(item);
        await UpdatePreviewAsync();
    }

    private ListBoxItem? TryGetContainer(MediaTileViewModel item)
        => MediaList.ItemContainerGenerator.ContainerFromItem(item) as ListBoxItem;

    private static bool TryGetTaggedMediaItem(object sender, out MediaTileViewModel item)
    {
        item = null!;
        if (sender is MenuItem { Tag: MediaTileViewModel taggedItem })
        {
            item = taggedItem;
            return true;
        }

        return false;
    }

    private async Task ToggleItemSelectionAsync(MediaTileViewModel item)
    {
        var container = TryGetContainer(item);
        if (container is null)
        {
            return;
        }

        _syncingMediaSelection = true;
        try
        {
            if (container.IsSelected)
            {
                container.IsSelected = false;
            }
            else
            {
                container.IsSelected = true;
            }

            container.Focus();
        }
        finally
        {
            _syncingMediaSelection = false;
        }

        await _viewModel.UpdateSelectionAsync(MediaList.SelectedItems.Cast<MediaTileViewModel>().ToList());
        await _viewModel.SetActiveSelectionAsync(item);
        await UpdatePreviewAsync();
    }

    private async Task AddItemToSelectionAsync(MediaTileViewModel item)
    {
        var container = TryGetContainer(item);
        if (container is null)
        {
            return;
        }

        if (container.IsSelected)
        {
            container.Focus();
            await _viewModel.SetActiveSelectionAsync(item);
            await UpdatePreviewAsync();
            return;
        }

        _syncingMediaSelection = true;
        try
        {
            container.IsSelected = true;
            container.Focus();
        }
        finally
        {
            _syncingMediaSelection = false;
        }

        await _viewModel.UpdateSelectionAsync(MediaList.SelectedItems.Cast<MediaTileViewModel>().ToList());
        await _viewModel.SetActiveSelectionAsync(item);
        await UpdatePreviewAsync();
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsMouseSelectionMode))
        {
            ApplyMouseSelectionMode();
        }
    }

    private void ApplyMouseSelectionMode()
    {
        MediaList.SelectionMode = _viewModel.IsMouseSelectionMode
            ? SelectionMode.Multiple
            : SelectionMode.Extended;
    }

    private void ApplyResponsiveLayout()
    {
        var headerShell = HeaderShell;
        var headerContentGrid = HeaderContentGrid;
        var headerTaglineText = HeaderTaglineText;
        var headerSearchHeadingText = HeaderSearchHeadingText;
        var headerSearchIntroText = HeaderSearchIntroText;
        var headerFiltersHeadingText = HeaderFiltersHeadingText;
        var libraryIntroText = LibraryIntroText;
        var sourceOfTruthText = SourceOfTruthText;
        var libraryColumn = LibraryColumn;
        var previewColumn = PreviewColumn;
        var commandRailColumn = CommandRailColumn;
        var previewRail = PreviewRail;
        var libraryRail = LibraryRail;
        var galleryRail = GalleryRail;
        var mainShellGrid = MainShellGrid;
        var libraryScrollViewer = LibraryScrollViewer;
        var previewStageRow = PreviewStageRow;

        if (headerShell is null ||
            headerContentGrid is null ||
            headerTaglineText is null ||
            headerSearchHeadingText is null ||
            headerSearchIntroText is null ||
            headerFiltersHeadingText is null ||
            libraryIntroText is null ||
            sourceOfTruthText is null ||
            libraryColumn is null ||
            previewColumn is null ||
            commandRailColumn is null ||
            previewRail is null ||
            libraryRail is null ||
            galleryRail is null ||
            mainShellGrid is null ||
            libraryScrollViewer is null ||
            previewStageRow is null)
        {
            return;
        }

        var mode = ActualWidth switch
        {
            <= 1060 => ShellLayoutMode.Stacked,
            <= 1320 => ShellLayoutMode.Compact,
            <= 1580 => ShellLayoutMode.Medium,
            _ => ShellLayoutMode.Wide
        };

        if (mode == _shellLayoutMode)
        {
            return;
        }

        _shellLayoutMode = mode;

        switch (mode)
        {
            case ShellLayoutMode.Wide:
                headerShell.Padding = new Thickness(14, 12, 14, 12);
                headerContentGrid.Margin = new Thickness(0);
                headerTaglineText.Visibility = Visibility.Visible;
                headerSearchHeadingText.Visibility = Visibility.Collapsed;
                headerSearchIntroText.Visibility = Visibility.Collapsed;
                headerFiltersHeadingText.Visibility = Visibility.Visible;
                libraryIntroText.Visibility = Visibility.Visible;
                sourceOfTruthText.Visibility = Visibility.Visible;

                commandRailColumn.Width = new GridLength(84);
                libraryColumn.Width = new GridLength(232);
                previewColumn.Width = new GridLength(320);
                Grid.SetRow(libraryRail, 0);
                Grid.SetColumn(libraryRail, 0);
                Grid.SetColumnSpan(libraryRail, 1);
                Grid.SetRow(galleryRail, 0);
                Grid.SetColumn(galleryRail, 1);
                Grid.SetColumnSpan(galleryRail, 1);
                Grid.SetRow(previewRail, 0);
                Grid.SetColumn(previewRail, 2);
                Grid.SetColumnSpan(previewRail, 1);
                previewRail.Margin = new Thickness(0);
                libraryRail.Margin = new Thickness(0, 0, 18, 0);
                galleryRail.Margin = new Thickness(0, 0, 18, 0);
                libraryRail.MaxHeight = double.PositiveInfinity;
                libraryScrollViewer.MaxHeight = double.PositiveInfinity;
                previewStageRow.Height = new GridLength(246);
                mainShellGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                mainShellGrid.RowDefinitions[1].Height = new GridLength(0);
                mainShellGrid.RowDefinitions[2].Height = new GridLength(0);
                break;

            case ShellLayoutMode.Medium:
                headerShell.Padding = new Thickness(12, 10, 12, 10);
                headerContentGrid.Margin = new Thickness(0);
                headerTaglineText.Visibility = Visibility.Visible;
                headerSearchHeadingText.Visibility = Visibility.Collapsed;
                headerSearchIntroText.Visibility = Visibility.Collapsed;
                headerFiltersHeadingText.Visibility = Visibility.Visible;
                libraryIntroText.Visibility = Visibility.Collapsed;
                sourceOfTruthText.Visibility = Visibility.Visible;

                commandRailColumn.Width = new GridLength(80);
                libraryColumn.Width = new GridLength(206);
                previewColumn.Width = new GridLength(268);
                Grid.SetRow(libraryRail, 0);
                Grid.SetColumn(libraryRail, 0);
                Grid.SetColumnSpan(libraryRail, 1);
                Grid.SetRow(galleryRail, 0);
                Grid.SetColumn(galleryRail, 1);
                Grid.SetColumnSpan(galleryRail, 1);
                Grid.SetRow(previewRail, 0);
                Grid.SetColumn(previewRail, 2);
                Grid.SetColumnSpan(previewRail, 1);
                previewRail.Margin = new Thickness(0);
                libraryRail.Margin = new Thickness(0, 0, 14, 0);
                galleryRail.Margin = new Thickness(0, 0, 14, 0);
                libraryRail.MaxHeight = double.PositiveInfinity;
                libraryScrollViewer.MaxHeight = double.PositiveInfinity;
                previewStageRow.Height = new GridLength(220);
                mainShellGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                mainShellGrid.RowDefinitions[1].Height = new GridLength(0);
                mainShellGrid.RowDefinitions[2].Height = new GridLength(0);
                break;

            case ShellLayoutMode.Compact:
                headerShell.Padding = new Thickness(12, 10, 12, 10);
                headerContentGrid.Margin = new Thickness(0);
                headerTaglineText.Visibility = Visibility.Collapsed;
                headerSearchHeadingText.Visibility = Visibility.Collapsed;
                headerSearchIntroText.Visibility = Visibility.Collapsed;
                headerFiltersHeadingText.Visibility = Visibility.Collapsed;
                libraryIntroText.Visibility = Visibility.Collapsed;
                sourceOfTruthText.Visibility = Visibility.Collapsed;

                commandRailColumn.Width = new GridLength(74);
                libraryColumn.Width = new GridLength(194);
                previewColumn.Width = new GridLength(0);
                Grid.SetRow(libraryRail, 0);
                Grid.SetColumn(libraryRail, 0);
                Grid.SetColumnSpan(libraryRail, 1);
                Grid.SetRow(galleryRail, 0);
                Grid.SetColumn(galleryRail, 1);
                Grid.SetColumnSpan(galleryRail, 2);
                Grid.SetRow(previewRail, 1);
                Grid.SetColumn(previewRail, 0);
                Grid.SetColumnSpan(previewRail, 3);
                previewRail.Margin = new Thickness(0, 18, 0, 0);
                libraryRail.Margin = new Thickness(0, 0, 16, 0);
                galleryRail.Margin = new Thickness(0);
                libraryRail.MaxHeight = double.PositiveInfinity;
                libraryScrollViewer.MaxHeight = double.PositiveInfinity;
                previewStageRow.Height = new GridLength(200);
                mainShellGrid.RowDefinitions[0].Height = new GridLength(1, GridUnitType.Star);
                mainShellGrid.RowDefinitions[1].Height = GridLength.Auto;
                mainShellGrid.RowDefinitions[2].Height = new GridLength(0);
                break;

            default:
                headerShell.Padding = new Thickness(10, 8, 10, 8);
                headerContentGrid.Margin = new Thickness(0);
                headerTaglineText.Visibility = Visibility.Collapsed;
                headerSearchHeadingText.Visibility = Visibility.Collapsed;
                headerSearchIntroText.Visibility = Visibility.Collapsed;
                headerFiltersHeadingText.Visibility = Visibility.Collapsed;
                libraryIntroText.Visibility = Visibility.Collapsed;
                sourceOfTruthText.Visibility = Visibility.Collapsed;

                commandRailColumn.Width = new GridLength(70);
                libraryColumn.Width = new GridLength(0);
                previewColumn.Width = new GridLength(0);
                Grid.SetRow(libraryRail, 0);
                Grid.SetColumn(libraryRail, 0);
                Grid.SetColumnSpan(libraryRail, 3);
                Grid.SetRow(galleryRail, 1);
                Grid.SetColumn(galleryRail, 0);
                Grid.SetColumnSpan(galleryRail, 3);
                Grid.SetRow(previewRail, 2);
                Grid.SetColumn(previewRail, 0);
                Grid.SetColumnSpan(previewRail, 3);
                libraryRail.Margin = new Thickness(0, 0, 0, 16);
                galleryRail.Margin = new Thickness(0, 0, 0, 16);
                previewRail.Margin = new Thickness(0);
                libraryRail.MaxHeight = 300;
                libraryScrollViewer.MaxHeight = 230;
                previewStageRow.Height = new GridLength(180);
                mainShellGrid.RowDefinitions[0].Height = GridLength.Auto;
                mainShellGrid.RowDefinitions[1].Height = new GridLength(1, GridUnitType.Star);
                mainShellGrid.RowDefinitions[2].Height = GridLength.Auto;
                break;
        }
    }
}
