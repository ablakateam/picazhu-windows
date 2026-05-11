using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Picazhu.Core;

namespace Picazhu.AI;

public static class AiProviderModelCatalog
{
    public const string DefaultOpenAiVisionModel = "gpt-4.1-mini";
    public const string DefaultOllamaEndpoint = "http://localhost:11434";
    public const string DefaultOllamaCloudEndpoint = "https://ollama.com";
    public const string DefaultLmStudioEndpoint = "http://localhost:1234/v1";

    public static readonly IReadOnlyList<string> DefaultOpenAiVisionModels =
    [
        "gpt-4.1-mini",
        "gpt-4.1",
        "gpt-4o-mini",
        "gpt-4o",
        "gpt-5-mini",
        "gpt-5"
    ];

    public static readonly IReadOnlyList<string> DefaultOllamaVisionModels =
    [
        "gemma3",
        "llama3.2-vision",
        "llava",
        "qwen2.5vl",
        "moondream"
    ];

    public static bool IsVisionModelId(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var value = modelId.Trim().ToLowerInvariant();
        return value.Contains("vision", StringComparison.Ordinal) ||
               value.Contains("-vl", StringComparison.Ordinal) ||
               value.Contains("vl-", StringComparison.Ordinal) ||
               value.Contains("qwen2.5vl", StringComparison.Ordinal) ||
               value.Contains("qwen2.5-vl", StringComparison.Ordinal) ||
               value.Contains("qwen-vl", StringComparison.Ordinal) ||
               value.Contains("llava", StringComparison.Ordinal) ||
               value.Contains("bakllava", StringComparison.Ordinal) ||
               value.Contains("moondream", StringComparison.Ordinal) ||
               value.Contains("pixtral", StringComparison.Ordinal) ||
               value.Contains("gemma3", StringComparison.Ordinal) ||
               value.Contains("gemma-3", StringComparison.Ordinal) ||
               value.Contains("minicpm-v", StringComparison.Ordinal) ||
               value.Contains("internvl", StringComparison.Ordinal) ||
               value.Contains("molmo", StringComparison.Ordinal) ||
               value.Contains("paligemma", StringComparison.Ordinal) ||
               value.Contains("ministral-3", StringComparison.Ordinal);
    }

    public static bool IsOpenAiVisionModelId(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var value = modelId.Trim().ToLowerInvariant();
        return value.StartsWith("gpt-5", StringComparison.Ordinal) ||
               value.StartsWith("gpt-4.1", StringComparison.Ordinal) ||
               value.StartsWith("gpt-4o", StringComparison.Ordinal) ||
               value.Contains("vision", StringComparison.Ordinal);
    }
}

public sealed class LmStudioProvider(IHttpClientFactory httpClientFactory) : IAiProvider
{
    public AiProviderInfo GetProviderInfo() => new("lmstudio", "LM Studio", false, true, true);

    public async Task<AiProviderStatusSnapshot> GetStatusAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeOpenAiCompatibleBaseUrl(settings.LmStudioEndpoint, AiProviderModelCatalog.DefaultLmStudioEndpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new AiProviderStatusSnapshot("lmstudio", "LM Studio", false, false, true, true, "Endpoint not configured");
        }

        var models = await TryGetOpenAiCompatibleModelsAsync(nameof(LmStudioProvider), endpoint, null, TimeSpan.FromSeconds(3), cancellationToken);
        if (models is null)
        {
            return new AiProviderStatusSnapshot("lmstudio", "LM Studio", true, false, true, true, $"Unavailable at {endpoint}");
        }

        var selectedModel = settings.LmStudioVisionModel?.Trim();
        var selectedExists = !string.IsNullOrWhiteSpace(selectedModel) && models.Contains(selectedModel, StringComparer.OrdinalIgnoreCase);
        var selectedSupportsVision = selectedExists && IsVisionModelId(selectedModel);
        var isAvailable = selectedExists && selectedSupportsVision;
        var summary = !selectedExists
            ? "Connected, but no active LM Studio vision model is selected."
            : selectedSupportsVision
                ? $"Connected using {selectedModel}"
                : $"Connected, but {selectedModel} does not look vision-capable.";

