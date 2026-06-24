using ImageVault.Models;
using Shiny.Jobs;

namespace ImageVault.Services;

public class ImageProcessingJob : IJob
{
    private readonly IImageProcessingService _processingService;
    private readonly ImportRequest _importRequest;

    public ImageProcessingJob(
        IImageProcessingService processingService,
        ImportRequest importRequest)
    {
        _processingService = processingService;
        _importRequest = importRequest;
    }

    private Task Show(string title, string message) =>
        AndroidNotification.Show(title, message);

    public async Task Run(CancellationToken ct)
    {
        List<ProcessingItem>? items = null;

        if (_importRequest.PickedFiles is { Count: > 0 })
        {
            items = _importRequest.PickedFiles;
            _importRequest.PickedFiles = null;
        }
        else
        {
            var directory = _importRequest.DirectoryPath;
            if (!string.IsNullOrEmpty(directory))
                items = ImageProcessingService.ScanDirectory(directory);
        }

        if (items == null || items.Count == 0)
        {
            await Show("Import complete", "No new images found");
            return;
        }

        await Show("Importing images", $"Found {items.Count} images");

        int total = items.Count;
        int processed = 0;

        _processingService.OnItemProcessed += (_, _, _) =>
        {
            var count = Interlocked.Increment(ref processed);
            if (count % 10 == 0 || count == total)
            {
                _ = Show("Importing images", $"{count}/{total} processed");
            }
        };

        _processingService.OnError += msg =>
        {
            _ = Show("Import error", msg);
        };

        try
        {
            await _processingService.ProcessBatchAsync(items, ct);
            await Show("Import complete", $"{total} images indexed");
        }
        catch (Exception ex)
        {
            await Show("Import failed", ex.Message);
        }
    }
}
