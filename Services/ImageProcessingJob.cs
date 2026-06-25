using ImageVault.Models;
using Shiny.Jobs;

namespace ImageVault.Services;

public class ImageProcessingJob : IJob
{
    private readonly IImageProcessingService _processingService;
    private readonly ImportRequest _importRequest;
    private readonly IFaceDbService? _faceDbService;

    public ImageProcessingJob(
        IImageProcessingService processingService,
        ImportRequest importRequest,
        IFaceDbService? faceDbService = null)
    {
        _processingService = processingService;
        _importRequest = importRequest;
        _faceDbService = faceDbService;
    }

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
            //await AndroidNotification.Show("Import complete", "No new images found");
            return;
        }

        int total = items.Count;
        int processed = 0;

        await AndroidNotification.ShowProgress("Importing images", $"0/{total} processed", 0, total);

        Action<ProcessingItem, int, int>? onProcessed = null;
        Action<string>? onError = null;

        onProcessed = (_, _, _) =>
        {
            var count = Interlocked.Increment(ref processed);
            var faceCount = _processingService.TotalFacesDetected;
            var text = faceCount > 0
                ? $"{count}/{total} — {faceCount} faces"
                : $"{count}/{total} processed";
            var _ = AndroidNotification.ShowProgress("Importing images", text, count, total);
        };

        onError = msg =>
        {
            var _ = AndroidNotification.Show("Import error", msg);
        };

        _processingService.OnItemProcessed += onProcessed;
        _processingService.OnError += onError;

        try
        {
            await _processingService.ProcessBatchAsync(items, ct);
            var faceCount = _processingService.TotalFacesDetected;
            var doneText = faceCount > 0
                ? $"{total} images indexed, {faceCount} faces detected"
                : $"{total} images indexed";
            await AndroidNotification.ShowDone("Import complete", doneText);
        }
        catch (Exception ex)
        {
            await AndroidNotification.Show("Import failed", ex.Message);
        }
        finally
        {
            _processingService.OnItemProcessed -= onProcessed;
            _processingService.OnError -= onError;
        }
    }
}
