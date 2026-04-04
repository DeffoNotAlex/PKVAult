using PKHeX.Core;
using PKHeX.Drawing.Mobile.Sprites;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Pages;

public partial class DatabasePage : ContentPage
{
    private readonly GameStrings _strings = GameInfo.GetStrings("en");
    private List<PokemonEntry> _all = [];
    private List<PokemonEntry> _filtered = [];
    private int _gpIndex = -1;
    private bool _gpNavigating;
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
        _gpIndex = Math.Clamp(_gpIndex + delta, 0, _filtered.Count - 1);

        _gpNavigating = true;
        ResultsView.SelectedItem = _filtered[_gpIndex];
        _gpNavigating = false;

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

                var spriteKey = "b" + SpriteName.GetResourceStringSprite(
                    pk.Species, pk.Form, pk.Gender,
                    pk is IFormArgument fa ? fa.FormArgument : 0u,
                    pk.Context, pk.IsShiny);

                var source = ImageSource.FromStream(
                    ct => FileSystem.OpenAppPackageFileAsync($"sprites/{spriteKey}.png").WaitAsync(ct));

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

        _gpIndex = -1;
        ResultsView.ItemsSource = _filtered;
    }

    private void OnFilterChanged(object sender, EventArgs e) => ApplyFilter();

    private async void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_gpNavigating) return;
        if (e.CurrentSelection.FirstOrDefault() is not PokemonEntry entry)
            return;

        ResultsView.SelectedItem = null;
        await Shell.Current.GoToAsync($"{nameof(PkmEditorPage)}?box={entry.Box}&slot={entry.Slot}");
    }
}

public record PokemonEntry(
    PKM Pk,
    int Box,
    int Slot,
    string DisplayName,
    string SubInfo,
    string BoxSlotLabel,
    ImageSource SpriteSource);
