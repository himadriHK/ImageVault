using SQLite;

namespace ImageVault.Models;

[Table("images")]
public class ImageEntity
{
    [PrimaryKey, AutoIncrement]
    public long Id { get; set; }

    [Indexed("FilePath", 1, Unique = true)]
    public string FilePath { get; set; } = string.Empty;

    public string FileName { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public long DateAddedUnixMs { get; set; }
    public long DateModifiedUnixMs { get; set; }

    [Ignore]
    public double[]? Embedding { get; set; }

    public byte[]? EmbeddingBlob
    {
        get => Embedding is null ? null : Serialize(Embedding);
        set => Embedding = value is null ? null : Deserialize(value);
    }

    private static byte[] Serialize(double[] array)
    {
        var bytes = new byte[array.Length * 8];
        Buffer.BlockCopy(array, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static double[] Deserialize(byte[] bytes)
    {
        var array = new double[bytes.Length / 8];
        Buffer.BlockCopy(bytes, 0, array, 0, bytes.Length);
        return array;
    }
}
