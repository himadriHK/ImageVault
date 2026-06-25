using FaceONNX;
using ImageVault.Models;
using Microsoft.ML.OnnxRuntime;
using SkiaSharp;

namespace ImageVault.Services;

public class FaceService : IFaceService, IDisposable
{
    private FaceDetector? _detector;
    private Face68LandmarksExtractor? _landmarks;
    private FaceEmbedder? _embedder;
    private FaceEmotionClassifier? _emotionClassifier;
    private bool _disposed;

    private static readonly string[] EmotionLabels = ["Neutral", "Happiness", "Surprise", "Sadness", "Anger", "Disgust", "Fear"];

    public bool IsModelLoaded => _detector != null;

    public Task LoadModelsAsync(IProgress<double>? progress = null, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var opts = CreateSessionOptions();
            progress?.Report(0);
            _detector = new FaceDetector(opts);
            progress?.Report(0.25);
            _landmarks = new Face68LandmarksExtractor(opts);
            progress?.Report(0.50);
            _embedder = new FaceEmbedder(opts);
            progress?.Report(0.75);
            _emotionClassifier = new FaceEmotionClassifier(opts);
            progress?.Report(1.0);
        }, ct);
    }

    private static SessionOptions CreateSessionOptions() => OrtOptimizer.CreateOptimizedOptions();

    public Task<List<DetectedFace>> ProcessImageAsync(string imagePath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            EnsureLoaded();

            using var bitmap = SKBitmap.Decode(imagePath);
            if (bitmap == null) return [];

            var image = BitmapToFloatArray(bitmap);
            var results = _detector!.Forward(image);

            var faces = new List<DetectedFace>();

            foreach (var result in results)
            {
                ct.ThrowIfCancellationRequested();

                var rect = result.Rectangle;
                if (rect.IsEmpty || rect.Width <= 0 || rect.Height <= 0) continue;

                float[] embedding;
                try
                {
                    var points = _landmarks!.Forward(image, rect);
                    var angle = points.RotationAngle;
                    var aligned = FaceProcessingExtensions.Align(image, rect, angle);
                    embedding = _embedder!.Forward(aligned);
                }
                catch
                {
                    embedding = new float[512];
                }

                string emotion;
                float confidence;
                try
                {
                    var faceCrop = CropFaceRegion(image, rect);
                    var scores = _emotionClassifier!.Forward(faceCrop);
                    var maxIdx = 0;
                    var maxScore = scores[0];
                    for (int i = 1; i < scores.Length; i++)
                    {
                        if (scores[i] > maxScore)
                        {
                            maxScore = scores[i];
                            maxIdx = i;
                        }
                    }
                    emotion = maxIdx < EmotionLabels.Length ? EmotionLabels[maxIdx] : "Neutral";
                    confidence = maxScore;
                }
                catch
                {
                    emotion = "Neutral";
                    confidence = 0;
                }

                faces.Add(new DetectedFace(
                    rect.X, rect.Y, rect.Width, rect.Height,
                    embedding, emotion, confidence));
            }

            return faces;
        }, ct);
    }

    private static float[][,] CropFaceRegion(float[][,] image, System.Drawing.Rectangle rect)
    {
        var h = image[0].GetLength(0);
        var w = image[0].GetLength(1);
        var x = Math.Max(0, rect.X);
        var y = Math.Max(0, rect.Y);
        var rw = Math.Min(rect.Width, w - x);
        var rh = Math.Min(rect.Height, h - y);

        if (rw <= 0 || rh <= 0) return image;

        var crop = new float[3][,];
        for (int c = 0; c < 3; c++)
        {
            crop[c] = new float[rh, rw];
            var dst = crop[c];
            var src = image[c];
            Parallel.For(0, rh, i =>
            {
                for (int j = 0; j < rw; j++)
                    dst[i, j] = src[y + i, x + j];
            });
        }
        return crop;
    }

    private void EnsureLoaded()
    {
        if (_detector != null) return;
        var opts = CreateSessionOptions();
        _detector = new FaceDetector(opts);
        _landmarks = new Face68LandmarksExtractor(opts);
        _embedder = new FaceEmbedder(opts);
        _emotionClassifier = new FaceEmotionClassifier(opts);
    }

    private static float[][,] BitmapToFloatArray(SKBitmap bitmap)
    {
        // Ensure consistent 4-byte RGBA format (Android decodes some JPEG as Rgb565)
        using var rgba = bitmap.ColorType != SKColorType.Rgba8888
            ? bitmap.Copy(SKColorType.Rgba8888)
            : null;

        var src = rgba ?? bitmap;
        var w = src.Width;
        var h = src.Height;
        var result = new float[3][,];
        for (int c = 0; c < 3; c++)
            result[c] = new float[h, w];

        var rowBytes = src.RowBytes;
        var ptr = src.GetPixels();
        var pixelData = new byte[h * rowBytes];
        System.Runtime.InteropServices.Marshal.Copy(ptr, pixelData, 0, pixelData.Length);

        Parallel.For(0, h, y =>
        {
            var rowStart = y * rowBytes;
            for (int x = 0; x < w; x++)
            {
                var offset = rowStart + x * 4;
                // SkiaSharp stores RGBA; FaceONNX expects BGR order
                result[0][y, x] = pixelData[offset + 2] / 255f; // B
                result[1][y, x] = pixelData[offset + 1] / 255f; // G
                result[2][y, x] = pixelData[offset + 0] / 255f; // R
            }
        });

        return result;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _detector?.Dispose();
        _landmarks?.Dispose();
        _embedder?.Dispose();
        _emotionClassifier?.Dispose();
    }
}
