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

        // Recover from a crash that occurred mid-battle during an Iron run
        var ironState = ExecutiveTrialMetaSave.Load();
        if (ironState != null && ironState.runActive && ironState.battleInProgress) // FIXED: runActive guard prevents spurious trigger on stale flag from a previous completed run
        {
            Debug.LogWarning("[ExecutiveTrial] Crash detected mid-battle - applying recovery loss.");

            ironState.battleInProgress = false; // FIXED: clear the checkpoint flag

            // Apply the loss: a crash mid-battle is treated as a defeat.
            // ShowGameOver() cannot be called here (MonoBehaviour systems not yet ready),
            // so we apply the terminal state directly to the persisted data.
            ironState.runActive = false; // FIXED: one loss always ends a run - mirrors ShowGameOver() setting _state.runActive = false

            ExecutiveTrialMetaSave.Save(ironState); // FIXED: persist the corrected state before any system initialises
            Debug.LogWarning("[ExecutiveTrial] Recovery complete - run marked ended. Game-over panel will show on next Executive Trial open.");
        }
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
                // Force push to cloud before suspend
                _ = CloudSaveSync.ForcePushArenaDataAsync();
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
            HonorService.CheckWeekReset();
        }
    }

    void OnApplicationQuit()
    {
        if (SaveManager.Data != null)
        {
            SaveManager.Data.lastClosedUnix = SaveManager.NowUnix();
            SaveManager.Save();
            // Force push to cloud before quit
            _ = CloudSaveSync.ForcePushArenaDataAsync();
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
