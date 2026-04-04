using UnityEngine;

// Manages the application lifecycle events
public class AppLifecycle : MonoBehaviour
{
    private static AppLifecycle _instance;
    private float _defaultFixedDeltaTime = 0.02f;

    void Awake()
    {
        if (_instance != null && _instance != this) { Destroy(gameObject); return; }
        _instance = this;
        _defaultFixedDeltaTime = Time.fixedDeltaTime;
    }

    void OnDestroy()
    {
        if (_instance == this) _instance = null;
    }

   void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            if (SaveManager.Data != null)
            {
                SaveManager.Data.lastClosedUnix = SaveManager.NowUnix();
                SaveManager.Save();
            }
        }
        else
        {
            RestoreGlobalTimeScale();
            SaveManager.OnResume();
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            RestoreGlobalTimeScale();
            SaveManager.OnResume();
        }
    }

    void OnApplicationQuit()
    {
        if (SaveManager.Data != null)
        {
            SaveManager.Data.lastClosedUnix = SaveManager.NowUnix();
            SaveManager.Save();
        }
    }

    private void RestoreGlobalTimeScale()
    {
        if (Time.timeScale >= 0.999f) return;

        Time.timeScale = 1f;
        if (_defaultFixedDeltaTime > 0f)
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
    }
}
