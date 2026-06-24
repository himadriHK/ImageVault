using ImageVault.Models;
using ImageVault.ViewModels;

namespace ImageVault;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _viewModel;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (!_viewModel.IsModelLoading && !_viewModel.IsBusy)
            await _viewModel.InitializeCommand.ExecuteAsync(null);
    }

    private async void OnImageTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not View view) return;

        var path = view.BindingContext switch
        {
            ImageEntity img => img.FilePath,
            SearchResult sr => sr.Entity.FilePath,
            _ => null
        };

        if (path is not null)
            await Shell.Current.GoToAsync($"viewer?path={Uri.EscapeDataString(path)}");
    }
}
