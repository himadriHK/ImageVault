namespace ImageVault;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
        Routing.RegisterRoute("viewer", typeof(ImageViewerPage));
    }
}
