using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Assertions;

public class SettingsPanel : MonoBehaviour
{
    [Header("Debug")]
    [SerializeField] private bool debugLogs = true;

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
    bool _started;   // to avoid double-subscribe before Start

    void Awake()
    {
        if (masterSlider) { masterSlider.minValue = 0f; masterSlider.maxValue = 1f; }
        if (musicSlider)  { musicSlider.minValue  = 0f; musicSlider.maxValue  = 1f; }
        if (sfxSlider)    { sfxSlider.minValue    = 0f; sfxSlider.maxValue    = 1f; }

        if (resetButton) resetButton.onClick.RemoveAllListeners();

        if (debugLogs) Debug.Log($"[SettingsPanel] Awake on {name}.", this);
    }

    void Start()
    {
        _started = true;
        if (debugLogs) Debug.Log($"[SettingsPanel] Start on {name}. EventSystem present: {FindObjectOfType<UnityEngine.EventSystems.EventSystem>()!=null}", this);

        // If this panel starts enabled, we might miss an early settings init order.
        // Force one more refresh in Start to be safe.
        SafeSubscribe();
        Refresh();

        // Safety check for common visibility issues
        var cg = GetComponentInParent<CanvasGroup>();
        if (cg && cg.alpha < 0.99f && debugLogs)
            Debug.LogWarning($"[SettingsPanel] CanvasGroup alpha = {cg.alpha}. If you 'see nothing', this may be why.", this);

        var canvas = GetComponentInParent<Canvas>();
        if (!canvas && debugLogs)
            Debug.LogWarning("[SettingsPanel] No Canvas found in parents. UI won't render.", this);
    }

    void OnEnable()
    {
        if (debugLogs) Debug.Log($"[SettingsPanel] OnEnable on {name}.", this);

        SafeSubscribe();
        Refresh();

        if (resetButton)
        {
            resetButton.onClick.RemoveAllListeners();
            resetButton.onClick.AddListener(() =>
            {
                var mgr = SettingsManager.I;
                if (mgr != null) mgr.OnReset();
                if (debugLogs) Debug.Log("[SettingsPanel] Reset pressed: SettingsManager.OnReset()", this);
            });
        }

        WireEvents();
    }

    void OnDisable()
    {
        if (debugLogs) Debug.Log($"[SettingsPanel] OnDisable on {name}.", this);

        SafeUnsubscribe();

        if (resetButton) resetButton.onClick.RemoveAllListeners();
        if (autoScrollLogToggle) autoScrollLogToggle.onValueChanged.RemoveListener(OnAutoScrollChanged);

        UnwireEvents();
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
        else if (debugLogs)
        {
            Debug.LogWarning("[SettingsPanel] SettingsManager.I is null at subscribe time.", this);
        }
    }

    void SafeUnsubscribe()
    {
        var sm = SettingsManager.I;
        if (sm != null) sm.OnSettingsChanged -= Refresh;
    }

    void Refresh()
    {
        if (debugLogs) Debug.Log("[SettingsPanel] Refresh()", this);

        // Audio
        if (AudioManager.I)
        {
            if (masterSlider) masterSlider.SetValueWithoutNotify(AudioManager.I.GetMasterVolume());
            if (musicSlider)  musicSlider .SetValueWithoutNotify(AudioManager.I.GetMusicVolume());
            if (sfxSlider)    sfxSlider   .SetValueWithoutNotify(AudioManager.I.GetSfxVolume());
        }
        else if (debugLogs)
        {
            Debug.LogWarning("[SettingsPanel] AudioManager.I is null during Refresh().", this);
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
        else if (debugLogs)
        {
            Debug.LogWarning("[SettingsPanel] Settings object is null (SettingsManager.S or SaveManager.Data.settings).", this);
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

    // -------------- Handlers --------------

    void OnMasterChanged(float v) { if (AudioManager.I) AudioManager.I.SetMasterVolume(v); }
    void OnMusicChanged(float v)  { if (AudioManager.I) AudioManager.I.SetMusicVolume(v); }
    void OnSfxChanged(float v)    { if (AudioManager.I) AudioManager.I.SetSfxVolume(v); }

    void OnMuteAll(bool on)       { if (AudioManager.I) AudioManager.I.OnMuteAllToggle(on); }
    void OnMuteMusic(bool on)     { if (AudioManager.I) AudioManager.I.OnMuteMusicToggle(on); }
    void OnMuteSfx(bool on)       { if (AudioManager.I) AudioManager.I.OnMuteSfxToggle(on); }

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

    // ------------ Editor helpers ------------

    [ContextMenu("Log Reference Status")]
    void LogRefs()
    {
        Debug.Log(
            $"[SettingsPanel] Refs — master:{(masterSlider? "Y":"-")} music:{(musicSlider? "Y":"-")} sfx:{(sfxSlider? "Y":"-")} " +
            $"muteAll:{(muteAllToggle? "Y":"-")} muteMusic:{(muteMusicToggle? "Y":"-")} muteSfx:{(muteSfxToggle? "Y":"-")} " +
            $"autoDupes:{(autoConvertDupesToggle? "Y":"-")} autoScroll:{(autoScrollLogToggle? "Y":"-")} reset:{(resetButton? "Y":"-")}",
            this);
    }
}
