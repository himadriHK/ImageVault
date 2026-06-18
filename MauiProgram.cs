using CommunityToolkit.Maui;
using ImageVault.Services;
using ImageVault.ViewModels;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace ImageVault;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .UseMauiCommunityToolkit()
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services.AddSingleton<IClipService, ClipService>();
        builder.Services.AddSingleton<IVectorDbService, VectorDbService>();
        builder.Services.AddSingleton<IImageProcessingService, ImageProcessingService>();

        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainPage>();

        return builder.Build();
    }
}
