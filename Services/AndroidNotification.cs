#nullable disable
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.Core.App;

namespace ImageVault.Services;

public static class AndroidNotification
{
    private const string ChannelId = "imagevault_notify";
    private const int ProgressNotificationId = 1001;
    private static int _messageId = ProgressNotificationId + 1;
    private static bool _channelInitialized;

    private static void EnsureChannel()
    {
        if (!_channelInitialized && OperatingSystem.IsAndroidVersionAtLeast(26))
        {
            var manager = (NotificationManager)Platform.AppContext!
                .GetSystemService(Context.NotificationService);
            var channel = new NotificationChannel(ChannelId, "Image Import", NotificationImportance.High)
            {
                Description = "Image import notifications"
            };
            manager.CreateNotificationChannel(channel);
            _channelInitialized = true;
        }
    }

    public static Task Show(string title, string message)
    {
        if (!HasPermission())
            return Task.CompletedTask;

        var pendingIntent = BuildPendingIntent();

        EnsureChannel();

        var builder = new NotificationCompat.Builder(Platform.AppContext, ChannelId)
            .SetContentIntent(pendingIntent)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetAutoCancel(true);

        var native = builder.Build();
        if (native != null)
            NotificationManagerCompat.From(Platform.AppContext)
                .Notify(Interlocked.Increment(ref _messageId), native);

        return Task.CompletedTask;
    }

    public static Task ShowProgress(string title, string message, int current, int max)
    {
        if (!HasPermission())
            return Task.CompletedTask;

        var pendingIntent = BuildPendingIntent();
        EnsureChannel();

        var builder = new NotificationCompat.Builder(Platform.AppContext, ChannelId)
            .SetContentIntent(pendingIntent)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetProgress(max, current, false)
            .SetOngoing(true);

        var native = builder.Build();
        if (native != null)
            NotificationManagerCompat.From(Platform.AppContext)
                .Notify(ProgressNotificationId, native);

        return Task.CompletedTask;
    }

    public static Task ShowDone(string title, string message)
    {
        if (!HasPermission())
            return Task.CompletedTask;

        var pendingIntent = BuildPendingIntent();
        EnsureChannel();

        var builder = new NotificationCompat.Builder(Platform.AppContext, ChannelId)
            .SetContentIntent(pendingIntent)
            .SetContentTitle(title)
            .SetContentText(message)
            .SetSmallIcon(Android.Resource.Drawable.IcDialogInfo)
            .SetProgress(0, 0, false)
            .SetOngoing(false)
            .SetAutoCancel(true);

        var native = builder.Build();
        if (native != null)
            NotificationManagerCompat.From(Platform.AppContext)
                .Notify(ProgressNotificationId, native);

        return Task.CompletedTask;
    }

    private static bool HasPermission()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            return Platform.AppContext!.CheckSelfPermission(Android.Manifest.Permission.PostNotifications)
                == Android.Content.PM.Permission.Granted;
        return true;
    }

    private static PendingIntent BuildPendingIntent()
    {
        var intent = Platform.AppContext!.PackageManager!
            .GetLaunchIntentForPackage(Platform.AppContext.PackageName!);
        if (intent != null)
            intent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);

        var flags = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable
            : PendingIntentFlags.UpdateCurrent;

        return PendingIntent.GetActivity(Platform.AppContext, 0, intent, flags);
    }

    public static async Task RequestPermissionAsync()
    {
        if (OperatingSystem.IsAndroidVersionAtLeast(33))
            await Permissions.RequestAsync<Permissions.PostNotifications>();
    }
}
