using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Pages;

public partial class FolderManagerPage : ContentPage
{
    private readonly SaveDirectoryService _dirService = new();
    private List<WatchedEntry> _items = [];
    private int _listCursor = -1;

#if ANDROID
    private readonly IDirectoryPicker _dirPicker = new Platforms.Android.AndroidDirectoryPicker();
    private readonly ISaveFilePicker  _filePicker = new Platforms.Android.AndroidFilePicker();
#else
    private readonly IDirectoryPicker _dirPicker  = new NullDirectoryPicker();
    private readonly ISaveFilePicker  _filePicker = new NullFilePicker();
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
        _items = [
            .. _dirService.GetWatchedDirectories().Select(u => new WatchedEntry(u, false)),
            .. _dirService.GetWatchedFiles().Select(u => new WatchedEntry(u, true)),
        ];
        DirList.ItemsSource = null;
        DirList.ItemsSource = _items;
        _listCursor = _items.Count > 0 ? 0 : -1;
    }

    private async void OnAddFolderClicked(object sender, EventArgs e)
    {
        var uri = await _dirPicker.PickDirectoryAsync();
        if (uri is null) return;
        _dirService.AddDirectory(uri);
        RefreshList();
    }

    private async void OnAddFileClicked(object sender, EventArgs e)
    {
        var uri = await _filePicker.PickFileAsync();
        if (uri is null) return;
        _dirService.AddFile(uri);
        RefreshList();
    }

    private void OnRemoveClicked(object sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is WatchedEntry entry)
        {
            if (entry.IsFile) _dirService.RemoveFile(entry.Uri);
            else              _dirService.RemoveDirectory(entry.Uri);
            RefreshList();
        }
    }

    private async void OnBackClicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");

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
                MoveCursor(-1); break;
            case Android.Views.Keycode.DpadDown:
                MoveCursor(+1); break;
            case Android.Views.Keycode.ButtonX:
                RemoveFocused(); break;
            case Android.Views.Keycode.ButtonL1:
            case Android.Views.Keycode.Button5:
                OnAddFolderClicked(this, EventArgs.Empty); break;
            case Android.Views.Keycode.ButtonR1:
            case Android.Views.Keycode.Button6:
                OnAddFileClicked(this, EventArgs.Empty); break;
        }
    }
#endif

    private void MoveCursor(int delta)
    {
        if (_items.Count == 0) return;
        _listCursor = Math.Clamp(_listCursor + delta, 0, _items.Count - 1);
        DirList.SelectedItem = _items[_listCursor];
        DirList.ScrollTo(_listCursor, -1, ScrollToPosition.MakeVisible, false);
    }

    private void RemoveFocused()
    {
        if (_listCursor < 0 || _listCursor >= _items.Count) return;
        var entry = _items[_listCursor];
        if (entry.IsFile) _dirService.RemoveFile(entry.Uri);
        else              _dirService.RemoveDirectory(entry.Uri);
        RefreshList();
    }

    // ── View model ────────────────────────────────────────────────────────

    public sealed record WatchedEntry(string Uri, bool IsFile)
    {
        public string Icon  => IsFile ? "📄" : "📁";
        public string Label => ExtractLabel(Uri);

        private static string ExtractLabel(string uri)
        {
            var decoded = System.Uri.UnescapeDataString(uri);
            var idx = Math.Max(decoded.LastIndexOf(':'), decoded.LastIndexOf('/'));
            return idx >= 0 && idx < decoded.Length - 1 ? decoded[(idx + 1)..] : decoded;
        }
    }

    // ── Stubs for non-Android ─────────────────────────────────────────────

    private sealed class NullDirectoryPicker : IDirectoryPicker
    {
        public Task<string?> PickDirectoryAsync() => Task.FromResult<string?>(null);
    }

    private sealed class NullFilePicker : ISaveFilePicker
    {
        public Task<string?> PickFileAsync() => Task.FromResult<string?>(null);
    }
}