        return new AiProviderStatusSnapshot(
            "lmstudio",
            "LM Studio",
            true,
            isAvailable,
            true,
            true,
            summary,
            selectedModel,
            selectedSupportsVision,
            models);
    }

    public async Task<AiAnalysisResult?> DescribeImageAsync(string imagePath, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeOpenAiCompatibleBaseUrl(settings.LmStudioEndpoint, AiProviderModelCatalog.DefaultLmStudioEndpoint);
        var model = settings.LmStudioVisionModel?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || !File.Exists(imagePath))
        {
            return null;
        }

        return await SendOpenAiCompatibleVisionAsync(
            nameof(LmStudioProvider),
            endpoint,
            model,
            [imagePath],
            "LM Studio",
            null,
            preferJsonObjectResponse: false,
            maxTokens: 180,
            cancellationToken);
    }

    public async Task<AiAnalysisResult?> DescribeVideoFrameSetAsync(IReadOnlyList<string> framePaths, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeOpenAiCompatibleBaseUrl(settings.LmStudioEndpoint, AiProviderModelCatalog.DefaultLmStudioEndpoint);
        var model = settings.LmStudioVisionModel?.Trim();
        var usableFrames = framePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Take(2)
            .ToList();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || usableFrames.Count == 0)
        {
            return null;
        }

        return await SendOpenAiCompatibleVisionAsync(
            nameof(LmStudioProvider),
            endpoint,
            model,
            usableFrames,
            "LM Studio video",
            null,
            preferJsonObjectResponse: false,
            maxTokens: 220,
            cancellationToken);
    }

    public Task<IReadOnlyList<float>> CreateTextEmbeddingsAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<float>>([]);

    public Task<IReadOnlyList<string>> RerankResultsAsync(string query, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default)
        => Task.FromResult(candidates);

    public static bool IsVisionModelId(string? modelId) => AiProviderModelCatalog.IsVisionModelId(modelId);

    private static AiAnalysisResult? ParseAnalysisResponse(string responseBody)
        => VisionAnalysisJson.ParseOpenAiChatResponse(responseBody);

    private async Task<IReadOnlyList<string>?> TryGetOpenAiCompatibleModelsAsync(
        string clientName,
        string endpoint,
        Action<HttpRequestMessage>? configureRequest,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(clientName);
            client.Timeout = timeout;
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/models");
            configureRequest?.Invoke(request);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            return await ParseOpenAiCompatibleModelsAsync(stream, cancellationToken);
        }
        catch
        {
            return null;
        }
    }

    private async Task<AiAnalysisResult?> SendOpenAiCompatibleVisionAsync(
        string clientName,
        string endpoint,
        string model,
        IReadOnlyList<string> imagePaths,
        string failureLabel,
        Action<HttpRequestMessage>? configureRequest,
        bool preferJsonObjectResponse,
        int maxTokens,
        CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(clientName);
        client.Timeout = TimeSpan.FromSeconds(90);

        var contentItems = await BuildOpenAiVisionContentAsync(imagePaths, cancellationToken);
        var payload = CreateOpenAiCompatibleVisionPayload(model, contentItems, maxTokens, preferJsonObjectResponse ? "json_object" : "text");
        var result = await SendOpenAiCompatibleVisionRequestAsync(client, endpoint, payload, configureRequest, failureLabel, cancellationToken);
        if (result.IsSuccess)
        {
            var parsed = ParseAnalysisResponse(result.ResponseBody);
            return parsed is null ? null : parsed with { ProviderModel = model, AnalyzedUtc = DateTimeOffset.UtcNow };
        }

        if (result.ResponseBody.Contains("response_format", StringComparison.OrdinalIgnoreCase))
        {
            var retryPayload = CreateOpenAiCompatibleVisionPayload(model, contentItems, maxTokens, null);
            result = await SendOpenAiCompatibleVisionRequestAsync(client, endpoint, retryPayload, configureRequest, failureLabel, cancellationToken);
            if (result.IsSuccess)
            {
                var parsed = ParseAnalysisResponse(result.ResponseBody);
                return parsed is null ? null : parsed with { ProviderModel = model, AnalyzedUtc = DateTimeOffset.UtcNow };
            }
        }

        throw new InvalidOperationException($"{failureLabel} request failed ({result.StatusCode}): {result.ResponseBody}");
    }

    private static async Task<IReadOnlyList<string>> ParseOpenAiCompatibleModelsAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
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

    private static string NormalizeOpenAiCompatibleBaseUrl(string? configuredBaseUrl, string defaultBaseUrl)
    {
        var trimmed = (string.IsNullOrWhiteSpace(configuredBaseUrl) ? defaultBaseUrl : configuredBaseUrl)
            .Trim()
            .TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/v1";
    }

    private static async Task<List<object>> BuildOpenAiVisionContentAsync(IReadOnlyList<string> imagePaths, CancellationToken cancellationToken)
    {
        var contentItems = new List<object>
        {
            new
            {
                type = "text",
                text = VisionAnalysisJson.Prompt
            }
        };

        foreach (var imagePath in imagePaths.Where(File.Exists))
        {
            var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var dataUrl = $"data:{GetMimeType(imagePath)};base64,{Convert.ToBase64String(imageBytes)}";
            contentItems.Add(new { type = "image_url", image_url = new { url = dataUrl, detail = "low" } });
        }

        return contentItems;
    }

    private static object CreateOpenAiCompatibleVisionPayload(string model, IReadOnlyList<object> contentItems, int maxTokens, string? responseFormat)
    {
        var messages = new object[]
        {
            new
            {
                role = "user",
                content = contentItems
            }
        };

        return string.IsNullOrWhiteSpace(responseFormat)
            ? new
            {
                model,
                temperature = 0.1,
                max_tokens = maxTokens,
                messages
            }
            : new
            {
                model,
                temperature = 0.1,
                max_tokens = maxTokens,
                response_format = new { type = responseFormat },
                messages
            };
    }

    private static async Task<(bool IsSuccess, int StatusCode, string ResponseBody)> SendOpenAiCompatibleVisionRequestAsync(
        HttpClient client,
        string endpoint,
        object payload,
        Action<HttpRequestMessage>? configureRequest,
        string failureLabel,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, VisionAnalysisJson.JsonOptions), Encoding.UTF8, "application/json")
        };
        configureRequest?.Invoke(request);

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.IsSuccessStatusCode, (int)response.StatusCode, responseBody);
    }

    private static string GetMimeType(string imagePath)
    {
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            ".tif" or ".tiff" => "image/tiff",
            ".heic" or ".heif" => "image/jpeg",
            _ => "image/jpeg"
        };
    }
}

