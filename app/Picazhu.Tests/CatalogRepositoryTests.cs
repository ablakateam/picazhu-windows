using FluentAssertions;
using Picazhu.Core;
using Picazhu.Data;

namespace Picazhu.Tests;

public sealed class CatalogRepositoryTests
{
    [Fact]
    public async Task Repository_ShouldInitializeAndPersistRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "picazhu-tests-db", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var paths = new TestPaths(root);
        var repository = new CatalogRepository(paths);

        await repository.InitializeAsync();
        var added = await repository.AddWatchedRootAsync(root, includeSubfolders: false);
        var roots = await repository.GetWatchedRootsAsync();

        roots.Should().ContainSingle(item => item.Path == added.Path);
        roots.Should().ContainSingle(item => item.Path == added.Path && item.IncludeSubfolders == false);
    }

    [Fact]
    public async Task QueryMedia_ShouldMatchFolderNamesAndRelativePaths()
    {
        var root = Path.Combine(Path.GetTempPath(), "picazhu-tests-db", Guid.NewGuid().ToString("N"));
        var nestedFolder = Path.Combine(root, "ClientA", "Finals");
        Directory.CreateDirectory(nestedFolder);

        var paths = new TestPaths(root);
        var repository = new CatalogRepository(paths);
        await repository.InitializeAsync();

        var watchedRoot = await repository.AddWatchedRootAsync(root, includeSubfolders: true);
        var rootFolderId = "root-folder";
        var nestedFolderId = "nested-folder";
        var mediaId = "media-1";
        var now = DateTimeOffset.UtcNow;

        await repository.UpsertFolderAsync(new FolderEntry(
            rootFolderId,
            watchedRoot.Id,
            null,
            root,
            Path.GetFileName(root),
            ".",
            0,
            1,
            now,
            now));

        await repository.UpsertFolderAsync(new FolderEntry(
            nestedFolderId,
            watchedRoot.Id,
            rootFolderId,
            nestedFolder,
            "Finals",
            Path.Combine("ClientA", "Finals"),
            1,
            0,
            now,
            now));

        await repository.UpsertMediaItemAsync(new MediaItem(
            mediaId,
            nestedFolderId,
            Path.Combine(nestedFolder, "hero-shot.jpg"),
            "hero-shot.jpg",
            ".jpg",
            MediaKind.Image,
            "image/jpeg",
            1024,
            now,
            now,
            now,
            1200,
            800,
            null,
            null,
            false,
            true,
            MetadataState.Done,
            ThumbState.Done,
            null,
            "hash",
            null,
            null,
            now,
            now));

        var byFolderName = await repository.QueryMediaAsync(new MediaQuery(root, true, "Finals", false, false, false, false, false, false, SortMode.Name, 100));
        var byRelativePath = await repository.QueryMediaAsync(new MediaQuery(root, true, "ClientA", false, false, false, false, false, false, SortMode.Name, 100));

        byFolderName.Should().ContainSingle(item => item.Id == mediaId);
        byRelativePath.Should().ContainSingle(item => item.Id == mediaId);
    }

    [Fact]
    public async Task QueryMedia_WithSubfolders_ShouldNotIncludeSiblingPrefixPaths()
    {
        var basePath = Path.Combine(Path.GetTempPath(), "picazhu-tests-db", Guid.NewGuid().ToString("N"));
        var root = Path.Combine(basePath, "Photos");
        var sibling = Path.Combine(basePath, "Photos2");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sibling);

        var paths = new TestPaths(basePath);
        var repository = new CatalogRepository(paths);
        await repository.InitializeAsync();

        var watchedRoot = await repository.AddWatchedRootAsync(root, includeSubfolders: true);
        var watchedSibling = await repository.AddWatchedRootAsync(sibling, includeSubfolders: true);
        var now = DateTimeOffset.UtcNow;

        await repository.UpsertFolderAsync(new FolderEntry("photos-folder", watchedRoot.Id, null, root, "Photos", ".", 0, 1, now, now));
        await repository.UpsertFolderAsync(new FolderEntry("photos2-folder", watchedSibling.Id, null, sibling, "Photos2", ".", 0, 1, now, now));

        await repository.UpsertMediaItemAsync(new MediaItem(
            "media-inside-root",
            "photos-folder",
            Path.Combine(root, "inside.jpg"),
            "inside.jpg",
            ".jpg",
            MediaKind.Image,
            "image/jpeg",
            1024,
            now,
            now,
            now,
            1200,
            800,
            null,
            null,
            false,
            true,
            MetadataState.Done,
            ThumbState.Done,
            null,
            "hash-root",
            null,
            null,
            now,
            now));

        await repository.UpsertMediaItemAsync(new MediaItem(
            "media-sibling-prefix",
            "photos2-folder",
            Path.Combine(sibling, "outside.jpg"),
            "outside.jpg",
            ".jpg",
            MediaKind.Image,
            "image/jpeg",
            1024,
            now,
            now,
            now,
            1200,
            800,
            null,
            null,
            false,
            true,
            MetadataState.Done,
            ThumbState.Done,
            null,
            "hash-sibling",
            null,
            null,
            now,
            now));

        var results = await repository.QueryMediaAsync(new MediaQuery(root, true, null, false, false, false, false, false, false, SortMode.Name, 100));

        results.Should().ContainSingle(item => item.Id == "media-inside-root");
    }

    [Theory]
    [InlineData("._IMG_0001.HEIC", true)]
    [InlineData("._video.mp4", true)]
    [InlineData("IMG_0001.HEIC", false)]
    [InlineData("clip.mp4", false)]
    public void MediaSupport_ShouldIgnoreAppleDoubleSidecars(string fileName, bool expectedIgnored)
    {
        MediaSupport.ShouldIgnoreFile(fileName).Should().Be(expectedIgnored);
    }

    [Fact]
    public async Task ExportService_ShouldCopyAndAutoRenameConflicts()
    {
        var root = Path.Combine(Path.GetTempPath(), "picazhu-export-tests", Guid.NewGuid().ToString("N"));
        var sourceFolder = Path.Combine(root, "source");
        var destinationFolder = Path.Combine(root, "dest");
        Directory.CreateDirectory(sourceFolder);
        Directory.CreateDirectory(destinationFolder);

        var sourcePath = Path.Combine(sourceFolder, "print.jpg");
        await File.WriteAllTextAsync(sourcePath, "source-a");
        await File.WriteAllTextAsync(Path.Combine(destinationFolder, "print.jpg"), "existing");

        var service = new ExportService();
        var result = await service.ExportOriginalsAsync(new ExportRequest(
            [new ExportSourceItem("1", sourcePath, "print.jpg", new FileInfo(sourcePath).Length)],
            destinationFolder));

        result.CopiedCount.Should().Be(1);
        result.RenamedCount.Should().Be(1);
        result.FailedCount.Should().Be(0);
        File.Exists(Path.Combine(destinationFolder, "print.jpg")).Should().BeTrue();
        File.Exists(Path.Combine(destinationFolder, "print (2).jpg")).Should().BeTrue();
    }

    [Fact]
    public async Task ExportService_ShouldReportMissingSourceFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "picazhu-export-tests", Guid.NewGuid().ToString("N"));
        var destinationFolder = Path.Combine(root, "dest");
        Directory.CreateDirectory(destinationFolder);

        var service = new ExportService();
        var result = await service.ExportOriginalsAsync(new ExportRequest(
            [new ExportSourceItem("1", Path.Combine(root, "missing.jpg"), "missing.jpg", 0)],
            destinationFolder));

        result.CopiedCount.Should().Be(0);
        result.FailedCount.Should().Be(1);
        result.Errors.Should().ContainSingle();
    }

    [Fact]
    public async Task Repository_ShouldRoundTripMediaMetadata()
    {
        var root = Path.Combine(Path.GetTempPath(), "picazhu-tests-db", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new TestPaths(root);
        var repository = new CatalogRepository(paths);
        await repository.InitializeAsync();

        var takenUtc = new DateTimeOffset(2026, 4, 13, 18, 45, 0, TimeSpan.Zero);
        var metadata = new MediaMetadata(
            "media-123",
            "Canon",
            "R5",
            takenUtc,
            39.7392,
            -104.9903,
            "hvc1",
            52000,
            29.97,
            "sRGB",
            null);

        await repository.UpsertMediaMetadataAsync(metadata);
        var loaded = await repository.GetMediaMetadataAsync("media-123");

        loaded.Should().NotBeNull();
        loaded!.CameraMake.Should().Be("Canon");
        loaded.CameraModel.Should().Be("R5");
        loaded.DateTakenUtc.Should().Be(takenUtc);
        loaded.GpsLat.Should().BeApproximately(39.7392, 0.000001);
        loaded.GpsLon.Should().BeApproximately(-104.9903, 0.000001);
        loaded.Codec.Should().Be("hvc1");
        loaded.Bitrate.Should().Be(52000);
        loaded.FrameRate.Should().BeApproximately(29.97, 0.001);
        loaded.ColorProfile.Should().Be("sRGB");
    }

    [Fact]
    public async Task Repository_ShouldCreateAndUpdateAiAnalysisRecords()
    {
        var root = Path.Combine(Path.GetTempPath(), "picazhu-tests-db", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new TestPaths(root);
        var repository = new CatalogRepository(paths);
        await repository.InitializeAsync();

        await repository.EnsureAiAnalysisRecordAsync("media-ai-1");
        var seeded = await repository.GetAiAnalysisAsync("media-ai-1");

        seeded.Should().NotBeNull();
        seeded!.AnalysisState.Should().Be(AiAnalysisState.Pending);

        var updatedUtc = DateTimeOffset.UtcNow;
        await repository.UpdateAiAnalysisAsync(new AiAnalysisRecord(
            "media-ai-1",
            AiAnalysisState.Done,
            "openai",
            "gpt-vision",
            "A dog on the beach",
            "[\"dog\",\"beach\"]",
            "[\"dog\"]",
            "Walmart",
            "dog beach Walmart",
            null,
            DateTimeOffset.UtcNow.AddMinutes(-2),
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            updatedUtc));

        var loaded = await repository.GetAiAnalysisAsync("media-ai-1");
        loaded.Should().NotBeNull();
        loaded!.AnalysisState.Should().Be(AiAnalysisState.Done);
        loaded.ProviderId.Should().Be("openai");
        loaded.Caption.Should().Be("A dog on the beach");
        loaded.OcrText.Should().Be("Walmart");
        loaded.CombinedText.Should().Be("dog beach Walmart");
    }

    [Fact]
    public async Task QueryMedia_ShouldMatchAiOcrText()
    {
        var root = Path.Combine(Path.GetTempPath(), "picazhu-tests-db", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        var paths = new TestPaths(root);
        var repository = new CatalogRepository(paths);
        await repository.InitializeAsync();

        var watchedRoot = await repository.AddWatchedRootAsync(root, includeSubfolders: true);
        var now = DateTimeOffset.UtcNow;
        var folderId = "folder-ai-search";
        var mediaId = "media-ai-search";

        await repository.UpsertFolderAsync(new FolderEntry(
            folderId,
            watchedRoot.Id,
            null,
            root,
            Path.GetFileName(root),
            ".",
            1,
            0,
            now,
            now));

        await repository.UpsertMediaItemAsync(new MediaItem(
            mediaId,
            folderId,
            Path.Combine(root, "storefront.jpg"),
            "storefront.jpg",
            ".jpg",
            MediaKind.Image,
            "image/jpeg",
            1024,
            now,
            now,
            now,
            1200,
            800,
            null,
            null,
            false,
            true,
            MetadataState.Done,
            ThumbState.Done,
            null,
            "hash",
            null,
            null,
            now,
            now));

        await repository.UpdateAiAnalysisAsync(new AiAnalysisRecord(
            mediaId,
            AiAnalysisState.Done,
            "windows-ocr",
            "Windows OCR",
            null,
            null,
            null,
            "Walmart Pharmacy",
            "Walmart Pharmacy",
            null,
            now,
            now,
            now,
            now));

        var results = await repository.QueryMediaAsync(new MediaQuery(root, true, "Walmart", false, false, false, false, false, false, SortMode.Name, 100));
        results.Should().ContainSingle(item => item.Id == mediaId);
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
