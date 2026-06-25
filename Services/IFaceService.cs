using ImageVault.Models;

namespace ImageVault.Services;

public record DetectedFace(
    int BoxX, int BoxY, int BoxWidth, int BoxHeight,
    float[] Embedding,
    string Emotion,
    float EmotionConfidence);

public interface IFaceService
{
    bool IsModelLoaded { get; }
    Task LoadModelsAsync(IProgress<double>? progress = null, CancellationToken ct = default);
    Task<List<DetectedFace>> ProcessImageAsync(string imagePath, CancellationToken ct = default);
}
