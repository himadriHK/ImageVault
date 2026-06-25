using System.Text.RegularExpressions;
using ImageVault.Models;

namespace ImageVault.Services;

public class FaceDbService : IFaceDbService
{
    private FaceVectorDatabase? _db;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private const string DbFileName = "imagevault_faces.b59vdb";

    private static readonly Dictionary<string, string> EmotionKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["happy"] = "Happiness", ["happiness"] = "Happiness", ["joy"] = "Happiness",
        ["laugh"] = "Happiness", ["laughing"] = "Happiness", ["smile"] = "Happiness",
        ["smiling"] = "Happiness", ["cheerful"] = "Happiness", ["glad"] = "Happiness",
        ["sad"] = "Sadness", ["sadness"] = "Sadness", ["cry"] = "Sadness",
        ["crying"] = "Sadness", ["unhappy"] = "Sadness", ["depressed"] = "Sadness",
        ["angry"] = "Anger", ["anger"] = "Anger", ["mad"] = "Anger",
        ["furious"] = "Anger", ["rage"] = "Anger",
        ["surprise"] = "Surprise", ["surprised"] = "Surprise", ["shock"] = "Surprise",
        ["amazed"] = "Surprise",
        ["fear"] = "Fear", ["fearful"] = "Fear", ["scared"] = "Fear",
        ["afraid"] = "Fear", ["terrified"] = "Fear",
        ["disgust"] = "Disgust", ["disgusted"] = "Disgust",
        ["neutral"] = "Neutral"
    };

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        await _initLock.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            _db = new FaceVectorDatabase();
            var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbFileName);
            if (File.Exists(dbPath))
                await _db.LoadFromFileAsync(dbPath);
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
            throw new InvalidOperationException("FaceDbService not initialized.");
    }

    private async Task PersistAsync()
    {
        var dbPath = Path.Combine(FileSystem.AppDataDirectory, DbFileName);
        await _db!.SaveToFileAsync(dbPath);
    }

    public async Task AddFaceAsync(FaceEntity entity)
    {
        EnsureInitialized();
        var id = await _db!.AddFaceAsync(entity);
        entity.Id = id;
        await PersistAsync();
    }

    public Task<List<FaceEntity>> GetAllAsync()
    {
        EnsureInitialized();
        return Task.FromResult(_db!.GetAll());
    }

    public async Task UpdateNameAsync(long id, string name)
    {
        EnsureInitialized();
        _db!.UpdateName((int)id, name);
        await PersistAsync();
    }

    public Task<List<FaceEntity>> SearchAsync(string query)
    {
        EnsureInitialized();
        if (string.IsNullOrWhiteSpace(query))
            return GetAllAsync();

        var knownNames = GetAllNamesSync();
        var queryLower = query.ToLowerInvariant();
        var tokens = Regex.Split(queryLower, @"[\s,\.!?;:]+")
            .Where(t => t.Length > 0)
            .ToHashSet();

        var matchedNames = knownNames
            .Where(n => n.Length > 0 && queryLower.Contains(n.ToLowerInvariant()))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var emotion = tokens
            .Select(t => EmotionKeywords.TryGetValue(t, out var e) ? e : null)
            .FirstOrDefault(e => e != null);

        var allFaces = _db!.GetAll();
        var results = allFaces.AsEnumerable();

        if (matchedNames.Count > 0)
            results = results.Where(f => matchedNames.Contains(f.Name));

        if (emotion != null)
            results = results.Where(f => string.Equals(f.Emotion, emotion, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(results
            .OrderByDescending(f => f.DateAddedUnixMs)
            .ToList());
    }

    public Task<List<FaceEntity>> GetAllByNameAsync(string name)
    {
        EnsureInitialized();
        var results = _db!.GetAll()
            .Where(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.DateAddedUnixMs)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<List<FaceEntity>> GetAllByEmotionAsync(string emotion)
    {
        EnsureInitialized();
        var results = _db!.GetAll()
            .Where(f => string.Equals(f.Emotion, emotion, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f.DateAddedUnixMs)
            .ToList();
        return Task.FromResult(results);
    }

    public Task<HashSet<string>> GetAllNamesAsync()
    {
        EnsureInitialized();
        return Task.FromResult(GetAllNamesSync());
    }

    private HashSet<string> GetAllNamesSync()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var face in _db!.GetAll())
        {
            if (!string.IsNullOrEmpty(face.Name))
                names.Add(face.Name);
        }
        return names;
    }

    public async Task ClearAsync()
    {
        EnsureInitialized();
        _db!.ClearAll();
        await PersistAsync();
    }

    public Task<int> CountAsync()
    {
        EnsureInitialized();
        return Task.FromResult(_db!.Count);
    }

    public Task<FaceEntity?> GetByIdAsync(long id)
    {
        EnsureInitialized();
        return Task.FromResult(_db!.GetById((int)id));
    }
}
