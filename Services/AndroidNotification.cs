#nullable disable
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace ImageVault.Services;

public static class AndroidNotification
{
    private const string ChannelId = "imagevault_notify";
    private static int _messageId;
    private static bool _channelInitialized;

    public static Task Show(string title, string message)
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
        {
            if (Platform.AppContext!.CheckSelfPermission(Android.Manifest.Permission.PostNotifications)
                != Android.Content.PM.Permission.Granted)
                return Task.CompletedTask;
        }

        if (!_channelInitialized && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var manager = (NotificationManager)Platform.AppContext!
                .GetSystemService(Context.NotificationService)!;
            var channel = new NotificationChannel(ChannelId, "Image Import", NotificationImportance.High)
            {
                Description = "Image import notifications"
            };
            manager.CreateNotificationChannel(channel);
            _channelInitialized = true;
        }

        var intent = Platform.AppContext!.PackageManager!
            .GetLaunchIntentForPackage(Platform.AppContext.PackageName!);
        if (intent != null)
            intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        var pendingIntentFlags = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;

        var pendingIntent = PendingIntent.GetActivity(
            Platform.AppContext, 0, intent, pendingIntentFlags);

        var builder = new NotificationCompat.Builder(Platform.AppContext!, ChannelId)!
            .SetContentIntent(pendingIntent)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true);

        var native = builder.Build();
        if (native != null)
        {
            NotificationManagerCompat.From(Platform.AppContext!)
                .Notify(Interlocked.Increment(ref _messageId), native);
        }

        return Task.CompletedTask;
    }

    public static async Task RequestPermissionAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            await Permissions.RequestAsync<Permissions.PostNotifications>();
    }
}
