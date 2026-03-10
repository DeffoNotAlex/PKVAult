using Android.App;
using Android.Content;
using PKHeX.Mobile.Services;

namespace PKHeX.Mobile.Platforms.Android;

public class AndroidDirectoryPicker : IDirectoryPicker
{
    public async Task<string?> PickDirectoryAsync()
    {
        var activity = Platform.CurrentActivity;
        if (activity == null) return null;

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);

        var tcs = new TaskCompletionSource<string?>();

        void Handler(global::Android.Net.Uri? uri)
        {
            if (uri != null)
            {
                activity.ContentResolver?.TakePersistableUriPermission(
                    uri,
                    ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
                tcs.TrySetResult(uri.ToString());
            }
            else
            {
                tcs.TrySetResult(null);
            }
            MainActivity.DirectoryPickResult -= Handler;
        }

        MainActivity.DirectoryPickResult += Handler;
        activity.StartActivityForResult(intent, MainActivity.RequestPickDirectory);

        return await tcs.Task;
    }
}
