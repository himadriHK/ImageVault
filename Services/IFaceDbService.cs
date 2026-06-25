using ImageVault.Models;

namespace ImageVault.Services;

public interface IFaceDbService
{
    Task InitializeAsync(CancellationToken ct = default);
    Task AddFaceAsync(FaceEntity entity);
    Task<List<FaceEntity>> GetAllAsync();
    Task UpdateNameAsync(long id, string name);
    Task<List<FaceEntity>> SearchAsync(string query);
    Task<List<FaceEntity>> GetAllByNameAsync(string name);
    Task<List<FaceEntity>> GetAllByEmotionAsync(string emotion);
    Task<HashSet<string>> GetAllNamesAsync();
    Task ClearAsync();
    Task<int> CountAsync();
    Task<FaceEntity?> GetByIdAsync(long id);
}
