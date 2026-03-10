using PKHeX.Core;
using PKHeX.Mobile.Pages;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile;

public partial class MainPage : ContentPage
{
    private readonly IFileService _fileService;
    private string _loadedFileName = "save.bin";

    public MainPage()
    {
        InitializeComponent();
        _fileService = new FileService();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
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
            // A / Start: load save (no save loaded) or open boxes (save loaded)
            case Android.Views.Keycode.ButtonA:
            case Android.Views.Keycode.ButtonStart:
                if (App.ActiveSave is null)
                    OnLoadClicked(this, EventArgs.Empty);
                else
                    OnBoxClicked(this, EventArgs.Empty);
                break;

            // X: search (save loaded)
            case Android.Views.Keycode.ButtonX:
                if (App.ActiveSave is not null)
                    OnSearchClicked(this, EventArgs.Empty);
                break;

            // Y: mystery gifts (save loaded)
            case Android.Views.Keycode.ButtonY:
                if (App.ActiveSave is not null)
                    OnGiftDBClicked(this, EventArgs.Empty);
                break;

            // Select: settings
            case Android.Views.Keycode.ButtonSelect:
                OnSettingsClicked(this, EventArgs.Empty);
                break;
        }
    }
#endif

    private async void OnLoadClicked(object sender, EventArgs e)
    {
        ErrorLabel.IsVisible = false;
        SaveInfoCard.IsVisible = false;
        LoadButton.IsEnabled = false;

        try
        {
            var result = await _fileService.PickFileAsync();
            if (result is null)
                return;

            var (data, fileName) = result.Value;
            _loadedFileName = fileName;
            App.ActiveSaveFileName = fileName;

            if (!SaveUtil.TryGetSaveFile(data, out var sav))
            {
                ShowError("Could not parse save file. Ensure the file is not encrypted with console-specific keys.");
                return;
            }

            DisplaySaveInfo(sav);
        }
        catch (Exception ex)
        {
            ShowError($"Unexpected error: {ex.Message}");
        }
        finally
        {
            LoadButton.IsEnabled = true;
        }
    }

    private void DisplaySaveInfo(SaveFile sav)
    {
        App.ActiveSave = sav;

        GameLabel.Text = $"{sav.Version} — Generation {sav.Generation}";
        TrainerLabel.Text = sav.OT;
        IDLabel.Text = $"TID: {sav.TID16} / SID: {sav.SID16}";
        PlaytimeLabel.Text = sav.PlayTimeString;
        StorageLabel.Text = $"{sav.BoxCount} boxes / {sav.SlotCount} slots";

        SaveInfoCard.IsVisible = true;
        BoxButton.IsVisible = true;
        SearchButton.IsVisible = true;
        GiftDBButton.IsVisible = true;
        ExportButton.IsVisible = true;
    }

    private async void OnExportClicked(object sender, EventArgs e)
    {
        if (App.ActiveSave is null) return;
        ExportButton.IsEnabled = false;
        try
        {
            var data = App.ActiveSave.Write().ToArray();
            await _fileService.ExportFileAsync(data, _loadedFileName);
        }
        catch (Exception ex)
        {
            ShowError($"Export failed: {ex.Message}");
        }
        finally
        {
            ExportButton.IsEnabled = true;
        }
    }

    private async void OnBoxClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(GamePage));
    }

    private async void OnSearchClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(DatabasePage));
    }

    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(SettingsPage));
    }

    private async void OnGiftDBClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync(nameof(MysteryGiftDBPage));
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