public sealed class OpenAiProvider(IHttpClientFactory httpClientFactory) : IAiProvider
{
    private const string Endpoint = "https://api.openai.com/v1";

    public AiProviderInfo GetProviderInfo() => new("openai", "OpenAI", true, true, true);

    public async Task<AiProviderStatusSnapshot> GetStatusAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var apiKey = settings.OpenAiApiKeyPlaceholder?.Trim();
        var configured = !string.IsNullOrWhiteSpace(apiKey);
        var selectedModel = settings.OpenAiVisionModel?.Trim();
        if (!configured)
        {
            return new AiProviderStatusSnapshot(
                "openai",
                "OpenAI",
                false,
                false,
                true,
                true,
                "API key not configured",
                selectedModel,
                false,
                AiProviderModelCatalog.DefaultOpenAiVisionModels);
        }

        var models = await TryGetModelsAsync(apiKey!, cancellationToken);
        if (models is null)
        {
            return new AiProviderStatusSnapshot(
                "openai",
                "OpenAI",
                true,
                false,
                true,
                true,
                "API key configured, but OpenAI could not be reached",
                selectedModel,
                false,
                AiProviderModelCatalog.DefaultOpenAiVisionModels);
        }

        var visionModels = models
            .Where(AiProviderModelCatalog.IsOpenAiVisionModelId)
            .Concat(AiProviderModelCatalog.DefaultOpenAiVisionModels)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(model => model, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var selectedSupportsVision = AiProviderModelCatalog.IsOpenAiVisionModelId(selectedModel);
        var isAvailable = !string.IsNullOrWhiteSpace(selectedModel) && selectedSupportsVision;
        var summary = string.IsNullOrWhiteSpace(selectedModel)
            ? "Connected, but no active OpenAI vision model is selected."
            : selectedSupportsVision
                ? $"Connected using {selectedModel}"
                : $"Connected, but {selectedModel} is not recognized as a vision model.";

        return new AiProviderStatusSnapshot(
            "openai",
            "OpenAI",
            true,
            isAvailable,
            true,
            true,
            summary,
            selectedModel,
            selectedSupportsVision,
            visionModels);
    }

