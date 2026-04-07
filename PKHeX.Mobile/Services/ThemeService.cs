using PKHeX.Mobile.Resources.Styles;
using SkiaSharp;

namespace PKHeX.Mobile.Services;

public enum PkTheme { Dark, Light }

/// <summary>
/// Manages the active color theme. Call ApplyOnStartup() at launch,
/// then Apply() to switch. Subscribe to ThemeChanged to trigger canvas redraws.
/// </summary>
public static class ThemeService
{
    public const string PrefKey = "app_theme";

    public static PkTheme Current { get; private set; } = PkTheme.Dark;

    /// <summary>Fired on the main thread after the theme dictionary is swapped.</summary>
    public static event Action? ThemeChanged;

    // ── SkiaSharp palette ────────────────────────────────────────────────────

    public static SKColor CanvasBg   => Pick(new SKColor(0xF2, 0xF4, 0xF8), new SKColor(7, 12, 26));
    public static SKColor SlotFilled => Pick(new SKColor(0xFF, 0xFF, 0xFF),  new SKColor(0x11, 0x1C, 0x33));
    public static SKColor SlotEmpty  => Pick(new SKColor(0xE4, 0xE8, 0xF0),  new SKColor(0x0E, 0x15, 0x29));
    public static SKColor SlotBorder => Pick(new SKColor(0, 0, 0, 20),       new SKColor(255, 255, 255, 8));
    public static SKColor RadarBg    => Pick(new SKColor(255, 255, 255, 200), new SKColor(16, 24, 40, 150));
    public static SKColor RadarGrid  => Pick(new SKColor(0, 0, 0, 35),       new SKColor(255, 255, 255, 40));
    public static SKColor RadarLabel => Pick(new SKColor(0x0D, 0x11, 0x17),  new SKColor(0xED, 0xF0, 0xFF));
    public static SKColor RadarStat  => Pick(new SKColor(0x4A, 0x55, 0x68),  new SKColor(0x88, 0x92, 0xB5));

    private static SKColor Pick(SKColor light, SKColor dark)
        => Current == PkTheme.Light ? light : dark;

    // ── Theme switching ──────────────────────────────────────────────────────

    public static void Apply(PkTheme theme)
    {
        Current = theme;
        Preferences.Default.Set(PrefKey, (int)theme);
        SwapDictionary(theme);
        ThemeChanged?.Invoke();
    }

    /// <summary>Call once at startup before any page loads. Does not fire ThemeChanged.</summary>
    public static void ApplyOnStartup()
    {
        var saved = (PkTheme)Preferences.Default.Get(PrefKey, (int)PkTheme.Dark);
        Current = saved;
        SwapDictionary(saved);
    }

    private static void SwapDictionary(PkTheme theme)
    {
        if (Application.Current is null) return;
        var merged = Application.Current.Resources.MergedDictionaries;
        merged.Clear();
        merged.Add(theme == PkTheme.Light ? new LightTheme() : new DarkTheme());
    }
}
