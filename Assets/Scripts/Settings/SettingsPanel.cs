using UnityEngine;
using UnityEngine.UI;

public class SettingsPanel : MonoBehaviour
{
    [Header("Volume Sliders (0..1)")]
    [SerializeField] private Slider masterSlider;
    [SerializeField] private Slider musicSlider;
    [SerializeField] private Slider sfxSlider;

    [Header("Mute Toggles")]
    [SerializeField] private Toggle muteAllToggle;
    [SerializeField] private Toggle muteMusicToggle;
    [SerializeField] private Toggle muteSfxToggle;

    [Header("Gameplay")]
    [Tooltip("If ON: duplicates auto-convert into Training XP; if OFF: keep duplicates.")]
    [SerializeField] private Toggle autoConvertDupesToggle;

    [Tooltip("If ON: the Battle Log scrolls to latest entry automatically.")]
    [SerializeField] private Toggle autoScrollLogToggle;

    [SerializeField] private Button resetButton;

    bool _wired;

    void Awake()
    {
        if (masterSlider) { masterSlider.minValue = 0f; masterSlider.maxValue = 1f; }
        if (musicSlider)  { musicSlider.minValue  = 0f; musicSlider.maxValue  = 1f; }
        if (sfxSlider)    { sfxSlider.minValue    = 0f; sfxSlider.maxValue    = 1f; }

        if (resetButton)
        {
            resetButton.onClick.RemoveAllListeners();
        }
    }

    void OnEnable()
    {
        if (SettingsManager.I) SettingsManager.I.OnSettingsChanged += Refresh;

        Refresh();  // pulls values and sets toggles/sliders

        if (resetButton)
        {
            resetButton.onClick.RemoveAllListeners();
            resetButton.onClick.AddListener(() =>
            {
                var mgr = SettingsManager.I;
                if (mgr != null) mgr.OnReset();
            });
        }

        WireEvents();
    }

    void OnDisable()
    {
        if (SettingsManager.I) SettingsManager.I.OnSettingsChanged -= Refresh;

        // optional tidy-up
        if (resetButton) resetButton.onClick.RemoveAllListeners();
        if (autoScrollLogToggle) autoScrollLogToggle.onValueChanged.RemoveListener(OnAutoScrollChanged);

        UnwireEvents();
    }

    // Centralized repaint from current state
    void Refresh()
    {
        if (AudioManager.I)
        {
            if (masterSlider) masterSlider.SetValueWithoutNotify(AudioManager.I.GetMasterVolume());
            if (musicSlider)  musicSlider .SetValueWithoutNotify(AudioManager.I.GetMusicVolume());
            if (sfxSlider)    sfxSlider   .SetValueWithoutNotify(AudioManager.I.GetSfxVolume());
        }

        var s = SettingsManager.I ? SettingsManager.I.S : SaveManager.Data?.settings;
        if (s != null)
        {
            if (muteAllToggle)   muteAllToggle  .SetIsOnWithoutNotify(s.muteAll);
            if (muteMusicToggle) muteMusicToggle.SetIsOnWithoutNotify(s.muteMusic);
            if (muteSfxToggle)   muteSfxToggle  .SetIsOnWithoutNotify(s.muteSfx);

            if (autoConvertDupesToggle)
                autoConvertDupesToggle.SetIsOnWithoutNotify(s.autoConvertDuplicates);

            if (autoScrollLogToggle)
                autoScrollLogToggle.SetIsOnWithoutNotify(s.autoScrollBattleLog);
        }
    }

