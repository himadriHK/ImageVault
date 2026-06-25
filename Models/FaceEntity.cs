namespace ImageVault.Models;

public class FaceEntity
{
    public long Id { get; set; }
    public string Name { get; set; } = "Unknown";
    public string Emotion { get; set; } = "Neutral";
    public float EmotionConfidence { get; set; }
    public string ImageFilePath { get; set; } = string.Empty;
    public int BoxX { get; set; }
    public int BoxY { get; set; }
    public int BoxWidth { get; set; }
    public int BoxHeight { get; set; }
    public float[]? Embedding { get; set; }
    public long DateAddedUnixMs { get; set; }
}
