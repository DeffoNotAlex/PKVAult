using PKHeX.Core;
using PKHeX.Drawing.Mobile.Sprites;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Pages;

public partial class SettingsPage : ContentPage
{
    public const string KeyLanguage = "language";
    public const string KeyShinySprites = "shiny_sprites";

    private static readonly string[] LanguageCodes = ["ja", "en", "fr", "it", "de", "es", "es-419", "ko", "zh-Hans", "zh-Hant"];
    private static readonly string[] LanguageNames = ["日本語", "English", "Français", "Italiano", "Deutsch", "Español", "Español (LATAM)", "한국어", "中文 (简)", "中文 (繁)"];

    private bool _loading;

    public SettingsPage()
    {
        InitializeComponent();
        foreach (var name in LanguageNames)
            LanguagePicker.Items.Add(name);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        _loading = true;

        var lang = Preferences.Default.Get(KeyLanguage, "en");
        var idx = Array.IndexOf(LanguageCodes, lang);
        LanguagePicker.SelectedIndex = idx >= 0 ? idx : 1; // default English

        ShinySwitch.IsToggled = Preferences.Default.Get(KeyShinySprites, true);

        _loading = false;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
    }

#if ANDROID
    private void OnGamepadKey(Android.Views.Keycode keyCode, Android.Views.KeyEventActions action)
    {
        if (action != Android.Views.KeyEventActions.Down) return;
        if (keyCode == Android.Views.Keycode.ButtonB)
            MainThread.BeginInvokeOnMainThread(async () => await Shell.Current.GoToAsync(".."));
    }
#endif

    private void OnLanguageChanged(object sender, EventArgs e)
    {
        if (_loading || LanguagePicker.SelectedIndex < 0) return;
        var lang = LanguageCodes[LanguagePicker.SelectedIndex];
        Preferences.Default.Set(KeyLanguage, lang);
        GameInfo.CurrentLanguage = lang;
    }

    private void OnShinySwitchToggled(object sender, ToggledEventArgs e)
    {
        if (_loading) return;
        Preferences.Default.Set(KeyShinySprites, e.Value);
        SpriteName.AllowShinySprite = e.Value;
    }

    /// <summary>
    /// Apply persisted settings on app startup.
    /// </summary>
    public static void ApplyOnStartup()
    {
        var lang = Preferences.Default.Get(KeyLanguage, "en");
        GameInfo.CurrentLanguage = lang;
        SpriteName.AllowShinySprite = Preferences.Default.Get(KeyShinySprites, true);
    }
}
