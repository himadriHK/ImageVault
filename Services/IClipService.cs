namespace ImageVault.Services;

public interface IClipService
{
    Task<double[]> GetImageEmbeddingAsync(string imagePath, CancellationToken ct = default);
    Task<double[]> GetTextEmbeddingAsync(string text, CancellationToken ct = default);
    bool IsModelLoaded { get; }
    Task LoadModelsAsync(CancellationToken ct = default);
}
