using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager I { get; private set; }
    public event Action OnSettingsChanged;

    public SettingsState S => Ensure();
    private SettingsState _fallback;

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

    private SettingsState Ensure()
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

    private void Persist()
    {
        if (SaveManager.Data != null)
            SaveManager.Save();

        OnSettingsChanged?.Invoke();
    }

    // ─────────────────────────────────────────────────────────
    // Defaults / Reset
    // ─────────────────────────────────────────────────────────

    public void ApplyDefaults()
    {
        if (SaveManager.Data == null) return;

        SaveManager.Data.settings = new SettingsState();
        SaveManager.Save();
        OnSettingsChanged?.Invoke();

        if (AudioManager.I != null)
            AudioManager.I.SendMessage("ApplyVolumes", SendMessageOptions.DontRequireReceiver);
    }

    public void OnReset()
    {
        SaveManager.ClearAll();
        SaveManager.LoadOrCreate();

        if (ResourceManager.I != null)
            ResourceManager.I.InitializeNewAccountResources();

        ApplyDefaults();

        Time.timeScale = 1f;
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // ─────────────────────────────────────────────────────────
    // Existing settings (examples you already had / we kept)
    // ─────────────────────────────────────────────────────────

    public bool GetAutoConvertDuplicates() => S.autoConvertDuplicates;

    public void SetAutoConvertDuplicates(bool enabled)
    {
        if (S.autoConvertDuplicates == enabled) return;
        S.autoConvertDuplicates = enabled;
        Persist();
    }

    public bool GetAutoScrollBattleLog() => S.autoScrollBattleLog;

    public void SetAutoScrollBattleLog(bool enabled)
    {
        if (S.autoScrollBattleLog == enabled) return;
        S.autoScrollBattleLog = enabled;
        Persist();
    }

    public string GetCustomSeed() => S.customSeed ?? string.Empty;

    public void SetCustomSeed(string seed)
    {
        seed ??= string.Empty;
        if (S.customSeed == seed) return;
        S.customSeed = seed;
        Persist();
    }

    public bool GetUseCustomSeed() => S.useCustomSeed;

    public void SetUseCustomSeed(bool enabled)
    {
        if (S.useCustomSeed == enabled) return;
        S.useCustomSeed = enabled;
        Persist();
    }

    // ─────────────────────────────────────────────────────────
    // BATTLE / UI SETTINGS (RESTORED to fix your compile errors)
    // ─────────────────────────────────────────────────────────

    public bool GetCondensedBattleText() => S.condensedBattleText;

    public void SetCondensedBattleText(bool enabled)
    {
        if (S.condensedBattleText == enabled) return;
        S.condensedBattleText = enabled;
        Persist();
    }

    public bool GetCompressAutoBattleText() => S.compressAutoBattleText;

    public void SetCompressAutoBattleText(bool enabled)
    {
        if (S.compressAutoBattleText == enabled) return;
        S.compressAutoBattleText = enabled;
        Persist();
    }

    public bool GetBattleHistoryEnabled() => S.battleHistoryEnabled;

    public void SetBattleHistoryEnabled(bool enabled)
    {
        if (S.battleHistoryEnabled == enabled) return;
        S.battleHistoryEnabled = enabled;
        Persist();
    }

    public bool GetShowInlineBattleIcons() => S.showInlineBattleIcons;

    public void SetShowInlineBattleIcons(bool enabled)
    {
        if (S.showInlineBattleIcons == enabled) return;
        S.showInlineBattleIcons = enabled;
        Persist();
    }

    // ─────────────────────────────────────────────────────────
    // Notifications (NEW)
    // ─────────────────────────────────────────────────────────

    public bool GetNotificationsEnabled() => S.notificationsEnabled;

    public void SetNotificationsEnabled(bool enabled)
    {
        if (S.notificationsEnabled == enabled) return;
        S.notificationsEnabled = enabled;
        Persist();
    }

    public bool GetNotifyJobStorageFull() => S.notifyJobStorageFull;

    public void SetNotifyJobStorageFull(bool enabled)
    {
        if (S.notifyJobStorageFull == enabled) return;
        S.notifyJobStorageFull = enabled;
        Persist();
    }

    public bool GetNotifyEnergyFull() => S.notifyEnergyFull;

    public void SetNotifyEnergyFull(bool enabled)
    {
        if (S.notifyEnergyFull == enabled) return;
        S.notifyEnergyFull = enabled;
        Persist();
    }

    public bool GetNotifyBoostExpiry() => S.notifyBoostExpiry;

    public void SetNotifyBoostExpiry(bool enabled)
    {
        if (S.notifyBoostExpiry == enabled) return;
        S.notifyBoostExpiry = enabled;
        Persist();
    }

    public bool GetNotifyFallback24h() => S.notifyFallback24h;

    public void SetNotifyFallback24h(bool enabled)
    {
        if (S.notifyFallback24h == enabled) return;
        S.notifyFallback24h = enabled;
        Persist();
    }
}
