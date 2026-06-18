using ImageVault.Models;
using SQLite;

namespace ImageVault.Services;

public class VectorDbService : IVectorDbService
{
    private SQLiteAsyncConnection? _db;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, "imagevault.db");

            _db = new SQLiteAsyncConnection(dbPath,
                SQLiteOpenFlags.ReadWrite |
                SQLiteOpenFlags.Create |
                SQLiteOpenFlags.SharedCache);

            await _db.CreateTableAsync<ImageEntity>();

            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private void EnsureInitialized()
    {
        if (!_initialized || _db == null)
            throw new InvalidOperationException("VectorDbService not initialized.");
    }

    public async Task<long> InsertAsync(ImageEntity entity)
    {
        EnsureInitialized();
        await _db!.InsertAsync(entity);
        return entity.Id;
    }

    public async Task InsertBatchAsync(IEnumerable<ImageEntity> entities)
    {
        EnsureInitialized();
        await _db!.InsertAllAsync(entities.ToList());
    }

    public async Task<List<SearchResult>> SearchAsync(double[] queryEmbedding, int limit = 20)
    {
        EnsureInitialized();

        return await Task.Run(async () =>
        {
            var allEntities = await _db!.Table<ImageEntity>().ToListAsync();

            var scored = new List<(ImageEntity Entity, double Score)>();
            var normalizedQuery = NormalizeEmbedding(queryEmbedding);

            foreach (var entity in allEntities)
            {
                if (entity.Embedding == null) continue;

                var score = CosineSimilarity(normalizedQuery, entity.Embedding);
                if (score > 0.08)
                    scored.Add((entity, score));
            }

            return scored
                .OrderByDescending(x => x.Score)
                .Take(limit)
                .Select(x => new SearchResult
                {
                    Entity = x.Entity,
                    Score = x.Score
                })
                .ToList();
        });
    }

    private static double[] NormalizeEmbedding(double[] vec)
    {
        var sumSq = vec.Sum(v => v * v);
        var norm = Math.Sqrt(sumSq);
        if (norm < 1e-12) return vec;
        var result = new double[vec.Length];
        for (int i = 0; i < vec.Length; i++)
            result[i] = vec[i] / norm;
        return result;
    }

    public async Task<List<ImageEntity>> GetAllAsync()
    {
        EnsureInitialized();
        return await _db!.Table<ImageEntity>().OrderByDescending(e => e.DateAddedUnixMs).ToListAsync();
    }

    public async Task<ImageEntity?> GetByIdAsync(long id)
    {
        EnsureInitialized();
        return await _db!.Table<ImageEntity>().FirstOrDefaultAsync(e => e.Id == id);
    }

    public async Task<bool> ExistsByPathAsync(string filePath)
    {
        EnsureInitialized();
        var count = await _db!.Table<ImageEntity>()
            .Where(e => e.FilePath == filePath)
            .CountAsync();
        return count > 0;
    }

    public async Task<int> CountAsync()
    {
        EnsureInitialized();
        return await _db!.Table<ImageEntity>().CountAsync();
    }

    public async Task ClearAsync()
    {
        EnsureInitialized();
        await _db!.DeleteAllAsync<ImageEntity>();
    }

    private static double CosineSimilarity(double[] a, double[] b)
    {
        if (a.Length != b.Length)
            throw new ArgumentException("Vectors must have same length");

        double dotProduct = 0, normA = 0, normB = 0;

        for (int i = 0; i < a.Length; i++)
        {
            dotProduct += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        var denom = Math.Sqrt(normA) * Math.Sqrt(normB);
        return denom == 0 ? 0 : dotProduct / denom;
    }

    public void Dispose()
    {
        _db?.CloseAsync();
    }
}
