using PKHeX.Mobile.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace PKHeX.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseSkiaSharp();

        builder.Services.AddSingleton<IFileService, FileService>();

        return builder.Build();
    }
}
