using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections;
using System.Collections.Generic;
using Random = UnityEngine.Random;

public class EncounterPanelUI : MonoBehaviour
{
    public static EncounterPanelUI I { get; private set; }

    [Header("Refs")]
    [SerializeField] private Button encounterBtn;
    [SerializeField] private TextMeshProUGUI energyLabel;
    [SerializeField] private TextMeshProUGUI energyEtaLabel;
    [SerializeField, Min(1f)] private float energySecondsPerPoint = 1200f;

    [Header("Fatigue Forecast")]
    [SerializeField] private TextMeshProUGUI fatigueForecastLabel;
    [SerializeField] private bool showFatigueForecast = true;

    [Tooltip("How many team slots participate in a battle (used for forecast count).")]
    [SerializeField, Range(1, 6)] private int battleTeamSlots = 3;

    [Tooltip("Template used for the forecast. Use {0} for the number.")]
    [SerializeField] private string fatigueForecastTemplate = "This battle will exhaust {0} Bitling{1}";

    // ─────────────────────────────────────────────────────────────
    // Encounter Bar Buttons + Labels (Icon + TMP under it)
    // ─────────────────────────────────────────────────────────────
    [Header("Encounter Bar Buttons")]

    [Header("Energy (icon is a button; NO timer text under icon)")]
    [SerializeField] private Button energyInfoButton;
    [SerializeField] private TooltipTrigger energyTooltip;

    [Header("Flyer (icon is a button; NO timer text under icon)")]
    [SerializeField] private GameObject flyerRoot;
    [SerializeField] private Button flyerButton;
    [SerializeField] private TooltipTrigger flyerTooltip;
    [SerializeField] private Image flyerIcon;
    [SerializeField] private TextMeshProUGUI flyerTypeLabel; // shows type name only

    [Header("Shiny Boost (shows timer under icon)")]
    [SerializeField] private GameObject shinyRoot;
    [SerializeField] private Button shinyButton;
    [SerializeField] private TooltipTrigger shinyTooltip;
    [SerializeField] private Image shinyIcon;
    [SerializeField] private TextMeshProUGUI shinyTimerLabel;

    [Header("Capture Boost (shows timer under icon)")]
    [SerializeField] private GameObject captureRoot;
    [SerializeField] private Button captureButton;
    [SerializeField] private TooltipTrigger captureTooltip;
    [SerializeField] private Image captureIcon;
    [SerializeField] private TextMeshProUGUI captureTimerLabel;

    [Header("Favor / Luck Boost (shows timer under icon)")]
    [SerializeField] private GameObject favorRoot;
    [SerializeField] private Button favorButton;
    [SerializeField] private TooltipTrigger favorTooltip;
    [SerializeField] private Image favorIcon;
    [SerializeField] private TextMeshProUGUI favorTimerLabel;

    [Header("Timer Warning FX (for timed boosts only)")]
    [SerializeField] private bool warningFxEnabled = true;
    [SerializeField] private int warningSeconds = 60;
    [SerializeField] private Color timerNormalColor = Color.white;
    [SerializeField] private Color timerWarningColor = new Color(1f, 0.35f, 0.35f);
    [SerializeField, Min(1.01f)] private float pulseScale = 1.08f;
    [SerializeField, Min(0.05f)] private float pulseTime = 0.25f;

    private int _shinyPulseTweenId = -1;
    private int _capturePulseTweenId = -1;
    private int _favorPulseTweenId = -1;

    // ─────────────────────────────────────────────────────────────
    // Blinder (localized + weighted random)
    // ─────────────────────────────────────────────────────────────
    [Header("Blinder")]
    [SerializeField] private CanvasGroup blinderGroup;
    [SerializeField] private TextMeshProUGUI blinderText;
    [SerializeField, Range(0.05f, 1.5f)] private float preFadeDelay = 0.25f;
    [SerializeField, Range(0.1f, 2.0f)] private float fadeDuration = 0.6f;

    [Header("Blinder Typewriter")]
    [SerializeField, Range(0.005f, 0.08f)] private float typewriterCharDelay = 0.03f;

    [Header("Blinder Background Tints")]
    [SerializeField] private Image blinderBackground;
    [SerializeField] private Color defaultBlinderTint = new Color(0f, 0f, 0f, 0.7f);
    [SerializeField] private Color bossImminentTint = new Color(0.8f, 0.25f, 0.25f, 0.8f);
    [SerializeField] private Color genericLureTint = new Color(0.25f, 0.5f, 0.9f, 0.7f);

    [System.Serializable]
    public class LureTint
    {
        public MonsterType type;
        public Color tint = Color.white;
    }

    [Tooltip("Optional per-type lure tints. If a type isn’t listed, genericLureTint is used.")]
    [SerializeField] private List<LureTint> lureTints = new();

    [Header("Blinder Localization & Random")]
    [SerializeField] private bool useRandomBlinder = true;
    [SerializeField] private string preferredLanguageCode = "";
    [SerializeField] private BlinderMessagePackSO overridePack;
    [SerializeField] private BlinderMessageLibrarySO blinderLibrary;
    [SerializeField] private string hardFallbackLine = "I WONDER WHAT WE WILL ENCOUNTER";

    string _lastBlinderLine = null;

    // ─────────────────────────────────────────────────────────────
    // Team Preview
    // ─────────────────────────────────────────────────────────────
    [Header("Team Preview")]
    [SerializeField] private Transform teamPreviewRoot;
    [SerializeField] private TeamPreviewItemUI teamItemPrefab;
    [SerializeField, Range(1, 12)] private int maxTeamShown = 6;
    [SerializeField, Range(0f, 0.25f)] private float teamStaggerFade = 0.05f;

    [Header("Button / Energy FX")]
    [SerializeField, Min(1.01f)] private float energyGainPunchScale = 1.08f;
    [SerializeField, Min(0.01f)] private float energyGainPunchTime = 0.12f;

    [SerializeField, Min(1.01f)] private float noEnergyPunchScale = 1.08f;
    [SerializeField, Min(0.01f)] private float noEnergyPunchTime = 0.12f;

    [Header("Energy Toast")]
    [SerializeField] private RectTransform energyToastAnchor;
    [SerializeField] private GameObject energyToastPrefab;
    [SerializeField] private float energyToastRiseY = 30f;
    [SerializeField] private float energyToastDuration = 0.8f;

    [Tooltip("Small 'already caught' icon shown when this species is in your collection.")]
    [SerializeField] private GameObject ownedCapturedIcon;

