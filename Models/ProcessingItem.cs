namespace ImageVault.Models;

public class ProcessingItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long DateModifiedUnixMs { get; set; }
}
