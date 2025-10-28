using UnityEngine;

// Manages the application lifecycle events
public class AppLifecycle : MonoBehaviour
{
   void OnApplicationPause(bool paused)
    {
        if (paused)
        {
            SaveManager.Data.lastClosedUnix = SaveManager.NowUnix();
            SaveManager.Save();
        }
        else
        {
            SaveManager.OnResume();
        }
    }

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus) SaveManager.OnResume();
    }

    void OnApplicationQuit()
    {
        SaveManager.Data.lastClosedUnix = SaveManager.NowUnix();
        SaveManager.Save();
    }
}