    // ─────────────────────────────────────────────────────────────
    // Hire Decision
    // ─────────────────────────────────────────────────────────────
    [Header("Hire Decision")]
    [SerializeField] private GameObject hireDecisionRoot;
    [SerializeField] private Image hireMonsterIcon;
    [SerializeField] private TextMeshProUGUI hirePromptText;

    [Header("Hire Decision Buttons")]
    [SerializeField] private GameObject hireButtonsRoot;
    [SerializeField] private Button hireYesButton;
    [SerializeField] private Button hireNoButton;
    [SerializeField] private Button hireContinueButton;

    [Header("Hire Decision Result Prefabs")]
    [SerializeField] private Transform hireResultSpawnPoint;
    [SerializeField] private GameObject hireAgreePrefab;
    [SerializeField] private GameObject hireDenyPrefab;

    [Header("Navigation Lock")]
    [SerializeField] private GameObject closeButtonRoot;
    [SerializeField] private bool hideCloseDuringBattle = true;

    private MonsterDataSO _pendingHireDef;
    private int _pendingHireLevel;
    private bool _pendingHireIsShiny;

    private bool _hireChoseYes;
    private bool _hireCaptureSucceeded;
    private bool _hireDecisionLocked;

    public bool IsHireDecisionOpen => hireDecisionRoot && hireDecisionRoot.activeSelf;

    private TextMeshProUGUI encounterLabel;
    float _etaTickAccum = 0f;
    bool _isFading;
    Coroutine _fadeCo;
    Coroutine _typewriterCo;
    readonly List<TeamPreviewItemUI> _previewItems = new();

    // NEW: prevents blinder reappearing mid-transition
    bool _suppressAutoBlinderUntilBattle;

    // ─────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────
    void Awake()
    {
        I = this;

        encounterLabel = encounterBtn ? encounterBtn.GetComponentInChildren<TextMeshProUGUI>() : null;

        if (blinderGroup)
        {
            blinderGroup.alpha = 1f;
            blinderGroup.blocksRaycasts = true;
            blinderGroup.interactable = true;
        }

        if (ownedCapturedIcon)
            ownedCapturedIcon.SetActive(false);

        if (hireDecisionRoot)
            hireDecisionRoot.SetActive(false);

        if (hireContinueButton)
            hireContinueButton.gameObject.SetActive(false);

        if (hireButtonsRoot)
            hireButtonsRoot.SetActive(true);

        SetBoostRootActive(flyerRoot, false);
        SetBoostRootActive(shinyRoot, false);
        SetBoostRootActive(captureRoot, false);
        SetBoostRootActive(favorRoot, false);

        RefreshBlinderTint();
        PickAndApplyBlinderLine(forcePick: true);

        // Fatigue forecast is optional; keep hidden until first refresh.
        if (fatigueForecastLabel)
            fatigueForecastLabel.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        ForceBlinderAlphaToOne();

        if (encounterBtn)
        {
            encounterBtn.onClick.RemoveAllListeners();
            encounterBtn.onClick.AddListener(OnClickEncounter);
        }

        EnsureTooltipTrigger(energyInfoButton, ref energyTooltip);
        EnsureTooltipTrigger(flyerButton, ref flyerTooltip);
        EnsureTooltipTrigger(shinyButton, ref shinyTooltip);
        EnsureTooltipTrigger(captureButton, ref captureTooltip);
        EnsureTooltipTrigger(favorButton, ref favorTooltip);

        if (EncounterManager.I != null)
        {
            EncounterManager.I.OnStateChanged += OnEncounterStateChanged;
            GameEvents.EnergyChanged += RefreshEnergy;
        }

        EncounterManager.OnEnergyGained += OnEnergyGained;
        GameEvents.BattleFinished += OnBattleFinished;
        GameEvents.WinStreakChanged += OnWinStreakChanged;
        GameEvents.OnResourcesChanged += OnResourcesChanged;
        GameEvents.OnBattleStateChanged += HandleBattleStateChanged;
        GameEvents.OnTeamChanged += OnTeamChanged;
        GameEvents.OnEncounterAutoModeChanged += ApplyCloseLock;

        RefreshFatigueForecast();

        if (!IsInBattle())
        {
            _suppressAutoBlinderUntilBattle = false;
            ShowBlinder(true, instant: true);
            EnsureTeamPreviewForCurrentState(forceRebuild: true);
            PickAndApplyBlinderLine();
        }
        else
        {
            ShowBlinder(false, instant: true);
            EnsureTeamPreviewForCurrentState(forceRebuild: false);
            ApplyCloseLock();
        }

        if (hireYesButton)
        {
            hireYesButton.onClick.RemoveAllListeners();
            hireYesButton.onClick.AddListener(OnClickHireYes);
        }
        if (hireNoButton)
        {
            hireNoButton.onClick.RemoveAllListeners();
            hireNoButton.onClick.AddListener(OnClickHireNo);
        }
        if (hireContinueButton)
        {
            hireContinueButton.onClick.RemoveAllListeners();
            hireContinueButton.onClick.AddListener(OnClickHireContinue);
        }

        RefreshAll();
        RefreshEncounterBoostIconsAndTooltips(force: true);
    }

    void OnDisable()
    {
        if (EncounterManager.I != null)
        {
            EncounterManager.I.OnStateChanged -= OnEncounterStateChanged;
            GameEvents.EnergyChanged -= RefreshEnergy;
        }

        EncounterManager.OnEnergyGained -= OnEnergyGained;
        GameEvents.BattleFinished -= OnBattleFinished;
        GameEvents.WinStreakChanged -= OnWinStreakChanged;
        GameEvents.OnResourcesChanged -= OnResourcesChanged;
        GameEvents.OnBattleStateChanged -= HandleBattleStateChanged;
        GameEvents.OnTeamChanged -= OnTeamChanged;
        GameEvents.OnEncounterAutoModeChanged -= ApplyCloseLock;

        if (encounterBtn) encounterBtn.onClick.RemoveAllListeners();
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        if (_typewriterCo != null) StopCoroutine(_typewriterCo);
        _isFading = false;

        StopPulse(ref _shinyPulseTweenId, shinyTimerLabel);
        StopPulse(ref _capturePulseTweenId, captureTimerLabel);
        StopPulse(ref _favorPulseTweenId, favorTimerLabel);
    }

