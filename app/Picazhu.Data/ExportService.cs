using Picazhu.Core;

namespace Picazhu.Data;

public sealed class ExportService : IExportService
{
    public async Task<ExportResult> ExportOriginalsAsync(ExportRequest request, IProgress<ExportProgressSnapshot>? progress = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.DestinationFolder))
        {
            throw new ArgumentException("Destination folder is required.", nameof(request));
        }

        Directory.CreateDirectory(request.DestinationFolder);

        var copiedCount = 0;
        var renamedCount = 0;
        var failedCount = 0;
        var errors = new List<string>();
        var totalItems = request.Items.Count;

        for (var index = 0; index < totalItems; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = request.Items[index];
            progress?.Report(new ExportProgressSnapshot(true, totalItems, index, copiedCount, renamedCount, failedCount, item.FileName));

            try
            {
                if (!File.Exists(item.FullPath))
                {
                    failedCount++;
                    errors.Add($"{item.FileName}: source file was not found.");
                    continue;
                }

                var destinationPath = GetUniqueDestinationPath(request.DestinationFolder, item.FileName, out var renamed);
                await using var sourceStream = File.Open(item.FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                await using var destinationStream = File.Create(destinationPath);
                await sourceStream.CopyToAsync(destinationStream, cancellationToken);
                copiedCount++;
                if (renamed)
                {
                    renamedCount++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                failedCount++;
                errors.Add($"{item.FileName}: {ex.Message}");
            }
            progress?.Report(new ExportProgressSnapshot(true, totalItems, index + 1, copiedCount, renamedCount, failedCount, item.FileName));
        }

        progress?.Report(new ExportProgressSnapshot(false, totalItems, totalItems, copiedCount, renamedCount, failedCount, null));
        return new ExportResult(totalItems, copiedCount, renamedCount, failedCount, errors);
    }

    internal static string GetUniqueDestinationPath(string destinationFolder, string fileName, out bool renamed)
    {
        var candidatePath = Path.Combine(destinationFolder, fileName);
        if (!File.Exists(candidatePath))
        {
            renamed = false;
            return candidatePath;
        }

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        var suffix = 2;

        while (true)
        {
            candidatePath = Path.Combine(destinationFolder, $"{baseName} ({suffix}){extension}");
            if (!File.Exists(candidatePath))
            {
                renamed = true;
                return candidatePath;
            }

            suffix++;
        }
    }
}
