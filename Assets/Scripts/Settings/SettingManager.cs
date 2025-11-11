using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager I { get; private set; }
    public event Action OnSettingsChanged;

    public SettingsState S => Ensure();
    SettingsState _fallback;

    void Awake()
    {
        if (I != null && I != this)
        {
            enabled = false;
            return;
        }
        I = this;
        Ensure();
    }

    SettingsState Ensure()
    {
        if (SaveManager.Data == null)
            return _fallback ??= new SettingsState();

        if (SaveManager.Data.settings == null)
        {
            SaveManager.Data.settings = new SettingsState();
            SaveManager.Save();
        }
        return SaveManager.Data.settings;
    }

    void Persist()
    {
        if (SaveManager.Data != null) SaveManager.Save();
        OnSettingsChanged?.Invoke();
    }

    // ─────────────────────────────────────────────────────────
    // Defaults / Reset
    // ─────────────────────────────────────────────────────────

    public void ApplyDefaults()
    {
        if (SaveManager.Data == null) return;

        // Fresh defaults from SettingsState ctor
        SaveManager.Data.settings = new SettingsState(); // e.g., autoConvertDuplicates = true, autoScrollBattleLog = true
        SaveManager.Save();
        OnSettingsChanged?.Invoke();

        if (AudioManager.I != null)
            AudioManager.I.SendMessage("ApplyVolumes", SendMessageOptions.DontRequireReceiver);
    }

    public void OnReset()
    {
        SaveManager.ClearAll();
        ApplyDefaults();
        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ─────────────────────────────────────────────────────────
    // Duplicate policy accessors
    // ─────────────────────────────────────────────────────────

    public bool GetAutoConvertDuplicates() => S.autoConvertDuplicates;

    public void SetAutoConvertDuplicates(bool enabled)
    {
        if (S.autoConvertDuplicates == enabled) return;
        S.autoConvertDuplicates = enabled;
        Persist();
    }

    // ─────────────────────────────────────────────────────────
    // Battle Log: Auto-scroll setting
    // ─────────────────────────────────────────────────────────

    public bool GetAutoScrollBattleLog() => S.autoScrollBattleLog;

    public void SetAutoScrollBattleLog(bool enabled)
    {
        if (S.autoScrollBattleLog == enabled) return;
        S.autoScrollBattleLog = enabled;
        Persist();
    }
}
