using System.Text.Json;
using Build5Nines.SharpVector;
using Build5Nines.SharpVector.VectorStore;
using ImageVault.Models;

namespace ImageVault.Services;

public class ImageVectorDatabase : MemoryVectorDatabase<string>
{
    private readonly JsonSerializerOptions _jsonOptions = new() { PropertyNameCaseInsensitive = true };
    public int AddVector(string text, string metadata, float[] vector)
    {
        var id = GetNextId();
        VectorStore.Set(id, new VectorTextItem<string, string>(text, metadata, vector));
        return id;
    }

    public async Task<int> AddVectorAsync(string text, string metadata, float[] vector)
    {
        var id = GetNextId();
        await VectorStore.SetAsync(id, new VectorTextItem<string, string>(text, metadata, vector));
        return id;
    }

    public List<(int Id, string Text, float Similarity, float DotProduct, float Logit)> SearchByVector(
        float[] queryVector, SortMetric sortBy = SortMetric.Relevance, double filterThreshold = 0.0, int? limit = null)
    {
        var bag = new System.Collections.Concurrent.ConcurrentBag<(
            int Id, string Text, float CosSim, float DotProd, long DateAdded, long FileSize)>();
        var scoreBag = new System.Collections.Concurrent.ConcurrentBag<float>();

        Parallel.ForEach(VectorStore, kvp =>
        {
            var (cosSim, dotProd) = CosineSimilarity(queryVector, kvp.Value.Vector);
            var meta = TryDeserializeMetadata(kvp.Value.Metadata);
            bag.Add((kvp.Key, kvp.Value.Text, cosSim, dotProd,
                meta?.DateAddedUnixMs ?? 0, meta?.FileSize ?? 0));
            scoreBag.Add(100.0f * cosSim);
        });

        var entries = bag.ToList();
        var scores = scoreBag.ToList();
        var probs = Softmax([.. scores]);

        var results = new List<(int Id, string Text, float Similarity, float DotProduct, float Logit)>();
        for (int i = 0; i < entries.Count; i++)
        {
            if (probs[i] < filterThreshold)
                continue;

            results.Add((
                entries[i].Id,
                entries[i].Text,
                probs[i],
                entries[i].DotProd,
                LogitScore(entries[i].CosSim)
            ));
        }

        results.Sort(sortBy switch
        {
            SortMetric.Recency => (a, b) =>
            {
                var aEntity = GetById(a.Id);
                var bEntity = GetById(b.Id);
                return (bEntity?.DateAddedUnixMs ?? 0).CompareTo(aEntity?.DateAddedUnixMs ?? 0);
            },
            SortMetric.FileSize => (a, b) =>
            {
                var aEntity = GetById(a.Id);
                var bEntity = GetById(b.Id);
                return (bEntity?.FileSize ?? 0).CompareTo(aEntity?.FileSize ?? 0);
            },
            _ => (a, b) => b.Similarity.CompareTo(a.Similarity)
        });

        if (limit.HasValue)
            results = results.Take(limit.Value).ToList();

        return results;
    }

    private Metadata? TryDeserializeMetadata(string? metadata)
    {
        if (string.IsNullOrEmpty(metadata)) return null;
        try { return JsonSerializer.Deserialize<Metadata>(metadata, _jsonOptions); }
        catch { return null; }
    }

    private sealed record Metadata(string FileName, long FileSize, long DateAddedUnixMs);

    public bool ExistsByPath(string filePath)
    {
        return VectorStore.Any(kvp => kvp.Value.Text == filePath);
    }

    public int Count => VectorStore.Count;

    public void ClearAll()
    {
        var ids = VectorStore.GetIds().ToList();
        foreach (var id in ids)
            VectorStore.Delete(id);
    }

    public ImageEntity? GetById(int id)
    {
        if (!VectorStore.ContainsKey(id))
            return null;
        return ToEntity(id, VectorStore.Get(id));
    }

    public List<ImageEntity> GetAll(SortMetric sortBy = SortMetric.Relevance)
    {
        var entities = VectorStore.AsEnumerable()
            .Select(kvp => ToEntity(kvp.Key, kvp.Value))
            .ToList();

        entities.Sort(sortBy switch
        {
            SortMetric.Recency => (a, b) => b.DateAddedUnixMs.CompareTo(a.DateAddedUnixMs),
            SortMetric.FileSize => (a, b) => b.FileSize.CompareTo(a.FileSize),
            _ => (a, b) => a.Id.CompareTo(b.Id)
        });

        return entities;
    }

    private int GetNextId()
    {
        var ids = VectorStore.GetIds().ToArray();
        return ids.Length > 0 ? ids.Max() + 1 : 1;
    }

    internal static string SerializeMetadata(ImageEntity entity)
    {
        return JsonSerializer.Serialize(new
        {
            entity.FileName,
            entity.FileSize,
            entity.Width,
            entity.Height,
            entity.DateAddedUnixMs,
            entity.DateModifiedUnixMs
        });
    }

    internal static ImageEntity ToEntity(int id, IVectorTextItem<string, string> item)
    {
        var entity = new ImageEntity { Id = id, FilePath = item.Text ?? "" };

        if (!string.IsNullOrEmpty(item.Metadata))
        {
            using var doc = JsonDocument.Parse(item.Metadata);
            var root = doc.RootElement;
            entity.FileName = root.TryGetProperty("FileName", out var fn) ? fn.GetString() ?? "" : "";
            entity.FileSize = root.TryGetProperty("FileSize", out var fs) ? fs.GetInt64() : 0;
            entity.Width = root.TryGetProperty("Width", out var w) ? w.GetInt32() : 0;
            entity.Height = root.TryGetProperty("Height", out var h) ? h.GetInt32() : 0;
            entity.DateAddedUnixMs = root.TryGetProperty("DateAddedUnixMs", out var da) ? da.GetInt64() : 0;
            entity.DateModifiedUnixMs = root.TryGetProperty("DateModifiedUnixMs", out var dm) ? dm.GetInt64() : 0;
        }

        entity.Embedding = System.Array.ConvertAll(item.Vector, v => (double)v);
        return entity;
    }

    private static (float cosSim, float dotProd) CosineSimilarity(float[] a, float[] b)
    {
        double dot = 0, normA = 0, normB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }
        return ((float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB))), (float)dot);
    }

    private static float LogitScore(float cosineSimilarity)
    {
        const float Temperature = 14.2857f;
        return cosineSimilarity * Temperature;
    }

    private static float[] Softmax(float[] values)
    {
        int n = values.Length;
        var expVals = new double[n];
        float max = values[0];
        for (int i = 1; i < n; i++)
            if (values[i] > max) max = values[i];

        double sum = 0;
        for (int i = 0; i < n; i++)
        {
            expVals[i] = Math.Exp(values[i] - max);
            sum += expVals[i];
        }

        var result = new float[n];
        Parallel.For(0, n, i => result[i] = (float)(expVals[i] / sum));
        return result;
    }
}
