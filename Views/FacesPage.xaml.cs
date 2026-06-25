using ImageVault.ViewModels;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace ImageVault.Views;

public partial class FacesPage : ContentPage
{
    public FacesPage(FacesViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (BindingContext is FacesViewModel vm)
            vm.LoadFacesCommand.Execute(null);
    }

    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear();

        if (sender is not SKCanvasView skiaView) return;
        if (skiaView.BindingContext is not FaceDisplayItem item) return;
        if (item.Thumbnail == null) return;

        var thumb = item.Thumbnail;
        var scale = Math.Min((float)info.Width / thumb.Width, (float)info.Height / thumb.Height);
        var destW = thumb.Width * scale;
        var destH = thumb.Height * scale;
        var destX = (info.Width - destW) / 2;
        var destY = (info.Height - destH) / 2;

        canvas.DrawBitmap(thumb, new SKRect(destX, destY, destX + destW, destY + destH));
    }
}
