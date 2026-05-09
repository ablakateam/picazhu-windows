using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Picazhu.Core;

namespace Picazhu.AI;

public sealed class LmStudioProvider(IHttpClientFactory httpClientFactory) : IAiProvider
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public AiProviderInfo GetProviderInfo() => new("lmstudio", "LM Studio", false, true, true);

    public async Task<AiProviderStatusSnapshot> GetStatusAsync(AppSettings settings, CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeLmStudioBaseUrl(settings.LmStudioEndpoint);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return new AiProviderStatusSnapshot("lmstudio", "LM Studio", false, false, true, true, "Endpoint not configured");
        }

        var models = await TryGetLmStudioModelsAsync(endpoint, cancellationToken);
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
        var endpoint = NormalizeLmStudioBaseUrl(settings.LmStudioEndpoint);
        var model = settings.LmStudioVisionModel?.Trim();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || !File.Exists(imagePath))
        {
            return null;
        }

        using var client = httpClientFactory.CreateClient(nameof(LmStudioProvider));
        client.Timeout = TimeSpan.FromSeconds(90);

        var imageBytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var mimeType = GetMimeType(imagePath);
        var dataUrl = $"data:{mimeType};base64,{Convert.ToBase64String(imageBytes)}";

        var payload = new
        {
            model,
            temperature = 0.1,
            max_tokens = 180,
            response_format = new { type = "text" },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = new object[]
                    {
                        new { type = "text", text = "Return JSON only with caption, tags, objects, logos. Keep all values short and deduplicated." },
                        new { type = "image_url", image_url = new { url = dataUrl } }
                    }
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LM Studio request failed ({(int)response.StatusCode}): {responseBody}");
        }

        var parsed = ParseAnalysisResponse(responseBody);
        return parsed is null
            ? null
            : parsed with { ProviderModel = model, AnalyzedUtc = DateTimeOffset.UtcNow };
    }

    public async Task<AiAnalysisResult?> DescribeVideoFrameSetAsync(IReadOnlyList<string> framePaths, AppSettings settings, CancellationToken cancellationToken = default)
    {
        var endpoint = NormalizeLmStudioBaseUrl(settings.LmStudioEndpoint);
        var model = settings.LmStudioVisionModel?.Trim();
        var usableFrames = framePaths
            .Where(path => !string.IsNullOrWhiteSpace(path) && File.Exists(path))
            .Take(2)
            .ToList();
        if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(model) || usableFrames.Count == 0)
        {
            return null;
        }

        using var client = httpClientFactory.CreateClient(nameof(LmStudioProvider));
        client.Timeout = TimeSpan.FromSeconds(90);

        var contentItems = new List<object>
        {
            new { type = "text", text = "Return JSON only with caption, tags, objects, logos. Describe the video across these frames. Keep all values short and deduplicated." }
        };

        foreach (var framePath in usableFrames)
        {
            var frameBytes = await File.ReadAllBytesAsync(framePath, cancellationToken);
            var dataUrl = $"data:{GetMimeType(framePath)};base64,{Convert.ToBase64String(frameBytes)}";
            contentItems.Add(new { type = "image_url", image_url = new { url = dataUrl } });
        }

        var payload = new
        {
            model,
            temperature = 0.1,
            max_tokens = 220,
            response_format = new { type = "text" },
            messages = new object[]
            {
                new
                {
                    role = "user",
                    content = contentItems.ToArray()
                }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{endpoint}/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json")
        };

        using var response = await client.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"LM Studio video request failed ({(int)response.StatusCode}): {responseBody}");
        }

        var parsed = ParseAnalysisResponse(responseBody);
        return parsed is null
            ? null
            : parsed with { ProviderModel = model, AnalyzedUtc = DateTimeOffset.UtcNow };
    }

    public Task<IReadOnlyList<float>> CreateTextEmbeddingsAsync(string text, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<float>>([]);

    public Task<IReadOnlyList<string>> RerankResultsAsync(string query, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default)
        => Task.FromResult(candidates);

    private static async Task<IReadOnlyList<string>?> TryGetLmStudioModelsAsync(string endpoint, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var response = await client.GetAsync($"{endpoint}/models", cancellationToken);
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
                .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return null;
        }
    }

    private static AiAnalysisResult? ParseAnalysisResponse(string responseBody)
    {
        using var document = JsonDocument.Parse(responseBody);
        if (!document.RootElement.TryGetProperty("choices", out var choices) || choices.ValueKind != JsonValueKind.Array || choices.GetArrayLength() == 0)
        {
            return null;
        }

        var message = choices[0].GetProperty("message");
        var content = message.GetProperty("content");
        var contentText = content.ValueKind switch
        {
            JsonValueKind.String => content.GetString(),
            JsonValueKind.Array => string.Join("", content.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out _))
                .Select(item => item.GetProperty("text").GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text))),
            _ => null
        };

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
        if (string.IsNullOrWhiteSpace(contentText))
        {
            return false;
        }

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
            var mergedTags = tags.Concat(logos).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            result = new AiAnalysisResult(
                caption,
                mergedTags,
                objects,
                null,
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

    public static bool IsVisionModelId(string? modelId)
    {
        if (string.IsNullOrWhiteSpace(modelId))
        {
            return false;
        }

        var value = modelId.ToLowerInvariant();
        return value.Contains("vision") ||
               value.Contains("-vl") ||
               value.Contains("vl-") ||
               value.Contains("qwen2.5-vl") ||
               value.Contains("llava") ||
               value.Contains("pixtral") ||
               value.Contains("gemma-3") ||
               value.Contains("minicpm-v") ||
               value.Contains("internvl") ||
               value.Contains("molmo") ||
               value.Contains("paligemma") ||
               value.Contains("ministral-3");
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

public sealed class OpenAiProvider : IAiProvider
{
    public AiProviderInfo GetProviderInfo() => new("openai", "OpenAI", true, true, true);
    public Task<AiProviderStatusSnapshot> GetStatusAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => Task.FromResult(new AiProviderStatusSnapshot("openai", "OpenAI", !string.IsNullOrWhiteSpace(settings.OpenAiApiKeyPlaceholder), false, true, true, "Not implemented yet", settings.OpenAiVisionModel, false, []));
    public Task<AiAnalysisResult?> DescribeImageAsync(string imagePath, AppSettings settings, CancellationToken cancellationToken = default) => Task.FromResult<AiAnalysisResult?>(null);
    public Task<AiAnalysisResult?> DescribeVideoFrameSetAsync(IReadOnlyList<string> framePaths, AppSettings settings, CancellationToken cancellationToken = default) => Task.FromResult<AiAnalysisResult?>(null);
    public Task<IReadOnlyList<float>> CreateTextEmbeddingsAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<float>>([]);
    public Task<IReadOnlyList<string>> RerankResultsAsync(string query, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default) => Task.FromResult(candidates);
}

public sealed class OllamaProvider : IAiProvider
{
    public AiProviderInfo GetProviderInfo() => new("ollama", "Ollama", false, true, true);
    public Task<AiProviderStatusSnapshot> GetStatusAsync(AppSettings settings, CancellationToken cancellationToken = default)
        => Task.FromResult(new AiProviderStatusSnapshot("ollama", "Ollama", !string.IsNullOrWhiteSpace(settings.OllamaEndpoint), false, true, true, "Not implemented yet", settings.OllamaVisionModel, false, []));
    public Task<AiAnalysisResult?> DescribeImageAsync(string imagePath, AppSettings settings, CancellationToken cancellationToken = default) => Task.FromResult<AiAnalysisResult?>(null);
    public Task<AiAnalysisResult?> DescribeVideoFrameSetAsync(IReadOnlyList<string> framePaths, AppSettings settings, CancellationToken cancellationToken = default) => Task.FromResult<AiAnalysisResult?>(null);
    public Task<IReadOnlyList<float>> CreateTextEmbeddingsAsync(string text, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<float>>([]);
    public Task<IReadOnlyList<string>> RerankResultsAsync(string query, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default) => Task.FromResult(candidates);
}
