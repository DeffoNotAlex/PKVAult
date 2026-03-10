using PKHeX.Core;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile;

public partial class MainPage : ContentPage
{
    private readonly IFileService _fileService;

    public MainPage(IFileService fileService)
    {
        InitializeComponent();
        _fileService = fileService;
    }

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
        GameLabel.Text = $"{sav.Version} — Generation {sav.Generation}";
        TrainerLabel.Text = sav.OT;
        IDLabel.Text = $"TID: {sav.TID16} / SID: {sav.SID16}";
        PlaytimeLabel.Text = sav.PlayTimeString;
        StorageLabel.Text = $"{sav.BoxCount} boxes / {sav.SlotCount} slots";

        SaveInfoCard.IsVisible = true;
    }

    private void ShowError(string message)
    {
        ErrorLabel.Text = message;
        ErrorLabel.IsVisible = true;
    }
}
