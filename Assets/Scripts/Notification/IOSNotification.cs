using UnityEngine;
#if UNITY_IOS
using Unity.Notifications.iOS;
#endif
using System.Collections;

public class IOSNotification : MonoBehaviour
{
    #if UNITY_IOS
    //Request authorization to send notifications
    public IEnumerator RequestAuthorization()
    {
        var request = new AuthorizationRequest(AuthorizationOption.Alert | AuthorizationOption.Badge | AuthorizationOption.Sound, true);
        while (!request.IsFinished)
        {
            yield return null;
        }
    }

    //set up notification

    public void SendNotification(string title, string body, string subTitle, int fireTimeInHours)
    {
        var timeTrigger = new iOSNotificationTimeIntervalTrigger()
        {
            TimeInterval = new System.TimeSpan(fireTimeInHours, 0, 0),
            Repeats = false
        };

        var notification = new iOSNotification()
        {
            Identifier = "Jobs_Full",
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

    #endif
}
