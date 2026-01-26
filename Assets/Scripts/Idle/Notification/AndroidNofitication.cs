using UnityEngine;

#if UNITY_ANDROID
using Unity.Notifications.Android;
using UnityEngine.Android;
#endif

public class AndroidNofitication : MonoBehaviour
{
#if UNITY_ANDROID
    public void RequestAuthorization()
    {
        // Android 13+ needs runtime notification permission.
        if (!Permission.HasUserAuthorizedPermission("android.permission.POST_NOTIFICATIONS"))
            Permission.RequestUserPermission("android.permission.POST_NOTIFICATIONS");
    }

    public void RegisterNotificationChannel()
    {
        var channel = new AndroidNotificationChannel()
        {
            Id = "channel_id",
            Name = "Default Channel",
            Importance = Importance.Default,
            Description = "Game notifications",
        };

        AndroidNotificationCenter.RegisterNotificationChannel(channel);
    }

    /// <summary>Schedule a notification using seconds (precise).</summary>
    public int SendNotificationSeconds(string title, string text, int fireTimeInSeconds)
    {
        fireTimeInSeconds = Mathf.Max(60, fireTimeInSeconds);

        var notification = new AndroidNotification
        {
            Title = title,
            Text = text,
            FireTime = System.DateTime.Now.AddSeconds(fireTimeInSeconds),
            SmallIcon = "icon_0",
            LargeIcon = "icon_1",
        };

        return AndroidNotificationCenter.SendNotification(notification, "channel_id");
    }

    /// <summary>Backwards compatible wrapper (hours -> seconds).</summary>
    public int SendNotification(string title, string text, int fireTimeInHours)
    {
        return SendNotificationSeconds(title, text, Mathf.Max(1, fireTimeInHours) * 3600);
    }
#endif
}