    public async Task<AiAnalysisResult?> DescribeImageAsync(string imagePath, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(settings.OpenAiVisionModel);
        var apiKey = settings.OpenAiApiKeyPlaceholder?.Trim();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model) || !File.Exists(imagePath))
        {
            return null;
        }

        return await SendOpenAiVisionAsync([imagePath], model, apiKey!, 180, cancellationToken);
    }

    public async Task<AiAnalysisResult?> DescribeVideoFrameSetAsync(IReadOnlyList<string> framePaths, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var model = ResolveModel(settings.OpenAiVisionModel);
        var apiKey = settings.OpenAiApiKeyPlaceholder?.Trim();
        var usableFrames = framePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Take(2)
            .ToList();
        if (string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model) || usableFrames.Count == 0)
        {
            return null;
        }

        return await SendOpenAiVisionAsync(usableFrames, model, apiKey!, 220, cancellationToken);
    }

    public Task<IReadOnlyList<float>> CreateTextEmbeddingsAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<float>>([]);

    public Task<IReadOnlyList<string>> RerankResultsAsync(string query, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default)
        => Task.FromResult(candidates);

    private async Task<IReadOnlyList<string>?> TryGetModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(nameof(OpenAiProvider));
            client.Timeout = TimeSpan.FromSeconds(8);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{Endpoint}/models");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return data.EnumerateArray()
                .Select(item => item.TryGetProperty("id", out var id) ? id.GetString() : null)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Select(id => id!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    private async Task<AiAnalysisResult?> SendOpenAiVisionAsync(IReadOnlyList<string> imagePaths, string model, string apiKey, int maxTokens, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(nameof(OpenAiProvider));
        client.Timeout = TimeSpan.FromSeconds(90);

        var contentItems = await BuildContentAsync(imagePaths, cancellationToken);
        var payload = CreatePayload(model, contentItems, maxTokens, includeJsonFormat: true);
        var result = await SendRequestAsync(client, payload, apiKey, cancellationToken);
        if (!result.IsSuccess && result.ResponseBody.Contains("response_format", StringComparison.OrdinalIgnoreCase))
        {
            payload = CreatePayload(model, contentItems, maxTokens, includeJsonFormat: false);
            result = await SendRequestAsync(client, payload, apiKey, cancellationToken);
        }

        if (!result.IsSuccess)
        {
            throw new InvalidOperationException($"OpenAI request failed ({result.StatusCode}): {result.ResponseBody}");
        }

        var parsed = VisionAnalysisJson.ParseOpenAiChatResponse(result.ResponseBody);
        return parsed is null ? null : parsed with { ProviderModel = model, AnalyzedUtc = DateTimeOffset.UtcNow };
    }

    private static string? ResolveModel(string? configuredModel)
        => string.IsNullOrWhiteSpace(configuredModel) ? AiProviderModelCatalog.DefaultOpenAiVisionModel : configuredModel.Trim();

    private static async Task<List<object>> BuildContentAsync(IReadOnlyList<string> imagePaths, CancellationToken cancellationToken)
    {
        var contentItems = new List<object>
        {
            new { type = "text", text = VisionAnalysisJson.Prompt }
        };

        foreach (var imagePath in imagePaths.Where(File.Exists))
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            var dataUrl = $"data:{GetMimeType(imagePath)};base64,{Convert.ToBase64String(bytes)}";
            contentItems.Add(new { type = "image_url", image_url = new { url = dataUrl, detail = "low" } });
        }

        return contentItems;
    }

    private static object CreatePayload(string model, IReadOnlyList<object> contentItems, int maxTokens, bool includeJsonFormat)
    {
        var messages = new object[]
        {
            new
            {
                role = "user",
                content = contentItems
            }
        };

        return includeJsonFormat
            ? new
            {
                model,
                temperature = 0.1,
                max_tokens = maxTokens,
                response_format = new { type = "json_object" },
                messages
            }
            : new
            {
                model,
                temperature = 0.1,
                max_tokens = maxTokens,
                messages
            };
    }

    private static async Task<(bool IsSuccess, int StatusCode, string ResponseBody)> SendRequestAsync(HttpClient client, object payload, string apiKey, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, VisionAnalysisJson.JsonOptions), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        return (response.IsSuccessStatusCode, (int)response.StatusCode, responseBody);
    }

    private static string GetMimeType(string imagePath)
    {
        return Path.GetExtension(imagePath).ToLowerInvariant() switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => "image/jpeg"
        };
    }
}

