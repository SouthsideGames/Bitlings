using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager I { get; private set; }
    public event Action OnSettingsChanged;

    public SettingsState settingsState => Ensure();
    private PlayerManager playerManager => SaveManager.Data;
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
        if (SaveManager.Data == null)
            return _fallback ??= new SettingsState();

        if (SaveManager.Data.settings == null)
        {
            SaveManager.Data.settings = new SettingsState();

            if (!SaveManager.IsHardWiping)
                SaveManager.Save();
        }

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
        // Broadcast to any tick-driven systems (Rift/UI/etc.) to early-out if they listen.
        GameEvents.HardResetting?.Invoke(true);

        // Disable RiftManager immediately to prevent Update() ticks while save is being rebuilt.
        if (RiftManager.I != null)
            RiftManager.I.enabled = false;

        // Hard wipe save + sidecars. SaveManager raises HardResetting internally too (safe if double).
        SaveManager.HardWipeAll(reloadFresh: true);

        if (ResourceManager.I != null)
            ResourceManager.I.InitializeNewAccountResources();

        // Apply default settings for the new account (will avoid saving if still resetting).
        ApplyDefaults();

        Time.timeScale = 1f;

        // Re-enable after reload; scene reload will rebuild singletons cleanly.
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

    }

    // ─────────────────────────────────────────────────────────
    // Existing settings
    // ─────────────────────────────────────────────────────────

    public bool GetAutoConvertDuplicates() => settingsState.autoConvertDuplicates;

    public void SetAutoConvertDuplicates(bool enabled)
    {
        if (settingsState.autoConvertDuplicates == enabled) return;
        settingsState.autoConvertDuplicates = enabled;
        Persist();
    }

    public bool GetAutoScrollBattleLog() => settingsState.autoScrollBattleLog;

    public void SetAutoScrollBattleLog(bool enabled)
    {
        if (settingsState.autoScrollBattleLog == enabled) return;
        settingsState.autoScrollBattleLog = enabled;
        Persist();
    }

    // ─────────────────────────────────────────────────────────
    // Battle QoL
    // ─────────────────────────────────────────────────────────

    public bool GetFastForwardBattle() => settingsState.fastForwardBattle;

    public void SetFastForwardBattle(bool enabled)
    {
        if (settingsState.fastForwardBattle == enabled) return;
        settingsState.fastForwardBattle = enabled;
        Persist();
    }

    public string GetCustomSeed() => settingsState.customSeed ?? string.Empty;

    public void SetCustomSeed(string seed)
    {
        seed ??= string.Empty;
        if (settingsState.customSeed == seed) return;

        settingsState.customSeed = seed;
        Persist();

        SeedService.ClearSessionSeed();
        SeedService.ApplyGlobalSeedForSession();
    }

    public bool GetUseCustomSeed() => settingsState.useCustomSeed;

    public void SetUseCustomSeed(bool enabled)
    {
        if (settingsState.useCustomSeed == enabled) return;

        settingsState.useCustomSeed = enabled;
        Persist();

        SeedService.ClearSessionSeed();
        SeedService.ApplyGlobalSeedForSession();
    }


    // ─────────────────────────────────────────────────────────
    // JOB / IDLE (matches JobManager.PullSettings())
    // ─────────────────────────────────────────────────────────

    public bool GetAutoBenchEnabled() => settingsState.autoBenchEnabled;

    public void SetAutoBenchEnabled(bool enabled)
    {
        if (settingsState.autoBenchEnabled == enabled) return;
        settingsState.autoBenchEnabled = enabled;
        Persist();
    }

    public float GetAutoBenchThreshold01() => Mathf.Clamp01(settingsState.autoBenchThreshold01);

    public void SetAutoBenchThreshold01(float threshold01)
    {
        threshold01 = Mathf.Clamp01(threshold01);
        if (Mathf.Approximately(settingsState.autoBenchThreshold01, threshold01)) return;
        settingsState.autoBenchThreshold01 = threshold01;
        Persist();
    }

    public bool GetAutoBenchAutoFill() => settingsState.autoBenchAutoFill;

    public void SetAutoBenchAutoFill(bool enabled)
    {
        if (settingsState.autoBenchAutoFill == enabled) return;
        settingsState.autoBenchAutoFill = enabled;
        Persist();
    }

    public bool GetAutoClinicReliefEnabled() => settingsState.autoClinicReliefEnabled;

    public void SetAutoClinicReliefEnabled(bool enabled)
    {
        if (settingsState.autoClinicReliefEnabled == enabled) return;
        settingsState.autoClinicReliefEnabled = enabled;
        Persist();
    }

