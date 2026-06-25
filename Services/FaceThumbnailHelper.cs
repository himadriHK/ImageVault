using SkiaSharp;

namespace ImageVault.Services;

public static class FaceThumbnailHelper
{
    public static void SaveThumbnail(string sourcePath, int boxX, int boxY, int boxWidth, int boxHeight, string outputPath)
    {
        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        using var bitmap = SKBitmap.Decode(sourcePath);
        if (bitmap == null) return;

        var cropX = Math.Max(0, boxX);
        var cropY = Math.Max(0, boxY);
        var cropW = Math.Min(boxWidth, bitmap.Width - cropX);
        var cropH = Math.Min(boxHeight, bitmap.Height - cropY);

        if (cropW <= 0 || cropH <= 0) return;

        using var cropped = new SKBitmap(cropW, cropH);
        using var canvas = new SKCanvas(cropped);
        canvas.DrawBitmap(bitmap, new SKRect(cropX, cropY, cropX + cropW, cropY + cropH), new SKRect(0, 0, cropW, cropH));

        using var resized = cropped.Resize(new SKImageInfo(128, 128), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        if (resized == null) return;

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 80);
        using var stream = File.OpenWrite(outputPath);
        data.SaveTo(stream);
    }

    public static string GetThumbnailPath(long faceId)
    {
        var dir = Path.Combine(FileSystem.CacheDirectory, "faces");
        return Path.Combine(dir, $"{faceId}.jpg");
    }
}
