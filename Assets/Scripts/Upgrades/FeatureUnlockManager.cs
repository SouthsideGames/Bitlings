using System;
using System.Collections.Generic;
using UnityEngine;

public class FeatureUnlockManager : MonoBehaviour
{
    public static FeatureUnlockManager I { get; private set; }

    [Header("Starting State")]
    [Tooltip("Features that should be unlocked for a brand new player.")]
    [SerializeField] private List<FeatureId> startingUnlocked = new List<FeatureId>();

    private readonly HashSet<FeatureId> _unlocked = new HashSet<FeatureId>();

    /// <summary>Fired whenever a feature is newly unlocked.</summary>
    public event Action<FeatureId> OnFeatureUnlocked;

    // Internal persistence key
    private const string PlayerPrefsKey = "FeatureUnlocks_JSON";

    [Serializable]
    private class FeatureUnlockSaveWrapper
    {
        public List<string> ids;
    }

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }

        I = this;

        LoadFromPrefsOrDefaults();
    }

    // ─────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────

    public bool IsUnlocked(FeatureId feature)
    {
        return _unlocked.Contains(feature);
    }

    /// <summary>
    /// Unlocks the feature if not already unlocked.
    /// Returns true if this call actually changed the state.
    /// </summary>
    public bool Unlock(FeatureId feature)
    {
        if (feature == FeatureId.None)
            return false;

        if (_unlocked.Contains(feature))
            return false;

        _unlocked.Add(feature);

        ApplySideEffectsForFeature(feature);
        OnFeatureUnlocked?.Invoke(feature);
        GameEvents.RaiseFeatureUnlocked(feature);
        SaveToPrefs();

        return true;
    }

    /// <summary>
    /// DEV/RESET SUPPORT (OPTION A)
    /// Full wipe of purchased feature unlocks (PlayerPrefs),
    /// restoring ONLY starting defaults.
    ///
    /// Use this for account reset / hard wipe flows.
    /// </summary>
    public void HardResetAllUnlocksToDefaults(bool fireEvents = false)
    {
        // Clear runtime state
        _unlocked.Clear();

        // Delete persisted purchased state
        if (PlayerPrefs.HasKey(PlayerPrefsKey))
            PlayerPrefs.DeleteKey(PlayerPrefsKey);

        PlayerPrefs.Save();

        // Re-apply defaults
        ApplyStartingDefaults();

        // Optional: broadcast unlock events for defaults (usually unnecessary during hard reset)
        if (fireEvents)
        {
            foreach (var f in _unlocked)
            {
                OnFeatureUnlocked?.Invoke(f);
                 GameEvents.RaiseFeatureUnlocked(f);
            }
        }
    }

    /// <summary>
    /// Clears ONLY job-related purchased feature unlocks, keeping other upgrades intact.
    /// Useful if you ever support a "reset jobs only" action.
    /// </summary>
    public void ClearJobUnlockFeaturesToDefaults(bool fireEvents = false)
    {
        // Remove job feature ids
        foreach (JobType j in Enum.GetValues(typeof(JobType)))
        {
            if (j == JobType.None) continue;

            if (FeatureIdJobs.TryGetJobFeature(j, out var feat) && feat != FeatureId.None)
                _unlocked.Remove(feat);
        }

        // Ensure defaults still present
        foreach (var f in startingUnlocked)
            _unlocked.Add(f);

        ApplySideEffectsForAllUnlocked();
        SaveToPrefs();

        if (fireEvents)
        {
            foreach (var f in _unlocked)
            {
                OnFeatureUnlocked?.Invoke(f);
                GameEvents.RaiseFeatureUnlocked(f);
            }
        }
    }

    // Optional hooks for future SaveManager JSON integration:
    public List<string> GetUnlockedIdsForSave()
    {
        var list = new List<string>(_unlocked.Count);
        foreach (var f in _unlocked)
            list.Add(f.ToString());
        return list;
    }

    public void RestoreFromSavedIds(List<string> savedIds)
    {
        _unlocked.Clear();

        if (savedIds != null)
        {
            foreach (var s in savedIds)
            {
                if (Enum.TryParse(s, out FeatureId f))
                    _unlocked.Add(f);
            }
        }

        // Always apply starting defaults too
        foreach (var f in startingUnlocked)
            _unlocked.Add(f);

        ApplySideEffectsForAllUnlocked();
    }

    // ─────────────────────────────────────────────────────────────
    // Internal: PlayerPrefs persistence
    // ─────────────────────────────────────────────────────────────

    private void LoadFromPrefsOrDefaults()
    {
        _unlocked.Clear();

        if (!PlayerPrefs.HasKey(PlayerPrefsKey))
        {
            ApplyStartingDefaults();
            return;
        }

        string json = PlayerPrefs.GetString(PlayerPrefsKey, string.Empty);
        if (string.IsNullOrEmpty(json))
        {
            ApplyStartingDefaults();
            return;
        }

        try
        {
            var wrapper = JsonUtility.FromJson<FeatureUnlockSaveWrapper>(json);
            if (wrapper != null && wrapper.ids != null)
            {
                RestoreFromSavedIds(wrapper.ids);
            }
            else
            {
                ApplyStartingDefaults();
            }
        }
        catch
        {
            ApplyStartingDefaults();
        }
    }

    private void SaveToPrefs()
    {
        var wrapper = new FeatureUnlockSaveWrapper
        {
            ids = GetUnlockedIdsForSave()
        };
        string json = JsonUtility.ToJson(wrapper);
        PlayerPrefs.SetString(PlayerPrefsKey, json);
        PlayerPrefs.Save();
    }

    private void ApplyStartingDefaults()
    {
        _unlocked.Clear();
        foreach (var f in startingUnlocked)
            _unlocked.Add(f);

        ApplySideEffectsForAllUnlocked();
    }

    // ─────────────────────────────────────────────────────────────
    // Feature-specific side effects
    // ─────────────────────────────────────────────────────────────

    private void ApplySideEffectsForAllUnlocked()
    {
        foreach (var f in _unlocked)
            ApplySideEffectsForFeature(f);
    }

    private void ApplySideEffectsForFeature(FeatureId feature)
    {
        switch (feature)
        {
            case FeatureId.IdleBattle_OfflineCapture:
                EnableOfflineCaptures();
                break;

            case FeatureId.IdleBattle_RewardBoost:
                // No runtime side-effects required; IdleBattleManager reads the unlock.
                break;

            // Seeds / RNG: no direct SO toggles needed. SeedService reads these flags.
            case FeatureId.Seeds_DailyBasic:
            case FeatureId.Seeds_CustomInput:
            case FeatureId.Seeds_RerollDailyOnce:
                // No-op here; used by SeedService + SettingsPanel.
                break;

            // ─────────────────────────────────────────────────────────────
            // Jobs: when job feature is unlocked (purchased), unlock the site
            // ─────────────────────────────────────────────────────────────
            default:
            {
                // If this FeatureId represents a job unlock, unlock the job site.
                if (FeatureIdJobs.TryGetJobFromFeature(feature, out var job) && job != JobType.None)
                {
                    // IMPORTANT: syncFeatureUnlock = false to avoid recursion.
                    JobUnlockBridge.UnlockJob(job, syncFeatureUnlock: false);
                }

                break;
            }
        }
    }

    private void EnableOfflineCaptures()
    {
        var cfg = Resources.Load<IdleBattleConfigSO>("IdleBattleConfig");
        if (cfg != null)
        {
            cfg.allowCapturesOffline = true;
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            DevLog.Log("[FeatureUnlockManager] Enabled Offline Captures (IdleBattle_OfflineCapture)");
            #endif
        }
        else
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning("[FeatureUnlockManager] IdleBattleConfigSO not found when enabling offline capture.");
            #endif
        }
    }
}
