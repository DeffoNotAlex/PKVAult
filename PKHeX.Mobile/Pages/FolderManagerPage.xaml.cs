using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Pages;

public partial class FolderManagerPage : ContentPage
{
    private readonly SaveDirectoryService _dirService = new();
    private List<string> _dirs = [];
    private int _listCursor = -1;

#if ANDROID
    private readonly IDirectoryPicker _dirPicker = new Platforms.Android.AndroidDirectoryPicker();
#else
    private readonly IDirectoryPicker _dirPicker = new NullDirectoryPicker();
#endif

    public FolderManagerPage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
#if ANDROID
        GamepadRouter.KeyReceived += OnGamepadKey;
#endif
        RefreshList();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
#if ANDROID
        GamepadRouter.KeyReceived -= OnGamepadKey;
#endif
    }

    private void RefreshList()
    {
        _dirs = _dirService.GetWatchedDirectories();
        DirList.ItemsSource = null;
        DirList.ItemsSource = _dirs;
        _listCursor = _dirs.Count > 0 ? 0 : -1;
    }

    private async void OnAddFolderClicked(object sender, EventArgs e)
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;
        _dirService.AddDirectory(uri);
        RefreshList();
    }

    private void OnRemoveClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string uri)
        {
            _dirService.RemoveDirectory(uri);
            RefreshList();
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("..");
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
                _ = Shell.Current.GoToAsync("..");
                break;

            case Android.Views.Keycode.DpadUp:
                MoveCursor(-1);
                break;

            case Android.Views.Keycode.DpadDown:
                MoveCursor(+1);
                break;

            case Android.Views.Keycode.ButtonX:
                RemoveFocused();
                break;

            case Android.Views.Keycode.ButtonA:
                // items are read-only, no action
                break;
        }
    }
#endif

    private void MoveCursor(int delta)
    {
        if (_dirs.Count == 0) return;
        _listCursor = Math.Clamp(_listCursor + delta, 0, _dirs.Count - 1);
        DirList.SelectedItem = _dirs[_listCursor];
        DirList.ScrollTo(_listCursor, -1, ScrollToPosition.MakeVisible, false);
    }

    private void RemoveFocused()
    {
        if (_listCursor < 0 || _listCursor >= _dirs.Count) return;
        var uri = _dirs[_listCursor];
        _dirService.RemoveDirectory(uri);
        RefreshList();
    }

    // Stub for non-Android platforms
    private sealed class NullDirectoryPicker : IDirectoryPicker
    {
        public Task<string?> PickDirectoryAsync() => Task.FromResult<string?>(null);
    }
}