    void Update()
    {
        _etaTickAccum += Time.unscaledDeltaTime;
        if (_etaTickAccum >= 1f)
        {
            _etaTickAccum = 0f;
            UpdateEnergyEtaUI();
            RefreshEncounterBoostIconsAndTooltips(force: false);
        }
    }

    void OnResourcesChanged()
    {
        RefreshEncounterBoostIconsAndTooltips(force: true);
        RefreshEnergy();

        RefreshFatigueForecast();

        // Team can change due to hires/captures, etc.
        EnsureTeamPreviewForCurrentState(forceRebuild: !IsInBattle());

        // Button state depends on both energy and team health.
        RefreshEncounterButtonInteractivity();
    }

    void OnTeamChanged()
    {
        // Team can change due to healing, level-ups, swaps, etc.
        EnsureTeamPreviewForCurrentState(forceRebuild: !IsInBattle());
        RefreshEncounterButtonInteractivity();

        RefreshFatigueForecast();
    }

    // ─────────────────────────────────────────────────────────────
    // Public refresh
    // ─────────────────────────────────────────────────────────────
    public void RefreshAll()
    {
        RefreshButtonAndLabel();
        RefreshEnergy();
        UpdateEnergyEtaUI();
        RefreshEncounterButtonInteractivity();

        RefreshFatigueForecast();
    }

    // ─────────────────────────────────────────────────────────────
    // Fatigue Forecast
    // ─────────────────────────────────────────────────────────────
    void RefreshFatigueForecast()
    {
        if (!fatigueForecastLabel) return;              // feature not wired
        if (!showFatigueForecast)
        {
            fatigueForecastLabel.gameObject.SetActive(false);
            return;
        }

        // Only meaningful when we're about to start an encounter.
        if (IsInBattle() || IsHireDecisionOpen)
        {
            fatigueForecastLabel.gameObject.SetActive(false);
            return;
        }

        int projected = CountProjectedExhaustedBitlings();
        if (projected <= 0)
        {
            fatigueForecastLabel.gameObject.SetActive(false);
            return;
        }

        int n = Mathf.Max(0, projected);
        string plural = (n == 1) ? "" : "s";

        // Template supports {0} for count, {1} for plural suffix.
        string tpl = string.IsNullOrEmpty(fatigueForecastTemplate)
            ? "This battle will exhaust {0} Bitling{1}"
            : fatigueForecastTemplate;

        fatigueForecastLabel.text = string.Format(tpl, n, plural);
        fatigueForecastLabel.gameObject.SetActive(true);
    }

    int CountProjectedExhaustedBitlings()
    {
        // Design intent: forecast how many Bitlings will be *committed* to the
        // battle (and therefore may end up exhausted / on downtime).
        //
        // We use the number of alive team members participating (first N slots).
        // This keeps the forecast deterministic and cheap, and avoids guessing
        // the outcome of battle.

        var data = SaveManager.Data;
        if (data == null || data.team == null) return 0;

        int slots = Mathf.Clamp(battleTeamSlots, 1, 6);
        int count = 0;

        for (int i = 0; i < data.team.Count && i < slots; i++)
        {
            var om = data.team[i];
            if (om == null) continue;
            if (string.IsNullOrEmpty(om.monsterId)) continue;

            // currentHP == 0 is the existing “exhausted” / KO state in your UI logic.
            // currentHP < 0 means “uninitialized”; treat as alive for forecast.
            if (om.currentHP == 0) continue;
            count++;
        }

        return count;
    }

    // ─────────────────────────────────────────────────────────────
    // Encounter Boost Icons + Tooltips
    // ─────────────────────────────────────────────────────────────
    void RefreshEncounterBoostIconsAndTooltips(bool force)
    {
        UpdateEnergyTooltip();

        long flyerRem = (EncounterManager.I != null) ? EncounterManager.I.GetFlyerSecondsRemaining() : -1;
        bool flyerActive = flyerRem > 0;

        SetBoostRootActive(flyerRoot, flyerActive);

        if (flyerActive)
        {
            var cur = EncounterManager.I != null ? EncounterManager.I.CurrentFlyer : null;

            if (flyerTypeLabel)
                flyerTypeLabel.text = (cur != null) ? cur.type.ToString() : "Unknown";

            UpdateFlyerTooltip(cur, flyerRem);
        }
        else
        {
            ClearTooltip(flyerTooltip);
        }

        long shinyRem = GetShinySecondsRemaining();
        bool shinyActive = shinyRem > 0;

        SetBoostRootActive(shinyRoot, shinyActive);

        if (shinyActive)
        {
            if (shinyTimerLabel)
                shinyTimerLabel.text = FormatHMS(shinyRem);

            ApplyTimerWarningFX(shinyRem, shinyTimerLabel, ref _shinyPulseTweenId);

            UpdateSimpleTimerTooltip(
                shinyTooltip,
                "Shiny Charm Active",
                $"Time Remaining: {FormatHMS(shinyRem)}"
            );
        }
        else
        {
            StopPulse(ref _shinyPulseTweenId, shinyTimerLabel);
            ClearTooltip(shinyTooltip);
        }

        long captureRem = GetCaptureSecondsRemaining();
        bool captureActive = captureRem > 0;

        SetBoostRootActive(captureRoot, captureActive);

        if (captureActive)
        {
            if (captureTimerLabel)
                captureTimerLabel.text = FormatHMS(captureRem);

            ApplyTimerWarningFX(captureRem, captureTimerLabel, ref _capturePulseTweenId);

            UpdateSimpleTimerTooltip(
                captureTooltip,
                "Capture Boost Active",
                $"Time Remaining: {FormatHMS(captureRem)}"
            );
        }
        else
        {
            StopPulse(ref _capturePulseTweenId, captureTimerLabel);
            ClearTooltip(captureTooltip);
        }

        long favorRem = GetFavorSecondsRemaining();
        bool favorActive = favorRem > 0;

        SetBoostRootActive(favorRoot, favorActive);

        if (favorActive)
        {
            if (favorTimerLabel)
                favorTimerLabel.text = FormatHMS(favorRem);

            ApplyTimerWarningFX(favorRem, favorTimerLabel, ref _favorPulseTweenId);

            UpdateSimpleTimerTooltip(
                favorTooltip,
                "Favor Boost Active",
                $"Time Remaining: {FormatHMS(favorRem)}"
            );
        }
        else
        {
            StopPulse(ref _favorPulseTweenId, favorTimerLabel);
            ClearTooltip(favorTooltip);
        }
    }

