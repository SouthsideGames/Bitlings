using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class SettingsPanel : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────
    // NEW: Section / Tab UI
    // ─────────────────────────────────────────────────────────
    public enum SettingsSection
    {
        Audio,
        Gameplay,
        Notifications,
        Seeds
    }

    [Header("Section Buttons")]
    [SerializeField] private Button audioTabButton;
    [SerializeField] private Button gameplayTabButton;
    [SerializeField] private Button notificationsTabButton;
    [SerializeField] private Button seedsTabButton;

    [Header("Section Roots (Each root should contain its section UI)")]
    [Tooltip("Root that holds master/music/sfx sliders + mute toggles + optional test SFX.")]
    [SerializeField] private GameObject audioSectionRoot;

    [Tooltip("Root that holds gameplay toggles (auto convert dupes, auto scroll log, etc.).")]
    [SerializeField] private GameObject gameplaySectionRoot;

    [Tooltip("Root that holds notifications toggles (master + fine-grained toggles).")]
    [SerializeField] private GameObject notificationsSectionRoot;

    [Tooltip("Root that holds seed toggles/fields and daily seed UI.")]
    [SerializeField] private GameObject seedsSectionRoot;

    [Header("Section Fade")]
    [SerializeField, Range(0f, 1f)] private float hiddenAlpha = 0f;
    [SerializeField, Min(0f)] private float fadeDuration = 0.15f;
    [SerializeField] private bool disableHiddenSections = true;

    [Header("Default Section On Open")]
    [SerializeField] private SettingsSection defaultSection = SettingsSection.Audio;

    private SettingsSection _activeSection;
    private bool _sectionWired;
    private CanvasGroup _audioCg, _gameplayCg, _notificationsCg, _seedsCg;

    // ─────────────────────────────────────────────────────────
    // Existing fields (UNCHANGED)
    // ─────────────────────────────────────────────────────────

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

    [Header("Notifications")]
    [SerializeField] private Toggle notificationsEnabledToggle;
    [SerializeField] private Toggle notifyJobStorageFullToggle;
    [SerializeField] private Toggle notifyEnergyFullToggle;
    [SerializeField] private Toggle notifyBoostExpiryToggle;
    [SerializeField] private Toggle notifyFallback24hToggle;

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

    [Header("Debug")]
    [SerializeField] private Button debugTestNotificationsButton;
    [SerializeField] private Button debugClearNotificationsButton;

    bool _wired;

    void Awake()
    {
        // Existing slider setup
        if (masterSlider) { masterSlider.minValue = 0f; masterSlider.maxValue = 1f; }
        if (musicSlider)  { musicSlider.minValue  = 0f; musicSlider.maxValue  = 1f; }
        if (sfxSlider)    { sfxSlider.minValue    = 0f; sfxSlider.maxValue    = 1f; }

        // Existing button cleanup
        if (resetButton) resetButton.onClick.RemoveAllListeners();
        if (testSfxButton) testSfxButton.onClick.RemoveAllListeners();
        if (rerollDailySeedButton) rerollDailySeedButton.onClick.RemoveAllListeners();

        if (debugTestNotificationsButton) debugTestNotificationsButton.onClick.RemoveAllListeners();
        if (debugClearNotificationsButton) debugClearNotificationsButton.onClick.RemoveAllListeners();

        // NEW: Setup section CanvasGroups
        CacheSectionCanvasGroups();
    }

    void Start()
    {
        SafeSubscribe();
        Refresh();

        // NEW: default section
        ShowSection(defaultSection, instant: true);
        WireSectionTabs();
    }

    void OnEnable()
    {
        SafeSubscribe();
        Refresh();

        // Existing listeners
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
                    AudioManager.I.PreviewSfx();
            });
        }

        if (rerollDailySeedButton)
        {
            rerollDailySeedButton.onClick.RemoveAllListeners();
            rerollDailySeedButton.onClick.AddListener(OnClickRerollDailySeed);
        }

        if (debugTestNotificationsButton)
        {
            debugTestNotificationsButton.onClick.RemoveAllListeners();
            debugTestNotificationsButton.onClick.AddListener(() =>
            {
                if (NotificationManager.I != null)
                    NotificationManager.I.DebugScheduleTestNotifications();
            });
        }

        if (debugClearNotificationsButton)
        {
            debugClearNotificationsButton.onClick.RemoveAllListeners();
            debugClearNotificationsButton.onClick.AddListener(() =>
            {
                if (NotificationManager.I != null)
                    NotificationManager.I.DebugClearScheduledNotifications();
            });
        }

        WireEvents();

        if (FeatureUnlockManager.I != null)
        {
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;
        }

        // NEW: ensure tabs are wired & show the active/default section on reopen
        CacheSectionCanvasGroups();
        WireSectionTabs();
        if (!IsSectionValid(_activeSection)) _activeSection = defaultSection;
        ShowSection(_activeSection, instant: true);
    }

    void OnDisable()
    {
        SafeUnsubscribe();

        if (resetButton) resetButton.onClick.RemoveAllListeners();
        if (testSfxButton) testSfxButton.onClick.RemoveAllListeners();
        if (rerollDailySeedButton) rerollDailySeedButton.onClick.RemoveAllListeners();

        if (debugTestNotificationsButton) debugTestNotificationsButton.onClick.RemoveAllListeners();
        if (debugClearNotificationsButton) debugClearNotificationsButton.onClick.RemoveAllListeners();

        UnwireEvents();

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;

        // NEW: tab listeners cleanup
        UnwireSectionTabs();
    }

    // ─────────────────────────────────────────────────────────
    // NEW: Tabs / Sections
    // ─────────────────────────────────────────────────────────

    void CacheSectionCanvasGroups()
    {
        _audioCg = EnsureCanvasGroup(audioSectionRoot);
        _gameplayCg = EnsureCanvasGroup(gameplaySectionRoot);
        _notificationsCg = EnsureCanvasGroup(notificationsSectionRoot);
        _seedsCg = EnsureCanvasGroup(seedsSectionRoot);
    }

    CanvasGroup EnsureCanvasGroup(GameObject root)
    {
        if (!root) return null;
        var cg = root.GetComponent<CanvasGroup>();
        if (!cg) cg = root.AddComponent<CanvasGroup>();
        return cg;
    }

    void WireSectionTabs()
    {
        if (_sectionWired) return;

        if (audioTabButton)
        {
            audioTabButton.onClick.RemoveAllListeners();
            audioTabButton.onClick.AddListener(() => ShowSection(SettingsSection.Audio));
        }

        if (gameplayTabButton)
        {
            gameplayTabButton.onClick.RemoveAllListeners();
            gameplayTabButton.onClick.AddListener(() => ShowSection(SettingsSection.Gameplay));
        }

        if (notificationsTabButton)
        {
            notificationsTabButton.onClick.RemoveAllListeners();
            notificationsTabButton.onClick.AddListener(() => ShowSection(SettingsSection.Notifications));
        }

        if (seedsTabButton)
        {
            seedsTabButton.onClick.RemoveAllListeners();
            seedsTabButton.onClick.AddListener(() => ShowSection(SettingsSection.Seeds));
        }

        _sectionWired = true;
    }

    void UnwireSectionTabs()
    {
        if (!_sectionWired) return;

        if (audioTabButton) audioTabButton.onClick.RemoveAllListeners();
        if (gameplayTabButton) gameplayTabButton.onClick.RemoveAllListeners();
        if (notificationsTabButton) notificationsTabButton.onClick.RemoveAllListeners();
        if (seedsTabButton) seedsTabButton.onClick.RemoveAllListeners();

        _sectionWired = false;
    }

    bool IsSectionValid(SettingsSection section)
    {
        return section switch
        {
            SettingsSection.Audio => audioSectionRoot != null,
            SettingsSection.Gameplay => gameplaySectionRoot != null,
            SettingsSection.Notifications => notificationsSectionRoot != null,
            SettingsSection.Seeds => seedsSectionRoot != null,
            _ => false
        };
    }

    public void ShowSection(SettingsSection section, bool instant = false)
    {
        _activeSection = section;

        // You can choose whether to fade or instantly swap.
        bool doInstant = instant || fadeDuration <= 0f;

        SetSectionVisible(_audioCg, section == SettingsSection.Audio, doInstant);
        SetSectionVisible(_gameplayCg, section == SettingsSection.Gameplay, doInstant);
        SetSectionVisible(_notificationsCg, section == SettingsSection.Notifications, doInstant);
        SetSectionVisible(_seedsCg, section == SettingsSection.Seeds, doInstant);
    }

    void SetSectionVisible(CanvasGroup cg, bool visible, bool instant)
    {
        if (!cg) return;

        var go = cg.gameObject;

        // Ensure active before fading in
        if (visible)
        {
            if (disableHiddenSections && !go.activeSelf) go.SetActive(true);
            cg.blocksRaycasts = true;
            cg.interactable = true;

            float target = 1f;
            if (instant)
            {
                CancelTween(cg);
                cg.alpha = target;
            }
            else
            {
                CancelTween(cg);
                cg.alpha = Mathf.Clamp01(cg.alpha);
                LeanTween.alphaCanvas(cg, target, fadeDuration).setIgnoreTimeScale(true);
            }
        }
        else
        {
            cg.blocksRaycasts = false;
            cg.interactable = false;

            float target = hiddenAlpha;
            if (instant)
            {
                CancelTween(cg);
                cg.alpha = target;
                if (disableHiddenSections) go.SetActive(false);
            }
            else
            {
                CancelTween(cg);
                LeanTween.alphaCanvas(cg, target, fadeDuration)
                    .setIgnoreTimeScale(true)
                    .setOnComplete(() =>
                    {
                        if (disableHiddenSections) go.SetActive(false);
                    });
            }
        }
    }

    void CancelTween(CanvasGroup cg)
    {
        if (!cg) return;
        // LeanTween-safe cancel (won’t throw if nothing exists)
        LeanTween.cancel(cg.gameObject);
    }

    // ---------------- Core ----------------

    void SafeSubscribe()
    {
        var sm = SettingsManager.I;
        if (sm != null)
        {
            sm.OnSettingsChanged -= Refresh;
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
            if (musicSlider)  musicSlider.SetValueWithoutNotify(AudioManager.I.GetMusicVolume());
            if (sfxSlider)    sfxSlider.SetValueWithoutNotify(AudioManager.I.GetSfxVolume());
        }

        var s = SettingsManager.I ? SettingsManager.I.S : SaveManager.Data?.settings;
        if (s != null)
        {
            if (muteAllToggle)   muteAllToggle.SetIsOnWithoutNotify(s.muteAll);
            if (muteMusicToggle) muteMusicToggle.SetIsOnWithoutNotify(s.muteMusic);
            if (muteSfxToggle)   muteSfxToggle.SetIsOnWithoutNotify(s.muteSfx);

            if (autoConvertDupesToggle)
                autoConvertDupesToggle.SetIsOnWithoutNotify(s.autoConvertDuplicates);

            if (autoScrollLogToggle)
                autoScrollLogToggle.SetIsOnWithoutNotify(s.autoScrollBattleLog);

            // Notifications
            if (notificationsEnabledToggle)
                notificationsEnabledToggle.SetIsOnWithoutNotify(s.notificationsEnabled);

            if (notifyJobStorageFullToggle)
                notifyJobStorageFullToggle.SetIsOnWithoutNotify(s.notifyJobStorageFull);

            if (notifyEnergyFullToggle)
                notifyEnergyFullToggle.SetIsOnWithoutNotify(s.notifyEnergyFull);

            if (notifyBoostExpiryToggle)
                notifyBoostExpiryToggle.SetIsOnWithoutNotify(s.notifyBoostExpiry);

            if (notifyFallback24hToggle)
                notifyFallback24hToggle.SetIsOnWithoutNotify(s.notifyFallback24h);

            // UX: disable children toggles if master is off
            bool masterOn = s.notificationsEnabled;
            if (notifyJobStorageFullToggle) notifyJobStorageFullToggle.interactable = masterOn;
            if (notifyEnergyFullToggle)     notifyEnergyFullToggle.interactable     = masterOn;
            if (notifyBoostExpiryToggle)    notifyBoostExpiryToggle.interactable    = masterOn;
            if (notifyFallback24hToggle)    notifyFallback24hToggle.interactable    = masterOn;
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

        // Notifications
        if (notificationsEnabledToggle)
        {
            notificationsEnabledToggle.onValueChanged.RemoveListener(OnNotificationsEnabledChanged);
            notificationsEnabledToggle.onValueChanged.AddListener(OnNotificationsEnabledChanged);
        }

        if (notifyJobStorageFullToggle)
        {
            notifyJobStorageFullToggle.onValueChanged.RemoveListener(OnNotifyJobStorageFullChanged);
            notifyJobStorageFullToggle.onValueChanged.AddListener(OnNotifyJobStorageFullChanged);
        }

        if (notifyEnergyFullToggle)
        {
            notifyEnergyFullToggle.onValueChanged.RemoveListener(OnNotifyEnergyFullChanged);
            notifyEnergyFullToggle.onValueChanged.AddListener(OnNotifyEnergyFullChanged);
        }

        if (notifyBoostExpiryToggle)
        {
            notifyBoostExpiryToggle.onValueChanged.RemoveListener(OnNotifyBoostExpiryChanged);
            notifyBoostExpiryToggle.onValueChanged.AddListener(OnNotifyBoostExpiryChanged);
        }

        if (notifyFallback24hToggle)
        {
            notifyFallback24hToggle.onValueChanged.RemoveListener(OnNotifyFallback24hChanged);
            notifyFallback24hToggle.onValueChanged.AddListener(OnNotifyFallback24hChanged);
        }

        // Seeds / RNG
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

        if (notificationsEnabledToggle)
            notificationsEnabledToggle.onValueChanged.RemoveListener(OnNotificationsEnabledChanged);
        if (notifyJobStorageFullToggle)
            notifyJobStorageFullToggle.onValueChanged.RemoveListener(OnNotifyJobStorageFullChanged);
        if (notifyEnergyFullToggle)
            notifyEnergyFullToggle.onValueChanged.RemoveListener(OnNotifyEnergyFullChanged);
        if (notifyBoostExpiryToggle)
            notifyBoostExpiryToggle.onValueChanged.RemoveListener(OnNotifyBoostExpiryChanged);
        if (notifyFallback24hToggle)
            notifyFallback24hToggle.onValueChanged.RemoveListener(OnNotifyFallback24hChanged);

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

    // Notifications
    void OnNotificationsEnabledChanged(bool on)
    {
        var mgr = SettingsManager.I;
        if (mgr != null) mgr.SetNotificationsEnabled(on);
    }

    void OnNotifyJobStorageFullChanged(bool on)
    {
        var mgr = SettingsManager.I;
        if (mgr != null) mgr.SetNotifyJobStorageFull(on);
    }

    void OnNotifyEnergyFullChanged(bool on)
    {
        var mgr = SettingsManager.I;
        if (mgr != null) mgr.SetNotifyEnergyFull(on);
    }

    void OnNotifyBoostExpiryChanged(bool on)
    {
        var mgr = SettingsManager.I;
        if (mgr != null) mgr.SetNotifyBoostExpiry(on);
    }

    void OnNotifyFallback24hChanged(bool on)
    {
        var mgr = SettingsManager.I;
        if (mgr != null) mgr.SetNotifyFallback24h(on);
    }

    // Seeds
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
            RefreshDailySeedUi();
            Debug.Log($"[SettingsPanel] Rerolled daily seed: {newSeed}");
        }
        else
        {
            Debug.Log("[SettingsPanel] Could not reroll daily seed (locked or already used today).");
        }
    }

    // Feature gating hook
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
