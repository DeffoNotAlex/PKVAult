using Android.App;
using Android.Content;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Platforms.Android;

public class AndroidFilePicker : ISaveFilePicker
{
    public async Task<string?> PickFileAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null) return null;

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("*/*");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);

        var tcs = new TaskCompletionSource<string?>();

        void Handler(global::Android.Net.Uri? uri)
        {
            if (uri != null)
            {
                activity.ContentResolver?.TakePersistableUriPermission(
                    uri,
                    ActivityFlags.GrantReadUriPermission);
                tcs.TrySetResult(uri.ToString());
            }
            else
            {
                tcs.TrySetResult(null);
            }
            MainActivity.FilePickResult -= Handler;
        }

        MainActivity.FilePickResult += Handler;
        activity.StartActivityForResult(intent, MainActivity.RequestPickFile);

        return await tcs.Task;
    }
}
