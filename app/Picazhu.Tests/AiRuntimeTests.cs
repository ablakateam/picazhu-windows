using Picazhu.AI;
using Picazhu.Core;
using System.Reflection;

namespace Picazhu.Tests;

public sealed class AiRuntimeTests
{
    [Fact]
    public void AiFeatureGate_ShouldRaiseChangeOnlyWhenStateChanges()
    {
        var gate = new AiFeatureGate();
        var changes = new List<bool>();
        gate.Changed += changes.Add;

        gate.SetEnabled(true);
        gate.SetEnabled(true);
        gate.SetEnabled(false);

        Assert.Equal([true, false], changes);
        Assert.False(gate.IsEnabled);
    }

    [Fact]
    public async Task ProviderStatusService_ShouldReportUnconfiguredProviders()
    {
        var service = new AiProviderStatusService([
            new FakeProvider("lmstudio", "LM Studio", "Not configured"),
            new FakeProvider("ollama", "Ollama", "Not configured"),
            new FakeProvider("openai", "OpenAI", "Not configured")
        ]);

        var statuses = await service.GetStatusesAsync(new AppSettings
        {
            LmStudioEndpoint = null,
            OllamaEndpoint = null,
            OpenAiApiKeyPlaceholder = null
        });

        Assert.Contains(statuses, item => item.ProviderId == "lmstudio" && !item.IsConfigured);
        Assert.Contains(statuses, item => item.ProviderId == "ollama" && !item.IsConfigured);
        Assert.Contains(statuses, item => item.ProviderId == "openai" && !item.IsConfigured);
    }

    [Theory]
    [InlineData("qwen2.5-vl-7b-instruct", true)]
    [InlineData("llava-1.6", true)]
    [InlineData("llama3.2-vision", true)]
    [InlineData("gemma3", true)]
    [InlineData("moondream", true)]
    [InlineData("qwen2.5-7b-instruct", false)]
    [InlineData("mistral-7b-instruct", false)]
    public void LmStudioProvider_VisionModelHeuristic_ShouldMatchExpectedValues(string modelId, bool expected)
    {
        Assert.Equal(expected, LmStudioProvider.IsVisionModelId(modelId));
    }

    [Fact]
    public void LmStudioProvider_ParseAnalysisResponse_ShouldHandleFencedJson()
    {
        var responseBody = """
        {
          "choices": [
            {
              "message": {
                "content": "```json\n{\"caption\":\"storefront at night\",\"tags\":[\"night\",\"storefront\"],\"objects\":[\"building\",\"sign\"],\"logos\":[\"walmart\"]}\n```"
              }
            }
          ]
        }
        """;

        var result = InvokeParseAnalysisResponse(responseBody);

        Assert.NotNull(result);
        Assert.Equal("storefront at night", result!.Caption);
        Assert.Contains("night", result.Tags);
        Assert.Contains("walmart", result.Tags);
        Assert.Contains("sign", result.Objects);
    }

    [Fact]
    public void LmStudioProvider_ParseAnalysisResponse_ShouldFallbackToCaptionWhenModelReturnsPlainText()
    {
        var responseBody = """
        {
          "choices": [
            {
              "message": {
                "content": "A beach scene with umbrellas and bright blue water."
              }
            }
          ]
        }
        """;

        var result = InvokeParseAnalysisResponse(responseBody);

        Assert.NotNull(result);
        Assert.Equal("A beach scene with umbrellas and bright blue water.", result!.Caption);
        Assert.Empty(result.Tags);
        Assert.Empty(result.Objects);
    }

    private static AiAnalysisResult? InvokeParseAnalysisResponse(string responseBody)
    {
        var method = typeof(LmStudioProvider).GetMethod("ParseAnalysisResponse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        return (AiAnalysisResult?)method!.Invoke(null, [responseBody]);
    }

    private sealed class FakeProvider(string providerId, string displayName, string summary) : IAiProvider
    {
        public AiProviderInfo GetProviderInfo() => new(providerId, displayName, false, true, true);

        public Task<AiProviderStatusSnapshot> GetStatusAsync(AppSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult(new AiProviderStatusSnapshot(providerId, displayName, false, false, true, true, summary));

        public Task<AiAnalysisResult?> DescribeImageAsync(string imagePath, AppSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult<AiAnalysisResult?>(null);

        public Task<AiAnalysisResult?> DescribeVideoFrameSetAsync(IReadOnlyList<string> framePaths, AppSettings settings, CancellationToken cancellationToken = default)
            => Task.FromResult<AiAnalysisResult?>(null);

        public Task<IReadOnlyList<float>> CreateTextEmbeddingsAsync(string text, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<float>>([]);

        public Task<IReadOnlyList<string>> RerankResultsAsync(string query, IReadOnlyList<string> candidates, CancellationToken cancellationToken = default)
            => Task.FromResult(candidates);
    }
}
