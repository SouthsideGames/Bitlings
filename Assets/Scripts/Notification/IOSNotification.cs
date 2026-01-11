using UnityEngine;
using System.Collections;

#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

public class IOSNotification : MonoBehaviour
{
#if UNITY_IOS
    // Request authorization to send notifications
    public IEnumerator RequestAuthorization()
    {
        var request = new AuthorizationRequest(
            AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound, true);

        while (!request.IsFinished)
            yield return null;
    }

    /// <summary>
    /// Schedule a notification with seconds and a unique identifier (so multiple can coexist).
    /// </summary>
    public void SendNotificationSeconds(string identifier, string title, string body, string subTitle, int fireTimeInSeconds)
    {
        fireTimeInSeconds = Mathf.Max(60, fireTimeInSeconds);

        var timeTrigger = new iOSNotificationTimeIntervalTrigger()
        {
            TimeInterval = new System.TimeSpan(0, 0, fireTimeInSeconds),
            Repeats = false
        };

        var notification = new iOSNotification()
        {
            Identifier = string.IsNullOrEmpty(identifier) ? "notif_default" : identifier,
            Title = title,
            Body = body,
            Subtitle = subTitle,
            ShowInForeground = true,
            ForegroundPresentationOption = (PresentationOption.Alert | PresentationOption.Badge | PresentationOption.Sound),
            CategoryIdentifier = "default_category",
            ThreadIdentifier = "thread1",
            Trigger = timeTrigger,
        };

        iOSNotificationCenter.ScheduleNotification(notification);
    }

    /// <summary>
    /// Backwards compatible wrapper (hours -> seconds). Keeps your existing call sites valid.
    /// </summary>
    public void SendNotification(string title, string body, string subTitle, int fireTimeInHours)
    {
        SendNotificationSeconds("Jobs_Full", title, body, subTitle, Mathf.Max(1, fireTimeInHours) * 3600);
    }
#endif
}