#if UNITY_EDITOR
    public bool GetLogProductionBreakdown() => settingsState.logProductionBreakdown;

    public void SetLogProductionBreakdown(bool enabled)
    {
        if (settingsState.logProductionBreakdown == enabled) return;
        settingsState.logProductionBreakdown = enabled;
        Persist();
    }
#endif

    // ─────────────────────────────────────────────────────────
    // BATTLE / UI SETTINGS
    // ─────────────────────────────────────────────────────────

    public bool GetCondensedBattleText() => settingsState.condensedBattleText;

    public void SetCondensedBattleText(bool enabled)
    {
        if (settingsState.condensedBattleText == enabled) return;
        settingsState.condensedBattleText = enabled;
        Persist();
    }

    public bool GetCompressAutoBattleText() => settingsState.compressAutoBattleText;

    public void SetCompressAutoBattleText(bool enabled)
    {
        if (settingsState.compressAutoBattleText == enabled) return;
        settingsState.compressAutoBattleText = enabled;
        Persist();
    }

    public bool GetBattleHistoryEnabled() => settingsState.battleHistoryEnabled;

    public void SetBattleHistoryEnabled(bool enabled)
    {
        if (settingsState.battleHistoryEnabled == enabled) return;
        settingsState.battleHistoryEnabled = enabled;
        Persist();
    }

    public bool GetShowInlineBattleIcons() => settingsState.showInlineBattleIcons;

    public void SetShowInlineBattleIcons(bool enabled)
    {
        if (settingsState.showInlineBattleIcons == enabled) return;
        settingsState.showInlineBattleIcons = enabled;
        Persist();
    }

    // ─────────────────────────────────────────────────────────
    // Notifications
    // ─────────────────────────────────────────────────────────

    public bool GetNotificationsEnabled() => settingsState.notificationsEnabled;

    public void SetNotificationsEnabled(bool enabled)
    {
        if (settingsState.notificationsEnabled == enabled) return;
        settingsState.notificationsEnabled = enabled;
        Persist();
    }

    public bool GetNotifyJobStorageFull() => settingsState.notifyJobStorageFull;

    public void SetNotifyJobStorageFull(bool enabled)
    {
        if (settingsState.notifyJobStorageFull == enabled) return;
        settingsState.notifyJobStorageFull = enabled;
        Persist();
    }

    public bool GetNotifyEnergyFull() => settingsState.notifyEnergyFull;

    public void SetNotifyEnergyFull(bool enabled)
    {
        if (settingsState.notifyEnergyFull == enabled) return;
        settingsState.notifyEnergyFull = enabled;
        Persist();
    }

    public bool GetNotifyBoostExpiry() => settingsState.notifyBoostExpiry;

    public void SetNotifyBoostExpiry(bool enabled)
    {
        if (settingsState.notifyBoostExpiry == enabled) return;
        settingsState.notifyBoostExpiry = enabled;
        Persist();
    }

    public bool GetNotifyFallback24h() => settingsState.notifyFallback24h;

    public void SetNotifyFallback24h(bool enabled)
    {
        if (settingsState.notifyFallback24h == enabled) return;
        settingsState.notifyFallback24h = enabled;
        Persist();
    }

    public int GetDifficultyMode()
    {
        if (playerManager == null) return 0;

        // Lock until Rank 15
        if (playerManager.promotionRank < 15) return 0;

        playerManager.settings ??= new SettingsState();
        return Mathf.Clamp(playerManager.settings.difficultyMode, 0, 2);
    }

    public void SetDifficultyMode(int mode)
    {
        if (playerManager == null) return;

        // Lock until Rank 15
        if (playerManager.promotionRank < 15)
            mode = 0;

        playerManager.settings ??= new SettingsState();
        playerManager.settings.difficultyMode = Mathf.Clamp(mode, 0, 2);

        SaveManager.Save();
    }
}
