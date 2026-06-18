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
}
