namespace ImageVault;

[QueryProperty(nameof(ImagePath), "path")]
public partial class ImageViewerPage : ContentPage
{
    private string _imagePath = string.Empty;

    public string ImagePath
    {
        get => _imagePath;
        set
        {
            _imagePath = Uri.UnescapeDataString(value);
            OnPropertyChanged();
        }
    }

    public ImageViewerPage()
    {
        InitializeComponent();
        BindingContext = this;
    }

    private async void OnTapToDismiss(object? sender, TappedEventArgs e)
    {
        await Shell.Current.GoToAsync("..");
    }
}
