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

                // Remove Android's orange focus ring from CollectionView (RecyclerView).
                // Gamepad input is intercepted at Activity.DispatchKeyEvent so view
                // focus is irrelevant for our navigation — safe to disable.
                Microsoft.Maui.Handlers.CollectionViewHandler.Mapper.AppendToMapping(
                    "DisableFocusHighlight", (handler, _) =>
                    {
                        handler.PlatformView.Focusable = false;
                        handler.PlatformView.FocusableInTouchMode = false;
                    });
#endif
            });

        builder.Services.AddSingleton<IFileService, FileService>();
        builder.Services.AddTransient<MainPage>();
        builder.Services.AddTransient<Pages.GamePage>();
        builder.Services.AddTransient<Pages.PkmEditorPage>();

#if ANDROID
        // Always use ThorSecondaryDisplay on Android — display detection is lazy
        // and deferred to Show(), so it works even if the second screen isn't ready at startup.
        builder.Services.AddSingleton<ISecondaryDisplay, Platforms.Android.ThorSecondaryDisplay>();
#else
        builder.Services.AddSingleton<ISecondaryDisplay, SingleScreenFallback>();
#endif

        return builder.Build();
    }
}
