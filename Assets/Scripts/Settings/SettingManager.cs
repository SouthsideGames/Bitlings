using UnityEngine;
using UnityEngine.SceneManagement;
using System;

public class SettingsManager : MonoBehaviour
{
    public static SettingsManager I { get; private set; }
    public event Action OnSettingsChanged;

    public SettingsState S => Ensure();
    SettingsState _fallback;

    bool _isResetting;

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
        if (_isResetting) return;
        _isResetting = true;

        try
        {
            Time.timeScale = 1f;

            SaveManager.ClearAll();
            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();

            SaveManager.LoadOrCreate();

            if (ResourceManager.I != null)
                ResourceManager.I.InitializeNewAccountResources();

            ApplyDefaults();

            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
        }
        finally
        {
            _isResetting = false;
        }
    }

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

    public bool GetShowInlineBattleIcons() => S.showInlineBattleIcons;

    public void SetShowInlineBattleIcons(bool enabled)
    {
        if (S.showInlineBattleIcons == enabled) return;
        S.showInlineBattleIcons = enabled;
        Persist();
    }

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
}
