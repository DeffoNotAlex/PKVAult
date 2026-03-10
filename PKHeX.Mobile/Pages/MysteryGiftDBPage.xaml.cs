using PKHeX.Core;
using PKHeX.Drawing.Mobile.Sprites;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Pages;

public partial class MysteryGiftDBPage : ContentPage
{
    private readonly GameStrings _strings = GameInfo.GetStrings("en");
    private List<GiftEntry> _all = [];

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
        if (keyCode == Android.Views.Keycode.ButtonB)
            MainThread.BeginInvokeOnMainThread(async () => await Shell.Current.GoToAsync(".."));
    }
#endif

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

        var filtered = _all.Where(e =>
        {
            if (text.Length > 0 &&
                !e.Title.Contains(text, StringComparison.OrdinalIgnoreCase) &&
                !e.SubInfo.Contains(text, StringComparison.OrdinalIgnoreCase))
                return false;

            if (compatOnly && !e.Gift.IsCardCompatible(sav!, out _))
                return false;

            return true;
        }).ToList();

        GiftList.ItemsSource = filtered;
    }

    private void OnFilterChanged(object sender, EventArgs e) => ApplyFilter();

    private async void OnGiftSelected(object sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is not GiftEntry entry)
            return;
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
