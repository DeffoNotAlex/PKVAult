using Microsoft.Extensions.Logging;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
#endif

        builder.Services.AddSingleton<IFileService, FileService>();
        builder.Services.AddTransient<MainPage>();

        return builder.Build();
    }
}
