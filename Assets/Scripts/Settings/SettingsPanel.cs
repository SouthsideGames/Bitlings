using UnityEngine;
using UnityEngine.UI;
using TMPro;

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

    [Header("Seeds / RNG")]
    [Tooltip("If ON, systems can prefer the custom seed (when unlocked).")]
    [SerializeField] private Toggle useCustomSeedToggle;

    [Tooltip("Custom random seed string (unlocked via Seeds_CustomInput).")]
    [SerializeField] private TMP_InputField seedInputField;

    [Header("Daily Seed UI")]
    [Tooltip("Displays the current daily seed (if unlocked).")]
    [SerializeField] private TextMeshProUGUI dailySeedLabel;

    [Tooltip("Button to reroll today's daily seed (if reroll feature unlocked).")]
    [SerializeField] private Button rerollDailySeedButton;

    [Header("Buttons")]
    [SerializeField] private Button resetButton;

    [Tooltip("Optional: plays a SFX so the player can hear the current volume.")]
    [SerializeField] private Button testSfxButton;

    bool _wired;

    void Awake()
    {
        if (masterSlider) { masterSlider.minValue = 0f; masterSlider.maxValue = 1f; }
        if (musicSlider)  { musicSlider.minValue  = 0f; musicSlider.maxValue  = 1f; }
        if (sfxSlider)    { sfxSlider.minValue    = 0f; sfxSlider.maxValue    = 1f; }

        if (resetButton) resetButton.onClick.RemoveAllListeners();
        if (testSfxButton) testSfxButton.onClick.RemoveAllListeners();

        if (rerollDailySeedButton) rerollDailySeedButton.onClick.RemoveAllListeners();
    }

    void Start()
    {
        SafeSubscribe();
        Refresh();
    }

    void OnEnable()
    {
        SafeSubscribe();
        Refresh();

        if (resetButton)
        {
            resetButton.onClick.RemoveAllListeners();
            resetButton.onClick.AddListener(() =>
            {
                var mgr = SettingsManager.I;
                if (mgr != null) mgr.OnReset();
            });
        }

        if (testSfxButton)
        {
            testSfxButton.onClick.RemoveAllListeners();
            testSfxButton.onClick.AddListener(() =>
            {
                if (AudioManager.I != null)
                    AudioManager.I.PreviewSfx(); // uses Click by default
            });
        }

        if (rerollDailySeedButton)
        {
            rerollDailySeedButton.onClick.RemoveAllListeners();
            rerollDailySeedButton.onClick.AddListener(OnClickRerollDailySeed);
        }

        WireEvents();

        // Listen for feature unlocks so we can reveal seed UI when unlocked
        if (FeatureUnlockManager.I != null)
        {
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;
        }
    }

    void OnDisable()
    {
        SafeUnsubscribe();

        if (resetButton) resetButton.onClick.RemoveAllListeners();
        if (testSfxButton) testSfxButton.onClick.RemoveAllListeners();

        if (autoScrollLogToggle)
            autoScrollLogToggle.onValueChanged.RemoveListener(OnAutoScrollChanged);

        if (rerollDailySeedButton)
            rerollDailySeedButton.onClick.RemoveAllListeners();

        UnwireEvents();

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
    }

    // ---------------- Core ----------------

    void SafeSubscribe()
    {
        var sm = SettingsManager.I;
        if (sm != null)
        {
            sm.OnSettingsChanged -= Refresh; // de-dupe
            sm.OnSettingsChanged += Refresh;
        }
    }

    void SafeUnsubscribe()
    {
        var sm = SettingsManager.I;
        if (sm != null) sm.OnSettingsChanged -= Refresh;
    }

    void Refresh()
    {
        // Audio
        if (AudioManager.I)
        {
            if (masterSlider) masterSlider.SetValueWithoutNotify(AudioManager.I.GetMasterVolume());
            if (musicSlider)  musicSlider .SetValueWithoutNotify(AudioManager.I.GetMusicVolume());
            if (sfxSlider)    sfxSlider   .SetValueWithoutNotify(AudioManager.I.GetSfxVolume());
        }

        // Settings state
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

        RefreshSeedUi(s);
        RefreshDailySeedUi();
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

        if (useCustomSeedToggle)
        {
            useCustomSeedToggle.onValueChanged.RemoveListener(OnUseCustomSeedChanged);
            useCustomSeedToggle.onValueChanged.AddListener(OnUseCustomSeedChanged);
        }

        if (seedInputField)
        {
            seedInputField.onValueChanged.RemoveListener(OnSeedInputChanged);
            seedInputField.onValueChanged.AddListener(OnSeedInputChanged);
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

        if (useCustomSeedToggle)
            useCustomSeedToggle.onValueChanged.RemoveListener(OnUseCustomSeedChanged);

        if (seedInputField)
            seedInputField.onValueChanged.RemoveListener(OnSeedInputChanged);

        _wired = false;
    }

    // -------------- Handlers --------------

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
        if (mgr != null) mgr.SetAutoConvertDuplicates(on);
    }

    void OnAutoScrollChanged(bool on)
    {
        var mgr = SettingsManager.I;
        if (mgr != null) mgr.SetAutoScrollBattleLog(on);
    }

    void OnUseCustomSeedChanged(bool on)
    {
        var mgr = SettingsManager.I;
        if (mgr != null) mgr.SetUseCustomSeed(on);
    }

    void OnSeedInputChanged(string text)
    {
        var mgr = SettingsManager.I;
        if (mgr != null) mgr.SetCustomSeed(text);
    }

    void OnClickRerollDailySeed()
    {
        if (SeedService.TryRerollDailySeed(out var newSeed))
        {
            // Update label immediately
            RefreshDailySeedUi();
            Debug.Log($"[SettingsPanel] Rerolled daily seed: {newSeed}");
        }
        else
        {
            Debug.Log("[SettingsPanel] Could not reroll daily seed (locked or already used today).");
        }
    }

    // -------------- Seeds UI / Feature gating --------------

    void HandleFeatureUnlocked(FeatureId feature)
    {
        if (feature == FeatureId.Seeds_CustomInput ||
            feature == FeatureId.Seeds_DailyBasic ||
            feature == FeatureId.Seeds_RerollDailyOnce)
        {
            Refresh();
        }
    }

    void RefreshSeedUi(SettingsState s)
    {
        bool hasFeatureMgr = FeatureUnlockManager.I != null;
        bool customUnlocked = hasFeatureMgr &&
                              FeatureUnlockManager.I.IsUnlocked(FeatureId.Seeds_CustomInput);

        if (useCustomSeedToggle)
        {
            useCustomSeedToggle.gameObject.SetActive(customUnlocked);
            if (s != null)
                useCustomSeedToggle.SetIsOnWithoutNotify(s.useCustomSeed);
        }

        if (seedInputField)
        {
            seedInputField.gameObject.SetActive(customUnlocked);
            if (s != null)
                seedInputField.SetTextWithoutNotify(s.customSeed ?? string.Empty);
        }
    }

    void RefreshDailySeedUi()
    {
        bool hasFeatureMgr = FeatureUnlockManager.I != null;
        bool dailyUnlocked = hasFeatureMgr &&
                             FeatureUnlockManager.I.IsUnlocked(FeatureId.Seeds_DailyBasic);
        bool rerollUnlocked = hasFeatureMgr &&
                              FeatureUnlockManager.I.IsUnlocked(FeatureId.Seeds_RerollDailyOnce);

        if (dailySeedLabel)
        {
            dailySeedLabel.gameObject.SetActive(dailyUnlocked);

            if (dailyUnlocked)
            {
                string seed = SeedService.GetCurrentDailySeedString();
                dailySeedLabel.text = string.IsNullOrEmpty(seed)
                    ? "Daily Seed: --"
                    : $"Daily Seed: {seed}";
            }
        }

        if (rerollDailySeedButton)
        {
            rerollDailySeedButton.gameObject.SetActive(dailyUnlocked && rerollUnlocked);
        }
    }
}
