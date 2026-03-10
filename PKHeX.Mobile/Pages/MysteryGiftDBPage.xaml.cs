using PKHeX.Core;
using PKHeX.Drawing.Mobile.Sprites;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Pages;

public partial class MysteryGiftDBPage : ContentPage
{
    private readonly GameStrings _strings = GameInfo.GetStrings("en");
    private List<GiftEntry> _all = [];
    private List<GiftEntry> _filtered = [];
    private int _gpIndex = -1;
    private bool _gpNavigating;
    private DateTime _lastListNav = DateTime.MinValue;

    public MysteryGiftDBPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        if (_all.Count == 0)
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
                    _ = InjectGift(_filtered[_gpIndex]);
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
        GiftList.SelectedItem = _filtered[_gpIndex];
        _gpNavigating = false;

        GiftList.ScrollTo(_filtered[_gpIndex], null, ScrollToPosition.MakeVisible, false);
    }

    private void BuildIndex()
    {
        _all = [];
        foreach (var gift in EncounterEvent.GetAllEvents())
        {
            var title = gift.CardHeader;
            string subInfo;
            ImageSource? sprite = null;

            if (gift.IsEntity && gift.Species > 0)
            {
                var speciesName = gift.Species < _strings.specieslist.Length
                    ? _strings.specieslist[gift.Species] : gift.Species.ToString();
                subInfo = $"{speciesName}  Lv.{gift.Level}";

                var spriteKey = "b" + SpriteName.GetResourceStringSprite(
                    gift.Species, gift.Form, 0, 0, gift.Context, false);
                sprite = ImageSource.FromStream(
                    ct => FileSystem.OpenAppPackageFileAsync($"sprites/{spriteKey}.png").WaitAsync(ct));
            }
            else if (!gift.IsEntity)
            {
                subInfo = "Item gift";
            }
            else
            {
                subInfo = "";
            }

            _all.Add(new GiftEntry(gift, title, subInfo, $"Gen {gift.Generation}", sprite));
        }
    }

    private void ApplyFilter()
    {
        var sav = App.ActiveSave;
        var text = SearchEntry.Text?.Trim() ?? "";
        var compatOnly = CompatibleOnly.IsToggled && sav is not null;

        _filtered = _all.Where(e =>
        {
            if (text.Length > 0 &&
                !e.Title.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                !e.SubInfo.Contains(text, StringComparison.OrdinalIgnoreCase))
                return false;

            if (compatOnly && !e.Gift.IsCardCompatible(sav!, out _))
                return false;

            return true;
        }).ToList();

        _gpIndex = -1;
        GiftList.ItemsSource = _filtered;
    }

    private void OnFilterChanged(object sender, EventArgs e) => ApplyFilter();

    private async void OnGiftSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_gpNavigating) return;
        if (e.CurrentSelection.FirstOrDefault() is not GiftEntry entry)
            return;
        GiftList.SelectedItem = null;
        await InjectGift(entry);
    }

    private async Task InjectGift(GiftEntry entry)
    {
        GiftList.SelectedItem = null;

        var sav = App.ActiveSave;
        if (sav is null) return;

        if (!entry.Gift.IsCardCompatible(sav, out var reason))
        {
            await DisplayAlertAsync("Not Compatible", reason, "OK");
            return;
        }

        bool confirm = await DisplayAlertAsync(
            entry.Gift.CardTitle,
            $"Inject this gift into your save?\n\n{entry.SubInfo}",
            "Inject", "Cancel");

        if (!confirm) return;

        // Convert gift to PKM and find first free box slot
        var pk = entry.Gift.ConvertToPKM(sav);
        sav.AdaptToSaveFile(pk);

        int targetBox = -1, targetSlot = -1;
        for (int box = 0; box < sav.BoxCount && targetBox < 0; box++)
        {
            var data = sav.GetBoxData(box);
            for (int slot = 0; slot < data.Length; slot++)
            {
                if (data[slot].Species == 0)
                {
                    targetBox = box;
                    targetSlot = slot;
                    break;
                }
            }
        }

        if (targetBox < 0)
        {
            await DisplayAlertAsync("No Space", "All boxes are full.", "OK");
            return;
        }

        sav.SetBoxSlotAtIndex(pk, targetBox, targetSlot);
        await DisplayAlertAsync("Done", $"Gift injected into Box {targetBox + 1}, slot {targetSlot + 1}.", "OK");
    }
}

public record GiftEntry(
    MysteryGift Gift,
    string Title,
    string SubInfo,
    string GenLabel,
    ImageSource? SpriteSource);
