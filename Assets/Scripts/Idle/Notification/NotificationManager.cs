using UnityEngine;

#if UNITY_ANDROID
using Unity.Notifications.Android;
#endif

#if UNITY_IOS
using Unity.Notifications.iOS;
#endif

public class NotificationManager : MonoBehaviour
{
    public static NotificationManager I { get; private set; }

    [SerializeField] private AndroidNofitication androidNotification;
    [SerializeField] private IOSNotification iosNotification;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;
        // Optional:
        // DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
#if UNITY_ANDROID
        if (androidNotification)
        {
            androidNotification.RequestAuthorization();
            androidNotification.RegisterNotificationChannel();
        }
#endif

#if UNITY_IOS
        if (iosNotification)
            StartCoroutine(iosNotification.RequestAuthorization());
#endif
    }

    void OnApplicationFocus(bool focus)
    {
        // Foreground: clear stale schedules.
        if (focus)
        {
            CancelAllScheduled();
            return;
        }

        // Background: schedule based on SettingsState.
        ScheduleAllOnAppBackground();
    }

    // ─────────────────────────────────────────────────────────────
    // Scheduling Core
    // ─────────────────────────────────────────────────────────────

    private void CancelAllScheduled()
    {
#if UNITY_ANDROID
        AndroidNotificationCenter.CancelAllNotifications();
#endif
#if UNITY_IOS
        iOSNotificationCenter.RemoveAllScheduledNotifications();
#endif
    }

    private void ScheduleAllOnAppBackground()
    {
        CancelAllScheduled();

        var data = SaveManager.Data;
        if (data == null) return;

        var s = (SettingsManager.I != null) ? SettingsManager.I.settingsState : data.settings;
        if (s == null) return;

        if (!s.notificationsEnabled)
            return;

        // 24h fallback reminder (optional)
        if (s.notifyFallback24h)
        {
            ScheduleSeconds(
                id: "fallback_24h",
                title: "Job Sites Full",
                body: "Check back to collect your resources.",
                subtitle: "Your Bitlings Await",
                secondsFromNow: 24 * 3600
            );
        }

        // Job storage full soonest
        if (s.notifyJobStorageFull)
        {
            int secs = ComputeSecondsUntilAnyJobSiteFull();
            if (secs > 0)
            {
                ScheduleSeconds(
                    id: "jobs_storage_full",
                    title: "Storage Full",
                    body: "A job site hit its storage cap. Collect to avoid waste.",
                    subtitle: "Production Paused",
                    secondsFromNow: secs
                );
            }
        }

        // Energy full (EncounterManager has GetSecondsUntilFull in your project)
        if (s.notifyEnergyFull)
        {
            int secs = ComputeSecondsUntilEnergyFull();
            if (secs > 0)
            {
                ScheduleSeconds(
                    id: "energy_full",
                    title: "Energy Full",
                    body: "Your energy is fully recharged.",
                    subtitle: "Ready for Field Ops",
                    secondsFromNow: secs
                );
            }
        }

        // Next boost expiry (based on your PlayerManager lists)
        if (s.notifyBoostExpiry)
        {
            int secs = ComputeSecondsUntilNextBoostExpiry();
            if (secs > 0)
            {
                ScheduleSeconds(
                    id: "boost_expired",
                    title: "Boost Expired",
                    body: "One of your active boosts has ended.",
                    subtitle: "Reapply When Ready",
                    secondsFromNow: secs
                );
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    // DEBUG
    // ─────────────────────────────────────────────────────────────

    public void DebugScheduleTestNotifications()
    {
        CancelAllScheduled();

        // Keep >= 60 seconds for reliability.
        ScheduleSeconds("dbg_1", "Debug: Ping 1", "If you see this, notifications are working.", "Debug", 90);
        ScheduleSeconds("dbg_2", "Debug: Ping 2", "Second test notification.", "Debug", 150);
        ScheduleSeconds("dbg_3", "Debug: Ping 3", "Third test notification.", "Debug", 240);

#if UNITY_EDITOR
        Debug.Log("[NotificationManager] Debug scheduled: 90s, 150s, 240s.");
#endif
    }

    public void DebugClearScheduledNotifications()
    {
        CancelAllScheduled();
#if UNITY_EDITOR
        Debug.Log("[NotificationManager] Cleared scheduled notifications.");
#endif
    }

    // ─────────────────────────────────────────────────────────────
    // Platform wrapper
    // ─────────────────────────────────────────────────────────────

    private void ScheduleSeconds(string id, string title, string body, string subtitle, int secondsFromNow)
    {
        secondsFromNow = Mathf.Max(60, secondsFromNow);

#if UNITY_ANDROID
        if (androidNotification)
            androidNotification.SendNotificationSeconds(title, body, secondsFromNow);
#endif

#if UNITY_IOS
        if (iosNotification)
            iosNotification.SendNotificationSeconds(id, title, body, subtitle, secondsFromNow);
#endif
    }

    // ─────────────────────────────────────────────────────────────
    // Time computations (built for YOUR systems)
    // ─────────────────────────────────────────────────────────────

    private int ComputeSecondsUntilEnergyFull()
    {
        try
        {
            if (EncounterManager.I == null) return 0;
            int secs = EncounterManager.I.GetSecondsUntilFull();
            return secs > 0 ? secs : 0;
        }
        catch { return 0; }
    }

    private int ComputeSecondsUntilAnyJobSiteFull()
    {
        try
        {
            if (JobManager.I == null || JobManager.I.States == null) return 0;

            float bestSeconds = float.PositiveInfinity;

            for (int i = 0; i < JobManager.I.States.Count; i++)
            {
                var st = JobManager.I.States[i];
                if (st == null || st.config == null) continue;

                int cap = JobManager.I.GetEffectiveStorageCap(st.config);
                if (cap <= 0) continue;

                if (st.storedUnits >= cap)
                {
                    bestSeconds = Mathf.Min(bestSeconds, 60f);
                    continue;
                }

                float ratePerHour = Mathf.Max(0f, st.cachedRatePerHour);
                if (ratePerHour <= 0.0001f) continue;

                float remaining = Mathf.Max(0f, cap - (st.storedUnits + st.storedRemainder));
                float hours = remaining / ratePerHour;
                float secs = hours * 3600f;

                if (secs > 0f)
                    bestSeconds = Mathf.Min(bestSeconds, secs);
            }

            if (float.IsInfinity(bestSeconds)) return 0;
            return Mathf.Clamp(Mathf.CeilToInt(bestSeconds), 60, 7 * 24 * 3600);
        }
        catch { return 0; }
    }

    private int ComputeSecondsUntilNextBoostExpiry()
    {
        try
        {
            var data = SaveManager.Data;
            if (data == null) return 0;

            long now = SaveManager.NowUnix();
            long best = long.MaxValue;

            if (data.activeFlyers != null)
                for (int i = 0; i < data.activeFlyers.Count; i++)
                {
                    var b = data.activeFlyers[i];
                    if (b.expireUnix > now) best = System.Math.Min(best, b.expireUnix);
                }

            if (data.activeWorkOrders != null)
                for (int i = 0; i < data.activeWorkOrders.Count; i++)
                {
                    var b = data.activeWorkOrders[i];
                    if (b.expireUnix > now) best = System.Math.Min(best, b.expireUnix);
                }

            if (data.activeFavorBoosts != null)
                for (int i = 0; i < data.activeFavorBoosts.Count; i++)
                {
                    var b = data.activeFavorBoosts[i];
                    if (b.expireUnix > now) best = System.Math.Min(best, b.expireUnix);
                }

            if (data.activeShinyBoosts != null)
                for (int i = 0; i < data.activeShinyBoosts.Count; i++)
                {
                    var b = data.activeShinyBoosts[i];
                    if (b.expireUnix > now) best = System.Math.Min(best, b.expireUnix);
                }

            if (best == long.MaxValue) return 0;

            long delta = best - now;
            if (delta <= 0) return 0;

            return Mathf.Clamp((int)delta, 60, 7 * 24 * 3600);
        }
        catch { return 0; }
    }
}