    void UpdateEnergyTooltip()
    {
        if (!energyTooltip) return;

        int cur = GetEnergyPoints();
        int max = GetEncounterMax();

        string eta = energyEtaLabel ? energyEtaLabel.text : "";
        if (string.IsNullOrEmpty(eta))
            eta = BuildEnergyEtaStringFallback(cur, max);

        energyTooltip.message = "Energy";
        energyTooltip.subtitle = $"{cur} / {max}\n{eta}";
    }

    void UpdateFlyerTooltip(object flyerObj, long remainingSeconds)
    {
        if (!flyerTooltip) return;

        string typeName = "Unknown";
        if (EncounterManager.I != null && EncounterManager.I.CurrentFlyer != null)
            typeName = EncounterManager.I.CurrentFlyer.type.ToString();

        flyerTooltip.message = "Flyer Active";
        flyerTooltip.subtitle = $"{typeName}\nTime Remaining: {FormatHMS(remainingSeconds)}";
    }

    void UpdateSimpleTimerTooltip(TooltipTrigger trigger, string title, string subtitle)
    {
        if (!trigger) return;
        trigger.message = title;
        trigger.subtitle = subtitle;
    }

    void ClearTooltip(TooltipTrigger trigger)
    {
        if (!trigger) return;
        trigger.message = "";
        trigger.subtitle = "";
    }

    string BuildEnergyEtaStringFallback(int cur, int max)
    {
        if (cur >= max) return "Energy full";

        int seconds = EncounterManager.I != null
            ? EncounterManager.I.GetSecondsUntilFull()
            : (int)((max - cur) * Mathf.Max(1f, energySecondsPerPoint));

        int hours = seconds / 3600;
        int minutes = (seconds % 3600) / 60;

        return (hours > 0)
            ? $"Full in ~ {hours}h {minutes:D2}m"
            : $"Full in ~ {minutes}m";
    }

    void EnsureTooltipTrigger(Button btn, ref TooltipTrigger trigger)
    {
        if (!btn) return;

        if (!trigger)
            trigger = btn.GetComponent<TooltipTrigger>();

        if (!trigger)
            trigger = btn.gameObject.AddComponent<TooltipTrigger>();
    }

    void SetBoostRootActive(GameObject root, bool active)
    {
        if (!root) return;
        if (root.activeSelf != active)
            root.SetActive(active);
    }

    void ApplyTimerWarningFX(long remainingSeconds, TextMeshProUGUI label, ref int tweenId)
    {
        if (!label) return;

        bool warn = warningFxEnabled && remainingSeconds > 0 && remainingSeconds <= Mathf.Max(1, warningSeconds);
        label.color = warn ? timerWarningColor : timerNormalColor;

        if (!warn)
        {
            StopPulse(ref tweenId, label);
            return;
        }

        var rt = label.rectTransform;
        if (!rt) return;

        if (tweenId != -1 && LeanTween.isTweening(tweenId))
            return;

        rt.localScale = Vector3.one;

        tweenId = LeanTween.scale(rt.gameObject, Vector3.one * pulseScale, pulseTime)
            .setEaseInOutSine()
            .setLoopPingPong()
            .id;
    }

    void StopPulse(ref int tweenId, TextMeshProUGUI label)
    {
        if (tweenId != -1)
        {
            if (LeanTween.isTweening(tweenId))
                LeanTween.cancel(tweenId);
            tweenId = -1;
        }

        if (label && label.rectTransform)
            label.rectTransform.localScale = Vector3.one;

        if (label)
            label.color = timerNormalColor;
    }

    long GetShinySecondsRemaining()
    {
        var list = SaveManager.Data?.activeShinyBoosts;
        if (list == null || list.Count == 0) return -1;

        var cur = list[0];
        if (cur == null) return -1;

        long rem = cur.expireUnix - SaveManager.NowUnix();
        if (rem <= 0)
        {
            list.Clear();
            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();
            return -1;
        }

        return Math.Max(0L, rem);
    }

    long GetCaptureSecondsRemaining()
    {
        var list = SaveManager.Data?.activeWorkOrders;
        if (list == null || list.Count == 0) return -1;

        var cur = list[0];
        if (cur == null) return -1;

        long rem = cur.expireUnix - SaveManager.NowUnix();
        if (rem <= 0)
        {
            list.Clear();
            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();
            return -1;
        }

        return Math.Max(0L, rem);
    }

    long GetFavorSecondsRemaining()
    {
        var list = SaveManager.Data?.activeFavorBoosts;
        if (list == null || list.Count == 0) return -1;

        var cur = list[0];
        if (cur == null) return -1;

        long rem = cur.expireUnix - SaveManager.NowUnix();
        if (rem <= 0)
        {
            list.Clear();
            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();
            return -1;
        }

        return Math.Max(0L, rem);
    }

    static string FormatHMS(long seconds)
    {
        seconds = Math.Max(0L, seconds);
        var t = TimeSpan.FromSeconds(seconds);

        if (t.TotalHours >= 1.0)
            return $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s";
        if (t.TotalMinutes >= 1.0)
            return $"{t.Minutes}m {t.Seconds}s";
        return $"{t.Seconds}s";
    }

    // ─────────────────────────────────────────────────────────────
    // Win Streak logging
    // ─────────────────────────────────────────────────────────────
    void OnWinStreakChanged(int value)
    {
        BattleLogger.Log($"Win Streak: {value}", LogScope.Encounter);
    }

    void LogCurrentWinStreak(string prefix = null)
    {
        int streak = (EncounterManager.I != null) ? EncounterManager.I.CurrentWinStreak : 0;
        string label = string.IsNullOrEmpty(prefix) ? "Win Streak" : $"{prefix} • Win Streak";
        BattleLogger.Log($"{label}: {streak}", LogScope.Encounter);
    }

    void OnBattleFinished(BattleResult _)
    {
        LogCurrentWinStreak("Updated");

        if (!IsInBattle())
        {
            _suppressAutoBlinderUntilBattle = false;
            ShowBlinder(true, instant: true);
            EnsureTeamPreviewForCurrentState(forceRebuild: true);
            PickAndApplyBlinderLine();
        }

        RefreshEncounterBoostIconsAndTooltips(force: true);
    }

    public void ForceBlinderAlphaToOne()
    {
        if (!blinderGroup) return;

        if (_fadeCo != null) { StopCoroutine(_fadeCo); _fadeCo = null; }
        _isFading = false;

        blinderGroup.alpha = 1f;
        blinderGroup.blocksRaycasts = true;
        blinderGroup.interactable = true;
    }

