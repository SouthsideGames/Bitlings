using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager I { get; private set; }
    public event Action OnSettingsChanged;

    public SettingsState S => Ensure();
    private SettingsState _fallback;

    private bool _bootstrapped;

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
        // During boot / hard reset windows, some systems may query settings before SaveManager has loaded.
        if (SaveManager.Data == null)
            return _fallback ??= new SettingsState();

        if (SaveManager.Data.settings == null)
        {
            SaveManager.Data.settings = new SettingsState();

            // During hard wipe/reset flows, avoid writing mid-rebuild.
            if (!SaveManager.IsHardWiping)
                SaveManager.Save();
        }

        // One-time bootstrap: make sure new fields have sane defaults.
        // IMPORTANT: do not overwrite existing user preferences; only normalize missing/invalid.
        if (!_bootstrapped)
        {
            _bootstrapped = true;

            bool changed = BootstrapDefaultsIfNeeded(SaveManager.Data.settings);
            if (changed && !SaveManager.IsHardWiping)
                SaveManager.Save();
        }

        return SaveManager.Data.settings;
    }

    private bool BootstrapDefaultsIfNeeded(SettingsState s)
    {
        if (s == null) return false;

        bool changed = false;

        // Defensive normalization for strings
        if (s.customSeed == null)
        {
            s.customSeed = string.Empty;
            changed = true;
        }

        // Normalize thresholds in [0..1]
        if (s.autoBenchThreshold01 < 0f || s.autoBenchThreshold01 > 1f)
        {
            s.autoBenchThreshold01 = Mathf.Clamp01(s.autoBenchThreshold01);
            changed = true;
        }

        return changed;
    }

    private void Persist()
    {
        // Never write during a reset/reload cycle.
        if (SaveManager.Data != null && !SaveManager.IsHardWiping)
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

        // Avoid write if we're in a reset cycle; OnReset will Save via SaveManager anyway.
        if (!SaveManager.IsHardWiping)
            SaveManager.Save();

        OnSettingsChanged?.Invoke();

        if (AudioManager.I != null)
            AudioManager.I.SendMessage("ApplyVolumes", SendMessageOptions.DontRequireReceiver);
    }

    public void OnReset()
    {
        // Broadcast to any tick-driven systems (Encounter/UI/etc.) to early-out if they listen.
        GameEvents.HardResetting?.Invoke(true);

        // Disable EncounterManager immediately to prevent Update() ticks while save is being rebuilt.
        if (EncounterManager.I != null)
            EncounterManager.I.enabled = false;

        // Hard wipe save + sidecars. SaveManager raises HardResetting internally too (safe if double).
        SaveManager.HardWipeAll(reloadFresh: true);

        if (ResourceManager.I != null)
            ResourceManager.I.InitializeNewAccountResources();

        // Apply default settings for the new account (will avoid saving if still resetting).
        ApplyDefaults();

        Time.timeScale = 1f;

        // Re-enable after reload; scene reload will rebuild singletons cleanly.
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

        // (No need to invoke HardResetting(false) here because the scene is reloading.)
    }

    // ─────────────────────────────────────────────────────────
    // Existing settings
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
    // JOB / IDLE (matches JobManager.PullSettings())
    // ─────────────────────────────────────────────────────────

    public bool GetAutoBenchEnabled() => S.autoBenchEnabled;

    public void SetAutoBenchEnabled(bool enabled)
    {
        if (S.autoBenchEnabled == enabled) return;
        S.autoBenchEnabled = enabled;
        Persist();
    }

    public float GetAutoBenchThreshold01() => Mathf.Clamp01(S.autoBenchThreshold01);

    public void SetAutoBenchThreshold01(float threshold01)
    {
        threshold01 = Mathf.Clamp01(threshold01);
        if (Mathf.Approximately(S.autoBenchThreshold01, threshold01)) return;
        S.autoBenchThreshold01 = threshold01;
        Persist();
    }

    public bool GetAutoBenchAutoFill() => S.autoBenchAutoFill;

    public void SetAutoBenchAutoFill(bool enabled)
    {
        if (S.autoBenchAutoFill == enabled) return;
        S.autoBenchAutoFill = enabled;
        Persist();
    }

    public bool GetAutoClinicReliefEnabled() => S.autoClinicReliefEnabled;

    public void SetAutoClinicReliefEnabled(bool enabled)
    {
        if (S.autoClinicReliefEnabled == enabled) return;
        S.autoClinicReliefEnabled = enabled;
        Persist();
    }

#if UNITY_EDITOR
    public bool GetLogProductionBreakdown() => S.logProductionBreakdown;

    public void SetLogProductionBreakdown(bool enabled)
    {
        if (S.logProductionBreakdown == enabled) return;
        S.logProductionBreakdown = enabled;
        Persist();
    }
#endif

    // ─────────────────────────────────────────────────────────
    // BATTLE / UI SETTINGS
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
    // Notifications
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
