using System.ComponentModel;
using PKHeX.Core;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Pages;

public partial class DatabasePage : ContentPage
{
    private readonly GameStrings _strings = GameInfo.GetStrings("en");
    private List<PokemonEntry> _all = [];
    private List<PokemonEntry> _filtered = [];
    private int _gpIndex = -1;
    private PokemonEntry? _highlightedEntry;
    private DateTime _lastListNav = DateTime.MinValue;

    public DatabasePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        if (App.ActiveSave is null) return;
        BuildIndex();
        ApplyFilter();
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
        MainThread.BeginInvokeOnMainThread(() => HandleGamepadKey(keyCode));
    }

    private void HandleGamepadKey(Android.Views.Keycode keyCode)
    {
        switch (keyCode)
        {
            case Android.Views.Keycode.ButtonB:
                _ = Shell.Current.GoToAsync(".."); break;

            case Android.Views.Keycode.DpadUp:
                MoveListCursor(-1); break;

            case Android.Views.Keycode.DpadDown:
                MoveListCursor(+1); break;

            case Android.Views.Keycode.ButtonA:
                if (_gpIndex >= 0 && _gpIndex < _filtered.Count)
                {
                    var entry = _filtered[_gpIndex];
                    _ = Shell.Current.GoToAsync($"{nameof(PkmEditorPage)}?box={entry.Box}&slot={entry.Slot}");
                }
                break;

            case Android.Views.Keycode.ButtonY:
                SearchEntry.Focus(); break;
        }
    }
#endif

    private void MoveListCursor(int delta)
    {
        if ((DateTime.UtcNow - _lastListNav).TotalMilliseconds < 150) return;
        _lastListNav = DateTime.UtcNow;

        if (_filtered.Count == 0) return;
        if (_highlightedEntry is not null) _highlightedEntry.IsHighlighted = false;
        _gpIndex = Math.Clamp(_gpIndex + delta, 0, _filtered.Count - 1);
        _highlightedEntry = _filtered[_gpIndex];
        _highlightedEntry.IsHighlighted = true;

        ResultsView.ScrollTo(_gpIndex, -1, ScrollToPosition.MakeVisible, false);
    }

    private void BuildIndex()
    {
        var sav = App.ActiveSave!;
        _all = new List<PokemonEntry>(sav.BoxCount * sav.BoxSlotCount);

        for (int box = 0; box < sav.BoxCount; box++)
        {
            var data = sav.GetBoxData(box);
            for (int slot = 0; slot < data.Length; slot++)
            {
                var pk = data[slot];
                if (pk.Species == 0) continue;

                var speciesName = pk.Species < _strings.specieslist.Length
                    ? _strings.specieslist[pk.Species] : pk.Species.ToString();
                var natureName = (int)pk.Nature < _strings.natures.Length
                    ? _strings.natures[(int)pk.Nature] : "";
                var abilityName = pk.Ability < _strings.abilitylist.Length
                    ? _strings.abilitylist[pk.Ability] : "";

                var displayName = (pk.IsShiny ? "✦ " : "") +
                    $"#{pk.Species:000} {speciesName}" +
                    (pk.IsNicknamed ? $"  \"{pk.Nickname}\"" : "");

                var subInfo = $"Lv.{pk.CurrentLevel}  {natureName}  {abilityName}";

                var boxLabel = sav is IBoxDetailName named
                    ? $"{named.GetBoxName(box)} #{slot + 1}"
                    : $"Box {box + 1} #{slot + 1}";

                var spriteUrl = pk.IsShiny
                    ? $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/shiny/{pk.Species}.png"
                    : $"https://raw.githubusercontent.com/PokeAPI/sprites/master/sprites/pokemon/other/home/{pk.Species}.png";

                var source = new UriImageSource
                {
                    Uri = new Uri(spriteUrl),
                    CacheValidity = TimeSpan.FromDays(30),
                };

                _all.Add(new PokemonEntry(pk, box, slot, displayName, subInfo, boxLabel, source));
            }
        }
    }

    private void ApplyFilter()
    {
        var text = SearchEntry.Text?.Trim().ToLowerInvariant() ?? "";
        var shinyOnly = ShinyFilter.IsToggled;

        _filtered = _all.Where(e =>
            (text.Length == 0 || e.DisplayName.Contains(text, StringComparison.OrdinalIgnoreCase)) &&
            (!shinyOnly || e.Pk.IsShiny)
        ).ToList();

        if (_highlightedEntry is not null) { _highlightedEntry.IsHighlighted = false; _highlightedEntry = null; }
        _gpIndex = -1;
        ResultsView.ItemsSource = _filtered;
    }

    private void OnFilterChanged(object sender, EventArgs e) => ApplyFilter();

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not PokemonEntry entry)
            return;

        ResultsView.SelectedItem = null;
        await Shell.Current.GoToAsync($"{nameof(PkmEditorPage)}?box={entry.Box}&slot={entry.Slot}");
    }
}

public class PokemonEntry(PKM pk, int box, int slot,
    string displayName, string subInfo, string boxSlotLabel, ImageSource spriteSource)
    : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public PKM Pk            { get; } = pk;
    public int  Box          { get; } = box;
    public int  Slot         { get; } = slot;
    public string DisplayName  { get; } = displayName;
    public string SubInfo      { get; } = subInfo;
    public string BoxSlotLabel { get; } = boxSlotLabel;
    public ImageSource SpriteSource { get; } = spriteSource;

    private bool _isHighlighted;
    public bool IsHighlighted
    {
        get => _isHighlighted;
        set
        {
            if (_isHighlighted == value) return;
            _isHighlighted = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHighlighted)));
        }
    }
}