    void WireEvents()
    {
        if (_wired) return;

        if (masterSlider)
        {
            masterSlider.onValueChanged.RemoveListener(OnMasterChanged);
            masterSlider.onValueChanged.AddListener(OnMasterChanged);
        }
        if (musicSlider)
        {
            musicSlider.onValueChanged.RemoveListener(OnMusicChanged);
            musicSlider.onValueChanged.AddListener(OnMusicChanged);
        }
        if (sfxSlider)
        {
            sfxSlider.onValueChanged.RemoveListener(OnSfxChanged);
            sfxSlider.onValueChanged.AddListener(OnSfxChanged);
        }

        if (muteAllToggle)
        {
            muteAllToggle.onValueChanged.RemoveListener(OnMuteAll);
            muteAllToggle.onValueChanged.AddListener(OnMuteAll);
        }
        if (muteMusicToggle)
        {
            muteMusicToggle.onValueChanged.RemoveListener(OnMuteMusic);
            muteMusicToggle.onValueChanged.AddListener(OnMuteMusic);
        }
        if (muteSfxToggle)
        {
            muteSfxToggle.onValueChanged.RemoveListener(OnMuteSfx);
            muteSfxToggle.onValueChanged.AddListener(OnMuteSfx);
        }

        if (autoConvertDupesToggle)
        {
            autoConvertDupesToggle.onValueChanged.RemoveListener(OnAutoConvertDupesToggled);
            autoConvertDupesToggle.onValueChanged.AddListener(OnAutoConvertDupesToggled);
        }

        if (autoScrollLogToggle)
        {
            autoScrollLogToggle.onValueChanged.RemoveListener(OnAutoScrollChanged);
            autoScrollLogToggle.onValueChanged.AddListener(OnAutoScrollChanged);
        }

        _wired = true;
    }

    void UnwireEvents()
    {
        if (!_wired) return;

        if (masterSlider) masterSlider.onValueChanged.RemoveListener(OnMasterChanged);
        if (musicSlider)  musicSlider.onValueChanged.RemoveListener(OnMusicChanged);
        if (sfxSlider)    sfxSlider.onValueChanged.RemoveListener(OnSfxChanged);

        if (muteAllToggle)   muteAllToggle.onValueChanged.RemoveListener(OnMuteAll);
        if (muteMusicToggle) muteMusicToggle.onValueChanged.RemoveListener(OnMuteMusic);
        if (muteSfxToggle)   muteSfxToggle.onValueChanged.RemoveListener(OnMuteSfx);

        if (autoConvertDupesToggle)
            autoConvertDupesToggle.onValueChanged.RemoveListener(OnAutoConvertDupesToggled);

        if (autoScrollLogToggle)
            autoScrollLogToggle.onValueChanged.RemoveListener(OnAutoScrollChanged);

        _wired = false;
    }

    // --- Handlers ---

    void OnMasterChanged(float v)
    {
        if (AudioManager.I) AudioManager.I.SetMasterVolume(v);
    }

    void OnMusicChanged(float v)
    {
        if (AudioManager.I) AudioManager.I.SetMusicVolume(v);
    }

    void OnSfxChanged(float v)
    {
        if (AudioManager.I) AudioManager.I.SetSfxVolume(v);
    }

    void OnMuteAll(bool on)
    {
        if (AudioManager.I) AudioManager.I.OnMuteAllToggle(on);
    }

    void OnMuteMusic(bool on)
    {
        if (AudioManager.I) AudioManager.I.OnMuteMusicToggle(on);
    }

    void OnMuteSfx(bool on)
    {
        if (AudioManager.I) AudioManager.I.OnMuteSfxToggle(on);
    }

    void OnAutoConvertDupesToggled(bool on)
    {
        var mgr = SettingsManager.I;
        if (mgr != null)
        {
            mgr.SetAutoConvertDuplicates(on);
        }
    }

    void OnAutoScrollChanged(bool on)
    {
        var mgr = SettingsManager.I;
        if (mgr != null)
        {
            // Preferred: add this method in SettingsManager
            mgr.SetAutoScrollBattleLog(on);

            // If you don't want to add a setter, use:
            // mgr.S.autoScrollBattleLog = on;
            // mgr.Save();
        }
    }
}
