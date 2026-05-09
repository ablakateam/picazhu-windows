using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Picazhu.AI;
using Picazhu.Core;

namespace Picazhu.App;

public partial class SettingsWindow : Window
{
    private readonly MainViewModel _ownerViewModel;
    private readonly SettingsDraftViewModel _draftViewModel;

    public SettingsWindow(MainViewModel viewModel)
    {
        _ownerViewModel = viewModel;
        _draftViewModel = new SettingsDraftViewModel(viewModel);
        InitializeComponent();
        DataContext = _draftViewModel;
    }

    public Task InitializeAsync() => _draftViewModel.RefreshProvidersAsync();

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
    private readonly string? _openAiApiKeyPlaceholder;
    private readonly string? _openAiVisionModel;
    private readonly string? _ollamaEndpoint;
    private readonly string? _ollamaVisionModel;

    public SettingsDraftViewModel(MainViewModel source)
    {
        var settings = source.CreateSettingsSnapshot();
        _baseSettings = settings;
        StatusText = source.StatusText;
        DiagnosticsText = source.DiagnosticsText;
        SettingsStatusText = source.SettingsStatusText;
        ThemeMode = string.IsNullOrWhiteSpace(settings.ThemeMode) ? "Dark" : settings.ThemeMode;
        IncludeSubfolders = settings.ShowSubfoldersByDefault;
        ThumbnailCacheLimitMb = settings.ThumbnailCacheLimitMb;
        EnableAiGlobally = settings.EnableAiGlobally;
        AiVisionProviderId = "lmstudio";
        LmStudioEndpoint = string.IsNullOrWhiteSpace(settings.LmStudioEndpoint) ? "http://localhost:1234/v1" : settings.LmStudioEndpoint;
        LmStudioVisionModel = settings.LmStudioVisionModel ?? string.Empty;
        AiIndexingProgressText = source.AiIndexingProgressText;
        AiIndexingDetailText = source.AiIndexingDetailText;
        AiProviderStatusText = source.AiProviderStatusText;
        AiIndexingProgressPercent = source.AiIndexingProgressPercent;

        _openAiApiKeyPlaceholder = settings.OpenAiApiKeyPlaceholder;
        _openAiVisionModel = settings.OpenAiVisionModel;
        _ollamaEndpoint = settings.OllamaEndpoint;
        _ollamaVisionModel = settings.OllamaVisionModel;

        foreach (var model in source.LmStudioAvailableModels)
        {
            LmStudioAvailableModels.Add(model);
        }
    }

    public ObservableCollection<string> LmStudioAvailableModels { get; } = [];

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
    [ObservableProperty] private string aiIndexingProgressText = "AI indexing is off";
    [ObservableProperty] private string aiIndexingDetailText = "Enable AI features to allow visual analysis and semantic search.";
    [ObservableProperty] private string aiProviderStatusText = "LM Studio: Not configured";
    [ObservableProperty] private double aiIndexingProgressPercent;
    [ObservableProperty] private string connectionTestStatusText = "Connection test has not been run yet.";
    [ObservableProperty] private bool isConnectionTestSuccessful;

    public string ActiveAiModelSummary => string.IsNullOrWhiteSpace(LmStudioVisionModel) ? "No LM Studio model selected" : LmStudioVisionModel;

    public AppSettings BuildSettings()
    {
        return _baseSettings with
        {
            ThumbnailCacheLimitMb = Math.Max(256, ThumbnailCacheLimitMb),
            ShowSubfoldersByDefault = IncludeSubfolders,
            ThemeMode = string.IsNullOrWhiteSpace(ThemeMode) ? "Dark" : ThemeMode.Trim(),
            EnableAiGlobally = EnableAiGlobally,
            AiVisionProviderId = "lmstudio",
            LmStudioEndpoint = string.IsNullOrWhiteSpace(LmStudioEndpoint) ? "http://localhost:1234/v1" : LmStudioEndpoint.Trim(),
            LmStudioVisionModel = string.IsNullOrWhiteSpace(LmStudioVisionModel) ? null : LmStudioVisionModel.Trim(),
            OpenAiApiKeyPlaceholder = _openAiApiKeyPlaceholder,
            OpenAiVisionModel = _openAiVisionModel,
            OllamaEndpoint = _ollamaEndpoint,
            OllamaVisionModel = _ollamaVisionModel
        };
    }