public sealed class OllamaProvider(IHttpClientFactory httpClientFactory) : OllamaProviderBase(httpClientFactory)
{
    protected override string ProviderId => "ollama";
    protected override string DisplayName => "Ollama";
    protected override bool RequiresRemoteAccess => false;
    protected override string DefaultEndpoint => AiProviderModelCatalog.DefaultOllamaEndpoint;
    protected override string? GetEndpoint(AppSettings settings) => settings.OllamaEndpoint;
    protected override string? GetModel(AppSettings settings) => settings.OllamaVisionModel;
    protected override string? GetApiKey(AppSettings settings) => null;
    protected override bool HasRequiredSecrets(AppSettings settings) => true;
}

public sealed class OllamaCloudProvider(IHttpClientFactory httpClientFactory) : OllamaProviderBase(httpClientFactory)
{
    protected override string ProviderId => "ollama-cloud";
    protected override string DisplayName => "Ollama Cloud";
    protected override bool RequiresRemoteAccess => true;
    protected override string DefaultEndpoint => AiProviderModelCatalog.DefaultOllamaCloudEndpoint;
    protected override string? GetEndpoint(AppSettings settings) => settings.OllamaCloudEndpoint;
    protected override string? GetModel(AppSettings settings) => settings.OllamaCloudVisionModel;
    protected override string? GetApiKey(AppSettings settings) => settings.OllamaCloudApiKeyPlaceholder;
    protected override bool HasRequiredSecrets(AppSettings settings) => !string.IsNullOrWhiteSpace(settings.OllamaCloudApiKeyPlaceholder);
}

public abstract class OllamaProviderBase(IHttpClientFactory httpClientFactory) : IAiProvider
{
    protected abstract string ProviderId { get; }
    protected abstract string DisplayName { get; }
    protected abstract bool RequiresRemoteAccess { get; }
    protected abstract string DefaultEndpoint { get; }
    protected abstract string? GetEndpoint(AppSettings settings);
    protected abstract string? GetModel(AppSettings settings);
    protected abstract string? GetApiKey(AppSettings settings);
    protected abstract bool HasRequiredSecrets(AppSettings settings);

    public AiProviderInfo GetProviderInfo() => new(ProviderId, DisplayName, RequiresRemoteAccess, true, true);

    public async Task<AiProviderStatusSnapshot> GetStatusAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeOllamaApiBaseUrl(GetEndpoint(settings), DefaultEndpoint);
        var model = GetModel(settings)?.Trim();
        var configured = !string.IsNullOrWhiteSpace(endpoint) && HasRequiredSecrets(settings);
        if (!configured)
        {
            var missingConfigurationSummary = RequiresRemoteAccess ? "API key not configured" : "Endpoint not configured";
            return new AiProviderStatusSnapshot(
                ProviderId,
                DisplayName,
                false,
                false,
                true,
                true,
                missingConfigurationSummary,
                model,
                false,
                AiProviderModelCatalog.DefaultOllamaVisionModels);
        }

