using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageVault.Models;
using ImageVault.Services;

namespace ImageVault.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IClipService _clipService;
    private readonly IVectorDbService _vectorDb;
    private readonly IImageProcessingService _processingService;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isModelLoading;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _totalImages;

    [ObservableProperty]
    private int _processedCount;

    [ObservableProperty]
    private double _processingProgress;

    public ObservableCollection<ImageEntity> Images { get; } = [];
    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    public MainViewModel(
        IClipService clipService,
        IVectorDbService vectorDb,
        IImageProcessingService processingService)
    {
        _clipService = clipService;
        _vectorDb = vectorDb;
        _processingService = processingService;

        _processingService.OnItemProcessed += (_, _, _) => { };
        _processingService.OnBatchProgress += count =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                ProcessedCount = count;
                ProcessingProgress = TotalImages > 0 ? (double)count / TotalImages : 0;
                StatusMessage = $"Processing: {count}/{TotalImages}";
            });
        };
        _processingService.OnError += msg =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StatusMessage = $"Error: {msg}";
            });
        };
        _processingService.OnBatchComplete += () =>
        {
            MainThread.BeginInvokeOnMainThread(async () =>
            {
                IsProcessing = false;
                StatusMessage = "Processing complete";
                await LoadImagesAsync();
            });
        };
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            IsModelLoading = true;
            StatusMessage = "Loading models...";

            await _vectorDb.InitializeAsync();
            await _clipService.LoadModelsAsync();

            IsModelLoading = false;
            StatusMessage = "Ready";
            await LoadImagesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Init failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task PickImagesAsync()
    {
        if (IsProcessing) return;

        try
        {
            var result = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Select images for embedding"
            });

            if (result == null) return;

            var files = result.ToList();
            if (files.Count == 0) return;

            var items = files
                .Where(f => f?.FullPath != null)
                .Select(file =>
                {
                    var path = file!.FullPath;
                    long fileSize = 0;
                    try { fileSize = new FileInfo(path).Length; } catch { }
                    return new ProcessingItem
                    {
                        FilePath = path,
                        FileName = file.FileName ?? string.Empty,
                        FileSize = fileSize,
                        DateModifiedUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
                    };
                }).ToList();

            TotalImages = items.Count;
            ProcessedCount = 0;
            ProcessingProgress = 0;
            IsProcessing = true;
            StatusMessage = $"Processing {items.Count} images...";

            await Task.Run(() =>
                _processingService.ProcessBatchAsync(items));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pick failed: {ex.Message}";
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        if (IsBusy || string.IsNullOrWhiteSpace(SearchQuery)) return;

        try
        {
            IsBusy = true;
            StatusMessage = "Searching...";

            var queryEmbedding = await _clipService.GetTextEmbeddingAsync(SearchQuery);
            var results = await _vectorDb.SearchAsync(queryEmbedding, 20);

            SearchResults.Clear();
            foreach (var result in results)
                SearchResults.Add(result);
        }
        catch (Exception ex)
        {
            StatusMessage = $"Search failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
            StatusMessage = SearchResults.Count > 0
                ? $"Found {SearchResults.Count} results"
                : "No results found";
        }
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadImagesAsync();
    }

    [RelayCommand]
    private async Task ClearDatabaseAsync()
    {
        if (IsBusy) return;

        try
        {
            IsBusy = true;
            await _vectorDb.ClearAsync();
            Images.Clear();
            SearchResults.Clear();
            TotalImages = 0;
            StatusMessage = "Database cleared";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Clear failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportDirectoryAsync(string? directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath) || IsProcessing) return;

        try
        {
            var items = ImageProcessingService.ScanDirectory(directoryPath);

            if (items.Count == 0)
            {
                StatusMessage = "No images found in directory";
                return;
            }

            TotalImages = items.Count;
            ProcessedCount = 0;
            ProcessingProgress = 0;
            IsProcessing = true;
            StatusMessage = $"Scanning {items.Count} images...";

            await Task.Run(() =>
                _processingService.ProcessBatchAsync(items));
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
            IsProcessing = false;
        }
    }

    private async Task LoadImagesAsync()
    {
        try
        {
            IsBusy = true;
            var images = await _vectorDb.GetAllAsync();
            Images.Clear();
            foreach (var img in images)
                Images.Add(img);
            TotalImages = Images.Count;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSearchQueryChanged(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            SearchResults.Clear();
            StatusMessage = $"Showing {Images.Count} images";
        }
    }
}
