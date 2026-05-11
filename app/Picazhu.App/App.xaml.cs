using System.Windows;
using System.IO;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Picazhu.AI;
using Picazhu.Cache;
using Picazhu.Core;
using Picazhu.Data;
using Picazhu.Indexing;
using Picazhu.Media;

namespace Picazhu.App;

public partial class App : Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += App_DispatcherUnhandledException;

        _host = Host.CreateDefaultBuilder()
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddDebug();
                logging.AddProvider(new FileLoggerProvider(new AppPaths().LogsPath));
            })
            .ConfigureServices(services =>
            {
                services.AddHttpClient();
                services.AddSingleton<IAppPaths, AppPaths>();
                services.AddSingleton<IThumbnailCacheService, ThumbnailCacheService>();
                services.AddSingleton<ISettingsService, SettingsService>();
                services.AddSingleton<ICatalogRepository, CatalogRepository>();
                services.AddSingleton<IExportService, ExportService>();
                services.AddSingleton<IPhoneImportService, PhoneImportService>();
                services.AddSingleton<IHeicDecoderService, HeicDecoderService>();
                services.AddSingleton<IMediaProbeService, MediaProbeService>();
                services.AddSingleton<IThumbnailGenerator, ThumbnailGenerator>();
                services.AddSingleton<IQuickPreviewService, QuickPreviewService>();
                services.AddSingleton<IAiFeatureGate, AiFeatureGate>();
                services.AddSingleton<IAiProviderStatusService, AiProviderStatusService>();
                services.AddSingleton<IOcrTextExtractor, WindowsOcrTextExtractor>();
                services.AddSingleton<IAiIndexingService, AiIndexingService>();
                services.AddSingleton<IAiProvider, LmStudioProvider>();
                services.AddSingleton<IAiProvider, OpenAiProvider>();
                services.AddSingleton<IAiProvider, OllamaProvider>();
                services.AddSingleton<IAiProvider, OllamaCloudProvider>();
                services.AddSingleton<IndexingService>();
                services.AddSingleton<IIndexingService>(provider => provider.GetRequiredService<IndexingService>());
                services.AddSingleton<MainViewModel>();
                services.AddSingleton<MainWindow>();
            })
            .Build();

        await _host.StartAsync();
        var repository = _host.Services.GetRequiredService<ICatalogRepository>();
        await repository.InitializeAsync();
        var settings = await _host.Services.GetRequiredService<ISettingsService>().LoadAsync();
        ThemeManager.ApplyTheme(settings.ThemeMode);

        var mainWindow = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = mainWindow;
        mainWindow.Show();
        _ = InitializeAfterFirstPaintAsync(mainWindow);
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            $"PICAZHU handled an unexpected UI error and kept running.\n\n{e.Exception.Message}",
            "PICAZHU",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.Services.GetRequiredService<IIndexingService>().StopAsync();
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }

    private async Task InitializeAfterFirstPaintAsync(MainWindow mainWindow)
    {
        try
        {
            await Task.Yield();
            await mainWindow.InitializeAsync();
            await _host!.Services.GetRequiredService<IHeicDecoderService>().InitializeAsync([]);
            await _host.Services.GetRequiredService<IIndexingService>().StartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"PICAZHU failed during startup initialization.\n\n{ex.Message}", "PICAZHU", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }
}
