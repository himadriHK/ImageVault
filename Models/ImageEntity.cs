namespace ImageVault.Models;

public class ImageEntity
{
    public long Id { get; set; }
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long DateAddedUnixMs { get; set; }
    public long DateModifiedUnixMs { get; set; }
    public double[]? Embedding { get; set; }
}
