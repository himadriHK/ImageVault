using ImageVault.Models;

namespace ImageVault.Services;

public interface IVectorDbService : IDisposable
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<long> InsertAsync(ImageEntity entity);
    Task InsertBatchAsync(IEnumerable<ImageEntity> entities);
    Task<List<SearchResult>> SearchAsync(double[] queryEmbedding, int limit = 20);
    Task<List<SearchResult>> SearchAsync(double[] queryEmbedding, SortMetric sortBy, double filterThreshold, int limit = 20);
    Task<List<ImageEntity>> GetAllAsync();
    Task<List<ImageEntity>> GetAllAsync(SortMetric sortBy);
    Task<ImageEntity?> GetByIdAsync(long id);
    Task<bool> ExistsByPathAsync(string filePath);
    Task<int> CountAsync();
    Task ClearAsync();
}
