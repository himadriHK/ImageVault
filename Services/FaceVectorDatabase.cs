using System.Text.Json;
using Build5Nines.SharpVector;
using Build5Nines.SharpVector.VectorStore;
using ImageVault.Models;

namespace ImageVault.Services;

public class FaceVectorDatabase : MemoryVectorDatabase<string>
{
    public Task SaveToFileAsync(string filePath) => ((IVectorDatabase<int, string>)this).SaveToFileAsync(filePath);
    public Task LoadFromFileAsync(string filePath) => ((IVectorDatabase<int, string>)this).LoadFromFileAsync(filePath);
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public async Task<int> AddFaceAsync(FaceEntity entity)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            entity.Emotion,
            entity.EmotionConfidence,
            entity.ImageFilePath,
            entity.BoxX,
            entity.BoxY,
            entity.BoxWidth,
            entity.BoxHeight,
            entity.DateAddedUnixMs
        }, JsonOpts);

        var vector = entity.Embedding ?? [];
        var id = GetNextId();
        await VectorStore.SetAsync(id, new VectorTextItem<string, string>(entity.Name, metadata, vector));
        return id;
    }

    public List<FaceEntity> GetAll()
    {
        return VectorStore.AsEnumerable()
            .Select(kvp => ToEntity(kvp.Key, kvp.Value))
            .OrderByDescending(e => e.DateAddedUnixMs)
            .ToList();
    }

    public FaceEntity? GetById(int id)
    {
        if (!VectorStore.ContainsKey(id)) return null;
        return ToEntity(id, VectorStore.Get(id));
    }

    public void UpdateName(int id, string name)
    {
        if (!VectorStore.ContainsKey(id)) return;
        var item = VectorStore.Get(id);
        var entity = ToEntity(id, item);
        entity.Name = name;
        VectorStore.Delete(id);

        var metadata = JsonSerializer.Serialize(new
        {
            entity.Emotion,
            entity.EmotionConfidence,
            entity.ImageFilePath,
            entity.BoxX,
            entity.BoxY,
            entity.BoxWidth,
            entity.BoxHeight,
            entity.DateAddedUnixMs
        }, JsonOpts);

        VectorStore.Set(id, new VectorTextItem<string, string>(name, metadata, entity.Embedding ?? []));
    }

    public bool ExistsById(int id) => VectorStore.ContainsKey(id);

    public int Count => VectorStore.Count;

    public void ClearAll()
    {
        foreach (var id in VectorStore.GetIds())
            VectorStore.Delete(id);
    }

    private int GetNextId()
    {
        var ids = VectorStore.GetIds().ToArray();
        return ids.Length > 0 ? ids.Max() + 1 : 1;
    }

    private static FaceEntity ToEntity(int id, IVectorTextItem<string, string> item)
    {
        var entity = new FaceEntity { Id = id };
        if (!string.IsNullOrEmpty(item.Text))
            entity.Name = item.Text;
        if (!string.IsNullOrEmpty(item.Metadata))
        {
            try
            {
                var doc = JsonDocument.Parse(item.Metadata);
                var root = doc.RootElement;
                entity.Emotion = root.TryGetProperty("Emotion", out var e) ? e.GetString() ?? "Neutral" : "Neutral";
                entity.EmotionConfidence = root.TryGetProperty("EmotionConfidence", out var ec) ? ec.GetSingle() : 0;
                entity.ImageFilePath = root.TryGetProperty("ImageFilePath", out var fp) ? fp.GetString() ?? "" : "";
                entity.BoxX = root.TryGetProperty("BoxX", out var bx) ? bx.GetInt32() : 0;
                entity.BoxY = root.TryGetProperty("BoxY", out var by) ? by.GetInt32() : 0;
                entity.BoxWidth = root.TryGetProperty("BoxWidth", out var bw) ? bw.GetInt32() : 0;
                entity.BoxHeight = root.TryGetProperty("BoxHeight", out var bh) ? bh.GetInt32() : 0;
                entity.DateAddedUnixMs = root.TryGetProperty("DateAddedUnixMs", out var da) ? da.GetInt64() : 0;
            }
            catch { }
        }
        entity.Embedding = item.Vector;
        return entity;
    }
}