        var models = await TryGetModelsAsync(endpoint, GetApiKey(settings), cancellationToken);
        if (models is null)
        {
            return new AiProviderStatusSnapshot(
                ProviderId,
                DisplayName,
                true,
                false,
                true,
                true,
                $"Unavailable at {endpoint}",
                model,
                false,
                []);
        }

        var selectedExists = !string.IsNullOrWhiteSpace(model) && models.Contains(model, StringComparer.OrdinalIgnoreCase);
        var selectedSupportsVision = selectedExists && AiProviderModelCatalog.IsVisionModelId(model);
        var summary = !selectedExists
            ? $"Connected at {endpoint}, but no active vision model is selected."
            : selectedSupportsVision
                ? $"Connected using {model}"
                : $"Connected, but {model} does not look vision-capable.";

        return new AiProviderStatusSnapshot(
            ProviderId,
            DisplayName,
            true,
            selectedExists && selectedSupportsVision,
            true,
            true,
            summary,
            model,
            selectedSupportsVision,
            models);
    }

    public async Task<AiAnalysisResult?> DescribeImageAsync(string imagePath, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeOllamaApiBaseUrl(GetEndpoint(settings), DefaultEndpoint);
        var model = GetModel(settings)?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || !HasRequiredSecrets(settings) || !File.Exists(imagePath))
        {
            return null;
        }

        return await SendOllamaVisionAsync(endpoint, model, [imagePath], GetApiKey(settings), 180, cancellationToken);
    }

    public async Task<AiAnalysisResult?> DescribeVideoFrameSetAsync(IReadOnlyList<string> framePaths, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeOllamaApiBaseUrl(GetEndpoint(settings), DefaultEndpoint);
        var model = GetModel(settings)?.Trim();
        var usableFrames = framePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Take(2)
            .ToList();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || !HasRequiredSecrets(settings) || usableFrames.Count == 0)
        {
            return null;
        }

        return await SendOllamaVisionAsync(endpoint, model, usableFrames, GetApiKey(settings), 220, cancellationToken);
    }

    public Task<IReadOnlyList<float>> CreateTextEmbeddingsAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<float>>([]);

    public Task<IReadOnlyList<string>> RerankResultsAsync(string query, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default)
        => Task.FromResult(candidates);

    private async Task<IReadOnlyList<string>?> TryGetModelsAsync(string endpoint, string? apiKey, CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(GetType().Name);
            client.Timeout = TimeSpan.FromSeconds(RequiresRemoteAccess ? 8 : 3);
            using var request = new HttpRequestMessage(HttpMethod.Get, $"{endpoint}/tags");
            ApplyAuthorization(request, apiKey);
            using var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return models.EnumerateArray()
                .Select(item => item.TryGetProperty("name", out var name) ? name.GetString() : null)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => name!.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    private async Task<AiAnalysisResult?> SendOllamaVisionAsync(string endpoint, string model, IReadOnlyList<string> imagePaths, string? apiKey, int maxTokens, CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(GetType().Name);
        client.Timeout = TimeSpan.FromSeconds(90);

        var images = new List<string>();
        foreach (var imagePath in imagePaths.Where(File.Exists))
        {
            var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
            images.Add(Convert.ToBase64String(bytes));
        }

        if (images.Count == 0)
        {
            return null;
        }

        var payload = new
        {
            model,
            stream = false,
            format = "json",
            options = new
            {
                temperature = 0.1,
                num_predict = maxTokens
            },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = VisionAnalysisJson.Prompt,
                    images
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, VisionAnalysisJson.JsonOptions), Encoding.UTF8, "application/json")
        };
        ApplyAuthorization(request, apiKey);

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"{DisplayName} request failed ({(int)response.StatusCode}): {responseBody}");
        }

        var parsed = VisionAnalysisJson.ParseOllamaChatResponse(responseBody);
        return parsed is null ? null : parsed with { ProviderModel = model, AnalyzedUtc = DateTimeOffset.UtcNow };
    }

    private static void ApplyAuthorization(HttpRequestMessage request, string? apiKey)
    {
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey.Trim());
        }
    }

    private static string NormalizeOllamaApiBaseUrl(string? configuredBaseUrl, string defaultBaseUrl)
    {
        var trimmed = (string.IsNullOrWhiteSpace(configuredBaseUrl) ? defaultBaseUrl : configuredBaseUrl)
            .Trim()
            .TrimEnd('/');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return string.Empty;
        }

        return trimmed.EndsWith("/api", StringComparison.OrdinalIgnoreCase)
            ? trimmed
            : $"{trimmed}/api";
    }
}

