using FluentAssertions;
using Picazhu.Data;

namespace Picazhu.Tests;

public sealed class PhoneImportPathTests
{
    [Theory]
    [InlineData(@"\\Internal Storage\\DCIM\\100APPLE\\IMG_0001.HEIC", true)]
    [InlineData(@"\\Internal Storage\\DCIM\\100APPLE\\IMG_0002.MOV", true)]
    [InlineData(@"\\Internal Storage\\DCIM\\100APPLE\\._IMG_0001.HEIC", false)]
    [InlineData(@"\\Internal Storage\\Downloads\\IMG_0001.HEIC", false)]
    [InlineData(@"\\Internal Storage\\DCIM\\100APPLE\\notes.txt", false)]
    public void IsSupportedDcimMediaPath_ShouldFilterDcimMediaOnly(string devicePath, bool expected)
    {
        PhoneImportPath.IsSupportedDcimMediaPath(devicePath).Should().Be(expected);
    }

    [Theory]
    [InlineData(@"\\Internal Storage\\202605_a\\IMG_7170.MOV", true)]
    [InlineData(@"\\Internal Storage\\202604_a\\IMG_7121.PNG", true)]
    [InlineData(@"\\Internal Storage\\202603_a\\QSUQO9148.AAE", false)]
    [InlineData(@"\\Internal Storage\\202605_a\\._IMG_7170.MOV", false)]
    public void IsSupportedPhoneMediaPath_ShouldAllowIPhoneCameraFolderMedia(string devicePath, bool expected)
    {
        PhoneImportPath.IsSupportedPhoneMediaPath(devicePath).Should().Be(expected);
    }

    [Fact]
    public void CreateDestinationRelativePath_ShouldPreserveDcimFolderLayout()
    {
        var relative = PhoneImportPath.CreateDestinationRelativePath(
            @"\\Internal Storage\\DCIM\\100APPLE\\IMG_0001.HEIC",
            preserveDcimFolders: true);

        relative.Should().Be(Path.Combine("DCIM", "100APPLE", "IMG_0001.HEIC"));
    }

    [Fact]
    public void CreateDestinationRelativePath_ShouldPreserveIPhoneCameraFolderLayout()
    {
        var relative = PhoneImportPath.CreateDestinationRelativePath(
            @"\\Internal Storage\\202605_a\\IMG_7170.MOV",
            preserveDcimFolders: true);

        relative.Should().Be(Path.Combine("202605_a", "IMG_7170.MOV"));
    }

    [Fact]
    public void CreateDestinationRelativePath_ShouldSanitizeUnsafeSegments()
    {
        var relative = PhoneImportPath.CreateDestinationRelativePath(
            @"\\Internal Storage\\DCIM\\100APPLE\\IMG:0001.HEIC",
            preserveDcimFolders: true);

        relative.Should().Be(Path.Combine("DCIM", "100APPLE", "IMG_0001.HEIC"));
    }

    [Fact]
    public async Task IsExactDuplicate_ShouldRequireSameSizeAndCloseModifiedTime()
    {
        var root = Path.Combine(Path.GetTempPath(), "picazhu-phone-import-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "IMG_0001.HEIC");
        await File.WriteAllTextAsync(path, "same-source");
        var modifiedUtc = new DateTimeOffset(2026, 4, 15, 18, 0, 0, TimeSpan.Zero);
        File.SetLastWriteTimeUtc(path, modifiedUtc.UtcDateTime);

        PhoneImportPath.IsExactDuplicate(path, new FileInfo(path).Length, modifiedUtc).Should().BeTrue();
        PhoneImportPath.IsExactDuplicate(path, new FileInfo(path).Length + 1, modifiedUtc).Should().BeFalse();
        PhoneImportPath.IsExactDuplicate(path, new FileInfo(path).Length, modifiedUtc.AddMinutes(5)).Should().BeFalse();
    }
}
