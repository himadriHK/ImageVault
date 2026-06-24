using Build5Nines.SharpVector;
using ImageVault.Models;

namespace ImageVault.Services;

public class VectorDbService : IVectorDbService
{
    private ImageVectorDatabase? _db;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private const string DbFileName = "imagevault.b59vdb";

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;

            _db = new ImageVectorDatabase();

            var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbFileName);
            if (File.Exists(dbPath))
            {
                await _db.LoadFromFileAsync(dbPath);
            }

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

    private async Task PersistAsync()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbFileName);
        await _db!.SaveToFileAsync(dbPath);
    }

    public async Task<long> InsertAsync(ImageEntity entity)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(entity.Embedding);

        var metadata = ImageVectorDatabase.SerializeMetadata(entity);
        var vector = System.Array.ConvertAll(entity.Embedding, v => (float)v);

        var id = await _db!.AddVectorAsync(entity.FilePath, string.Empty, vector);
        await PersistAsync();
        entity.Id = id;
        return id;
    }

    public async Task InsertBatchAsync(IEnumerable<ImageEntity> entities)
    {
        EnsureInitialized();
        var list = entities.ToList();
        if (list.Count == 0) return;

        foreach (var entity in list)
        {
            if (entity.Embedding == null) continue;

            var metadata = ImageVectorDatabase.SerializeMetadata(entity);
            var vector = System.Array.ConvertAll(entity.Embedding, v => (float)v);
            var id = await _db!.AddVectorAsync(entity.FilePath, metadata, vector);
            entity.Id = id;
        }

        await PersistAsync();
    }

    public async Task<List<SearchResult>> SearchAsync(double[] queryEmbedding, int limit = 20)
    {
        return await SearchAsync(queryEmbedding, SortMetric.Relevance, 0.0, limit);
    }

    public async Task<List<SearchResult>> SearchAsync(double[] queryEmbedding, SortMetric sortBy, double filterThreshold, int limit = 20)
    {
        EnsureInitialized();
        ArgumentNullException.ThrowIfNull(queryEmbedding);

        var queryVector = System.Array.ConvertAll(queryEmbedding, v => (float)v);

        var results = _db!.SearchByVector(queryVector, sortBy, filterThreshold, limit);

        return results
            .Select(r =>
            {
                var entity = _db.GetById(r.Id);
                return new SearchResult
                {
                    Entity = entity ?? new ImageEntity { Id = r.Id, FilePath = r.Text },
                    Score = r.Similarity,
                    DotProduct = r.DotProduct,
                    LogitScore = r.Logit
                };
            })
            .ToList();
    }

    public async Task<List<ImageEntity>> GetAllAsync()
    {
        return await GetAllAsync(SortMetric.Relevance);
    }

    public async Task<List<ImageEntity>> GetAllAsync(SortMetric sortBy)
    {
        EnsureInitialized();
        return _db!.GetAll(sortBy);
    }

    public async Task<ImageEntity?> GetByIdAsync(long id)
    {
        EnsureInitialized();
        return _db!.GetById((int)id);
    }

    public async Task<bool> ExistsByPathAsync(string filePath)
    {
        EnsureInitialized();
        return _db!.ExistsByPath(filePath);
    }

    public async Task<int> CountAsync()
    {
        EnsureInitialized();
        return _db!.Count;
    }

    public async Task ClearAsync()
    {
        EnsureInitialized();
        _db!.ClearAll();

        var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbFileName);
        await _db.SaveToFileAsync(dbPath);
    }

    public void Dispose()
    {
        _db = null;
    }
}
