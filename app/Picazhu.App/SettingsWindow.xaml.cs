using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Picazhu.AI;
using Picazhu.Core;

namespace Picazhu.App;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _ownerViewModel;
    private readonly SettingsDraftViewModel _draftViewModel;
    private bool _initialRefreshStarted;

    public SettingsWindow(MainViewModel viewModel, IAiProviderStatusService providerStatusService)
    {
        _ownerViewModel = viewModel;
        _draftViewModel = new SettingsDraftViewModel(viewModel, providerStatusService);
        InitializeComponent();
        DataContext = _draftViewModel;
        Loaded += SettingsWindow_Loaded;
    }

    public Task InitializeAsync() => Task.CompletedTask;

    private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        if (_initialRefreshStarted)
        {
            return;
        }

        _initialRefreshStarted = true;
        try
        {
            await _draftViewModel.RefreshProvidersAsync();
        }
        catch (Exception ex)
        {
            _draftViewModel.ConnectionTestStatusText = $"Initial connection check failed: {ex.Message}";
            _draftViewModel.IsConnectionTestSuccessful = false;
        }
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _ownerViewModel.ApplySettingsSnapshotAsync(_draftViewModel.BuildSettings());
            _draftViewModel.SettingsStatusText = "Settings saved and applied.";
            MessageBox.Show(this, "Settings were saved successfully.", "PICAZHU Settings", MessageBoxButton.OK, MessageBoxImage.Information);
            Close();
        }
        catch (Exception ex)
        {
            _draftViewModel.SettingsStatusText = $"Settings save failed: {ex.Message}";
            MessageBox.Show(this, $"Settings could not be saved.\n\n{ex.Message}", "PICAZHU Settings", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void RefreshProviders_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            await _draftViewModel.RefreshProvidersAsync();
            MessageBox.Show(
                this,
                _draftViewModel.ConnectionTestStatusText,
                "PICAZHU Connection Test",
                MessageBoxButton.OK,
                _draftViewModel.IsConnectionTestSuccessful ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            _draftViewModel.ConnectionTestStatusText = $"Connection test failed: {ex.Message}";
            _draftViewModel.IsConnectionTestSuccessful = false;
            MessageBox.Show(this, _draftViewModel.ConnectionTestStatusText, "PICAZHU Connection Test", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}

public partial class SettingsDraftViewModel : ObservableObject
{
    private readonly AppSettings _baseSettings;
    private readonly IAiProviderStatusService _providerStatusService;

    public SettingsDraftViewModel(MainViewModel source, IAiProviderStatusService providerStatusService)
    {
        _providerStatusService = providerStatusService;
        var settings = source.CreateSettingsSnapshot();
        _baseSettings = settings;
        StatusText = source.StatusText;
        DiagnosticsText = source.DiagnosticsText;
        SettingsStatusText = source.SettingsStatusText;
        ThemeMode = string.IsNullOrWhiteSpace(settings.ThemeMode) ? "Dark" : settings.ThemeMode;
        IncludeSubfolders = settings.ShowSubfoldersByDefault;
        ThumbnailCacheLimitMb = settings.ThumbnailCacheLimitMb;
        EnableAiGlobally = settings.EnableAiGlobally;
        AiVisionProviderId = NormalizeProviderId(settings.AiVisionProviderId);
        LmStudioEndpoint = string.IsNullOrWhiteSpace(settings.LmStudioEndpoint) ? AiProviderModelCatalog.DefaultLmStudioEndpoint : settings.LmStudioEndpoint;
        LmStudioVisionModel = settings.LmStudioVisionModel ?? string.Empty;
        OllamaEndpoint = string.IsNullOrWhiteSpace(settings.OllamaEndpoint) ? AiProviderModelCatalog.DefaultOllamaEndpoint : settings.OllamaEndpoint;
        OllamaVisionModel = settings.OllamaVisionModel ?? string.Empty;
        OllamaCloudEndpoint = string.IsNullOrWhiteSpace(settings.OllamaCloudEndpoint) ? AiProviderModelCatalog.DefaultOllamaCloudEndpoint : settings.OllamaCloudEndpoint;
        OllamaCloudApiKey = settings.OllamaCloudApiKeyPlaceholder ?? string.Empty;
        OllamaCloudVisionModel = settings.OllamaCloudVisionModel ?? string.Empty;
        OpenAiApiKey = settings.OpenAiApiKeyPlaceholder ?? string.Empty;
        OpenAiVisionModel = string.IsNullOrWhiteSpace(settings.OpenAiVisionModel) ? AiProviderModelCatalog.DefaultOpenAiVisionModel : settings.OpenAiVisionModel;
        AiIndexingProgressText = source.AiIndexingProgressText;
        AiIndexingDetailText = source.AiIndexingDetailText;
        AiProviderStatusText = source.AiProviderStatusText;
        AiIndexingProgressPercent = source.AiIndexingProgressPercent;

        Replace(LmStudioAvailableModels, source.LmStudioAvailableModels);
        Replace(OllamaAvailableModels, source.OllamaAvailableModels);
        Replace(OllamaCloudAvailableModels, source.OllamaCloudAvailableModels);
        Replace(OpenAiAvailableModels, source.OpenAiAvailableModels.Count > 0 ? source.OpenAiAvailableModels : AiProviderModelCatalog.DefaultOpenAiVisionModels);
    }

    public ObservableCollection<string> LmStudioAvailableModels { get; } = [];
    public ObservableCollection<string> OllamaAvailableModels { get; } = [];
    public ObservableCollection<string> OllamaCloudAvailableModels { get; } = [];
    public ObservableCollection<string> OpenAiAvailableModels { get; } = [];
    public ObservableCollection<AiProviderOptionViewModel> ProviderOptions { get; } =
    [
        new("lmstudio", "LM Studio"),
        new("ollama", "Ollama"),
        new("ollama-cloud", "Ollama Cloud"),
        new("openai", "OpenAI")
    ];

    [ObservableProperty] private string statusText = "Ready";
    [ObservableProperty] private string diagnosticsText = string.Empty;
    [ObservableProperty] private string settingsStatusText = "Settings ready.";
    [ObservableProperty] private string themeMode = "Dark";
    [ObservableProperty] private bool includeSubfolders = true;
    [ObservableProperty] private int thumbnailCacheLimitMb = 2048;
    [ObservableProperty] private bool enableAiGlobally;
    [ObservableProperty] private string aiVisionProviderId = "lmstudio";
    [ObservableProperty] private string lmStudioEndpoint = "http://localhost:1234/v1";
    [ObservableProperty] private string lmStudioVisionModel = string.Empty;
    [ObservableProperty] private string ollamaEndpoint = "http://localhost:11434";
    [ObservableProperty] private string ollamaVisionModel = string.Empty;
    [ObservableProperty] private string ollamaCloudEndpoint = "https://ollama.com";
    [ObservableProperty] private string ollamaCloudApiKey = string.Empty;
    [ObservableProperty] private string ollamaCloudVisionModel = string.Empty;
    [ObservableProperty] private string openAiApiKey = string.Empty;
    [ObservableProperty] private string openAiVisionModel = AiProviderModelCatalog.DefaultOpenAiVisionModel;
    [ObservableProperty] private string aiIndexingProgressText = "AI indexing is off";
    [ObservableProperty] private string aiIndexingDetailText = "Enable AI features to allow visual analysis and semantic search.";
    [ObservableProperty] private string aiProviderStatusText = "LM Studio: Not configured";
    [ObservableProperty] private double aiIndexingProgressPercent;
    [ObservableProperty] private string connectionTestStatusText = "Provider checks will run in the background after this window opens.";
    [ObservableProperty] private bool isConnectionTestSuccessful;
    [ObservableProperty] private bool isConnectionTestRunning;

    public string ActiveAiModelSummary
    {
        get
        {
            var providerName = AiVisionProviderId switch
            {
                "lmstudio" => "LM Studio",
                "ollama" => "Ollama",
                "ollama-cloud" => "Ollama Cloud",
                "openai" => "OpenAI",
                _ => "AI"
            };
            var model = AiVisionProviderId switch
            {
                "lmstudio" => LmStudioVisionModel,
                "ollama" => OllamaVisionModel,
                "ollama-cloud" => OllamaCloudVisionModel,
                "openai" => OpenAiVisionModel,
                _ => string.Empty
            };

            return string.IsNullOrWhiteSpace(model)
                ? $"{providerName}: no active model selected"
                : $"{providerName}: {model}";
        }
    }

    public AppSettings BuildSettings()
    {
        return _baseSettings with
        {
            ThumbnailCacheLimitMb = Math.Max(256, ThumbnailCacheLimitMb),
            ShowSubfoldersByDefault = IncludeSubfolders,
            ThemeMode = string.IsNullOrWhiteSpace(ThemeMode) ? "Dark" : ThemeMode.Trim(),
            EnableAiGlobally = EnableAiGlobally,
            AiVisionProviderId = NormalizeProviderId(AiVisionProviderId),
            LmStudioEndpoint = string.IsNullOrWhiteSpace(LmStudioEndpoint) ? AiProviderModelCatalog.DefaultLmStudioEndpoint : LmStudioEndpoint.Trim(),
            LmStudioVisionModel = string.IsNullOrWhiteSpace(LmStudioVisionModel) ? null : LmStudioVisionModel.Trim(),
            OllamaEndpoint = string.IsNullOrWhiteSpace(OllamaEndpoint) ? AiProviderModelCatalog.DefaultOllamaEndpoint : OllamaEndpoint.Trim(),
            OllamaVisionModel = string.IsNullOrWhiteSpace(OllamaVisionModel) ? null : OllamaVisionModel.Trim(),
            OllamaCloudEndpoint = string.IsNullOrWhiteSpace(OllamaCloudEndpoint) ? AiProviderModelCatalog.DefaultOllamaCloudEndpoint : OllamaCloudEndpoint.Trim(),
            OllamaCloudApiKeyPlaceholder = string.IsNullOrWhiteSpace(OllamaCloudApiKey) ? null : OllamaCloudApiKey.Trim(),
            OllamaCloudVisionModel = string.IsNullOrWhiteSpace(OllamaCloudVisionModel) ? null : OllamaCloudVisionModel.Trim(),
            OpenAiApiKeyPlaceholder = string.IsNullOrWhiteSpace(OpenAiApiKey) ? null : OpenAiApiKey.Trim(),
            OpenAiVisionModel = string.IsNullOrWhiteSpace(OpenAiVisionModel) ? AiProviderModelCatalog.DefaultOpenAiVisionModel : OpenAiVisionModel.Trim()
        };
    }

    public async Task RefreshProvidersAsync()
    {
        if (IsConnectionTestRunning)
        {
            return;
        }

        IsConnectionTestRunning = true;
        SettingsStatusText = "Testing AI provider connections from current form values...";
        ConnectionTestStatusText = "Testing configured AI providers...";
        IsConnectionTestSuccessful = false;
        try
        {
            var statuses = await _providerStatusService.GetStatusesAsync(BuildSettings());
            UpdateProviderModels(statuses);

            AiProviderStatusText = statuses.Count == 0
                ? "No provider status returned."
                : string.Join(" | ", statuses.Select(status => $"{status.DisplayName}: {status.Summary}"));

            var activeProviderId = NormalizeProviderId(AiVisionProviderId);
            var active = statuses.FirstOrDefault(status => string.Equals(status.ProviderId, activeProviderId, StringComparison.OrdinalIgnoreCase));
            IsConnectionTestSuccessful = active?.IsAvailable == true;
            ConnectionTestStatusText = active is null
                ? $"Connection test failed: active provider '{activeProviderId}' was not found."
                : active.IsAvailable
                    ? $"Connection test passed: {active.DisplayName} is ready with {active.ActiveModel}."
                    : $"Connection test failed: {active.DisplayName} is not ready. {active.Summary}";

            AiIndexingProgressText = active?.IsAvailable == true ? "AI provider ready" : "AI provider unavailable";
            AiIndexingDetailText = active is null
                ? "Select a provider and configure its endpoint, API key, and model."
                : active.Summary;
            SettingsStatusText = "Provider readiness refreshed from current form values.";
        }
        finally
        {
            IsConnectionTestRunning = false;
            OnPropertyChanged(nameof(ActiveAiModelSummary));
        }
    }

    private void UpdateProviderModels(IReadOnlyList<AiProviderStatusSnapshot> statuses)
    {
        var lmStudioModels = statuses.FirstOrDefault(status => status.ProviderId == "lmstudio")?.AvailableModels ?? [];
        var ollamaModels = statuses.FirstOrDefault(status => status.ProviderId == "ollama")?.AvailableModels ?? [];
        var ollamaCloudModels = statuses.FirstOrDefault(status => status.ProviderId == "ollama-cloud")?.AvailableModels ?? [];
        var openAiModels = statuses.FirstOrDefault(status => status.ProviderId == "openai")?.AvailableModels ?? AiProviderModelCatalog.DefaultOpenAiVisionModels;

        Replace(LmStudioAvailableModels, lmStudioModels);
        Replace(OllamaAvailableModels, ollamaModels);
        Replace(OllamaCloudAvailableModels, ollamaCloudModels);
        Replace(OpenAiAvailableModels, openAiModels.Count > 0 ? openAiModels : AiProviderModelCatalog.DefaultOpenAiVisionModels);

        if (string.IsNullOrWhiteSpace(LmStudioVisionModel))
        {
            LmStudioVisionModel = lmStudioModels.FirstOrDefault(AiProviderModelCatalog.IsVisionModelId) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(OllamaVisionModel))
        {
            OllamaVisionModel = ollamaModels.FirstOrDefault(AiProviderModelCatalog.IsVisionModelId) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(OllamaCloudVisionModel))
        {
            OllamaCloudVisionModel = ollamaCloudModels.FirstOrDefault(AiProviderModelCatalog.IsVisionModelId) ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(OpenAiVisionModel))
        {
            OpenAiVisionModel = OpenAiAvailableModels.FirstOrDefault(AiProviderModelCatalog.IsOpenAiVisionModelId) ?? AiProviderModelCatalog.DefaultOpenAiVisionModel;
        }
    }

    partial void OnAiVisionProviderIdChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));
    partial void OnLmStudioVisionModelChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));
    partial void OnOllamaVisionModelChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));
    partial void OnOllamaCloudVisionModelChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));
    partial void OnOpenAiVisionModelChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));

    private static string NormalizeProviderId(string? providerId)
    {
        var value = providerId?.Trim().ToLowerInvariant();
        return value is "lmstudio" or "ollama" or "ollama-cloud" or "openai"
            ? value
            : "lmstudio";
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items)
        {
            target.Add(item);
        }
    }
}

public sealed record AiProviderOptionViewModel(string Id, string Name);
