using UnityEngine;
using UnityEngine.UI;
using TMPro;
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

    // ─────────────────────────────────────────────────────────────
    // Capture Feedback (Success / Fail)
    // ─────────────────────────────────────────────────────────────
    [Header("Capture Feedback")]
    [SerializeField] private CanvasGroup captureBannerGroup;
    [SerializeField] private TextMeshProUGUI captureBannerText;
    [SerializeField] private RectTransform wildPanelRoot;

    [SerializeField] private Color successColor = new Color(0.3f, 1f, 0.3f);
    [SerializeField] private Color failColor = new Color(1f, 0.4f, 0.4f);
    [SerializeField] private Color shinyColor = new Color(1f, 0.85f, 0.2f);

    [SerializeField, Min(0.1f)] private float captureFxDuration = 0.9f;

    [Tooltip("Small 'already caught' icon shown when this species is in your collection.")]
    [SerializeField] private GameObject ownedCapturedIcon;

    // ─────────────────────────────────────────────────────────────
    // Hire Decision (now uses GameObject active/inactive; includes Continue pacing)
    // ─────────────────────────────────────────────────────────────
    [Header("Hire Decision")]
    [SerializeField] private GameObject hireDecisionRoot;          // Whole overlay root
    [SerializeField] private Image hireMonsterIcon;
    [SerializeField] private TextMeshProUGUI hirePromptText;

    [Header("Hire Decision Buttons")]
    [SerializeField] private GameObject hireButtonsRoot;           // Parent of Yes/No
    [SerializeField] private Button hireYesButton;
    [SerializeField] private Button hireNoButton;
    [SerializeField] private Button hireContinueButton;            // NEW: Continue

    [Header("Hire Decision Result Prefabs")]
    [SerializeField] private Transform hireResultSpawnPoint;
    [SerializeField] private GameObject hireAgreePrefab;
    [SerializeField] private GameObject hireDenyPrefab;

    private MonsterDataSO _pendingHireDef;
    private int _pendingHireLevel;

    // store what the player decided so Continue can finalize
    private bool _hireChoseYes;
    private bool _hireCaptureSucceeded;
    private bool _hireDecisionLocked;

    // Exposed for EncounterManager to ignore encounter clicks while open
    public bool IsHireDecisionOpen => hireDecisionRoot && hireDecisionRoot.activeSelf;

    // ─────────────────────────────────────────────────────────────
    // Wild State HUD (guard / shield / charge)
    // ─────────────────────────────────────────────────────────────
    [Header("Wild State")]
    [SerializeField] private TextMeshProUGUI wildStateLabel;
    [SerializeField] private GameObject wildGuardIcon;
    [SerializeField] private GameObject wildShieldIcon;
    [SerializeField] private GameObject wildChargeIcon;

    bool _wildIsGuarding;
    int _wildShieldAmount;
    int _wildChargeTurns;

    private TextMeshProUGUI encounterLabel;
    float _etaTickAccum = 0f;
    bool _isFading;
    Coroutine _fadeCo;
    Coroutine _typewriterCo;
    readonly List<TeamPreviewItemUI> _previewItems = new();

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

        if (captureBannerGroup)
            captureBannerGroup.alpha = 0f;

        if (ownedCapturedIcon)
            ownedCapturedIcon.SetActive(false);

        // Hire decision starts hidden
        if (hireDecisionRoot)
            hireDecisionRoot.SetActive(false);

        if (hireContinueButton)
            hireContinueButton.gameObject.SetActive(false);

        if (hireButtonsRoot)
            hireButtonsRoot.SetActive(true);

        RefreshBlinderTint();
        PickAndApplyBlinderLine(forcePick: true);
    }

    void OnEnable()
    {
        if (encounterBtn)
        {
            encounterBtn.onClick.RemoveAllListeners();
            encounterBtn.onClick.AddListener(OnClickEncounter);
        }

        if (EncounterManager.I != null)
        {
            EncounterManager.I.OnStateChanged += OnEncounterStateChanged;
            GameEvents.EnergyChanged += RefreshEnergy;
        }

        EncounterManager.OnEnergyGained += OnEnergyGained;

        LogCurrentWinStreak("Status");
        GameEvents.WinStreakChanged += OnWinStreakChanged;

        if (!IsInBattle())
        {
            ShowBlinder(true, instant: true);
            BuildTeamPreview();
            PickAndApplyBlinderLine();
        }
        else
        {
            ShowBlinder(false, instant: true);
            ClearTeamPreview();
        }

        // Hire decision button wiring
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

        GameEvents.BattleFinished += OnBattleFinished;
        UpdateEnergyEtaUI(); // seed ETA
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

        if (encounterBtn) encounterBtn.onClick.RemoveAllListeners();
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        if (_typewriterCo != null) StopCoroutine(_typewriterCo);
        _isFading = false;

        if (hireYesButton) hireYesButton.onClick.RemoveAllListeners();
        if (hireNoButton) hireNoButton.onClick.RemoveAllListeners();
        if (hireContinueButton) hireContinueButton.onClick.RemoveAllListeners();
    }

    void Update()
    {
        _etaTickAccum += Time.unscaledDeltaTime;
        if (_etaTickAccum >= 1f)
        {
            _etaTickAccum = 0f;
            UpdateEnergyEtaUI();
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Public refresh
    // ─────────────────────────────────────────────────────────────
    public void RefreshAll()
    {
        RefreshButtonAndLabel();
        RefreshEnergy();
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
        // CLEANUP REQUEST: ensure blinder alpha is forced back to 1 at battle end
        ForceBlinderAlphaToOne();

        LogCurrentWinStreak("Updated");

        if (!IsInBattle())
        {
            ShowBlinder(true, instant: true);
            BuildTeamPreview();
            PickAndApplyBlinderLine();
            ClearWildStateUI();
        }
    }

    void ForceBlinderAlphaToOne()
    {
        if (!blinderGroup) return;

        // stop any running fade so it can't leave alpha at 0
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

    void RefreshEnergy()
    {
        if (!energyLabel) return;

        int cur = GetEnergyPoints();
        int max = GetEncounterMax();
        bool has = cur >= GetEncounterCost();

        energyLabel.text = $"{cur} / {max}";
        energyLabel.color = has ? Color.white : new Color(1f, 0.5f, 0.5f);

        UpdateEnergyEtaUI();
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
            : (int)((max - cur) * Mathf.Max(1f, energySecondsPerPoint)); // fallback

        int hours = seconds / 3600;
        int minutes = (seconds % 3600) / 60;

        energyEtaLabel.text = (hours > 0)
            ? $"Full in ~ {hours}h {minutes:D2}m"
            : $"Full in ~ {minutes}m";
    }

    void OnEncounterStateChanged()
    {
        if (IsInBattle())
        {
            ShowBlinder(false, instant: true);
            ClearTeamPreview();
        }
        else
        {
            RefreshAll();
            ShowBlinder(true, instant: true);
            BuildTeamPreview();
            PickAndApplyBlinderLine();
            ClearWildStateUI();
        }
    }

    void OnClickEncounter()
    {
        if (_isFading) return;

        // If hire decision is visible, ignore encounter clicks
        if (IsHireDecisionOpen)
            return;

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
    // Blinder Fade helpers
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
                BuildTeamPreview();
            }
            else
            {
                ClearTeamPreview();
            }
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
            BuildTeamPreview();
        }
        else
        {
            ClearTeamPreview();
        }

        _isFading = false;
        RefreshButtonAndLabel();
    }

    // ─────────────────────────────────────────────────────────────
    // Hire Decision API (manual victory)
    // ─────────────────────────────────────────────────────────────
    public void ShowHireDecision(MonsterDataSO def, int level)
    {
        if (!hireDecisionRoot || def == null)
        {
            // Safety: if missing, do not block summary
            EncounterManager.I?.OnHireDecisionResolved(false, false);
            return;
        }

        _pendingHireDef = def;
        _pendingHireLevel = Mathf.Max(1, level);

        _hireChoseYes = false;
        _hireCaptureSucceeded = false;
        _hireDecisionLocked = false;

        if (hireMonsterIcon) hireMonsterIcon.sprite = def.icon;
        if (hirePromptText) hirePromptText.text = $"Do you want to hire {def.displayName}?";

        ClearHireResultVisuals();

        // UI state: decision buttons ON, continue OFF
        if (hireButtonsRoot) hireButtonsRoot.SetActive(true);
        if (hireContinueButton) hireContinueButton.gameObject.SetActive(false);

        // Hide blinder while decision is up
        ShowBlinder(false, instant: true);

        hireDecisionRoot.SetActive(true);
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

        // After decision, show Continue (pacing)
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

        // After decision, show Continue (pacing)
        if (hireButtonsRoot) hireButtonsRoot.SetActive(false);
        if (hireContinueButton) hireContinueButton.gameObject.SetActive(true);
    }

    void OnClickHireContinue()
    {
        if (!_hireDecisionLocked)
            return; // can't continue until player chose Yes/No

        // Close overlay
        if (hireDecisionRoot) hireDecisionRoot.SetActive(false);

        // Inform EncounterManager (it will patch summary + flush)
        if (EncounterManager.I != null)
            EncounterManager.I.OnHireDecisionResolved(_hireChoseYes, _hireCaptureSucceeded);

        // cleanup
        _pendingHireDef = null;
        _pendingHireLevel = 0;
        _hireDecisionLocked = false;

        if (hireContinueButton) hireContinueButton.gameObject.SetActive(false);
        if (hireButtonsRoot) hireButtonsRoot.SetActive(true);
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
    // Blinder visuals (tints + typewriter)
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
            var lure = (EncounterManager.I != null) ? EncounterManager.I.CurrentLure : null;
            if (lure != null)
            {
                tint = ResolveLureTint(lure.type);
            }
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

    // ─────────────────────────────────────────────────────────────
    // Team Preview
    // ─────────────────────────────────────────────────────────────
    void BuildTeamPreview()
    {
        if (!teamPreviewRoot || !teamItemPrefab) return;

        ClearTeamPreview();

        var team = SafeTeamList();
        if (team == null || team.Count == 0) return;

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

    void ClearTeamPreview()
    {
        for (int i = 0; i < _previewItems.Count; i++)
            if (_previewItems[i])
                Destroy(_previewItems[i].gameObject);
        _previewItems.Clear();
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
    // EncounterManager passthrough helpers (null-safe)
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
    // Localization + Weighted Picker
    // ─────────────────────────────────────────────────────────────
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
            string fallback = pack.GetAnyNonEmptyFallback(hardFallbackLine);
            ApplyBlinderText(fallback, instant: true);
            return;
        }

        string chosen = WeightedPick(pack, totalWeight);
        if (validCount > 1 && !string.IsNullOrEmpty(_lastBlinderLine) && chosen == _lastBlinderLine)
        {
            string reroll = WeightedPick(pack, totalWeight);
            if (!string.IsNullOrEmpty(reroll)) chosen = reroll;
        }

        _lastBlinderLine = chosen;
        string finalLine = string.IsNullOrEmpty(chosen)
            ? pack.GetAnyNonEmptyFallback(hardFallbackLine)
            : chosen;

        ApplyBlinderText(finalLine);
    }

    string WeightedPick(BlinderMessagePackSO pack, float totalWeight)
    {
        float r = Random.value * totalWeight;
        float acc = 0f;

        for (int i = 0; i < pack.entries.Count; i++)
        {
            var e = pack.entries[i];
            if (string.IsNullOrWhiteSpace(e.line) || e.weight <= 0f) continue;

            acc += e.weight;
            if (r <= acc) return e.line;
        }
        return pack.GetAnyNonEmptyFallback(hardFallbackLine);
    }

    // ─────────────────────────────────────────────────────────────
    // Energy events & FX
    // ─────────────────────────────────────────────────────────────
    void OnEnergyGained(int gained, int newTotal)
    {
        if (gained <= 0) return;

        PlayEnergyGainedFX();
        SpawnEnergyToast(gained);
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

    // ========================================================================
    // CAPTURE FX (Success / Fail)
    // ========================================================================
    public void OnCaptureSuccess(MonsterDataSO def, bool isShiny)
    {
        if (!captureBannerGroup || !captureBannerText)
            return;

        captureBannerGroup.gameObject.SetActive(true);
        captureBannerGroup.alpha = 0f;

        captureBannerText.text = "CAPTURED!";
        captureBannerText.color = isShiny ? shinyColor : successColor;

        if (wildPanelRoot)
        {
            LeanTween.cancel(wildPanelRoot.gameObject);
            wildPanelRoot.localScale = Vector3.one * 0.9f;

            LeanTween.scale(wildPanelRoot.gameObject, Vector3.one * 1.2f, 0.25f)
                .setEaseOutBack()
                .setOnComplete(() =>
                {
                    if (!wildPanelRoot) return;
                    LeanTween.scale(wildPanelRoot.gameObject, Vector3.one, 0.25f)
                        .setEaseOutCubic();
                });

            if (isShiny)
            {
                LeanTween.rotateZ(wildPanelRoot.gameObject, 15f, 0.12f)
                    .setLoopPingPong(2);
            }
        }

        LeanTween.value(gameObject, 0f, 1f, 0.25f)
            .setOnUpdate(a =>
            {
                if (captureBannerGroup)
                    captureBannerGroup.alpha = a;
            })
            .setOnComplete(() =>
            {
                LeanTween.value(gameObject, 1f, 0f, captureFxDuration)
                    .setDelay(0.4f)
                    .setOnUpdate(a =>
                    {
                        if (captureBannerGroup)
                            captureBannerGroup.alpha = a;
                    })
                    .setOnComplete(() =>
                    {
                        if (captureBannerGroup)
                            captureBannerGroup.gameObject.SetActive(false);
                    });
            });
    }

    public void OnCaptureFailed(MonsterDataSO def)
    {
        if (!captureBannerGroup || !captureBannerText)
            return;

        captureBannerGroup.gameObject.SetActive(true);
        captureBannerGroup.alpha = 0f;

        captureBannerText.text = "ESCAPED!";
        captureBannerText.color = failColor;

        if (wildPanelRoot)
        {
            LeanTween.cancel(wildPanelRoot.gameObject);
            Vector3 original = wildPanelRoot.localPosition;

            LeanTween.moveLocalX(wildPanelRoot.gameObject, original.x + 15f, 0.06f)
                .setLoopPingPong(3)
                .setOnComplete(() =>
                {
                    if (wildPanelRoot)
                        wildPanelRoot.localPosition = original;
                });
        }

        LeanTween.value(gameObject, 0f, 1f, 0.2f)
            .setOnUpdate(a =>
            {
                if (captureBannerGroup)
                    captureBannerGroup.alpha = a;
            })
            .setOnComplete(() =>
            {
                LeanTween.value(gameObject, 1f, 0f, captureFxDuration)
                    .setDelay(0.3f)
                    .setOnUpdate(a =>
                    {
                        if (captureBannerGroup)
                            captureBannerGroup.alpha = a;
                    })
                    .setOnComplete(() =>
                    {
                        if (captureBannerGroup)
                            captureBannerGroup.gameObject.SetActive(false);
                    });
            });
    }

    // ─────────────────────────────────────────────────────────────
    // Wild state reset + public state API
    // ─────────────────────────────────────────────────────────────
    void ClearWildStateUI()
    {
        if (ownedCapturedIcon)
            ownedCapturedIcon.SetActive(false);

        _wildIsGuarding = false;
        _wildShieldAmount = 0;
        _wildChargeTurns = 0;

        if (wildGuardIcon) wildGuardIcon.SetActive(false);
        if (wildShieldIcon) wildShieldIcon.SetActive(false);
        if (wildChargeIcon) wildChargeIcon.SetActive(false);

        if (wildStateLabel)
            wildStateLabel.text = string.Empty;
    }

    public void SetWildGuarding(bool isGuarding)
    {
        _wildIsGuarding = isGuarding;

        if (wildGuardIcon)
            wildGuardIcon.SetActive(isGuarding);

        RefreshWildStateLabel();
    }

    public void SetWildShield(int shieldAmount)
    {
        _wildShieldAmount = Mathf.Max(0, shieldAmount);

        if (wildShieldIcon)
            wildShieldIcon.SetActive(_wildShieldAmount > 0);

        RefreshWildStateLabel();
    }

    public void SetWildChargeTurns(int turnsRemaining)
    {
        _wildChargeTurns = Mathf.Max(0, turnsRemaining);

        if (wildChargeIcon)
            wildChargeIcon.SetActive(_wildChargeTurns > 0);

        RefreshWildStateLabel();
    }

    void RefreshWildStateLabel()
    {
        if (!wildStateLabel)
            return;

        var parts = new List<string>();

        if (_wildIsGuarding)
            parts.Add("Guard");

        if (_wildShieldAmount > 0)
            parts.Add($"Shield {_wildShieldAmount}");

        if (_wildChargeTurns > 0)
            parts.Add($"Charging ({_wildChargeTurns})");

        wildStateLabel.text = (parts.Count == 0) ? string.Empty : string.Join(" • ", parts);
    }

    public void OnWildSpawned(MonsterDataSO def)
    {
        ClearWildStateUI();

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
}