    public async Task RefreshProvidersAsync()
    {
        var endpoint = NormalizeLmStudioBaseUrl(LmStudioEndpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            LmStudioAvailableModels.Clear();
            AiProviderStatusText = "LM Studio: Endpoint not configured";
            AiIndexingProgressText = "AI provider unavailable";
            AiIndexingDetailText = "Set an LM Studio endpoint, then test connections.";
            SettingsStatusText = "Provider readiness refreshed from current form values.";
            ConnectionTestStatusText = "Connection test failed: LM Studio endpoint is not configured.";
            IsConnectionTestSuccessful = false;
            OnPropertyChanged(nameof(ActiveAiModelSummary));
            return;
        }

        var models = await TryGetLmStudioModelsAsync(endpoint);
        if (models is null)
        {
            LmStudioAvailableModels.Clear();
            AiProviderStatusText = $"LM Studio: Unavailable at {endpoint}";
            AiIndexingProgressText = "AI provider unavailable";
            AiIndexingDetailText = "PICAZHU could not reach LM Studio from the current endpoint.";
            SettingsStatusText = "Provider readiness refreshed from current form values.";
            ConnectionTestStatusText = $"Connection test failed: PICAZHU could not reach LM Studio at {endpoint}.";
            IsConnectionTestSuccessful = false;
            OnPropertyChanged(nameof(ActiveAiModelSummary));
            return;
        }

        Replace(LmStudioAvailableModels, models);
        if (string.IsNullOrWhiteSpace(LmStudioVisionModel) || !models.Contains(LmStudioVisionModel, StringComparer.OrdinalIgnoreCase))
        {
            LmStudioVisionModel = models.FirstOrDefault(LmStudioProvider.IsVisionModelId) ?? string.Empty;
        }

        var hasSelectedModel = !string.IsNullOrWhiteSpace(LmStudioVisionModel);
        var selectedSupportsVision = hasSelectedModel && LmStudioProvider.IsVisionModelId(LmStudioVisionModel);
        AiProviderStatusText = !hasSelectedModel
            ? "LM Studio: Connected, but no active vision model is selected."
            : selectedSupportsVision
                ? $"LM Studio: Connected using {LmStudioVisionModel}"
                : $"LM Studio: Connected, but {LmStudioVisionModel} does not look vision-capable.";
        AiIndexingProgressText = selectedSupportsVision ? "AI provider ready" : "AI provider unavailable";
        AiIndexingDetailText = selectedSupportsVision
            ? "LM Studio is reachable and the selected model appears vision-capable."
            : "Select a vision-capable LM Studio model before enabling visual tagging.";
        SettingsStatusText = "Provider readiness refreshed from current form values.";
        ConnectionTestStatusText = selectedSupportsVision
            ? $"Connection test passed: LM Studio is reachable and ready with {LmStudioVisionModel}."
            : hasSelectedModel
                ? $"Connection test failed: {LmStudioVisionModel} is reachable, but it does not appear vision-capable."
                : "Connection test failed: LM Studio is reachable, but no active vision model is selected.";
        IsConnectionTestSuccessful = selectedSupportsVision;
        OnPropertyChanged(nameof(ActiveAiModelSummary));
    }

    partial void OnLmStudioVisionModelChanged(string value) => OnPropertyChanged(nameof(ActiveAiModelSummary));

    private static async Task<IReadOnlyList<string>?> TryGetLmStudioModelsAsync(string endpoint)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync($"{endpoint}/models");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return data.EnumerateArray()
                .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    private static string NormalizeLmStudioBaseUrl(string? configuredBaseUrl)
    {
        var trimmed = configuredBaseUrl?.Trim().TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/v1";
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