    // ─────────────────────────────────────────────────────────────
    // Button / Energy / ETA
    // ─────────────────────────────────────────────────────────────
    void RefreshButtonAndLabel()
    {
        bool nextFree = NextEncounterIsFree();
        bool auto = IsAutoMode();

        if (!encounterBtn)
            return;

        if (!encounterLabel)
            encounterLabel = encounterBtn.GetComponentInChildren<TextMeshProUGUI>();

        if (encounterLabel)
        {
            if (auto)
                encounterLabel.text = "AUTO: ON";
            else if (nextFree)
                encounterLabel.text = "NEXT";
            else
                encounterLabel.text = "ENCOUNTER";
        }
    }

    void RefreshEncounterButtonInteractivity()
    {
        if (!encounterBtn) return;

        // Requirements:
        //  - not currently in battle
        //  - not mid-fade and not in the hire decision UI
        //  - player has at least one monster on the team that is alive (HP != 0)
        //  - enough energy to pay the cost, unless the next encounter is free
        bool inBattle = IsInBattle();
        bool busy = _isFading || IsHireDecisionOpen;
        bool hasAliveTeam = HasAliveTeamMember();
        bool hasEnergyOrFree = NextEncounterIsFree() || HasEnergy() || HasFallbackEnergy();

        // NOTE: We still allow the button to be interactable during battle IF auto-mode is currently enabled.
        // This enables the player to HOLD the button to disable auto-mode for the next battle,
        // while still preventing taps from starting a new encounter (EncounterManager blocks when in battle).
        bool allowDuringBattleForAutoToggle = inBattle && EncounterManager.I != null && EncounterManager.I.IsAutoMode;

        encounterBtn.interactable = !busy && hasAliveTeam && hasEnergyOrFree && (!inBattle || allowDuringBattleForAutoToggle);
    }

    bool HasAliveTeamMember()
    {
        var team = SafeTeamList();
        if (team == null || team.Count == 0) return false;

        for (int i = 0; i < team.Count; i++)
        {
            var m = team[i];
            if (m == null || string.IsNullOrEmpty(m.monsterId)) continue;
            if (m.currentHP != 0) return true; // -1 (uninitialized) counts as alive
        }

        return false;
    }

    bool HasFallbackEnergy()
    {
        // Safety fallback when EncounterManager isn't available yet.
        if (EncounterManager.I != null) return false;

        int current = Mathf.Max(0, ResourceBank.Get(ResourceType.Energy));
        int needed = Mathf.Max(1, GetEncounterCost());
        return current >= needed;
    }

    void RefreshEnergy()
    {
        if (!energyLabel) return;

        int cur = GetEnergyPoints();
        int max = GetEncounterMax();
        bool has = cur >= GetEncounterCost();

        energyLabel.text = $"{cur} / {max}";
        energyLabel.color = has ? Color.white : new Color(1f, 0.5f, 0.5f);

        UpdateEnergyEtaUI();
        UpdateEnergyTooltip();
    }

    void UpdateEnergyEtaUI()
    {
        if (!energyEtaLabel) return;

        int cur = GetEnergyPoints();
        int max = GetEncounterMax();

        if (cur >= max)
        {
            energyEtaLabel.text = "Energy full";
            return;
        }

        int seconds = EncounterManager.I != null
            ? EncounterManager.I.GetSecondsUntilFull()
            : (int)((max - cur) * Mathf.Max(1f, energySecondsPerPoint));

        int hours = seconds / 3600;
        int minutes = (seconds % 3600) / 60;

        energyEtaLabel.text = (hours > 0)
            ? $"Full in ~ {hours}h {minutes:D2}m"
            : $"Full in ~ {minutes}m";
    }

    void OnEncounterStateChanged()
    {
        bool inBattle = IsInBattle();

        if (inBattle)
        {
            _suppressAutoBlinderUntilBattle = false;
            ShowBlinder(false, instant: true);
        }
        else
        {
            RefreshAll();

            if (!_suppressAutoBlinderUntilBattle)
            {
                ShowBlinder(true, instant: true);
                PickAndApplyBlinderLine();
            }

            EnsureTeamPreviewForCurrentState(forceRebuild: true);
        }

        RefreshEncounterBoostIconsAndTooltips(force: true);
        ApplyCloseLock();
        RefreshEncounterButtonInteractivity();
    }

    void OnClickEncounter()
    {
        if (_isFading) return;
        if (IsHireDecisionOpen) return;

        bool auto = IsAutoMode();
        bool inBattle = IsInBattle();
        bool nextFree = NextEncounterIsFree();
        bool hasEnergy = HasEnergy();

        if (!inBattle && !auto && !nextFree && !hasEnergy)
        {
            PlayNoEnergyFX();
            AudioManager.I?.PlaySfx(SfxType.Denied);
            return;
        }

        // NEW: suppress auto blinder re-show until battle state flips
        _suppressAutoBlinderUntilBattle = true;

        if (blinderGroup && blinderGroup.alpha > 0.01f)
        {
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(Co_FadeOutBlinderThenStartEncounter());
        }
        else
        {
            RequestEncounterTap();
        }
    }

    IEnumerator Co_FadeOutBlinderThenStartEncounter()
    {
        _isFading = true;
        RefreshButtonAndLabel();

        if (blinderText) blinderText.raycastTarget = true;
        if (blinderGroup)
        {
            blinderGroup.blocksRaycasts = true;
            blinderGroup.interactable = true;
        }

        if (_typewriterCo != null)
        {
            StopCoroutine(_typewriterCo);
            _typewriterCo = null;
        }

        yield return new WaitForSecondsRealtime(Mathf.Max(0.05f, preFadeDelay));

        if (blinderGroup)
        {
            float t = 0f;
            float dur = Mathf.Max(0.1f, fadeDuration);
            float start = blinderGroup.alpha;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                blinderGroup.alpha = Mathf.Lerp(start, 0f, t / dur);
                yield return null;
            }
            blinderGroup.alpha = 0f;
            blinderGroup.blocksRaycasts = false;
            blinderGroup.interactable = false;
        }

