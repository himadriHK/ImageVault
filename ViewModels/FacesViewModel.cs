using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageVault.Models;
using ImageVault.Services;
using SkiaSharp;

namespace ImageVault.ViewModels;

public partial class FaceDisplayItem : ObservableObject
{
    private readonly FaceEntity _entity;

    public FaceEntity Entity => _entity;
    public long Id => _entity.Id;
    public string Name => _entity.Name;
    public string Emotion => _entity.Emotion;
    public float EmotionConfidence => _entity.EmotionConfidence;
    public string ImageFilePath => _entity.ImageFilePath;
    public string EmotionBadge => $"{Emotion} ({EmotionConfidence:P0})";

    [ObservableProperty]
    private SKBitmap? _thumbnail;

    public FaceDisplayItem(FaceEntity entity)
    {
        _entity = entity;
        LoadThumbnail();
    }

    private void LoadThumbnail()
    {
        try
        {
            var path = FaceThumbnailHelper.GetThumbnailPath(_entity.Id);
            if (File.Exists(path))
                Thumbnail = SKBitmap.Decode(path);
        }
        catch { }
    }
}

public partial class FacesViewModel : ObservableObject
{
    private readonly IFaceDbService _faceDb;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _totalFaces;

    public ObservableCollection<FaceDisplayItem> Faces { get; } = [];

    public FacesViewModel(IFaceDbService faceDb)
    {
        _faceDb = faceDb;
    }

    [RelayCommand]
    private async Task LoadFacesAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;
            StatusMessage = "Loading faces...";

            var entities = await _faceDb.GetAllAsync();
            Faces.Clear();
            foreach (var entity in entities)
                Faces.Add(new FaceDisplayItem(entity));

            TotalFaces = Faces.Count;
            StatusMessage = $"{TotalFaces} faces";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy) return;
        try
        {
            IsBusy = true;

            if (string.IsNullOrWhiteSpace(SearchQuery))
            {
                await LoadFacesAsync();
                return;
            }

            StatusMessage = "Searching...";
            var results = await _faceDb.SearchAsync(SearchQuery);
            Faces.Clear();
            foreach (var entity in results)
                Faces.Add(new FaceDisplayItem(entity));

            TotalFaces = Faces.Count;
            StatusMessage = $"{TotalFaces} matches";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task RenameFaceAsync(FaceDisplayItem? item)
    {
        if (item == null) return;

        var page = Application.Current?.Windows.FirstOrDefault()?.Page;
        if (page == null) return;

        var newName = await page.DisplayPromptAsync(
            "Rename Face",
            "Enter a name for this face:",
            initialValue: item.Name,
            accept: "Save",
            cancel: "Cancel");

        if (string.IsNullOrWhiteSpace(newName) || newName == item.Name) return;

        try
        {
            await _faceDb.UpdateNameAsync(item.Id, newName);

            var entity = item.Entity;
            entity.Name = newName;

            var idx = Faces.IndexOf(item);
            Faces.RemoveAt(idx);
            Faces.Insert(idx, new FaceDisplayItem(entity));

            StatusMessage = $"Renamed to '{newName}'";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Rename failed: {ex.Message}";
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            _ = LoadFacesAsync();
    }
}
