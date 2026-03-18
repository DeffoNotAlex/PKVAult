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
            .UseSkiaSharp()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("Nunito-Regular.ttf", "Nunito");
                fonts.AddFont("Nunito-Bold.ttf", "NunitoBold");
                fonts.AddFont("Nunito-ExtraBold.ttf", "NunitoExtraBold");
                fonts.AddFont("Quicksand-Bold.ttf", "Quicksand");
                fonts.AddFont("Quicksand-ExtraBold.ttf", "QuicksandExtraBold");
            })
            .ConfigureMauiHandlers(handlers =>
            {
#if ANDROID
                Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping(
                    "EnableJavaScript", (handler, _) =>
                        handler.PlatformView.Settings.JavaScriptEnabled = true);
#endif
            });

        builder.Services.AddSingleton<IFileService, FileService>();

#if ANDROID
        // Use Thor dual-screen if a secondary display is detected, otherwise fallback
        builder.Services.AddSingleton<ISecondaryDisplay>(sp =>
        {
            var thor = new Platforms.Android.ThorSecondaryDisplay();
            if (thor.IsAvailable)
                return thor;
            thor.Dispose();
            return new SingleScreenFallback();
        });
#else
        builder.Services.AddSingleton<ISecondaryDisplay, SingleScreenFallback>();
#endif

        return builder.Build();
    }
}
