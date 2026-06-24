using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ImageVault.Models;
using ImageVault.Services;
using Shiny.Jobs;

namespace ImageVault.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IClipService _clipService;
    private readonly IVectorDbService _vectorDb;
    private readonly IJobManager _jobManager;
    private readonly ImportRequest _importRequest;
    private readonly SettingsViewModel _settings;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isModelLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _searchQuery = string.Empty;

    [ObservableProperty]
    private int _totalImages;

    [ObservableProperty]
    private double _modelLoadingProgress;

    public ObservableCollection<ImageEntity> Images { get; } = [];
    public ObservableCollection<SearchResult> SearchResults { get; } = [];

    public MainViewModel(
        IClipService clipService,
        IVectorDbService vectorDb,
        IJobManager jobManager,
        ImportRequest importRequest,
        SettingsViewModel settings)
    {
        _clipService = clipService;
        _vectorDb = vectorDb;
        _jobManager = jobManager;
        _importRequest = importRequest;
        _settings = settings;
    }

    [RelayCommand]
    private async Task InitializeAsync()
    {
        if (IsBusy || _clipService.IsModelLoaded) return;

        try
        {
            IsBusy = true;
            IsModelLoading = true;
            ModelLoadingProgress = 0;
            StatusMessage = "Loading models...";

            await AndroidNotification.RequestPermissionAsync();
            await _vectorDb.InitializeAsync();
            await LoadImagesAsync();

            var progress = new Progress<double>(p =>
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    ModelLoadingProgress = p;
                    if (p < 1.0)
                        StatusMessage = $"Loading models: {p * 100:F0}%";
                });
            });

            _ = Task.Run(async () =>
            {
                try
                {
                    await _clipService.LoadModelsAsync(progress);
                }
                catch (Exception ex)
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                        StatusMessage = $"Model loading failed: {ex.Message}");
                }
                finally
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        IsModelLoading = false;
                        if (StatusMessage.StartsWith("Loading models"))
                            StatusMessage = "Ready";
                    });
                }
            });
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
        try
        {
            var result = await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = "Select images",
                FileTypes = FilePickerFileType.Images
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

            _importRequest.DirectoryPath = null;
            _importRequest.PickedFiles = items;
            var jobResult = await Task.Run(
                () => _jobManager.RunJob(typeof(ImageProcessingJob), CancellationToken.None));
            StatusMessage = jobResult.Exception != null
                ? $"Import failed: {jobResult.Exception.Message}"
                : $"{items.Count} files queued for import";
            await LoadImagesAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Pick failed: {ex.Message}";
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
            var results = await _vectorDb.SearchAsync(
                queryEmbedding, _settings.SortMetric, _settings.FilterThreshold, 20);

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

    private static async Task<bool> RequestStoragePermissionAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return await Permissions.RequestAsync<Permissions.Media>() == PermissionStatus.Granted;
        else
            return await Permissions.RequestAsync<Permissions.StorageRead>() == PermissionStatus.Granted;
    }

    [RelayCommand]
    private async Task ImportDirectoryAsync(string? directoryPath)
    {
        if (string.IsNullOrEmpty(directoryPath)) return;

        if (!await RequestStoragePermissionAsync())
        {
            StatusMessage = "Storage permission required";
            return;
        }

        try
        {
            _importRequest.PickedFiles = null;
            _importRequest.DirectoryPath = directoryPath;
            var result = await Task.Run(
                () => _jobManager.RunJob(typeof(ImageProcessingJob), CancellationToken.None));
            StatusMessage = result.Exception != null
                ? $"Import failed: {result.Exception.Message}"
                : "Background import started";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Import failed: {ex.Message}";
        }
    }

    private async Task LoadImagesAsync()
    {
        try
        {
            IsBusy = true;
            var images = await _vectorDb.GetAllAsync(_settings.SortMetric);
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