internal static class VisionAnalysisJson
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string Prompt =
        "Analyze this media for visual search. Return valid compact JSON only with keys caption, tags, objects, logos, ocr. Use concise lower-case search terms, deduplicate values, include readable text in ocr, and do not add commentary.";

    public static AiAnalysisResult? ParseOpenAiChatResponse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var message = choices[0].GetProperty("message");
        if (!message.TryGetProperty("content", out var content))
        {
            return null;
        }

        var contentText = content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Join("", content.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out _))
                .Select(item => item.GetProperty("text").GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => null
        };

        return ParseAnalysisContent(contentText);
    }

    public static AiAnalysisResult? ParseOllamaChatResponse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        string? contentText = null;
        if (document.RootElement.TryGetProperty("message", out var message) &&
            message.ValueKind == JsonValueKind.Object &&
            message.TryGetProperty("content", out var content) &&
            content.ValueKind == JsonValueKind.String)
        {
            contentText = content.GetString();
        }
        else if (document.RootElement.TryGetProperty("response", out var response) &&
                 response.ValueKind == JsonValueKind.String)
        {
            contentText = response.GetString();
        }

        return ParseAnalysisContent(contentText);
    }

    private static AiAnalysisResult? ParseAnalysisContent(string? contentText)
    {
        if (string.IsNullOrWhiteSpace(contentText))
        {
            return null;
        }

        var normalizedText = NormalizeModelContent(contentText);
        if (!TryParseAnalysisJson(normalizedText, out var parsed))
        {
            return new AiAnalysisResult(
                normalizedText,
                [],
                [],
                null,
                string.Empty,
                DateTimeOffset.UtcNow);
        }

        return parsed;
    }

    private static bool TryParseAnalysisJson(string contentText, out AiAnalysisResult? result)
    {
        result = null;
        var jsonPayload = ExtractJsonPayload(contentText);
        if (string.IsNullOrWhiteSpace(jsonPayload))
        {
            return false;
        }

        try
        {
            using var analysisDocument = JsonDocument.Parse(jsonPayload);
            var root = analysisDocument.RootElement;
            var caption = root.TryGetProperty("caption", out var captionElement) && captionElement.ValueKind == JsonValueKind.String
                ? captionElement.GetString()?.Trim()
                : null;

            var tags = ReadStringArray(root, "tags");
            var objects = ReadStringArray(root, "objects");
            var logos = ReadStringArray(root, "logos");
            var ocrText = ReadFirstString(root, "ocr", "ocrText", "ocr_text", "text");
            var mergedTags = tags.Concat(logos).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            result = new AiAnalysisResult(
                caption,
                mergedTags,
                objects,
                ocrText,
                string.Empty,
                DateTimeOffset.UtcNow);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string NormalizeModelContent(string contentText)
    {
        var trimmed = contentText.Trim();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
        {
            var lines = trimmed.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
                .Where(line => !line.StartsWith("```", StringComparison.Ordinal))
                .ToArray();
            if (lines.Length > 0)
            {
                return string.Join(Environment.NewLine, lines).Trim();
            }
        }

        return trimmed;
    }

    private static string ExtractJsonPayload(string contentText)
    {
        var trimmed = contentText.Trim();
        var start = trimmed.IndexOf('{');
        var end = trimmed.LastIndexOf('}');
        if (start >= 0 && end > start)
        {
            return trimmed[start..(end + 1)];
        }

        return trimmed;
    }

    private static IReadOnlyList<string> ReadStringArray(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var property) || property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? ReadFirstString(JsonElement root, params string[] propertyNames)
    {
        foreach (var propertyName in propertyNames)
        {
            if (root.TryGetProperty(propertyName, out var property) &&
                property.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(property.GetString()))
            {
                return property.GetString()!.Trim();
            }
        }

        return null;
    }
}
