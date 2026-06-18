using ImageVault.Models;

namespace ImageVault.Services;

public interface IImageProcessingService
{
    event Action<ProcessingItem, int, int>? OnItemProcessed;
    event Action<int>? OnBatchProgress;
    event Action<string>? OnError;
    event Action? OnBatchComplete;

    Task ProcessBatchAsync(
        IReadOnlyList<ProcessingItem> items,
        CancellationToken ct = default);
}
