using PKHeX.Mobile.Services;

namespace PKHeX.Mobile;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>();

        builder.Services.AddSingleton<IFileService, FileService>();

        return builder.Build();
    }
}
