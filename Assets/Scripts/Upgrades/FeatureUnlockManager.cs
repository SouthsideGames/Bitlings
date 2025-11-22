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

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }

        I = this;
        DontDestroyOnLoad(gameObject);

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

        OnFeatureUnlocked?.Invoke(feature);
        SaveToPrefs();

        return true;
    }

    // Optional hooks for future SaveManager integration:
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

        foreach (var f in startingUnlocked)
            _unlocked.Add(f);
    }

    // ─────────────────────────────────────────────────────────────
    // Internal: simple PlayerPrefs persistence for now
    // ─────────────────────────────────────────────────────────────

    private const string PlayerPrefsKey = "FeatureUnlocks_JSON";

    [Serializable]
    private class FeatureUnlockSaveWrapper
    {
        public List<string> ids;
    }

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
                RestoreFromSavedIds(wrapper.ids);
            else
                ApplyStartingDefaults();
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
    }
}
