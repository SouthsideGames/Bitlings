using UnityEngine;
#if UNITY_ANDROID
using Unity.Notifications.Android;
using UnityEngine.Android;
#endif

#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

public class NotificationManager : MonoBehaviour
{
    [SerializeField] private AndroidNotification androidNotification;
    [SerializeField] private IOSNotification iosNotification;


    void Start()
    {
#if UNITY_ANDROID
        androidNotification.RequestAuthorization();
        androidNotification.RegisterNotificationChannel();
#endif
#if UNITY_IOS
        StartCoroutine(iosNotification.RequestAuthorization());
#endif
    }

    void OnApplicationFocus(bool focus)
    {
        if (focus == false)
        {
            #if UNITY_ANDROID
            AndroidNotificationCenter.CancelAllNotifications();
            androidNotification.SendNotification("Job Sites Full", "Check back to collect your resources", 24);
            #endif

            #if UNITY_IOS
            iOSNotificationCenter.RemoveAllScheduledNotifications();
            iosNotification.SendNotification("Job Sites Full", "Check back to collect your resources", "Your Bitlings Await", 24);
            #endif
        }
    }
}
