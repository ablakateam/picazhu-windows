using FluentAssertions;
using Picazhu.Cache;
using Picazhu.Core;

namespace Picazhu.Tests;

public sealed class SearchAndCacheTests
{
    [Fact]
    public void SearchParser_ShouldSplitTermsPhraseAndFolder()
    {
        var query = SearchQueryParser.Parse("wedding -raw \"march trip\" folder:client-a");

        query.IncludeTerms.Should().Contain("wedding");
        query.ExcludeTerms.Should().Contain("raw");
        query.ExactPhrase.Should().Be("march trip");
        query.FolderTerm.Should().Be("client-a");
    }

    [Fact]
    public async Task ThumbnailCache_ShouldProduceStablePath()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "picazhu-tests", Guid.NewGuid().ToString("N"));
        var appPaths = new TestPaths(tempRoot);
        var cache = new ThumbnailCacheService(appPaths);

        var key = cache.CreateCacheKey(@"C:\media\image.jpg", DateTimeOffset.Parse("2024-01-01T00:00:00Z"), 1234, "v1");
        var relative = cache.GetRelativeThumbnailPath(key);
        var absolute = cache.GetAbsoluteThumbnailPath(key);

        relative.Should().EndWith(".jpg");
        absolute.Should().Contain(key);
        (await cache.GetCacheSizeBytesAsync()).Should().Be(0);
    }

    private sealed class TestPaths(string root) : IAppPaths
    {
        public string RootPath => root;
        public string DatabasePath => Path.Combine(root, "db", "catalog.db");
        public string ThumbsPath => Path.Combine(root, "thumbs");
        public string LogsPath => Path.Combine(root, "logs");
        public string TempPath => Path.Combine(root, "temp");
        public string AiPath => Path.Combine(root, "ai");
    }
}