        _isFading = false;
        RefreshButtonAndLabel();
        RequestEncounterTap();
    }

    // ─────────────────────────────────────────────────────────────
    // Blinder helpers
    // ─────────────────────────────────────────────────────────────
    void ShowBlinder(bool show, bool instant)
    {
        if (!blinderGroup) return;

        if (instant)
        {
            blinderGroup.alpha = show ? 1f : 0f;
            blinderGroup.blocksRaycasts = show;
            blinderGroup.interactable = show;

            if (show)
            {
                RefreshBlinderTint();
                PickAndApplyBlinderLine();
            }

            // IMPORTANT: team preview is NOT tied to blinder visibility anymore
            EnsureTeamPreviewForCurrentState(forceRebuild: false);
        }
        else
        {
            if (_fadeCo != null) StopCoroutine(_fadeCo);
            _fadeCo = StartCoroutine(show ? Co_FadeTo(1f) : Co_FadeTo(0f));
        }
    }

    IEnumerator Co_FadeTo(float target)
    {
        _isFading = true;
        float start = blinderGroup.alpha;
        float dur = Mathf.Max(0.1f, fadeDuration);
        float t = 0f;

        blinderGroup.blocksRaycasts = true;
        blinderGroup.interactable = true;

        if (_typewriterCo != null)
        {
            StopCoroutine(_typewriterCo);
            _typewriterCo = null;
        }

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            blinderGroup.alpha = Mathf.Lerp(start, target, t / dur);
            yield return null;
        }
        blinderGroup.alpha = target;

        blinderGroup.blocksRaycasts = target > 0.01f;
        blinderGroup.interactable = target > 0.01f;

        if (target > 0.01f)
        {
            RefreshBlinderTint();
            PickAndApplyBlinderLine();
        }

        EnsureTeamPreviewForCurrentState(forceRebuild: false);

        _isFading = false;
        RefreshButtonAndLabel();
    }

    // ─────────────────────────────────────────────────────────────
    // Hire Decision (kept as you had it)
    // ─────────────────────────────────────────────────────────────
    public void ShowHireDecision(MonsterDataSO def, int level)
    {
        bool shiny = EncounterManager.I != null && EncounterManager.I.CurrentWildIsShiny;
        ShowHireDecision(def, level, shiny);
    }

    public void ShowHireDecision(MonsterDataSO def, int level, bool isShiny)
    {
        if (!hireDecisionRoot || def == null)
        {
            EncounterManager.I?.OnHireDecisionResolved(false, false);
            return;
        }

        _pendingHireDef = def;
        _pendingHireLevel = Mathf.Max(1, level);
        _pendingHireIsShiny = isShiny;

        _hireChoseYes = false;
        _hireCaptureSucceeded = false;
        _hireDecisionLocked = false;

        if (hireMonsterIcon)
            hireMonsterIcon.sprite = MonsterNameFormatter.GetIcon(def, _pendingHireIsShiny, backIcon: false);

        if (hirePromptText)
            hirePromptText.text = $"Do you want to hire {MonsterNameFormatter.Format(def, _pendingHireIsShiny)}?";

        ClearHireResultVisuals();

        if (hireButtonsRoot) hireButtonsRoot.SetActive(true);
        if (hireContinueButton) hireContinueButton.gameObject.SetActive(false);

        ShowBlinder(false, instant: true);
        hireDecisionRoot.SetActive(true);
        RefreshEncounterButtonInteractivity();

    }

    void OnClickHireYes()
    {
        if (_isFading) return;
        if (_hireDecisionLocked) return;

        _hireDecisionLocked = true;
        _hireChoseYes = true;

        bool success = false;
        if (EncounterManager.I != null && _pendingHireDef != null)
            success = EncounterManager.I.TryCaptureFromDecision(_pendingHireDef, _pendingHireLevel);

        _hireCaptureSucceeded = success;

        SpawnHireResult(success);
        SetHirePromptForResult(choseYes: true, captureSucceeded: success);

        if (hireButtonsRoot) hireButtonsRoot.SetActive(false);
        if (hireContinueButton) hireContinueButton.gameObject.SetActive(true);
    }

    void OnClickHireNo()
    {
        if (_isFading) return;
        if (_hireDecisionLocked) return;

        _hireDecisionLocked = true;
        _hireChoseYes = false;
        _hireCaptureSucceeded = false;

        SpawnHireResult(false);
        SetHirePromptForResult(choseYes: false, captureSucceeded: false);

        if (hireButtonsRoot) hireButtonsRoot.SetActive(false);
        if (hireContinueButton) hireContinueButton.gameObject.SetActive(true);
    }

    void OnClickHireContinue()
    {
        if (!_hireDecisionLocked)
            return;

        if (hireDecisionRoot) hireDecisionRoot.SetActive(false);

        RefreshEncounterButtonInteractivity();

        EncounterManager.I?.OnHireDecisionResolved(_hireChoseYes, _hireCaptureSucceeded);

        _pendingHireDef = null;
        _pendingHireLevel = 0;
        _pendingHireIsShiny = false;
        _hireDecisionLocked = false;

        if (hireContinueButton) hireContinueButton.gameObject.SetActive(false);
        if (hireButtonsRoot) hireButtonsRoot.SetActive(true);

        if (!IsInBattle())
        {
            _suppressAutoBlinderUntilBattle = false;
            ShowBlinder(true, instant: true);
            EnsureTeamPreviewForCurrentState(forceRebuild: true);
            PickAndApplyBlinderLine();
        }
    }

    void SetHirePromptForResult(bool choseYes, bool captureSucceeded)
    {
        if (!hirePromptText) return;

        string name = (_pendingHireDef != null)
            ? MonsterNameFormatter.Format(_pendingHireDef, _pendingHireIsShiny)
            : "this monster";

        if (!choseYes)
        {
            hirePromptText.text = $"You declined {name}.";
            return;
        }

        hirePromptText.text = captureSucceeded
            ? $"Hired {name}! Added to your roster."
            : $"Hiring failed — {name} refused the offer.";
    }

    void SpawnHireResult(bool success)
    {
        if (!hireResultSpawnPoint) return;

        GameObject prefab = success ? hireAgreePrefab : hireDenyPrefab;
        if (!prefab) return;

        ClearHireResultVisuals();
        Instantiate(prefab, hireResultSpawnPoint.position, hireResultSpawnPoint.rotation, hireResultSpawnPoint);
    }

    void ClearHireResultVisuals()
    {
        if (!hireResultSpawnPoint) return;

        for (int i = hireResultSpawnPoint.childCount - 1; i >= 0; i--)
        {
            var child = hireResultSpawnPoint.GetChild(i);
            if (child) Destroy(child.gameObject);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Blinder visuals + picker (unchanged)
    // ─────────────────────────────────────────────────────────────
    void RefreshBlinderTint()
    {
        if (!blinderBackground) return;

        Color tint = defaultBlinderTint;

        if (IsBossImminent())
        {
            tint = bossImminentTint;
        }
        else
        {
            var lure = (EncounterManager.I != null) ? EncounterManager.I.CurrentFlyer : null;
            if (lure != null)
                tint = ResolveLureTint(lure.type);
        }

        blinderBackground.color = tint;
    }

    Color ResolveLureTint(MonsterType type)
    {
        if (lureTints != null)
        {
            for (int i = 0; i < lureTints.Count; i++)
            {
                var lt = lureTints[i];
                if (lt != null && lt.type == type)
                    return lt.tint;
            }
        }
        return genericLureTint;
    }

    bool IsBossImminent()
    {
        var data = SaveManager.Data;
        if (data == null) return false;

        int cadence = (data.bossEveryN > 0) ? data.bossEveryN : 10;
        if (cadence < 1) cadence = 10;

        int since = Mathf.Max(0, data.encountersSinceBoss);
        return since >= (cadence - 1);
    }

    void ApplyBlinderText(string message, bool instant = false)
    {
        if (!blinderText) return;

        if (_typewriterCo != null)
        {
            StopCoroutine(_typewriterCo);
            _typewriterCo = null;
        }

        bool skipTypewriter =
            instant ||
            typewriterCharDelay <= 0f ||
            !gameObject.activeInHierarchy;

        if (skipTypewriter)
        {
            blinderText.text = message;
            return;
        }

        _typewriterCo = StartCoroutine(Co_TypeWriter(message));
    }

    IEnumerator Co_TypeWriter(string fullText)
    {
        if (!blinderText)
            yield break;

        blinderText.text = string.Empty;

        for (int i = 0; i <= fullText.Length; i++)
        {
            blinderText.text = fullText.Substring(0, i);
            yield return new WaitForSecondsRealtime(typewriterCharDelay);
        }

        _typewriterCo = null;
    }

    void PickAndApplyBlinderLine(bool forcePick = false)
    {
        if (!blinderText) return;

        if (!useRandomBlinder && !forcePick)
        {
            ApplyBlinderText(hardFallbackLine, instant: true);
            return;
        }

        BlinderMessagePackSO pack = overridePack;
        if (!pack && blinderLibrary)
        {
            string lang = preferredLanguageCode;
            if (string.IsNullOrWhiteSpace(lang))
            {
                lang = Application.systemLanguage.ToString().ToLowerInvariant();
                if (lang.StartsWith("english")) lang = "en";
                else if (lang.StartsWith("spanish")) lang = "es";
                else if (lang.StartsWith("french")) lang = "fr";
                else if (lang.StartsWith("portuguese")) lang = "pt";
                else if (lang.StartsWith("german")) lang = "de";
                else if (lang.StartsWith("italian")) lang = "it";
                else if (lang.StartsWith("japanese")) lang = "ja";
                else if (lang.StartsWith("korean")) lang = "ko";
                else if (lang.StartsWith("chinese")) lang = "zh";
            }
            pack = blinderLibrary.ResolvePack(lang?.ToLowerInvariant());
        }

        if (!pack || pack.entries == null || pack.entries.Count == 0)
        {
            ApplyBlinderText(hardFallbackLine, instant: true);
            return;
        }

        float totalWeight = 0f;
        int validCount = 0;
        for (int i = 0; i < pack.entries.Count; i++)
        {
            var e = pack.entries[i];
            if (string.IsNullOrWhiteSpace(e.line)) continue;
            if (e.weight <= 0f) continue;
            totalWeight += e.weight;
            validCount++;
        }

        if (validCount == 0 || totalWeight <= 0f)
        {
            ApplyBlinderText(hardFallbackLine, instant: true);
            return;
        }

        float r = Random.value * totalWeight;
        float acc = 0f;

        string chosen = hardFallbackLine;
        for (int i = 0; i < pack.entries.Count; i++)
        {
            var e = pack.entries[i];
            if (string.IsNullOrWhiteSpace(e.line) || e.weight <= 0f) continue;

            acc += e.weight;
            if (r <= acc)
            {
                chosen = e.line;
                break;
            }
        }

        if (validCount > 1 && !string.IsNullOrEmpty(_lastBlinderLine) && chosen == _lastBlinderLine)
        {
            r = Random.value * totalWeight;
            acc = 0f;
            for (int i = 0; i < pack.entries.Count; i++)
            {
                var e = pack.entries[i];
                if (string.IsNullOrWhiteSpace(e.line) || e.weight <= 0f) continue;

                acc += e.weight;
                if (r <= acc)
                {
                    chosen = e.line;
                    break;
                }
            }
        }

        _lastBlinderLine = chosen;
        ApplyBlinderText(chosen);
        ForceBlinderAlphaToOne();
    }

    // ─────────────────────────────────────────────────────────────
    // Team preview (FIXED: governed by battle state, not blinder)
    // ─────────────────────────────────────────────────────────────
    void EnsureTeamPreviewForCurrentState(bool forceRebuild)
    {
        if (!teamPreviewRoot || !teamItemPrefab)
            return;

        bool inBattle = IsInBattle();

        if (inBattle)
        {
            ClearTeamPreview();
            return;
        }

        if (!teamPreviewRoot.gameObject.activeSelf)
            teamPreviewRoot.gameObject.SetActive(true);

        if (forceRebuild)
            BuildTeamPreview();
        else if (_previewItems.Count == 0)
            BuildTeamPreview();
    }

    void BuildTeamPreview()
    {
        if (!teamPreviewRoot || !teamItemPrefab) return;

        if (!teamPreviewRoot.gameObject.activeSelf) teamPreviewRoot.gameObject.SetActive(true);

        ClearTeamPreview(dontHideRoot: true);

        var team = SafeTeamList();
        if (team == null || team.Count == 0)
        {
            if (teamPreviewRoot) teamPreviewRoot.gameObject.SetActive(false);
            return;
        }

        int shown = 0;
        for (int i = 0; i < team.Count && shown < maxTeamShown; i++)
        {
            var om = team[i];
            if (om == null || string.IsNullOrEmpty(om.monsterId)) continue;

            var item = Instantiate(teamItemPrefab, teamPreviewRoot);
            item.Bind(om);
            _previewItems.Add(item);
            shown++;
        }

        if (_previewItems.Count > 0 && blinderGroup && blinderGroup.alpha > 0.01f && teamStaggerFade > 0f)
            StartCoroutine(Co_FadeInTeamPreview());
        else
            SetTeamAlpha(1f);
    }

    void ClearTeamPreview(bool dontHideRoot = false)
    {
        for (int i = 0; i < _previewItems.Count; i++)
            if (_previewItems[i])
                Destroy(_previewItems[i].gameObject);
        _previewItems.Clear();

        if (!dontHideRoot && teamPreviewRoot && teamPreviewRoot.gameObject.activeSelf)
            teamPreviewRoot.gameObject.SetActive(false);
    }

    IEnumerator Co_FadeInTeamPreview()
    {
        SetTeamAlpha(0f);
        for (int i = 0; i < _previewItems.Count; i++)
        {
            var item = _previewItems[i];
            if (!item) continue;

            float t = 0f;
            const float dur = 0.15f;
            while (t < dur)
            {
                t += Time.unscaledDeltaTime;
                item.SetAlpha(Mathf.Clamp01(t / dur));
                yield return null;
            }
            item.SetAlpha(1f);

            if (teamStaggerFade > 0f)
                yield return new WaitForSecondsRealtime(teamStaggerFade);
        }
    }

    void SetTeamAlpha(float a)
    {
        for (int i = 0; i < _previewItems.Count; i++)
            if (_previewItems[i]) _previewItems[i].SetAlpha(a);
    }

    List<OwnedMonsterData> SafeTeamList()
    {
        if (SaveManager.Data == null) return null;
        return SaveManager.Data.team;
    }

    // ─────────────────────────────────────────────────────────────
    // EncounterManager passthrough helpers
    // ─────────────────────────────────────────────────────────────
    bool IsInBattle() => EncounterManager.I != null && EncounterManager.I.IsInBattle;
    bool IsAutoMode() => EncounterManager.I != null && EncounterManager.I.IsAutoMode;
    bool NextEncounterIsFree() => EncounterManager.I != null && EncounterManager.I.NextEncounterIsFree;
    bool HasEnergy() => EncounterManager.I != null && EncounterManager.I.HasEnergy();
    public int GetEnergyPoints() => EncounterManager.I != null ? EncounterManager.I.GetEnergyPoints() : 0;
    public int GetEncounterMax() => EncounterManager.I != null ? EncounterManager.I.GetEncounterMax() : 0;
    public int GetEncounterCost() => EncounterManager.I != null ? EncounterManager.I.GetEncounterCost() : 0;

    void RequestEncounterTap() => EncounterManager.I?.RequestEncounterTap();
    public void OnClickToggleAuto() => EncounterManager.I?.ToggleAutoMode();

    // ─────────────────────────────────────────────────────────────
    // Energy events & FX (unchanged)
    // ─────────────────────────────────────────────────────────────
    void OnEnergyGained(int gained, int newTotal)
    {
        if (gained <= 0) return;

        PlayEnergyGainedFX();
        SpawnEnergyToast(gained);

        UpdateEnergyTooltip();
    }

    void PlayEnergyGainedFX()
    {
        if (!encounterBtn) return;

        var rt = encounterBtn.transform as RectTransform;
        if (!rt) return;

        rt.localScale = Vector3.one;

        LeanTween.scale(rt.gameObject,
                        Vector3.one * energyGainPunchScale,
                        energyGainPunchTime)
                .setEaseOutBack()
                .setOnComplete(() =>
                {
                    if (rt) rt.localScale = Vector3.one;
                });
    }

    void PlayNoEnergyFX()
    {
        if (!energyLabel) return;

        var rt = energyLabel.rectTransform;
        if (!rt) return;

        rt.localScale = Vector3.one;

        LeanTween.scale(rt.gameObject,
                        Vector3.one * noEnergyPunchScale,
                        noEnergyPunchTime)
                .setEaseShake()
                .setOnComplete(() =>
                {
                    if (rt) rt.localScale = Vector3.one;
                });
    }

    void SpawnEnergyToast(int gained)
    {
        if (!energyToastPrefab || !energyToastAnchor) return;

        var go = Instantiate(energyToastPrefab, energyToastAnchor);
        var rt = go.transform as RectTransform;
        var cg = go.GetComponent<CanvasGroup>();

        if (!cg)
            cg = go.AddComponent<CanvasGroup>();

        rt.anchoredPosition = Vector2.zero;
        cg.alpha = 1f;

        var label = go.GetComponentInChildren<TextMeshProUGUI>();
        if (label)
        {
            label.text = $"+{gained} Energy";
        }

        Vector2 startPos = rt.anchoredPosition;
        Vector2 endPos = startPos + new Vector2(0f, energyToastRiseY);

        LeanTween.value(go, 0f, 1f, energyToastDuration)
                .setOnUpdate((float t) =>
                {
                    if (!rt || !cg) return;
                    rt.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
                    cg.alpha = 1f - t;
                })
                .setOnComplete(() =>
                {
                    if (go)
                        Destroy(go);
                });
    }

    public void OnWildSpawned(MonsterDataSO def)
    {
        ForceBlinderAlphaToOne();

        if (!ownedCapturedIcon)
            return;

        if (SaveManager.Data == null || SaveManager.Data.ownedIds == null || def == null)
        {
            ownedCapturedIcon.SetActive(false);
            return;
        }

        bool alreadyOwned = SaveManager.Data.ownedIds.Contains(def.id);
        ownedCapturedIcon.SetActive(alreadyOwned);
    }

    private void HandleBattleStateChanged()
    {
        ApplyCloseLock();

        bool inBattle = IsInBattle();

        if (inBattle)
        {
            _suppressAutoBlinderUntilBattle = false;

            ShowBlinder(false, instant: true);
            EnsureTeamPreviewForCurrentState(forceRebuild: false);
            return;
        }

        // Not in battle
        if (!_suppressAutoBlinderUntilBattle)
        {
            ShowBlinder(true, instant: true);
            PickAndApplyBlinderLine();

            ForceBlinderAlphaToOne();
        }

        EnsureTeamPreviewForCurrentState(forceRebuild: true);
    }


    private void ApplyCloseLock()
    {
        if (!closeButtonRoot) return;

        bool isAuto = IsAutoMode();
        if (isAuto)
        {
            closeButtonRoot.SetActive(true);
            return;
        }

        if (!hideCloseDuringBattle)
        {
            closeButtonRoot.SetActive(true);
            return;
        }

        bool inBattle = IsInBattle();
        closeButtonRoot.SetActive(!inBattle);
    }


}
