using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public struct BattleResult
{
    public bool victory;
    public bool escaped;

    public int creditsGained;
    public MonsterDataSO wildDef;
    public int wildLevel;
    public float secondsSurvived;

    public int critCount;
    public int turnsSurvived;
    public int damageTaken;

    public int damageDealt;
    public bool gotFirstHit;
}


public partial class BattleManager : MonoBehaviour
{
    private enum PlayerAction { None, Attack, Defend, Focus, Swap, Run }
    private enum EnemyAction { Attack, Defend, Focus, Run }


    // ─────────────────────────────────────────────────────────
    // Battle Events (logic emits, UI/FX consumes)
    // This keeps BattleManager focused on rules/state while allowing
    // visuals/narration polish without risking combat logic regressions.
    // ─────────────────────────────────────────────────────────


// ─────────────────────────────────────────────────────────
// Battle Events (logic emits, UI/FX consumes)
// ─────────────────────────────────────────────────────────

private readonly BattleEventBus _eventBus = new BattleEventBus();

public event Action<BattleEvent> OnBattleEvent
{
    add { _eventBus.OnEvent += value; }
    remove { _eventBus.OnEvent -= value; }
}

public void RegisterBattleEventConsumer() => _eventBus.RegisterConsumer();
public void UnregisterBattleEventConsumer() => _eventBus.UnregisterConsumer();
private bool HasBattleEventConsumers => _eventBus.HasConsumers;

private void Emit(BattleEvent e) => _eventBus.Emit(e);


    // ─────────────────────────────────────────────────────────
    // Deterministic battle RNG (debuggable + daily seeds ready)
    // One RNG per battle, seeded.
    // ─────────────────────────────────────────────────────────


// ─────────────────────────────────────────────────────────
// Deterministic battle RNG (debuggable + daily seeds ready)
// One RNG per battle, seeded.
// ─────────────────────────────────────────────────────────

private static int _battleSerial;
private readonly BattleRngService _rng = new BattleRngService();

public int BattleSeed => _rng.BattleSeed;
public string BattleSeedLabel => _rng.BattleSeedLabel;

/// <summary>
/// Optional: EncounterManager can set the battle seed before calling Begin(...).
/// If not set, a deterministic seed will be derived from the active session/daily/custom seed.
/// </summary>
public void SetBattleSeed(int seed, string seedLabel = null) => _rng.SetBattleSeed(seed, seedLabel);

private float Rng01() => _rng.Rng01();

private void EnsureBattleRngInitialized()
{
    _rng.EnsureInitialized(ref _battleSerial, wildDef, wildLevel);
}


    
    [Header("Wild Intent Telegraph")]
    [SerializeField] private bool showWildIntentIcons = true;

    [Tooltip("In manual battles, we always show telegraph text. In auto battles, we only show text for the first N turns (icons always).")]
    [SerializeField, Min(0)] private int autoTelegraphTextFirstTurns = 3;

    [Tooltip("Unscaled seconds to keep the wild intent icon visible (manual).")]
    [SerializeField, Min(0.05f)] private float wildIntentIconDurationManual = 0.60f;

    [Tooltip("Unscaled seconds to keep the wild intent icon visible (auto).")]
    [SerializeField, Min(0.05f)] private float wildIntentIconDurationAuto = 0.30f;

    [Tooltip("Small unscaled pause after showing the intent icon (for readability, even when no text is shown).")]
    [SerializeField, Min(0f)] private float wildIntentTelegraphPause = 0.10f;

    [Header("Wild Intent Text")]
    [SerializeField] private string telegraphAttack = "Wild presses the attack!";
    [SerializeField] private string telegraphDefend = "Wild braces!";
    [SerializeField] private string telegraphFocus  = "Wild looks for an opening...";
    [SerializeField] private string telegraphRun    = "Wild tries to get away!";
[Header("Manual Turn Settings")]
    [SerializeField] private bool manualTurns = true;
    [SerializeField, Range(0f, 1f)] private float defendReducePct = 0.50f;
    [SerializeField, Range(0f, 1f)] private float guardConvertPct = 1.0f;
    [SerializeField, Range(0f, 2f)] private float chargeBonusPct = 0.5f;

    [Header("Manual Turn Failsafe (Optional)")]
    [Tooltip("If enabled, auto-queues an Attack if the player doesn't pick an action within the timeout.")]
    [SerializeField] private bool enableAutoQueueAttack = true;
    [SerializeField, Min(1f)] private float autoQueueAttackAfterSeconds = 20f;

    // Runtime overrides (so auto-battle can resolve quickly without altering prefab defaults)
    private bool _manualTurnsDefault;
    private bool _enableAutoQueueDefault;
    private float _autoQueueSecondsDefault;
    private bool _autoResolveActive;
    private bool _defaultsCaptured;

    void Awake()
    {
        CaptureDefaults();
    }

    private void CaptureDefaults()
    {
        if (_defaultsCaptured) return;
        _defaultsCaptured = true;

        _manualTurnsDefault = manualTurns;
        _enableAutoQueueDefault = enableAutoQueueAttack;
        _autoQueueSecondsDefault = autoQueueAttackAfterSeconds;
    }

    /// <summary>
    /// Called by EncounterManager at battle start.
    /// When true, the battle resolves in automatic mode (no manual input waits),
    /// and text/pace can be accelerated in UI scripts that query this flag.
    /// </summary>
    public bool AutoResolveActive => _autoResolveActive;

    public void ConfigureForAuto(bool isAuto)
    {
        CaptureDefaults();
        _autoResolveActive = isAuto;

        if (isAuto)
        {
            // Force automatic turn resolution.
            manualTurns = false;
            enableAutoQueueAttack = false;
            // Ensure timeout can't drag turns out if any manual-turn checks remain.
            autoQueueAttackAfterSeconds = 0.25f;
        }
        else
        {
            // Restore prefab defaults.
            manualTurns = _manualTurnsDefault;
            enableAutoQueueAttack = _enableAutoQueueDefault;
            autoQueueAttackAfterSeconds = _autoQueueSecondsDefault;
        }
    }

    [Header("Run Settings")]
    [SerializeField, Range(0f, 1f)] private float runBaseChance = 0.25f;
    [SerializeField, Range(0f, 1f)] private float runMinChance = 0.05f;
    [SerializeField, Range(0f, 1f)] private float runMaxChance = 0.95f;
    [SerializeField, Range(0f, 1f)] private float runSpeedWeight = 0.50f;
    [SerializeField, Range(0f, 1f)] private float runAttemptBonus = 0.10f;
    [SerializeField, Range(0f, 1f)] private float runHpWeight = 0.25f;

    [Header("Defend Reliability")]
    [SerializeField, Range(0f, 1f)] private float defendFirstUseSuccess = 1.0f;
    [SerializeField, Range(0f, 1f)] private float defendRepeatMultiplier = 0.5f;
    [SerializeField, Range(0f, 1f)] private float defendMinSuccess = 0.1f;

    private bool _isPlayerTurn;
    public bool IsPlayerTurn => _isPlayerTurn;
    public event Action<bool> OnPlayerTurnChanged;

    private bool isResolvingPlayerTurn = false;
    private PlayerAction pendingAction = PlayerAction.None;
    private int pendingSwapBenchSlot = -1;
    private bool defendActiveThisRound = false;

    [Header("Wild UI")]
    [SerializeField] private GameObject wildPanel;
    [SerializeField] private Slider wildHPBar;
    [SerializeField] private Image wildIcon;
    [SerializeField] private TextMeshProUGUI wildNameText;
    [SerializeField] private TextMeshProUGUI wildLevelText;
    [SerializeField] private TextMeshProUGUI wildIdText;
    [SerializeField] private TextMeshProUGUI wildTypeText;
    [SerializeField] private TextMeshProUGUI wildRarityText;
    [SerializeField] private TextMeshProUGUI wildHPText;
    [SerializeField] private TextMeshProUGUI wildATKText;
    [SerializeField] private TextMeshProUGUI wildDEFText;
    [SerializeField] private TextMeshProUGUI wildSPDText;

    [Header("Player UI")]
    [SerializeField] private GameObject playerPanel;
    [SerializeField] private Slider playerHPBar;
    [SerializeField] private Image playerIcon;
    [SerializeField] private TextMeshProUGUI playerNameText;
    [SerializeField] private TextMeshProUGUI playerLevelText;
    [SerializeField] private TextMeshProUGUI playerIdText;
    [SerializeField] private TextMeshProUGUI playerTypeText;
    [SerializeField] private TextMeshProUGUI playerRarityText;
    [SerializeField] private TextMeshProUGUI playerHPText;
    [SerializeField] private TextMeshProUGUI playerATKText;
    [SerializeField] private TextMeshProUGUI playerDEFText;
    [SerializeField] private TextMeshProUGUI playerSPDText;

    [Header("Bench UI")]
    [SerializeField] private Button benchBtn1;
    [SerializeField] private Button benchBtn2;
    [SerializeField] private Image benchImg1;
    [SerializeField] private Image benchImg2;
    [SerializeField] private TextMeshProUGUI benchHPText1;
    [SerializeField] private TextMeshProUGUI benchHPText2;

    [Header("Turn Pacing (unscaled)")]
    [SerializeField, Min(0.05f)] private float beginRoundDelay = 0.15f;
    [SerializeField, Min(0.05f)] private float hitPause = 0.25f;
    [SerializeField, Min(0.05f)] private float endRoundDelay = 0.60f;

    [Header("Combat Tunables")]
    [Range(0f, 1f)][SerializeField] private float critChancePlayer = 0.10f;
    [Range(0f, 1f)][SerializeField] private float critChanceWild = 0.08f;
    [SerializeField] private float critMultiplier = 1.8f;
    [SerializeField] private bool showEffectivenessText = true;

    [Header("Speed Control")]
    [SerializeField, Min(0.25f)] private float battleSpeed = 1f;
    public float BattleSpeed => battleSpeed;


    [Header("Battle Text Box")]
    [SerializeField] private BattleTextBoxUI battleTextBox;
    [SerializeField] private BattleSwitchToggle _bottomToggle;

    [Header("Encounter Tuning")]
    [SerializeField, Range(0.5f, 2.0f)] private float encounterThreatScalar = 1.0f;

    [Header("Feedback")]
    [SerializeField] private BattleFeedbackManager feedback;

    public bool NarrationLocked => _narrationLock;
    private bool _narrationLock;

    public MonsterDataSO WildDef => wildDef;
    public int WildLevel => wildLevel;

    private MonsterDataSO wildDef;
    private int wildLevel;
    private float wildMaxHP, wildHP;
    private float wildAttackPerTurn;

    private int teamCount, activeIndex;
    private MonsterDataSO[] teamDefs;
    private int[] teamLevels;
    private float[] teamMaxHP, teamHP;
    private string[] teamIds;

    private JobBattlePassives.Ctx[] jobCtx;

    private float[] shieldHP;
    private float[] pendingGuardShield;
    private bool[] chargedNextAttack;

    private float[] teamPendingBuffPct;
    private int[] teamPendingBuffTurns;

    private float[] slotDamageBuffPct;
    private int[] slotDamageBuffTurns;

    [Header("Debug - Titles")]
    [SerializeField] private bool debugTitles = false;
    [SerializeField] private bool debugTitlesEveryTurn = true;
    [SerializeField] private bool debugTitlesOnSwap = true;

    private int _turnIndex = 0;
    private bool inBattle;
    public bool InBattle => inBattle;

    // Read-only UI helpers (used by BattleFeedbackManager when consuming battle events)
    public float GetActivePlayerCurHP()
    {
        if (teamHP == null || activeIndex < 0 || activeIndex >= teamHP.Length) return 0f;
        return teamHP[activeIndex];
    }

    public float GetActivePlayerMaxHP()
    {
        return GetFinalMaxHPForIndex(activeIndex);
    }

    public float GetWildCurHP()
    {
        return wildHP;
    }

    public float GetWildMaxHP()
    {
        return wildMaxHP;
    }

    private Action<BattleResult> onEnd;
    private float startTime;
    private Coroutine turnCR;



    // ─────────────────────────────────────────────────────────────
    // GC / Allocation Scratch (mobile smoothness)
    // ─────────────────────────────────────────────────────────────
    private readonly List<int> _scratchOthers = new List<int>(4);
    private readonly BattleLogBuffer _logBuffer = new BattleLogBuffer();

    private void FillOtherIndices(List<int> dst)
    {
        if (dst == null) return;
        dst.Clear();
        for (int i = 0; i < teamCount; i++)
            if (i != activeIndex) dst.Add(i);
    }

    private bool playerTookFirstIncomingThisBattle = false;
    private bool playerLandedFirstHitThisBattle = false;

    private int playerNoDmgTurns = 0;
    private int playerNoCritTurns = 0;

    private int defendConsecutiveUses = 0;
    private float currentDefendSuccess = 1f;
    private int wildDefendConsecutiveUses = 0;
    private float wildDefendCurrentSuccess = 1f;

    private bool wildDefendActiveThisRound = false;
    private float wildShieldHP = 0f;
    private float wildPendingGuardShield = 0f;

    private int runAttempts = 0;
    private bool wildChargedNextAttack = false;

    private int _totalCritsThisBattle = 0;
    private int _totalDamageTakenThisBattle = 0;
    private int _totalDamageDealtThisBattle = 0;

    private static readonly Color StatNeutral = Color.white;
    private static readonly Color StatBuff = new Color(0.35f, 1f, 0.35f);
    private static readonly Color StatNerf = new Color(1f, 0.35f, 0.35f);

    void Start()
    {
        if (benchBtn1) benchBtn1.onClick.AddListener(() => ClickBench(0));
        if (benchBtn2) benchBtn2.onClick.AddListener(() => ClickBench(1));

        if (SaveManager.Data != null && SaveManager.Data.settings != null)
            battleSpeed = Mathf.Clamp(SaveManager.Data.settings.battleSpeed, 0.25f, 5f);

        if (!feedback) feedback = GetComponentInParent<BattleFeedbackManager>() ?? FindFirstObjectByType<BattleFeedbackManager>();
    }

    void OnEnable()
    {
        GameEvents.BattleFinished += HandleBattleFinishedUIRefresh;
        GameEvents.BattleStatsChanged += HandleBattleStatsChanged;
    }

    void OnDisable()
    {
        GameEvents.BattleFinished -= HandleBattleFinishedUIRefresh;
        GameEvents.BattleStatsChanged -= HandleBattleStatsChanged;

        // If the battle is being aborted (scene unload, disable, etc.), dump the recent
        // combat snapshot for debugging.
        if (inBattle)
            BattleLogger.DumpSnapshotToConsole("BattleManager disabled");
    }

    void OnDestroy()
    {
        if (benchBtn1) benchBtn1.onClick.RemoveAllListeners();
        if (benchBtn2) benchBtn2.onClick.RemoveAllListeners();
    }

    private void SetIsPlayerTurn(bool value)
    {
        if (_isPlayerTurn == value) return;
        _isPlayerTurn = value;
        OnPlayerTurnChanged?.Invoke(_isPlayerTurn);


        // Booster system: ensure turn gating is kept in sync (enables/disables booster buttons correctly).
        if (BattleBoosterController.I != null)
            BattleBoosterController.I.OnTurnStart(_isPlayerTurn);

        GameEvents.OnBattleStateChanged?.Invoke();
    }

    public void SetPlayerActionAttack() { TryQueueAction(PlayerAction.Attack); }
    public void SetPlayerActionDefend() { TryQueueAction(PlayerAction.Defend); }
    public void SetPlayerActionFocus() { TryQueueAction(PlayerAction.Focus); }
    public void SetPlayerActionRun() { TryQueueAction(PlayerAction.Run); }

    private void TryQueueAction(PlayerAction a)
    {
        if (!inBattle || !manualTurns) return;
        if (!IsPlayerTurn) return;
        if (isResolvingPlayerTurn) return;
        if (_narrationLock) return;
        if (pendingAction != PlayerAction.None) return;

        pendingAction = a;
        Emit(BattleEvent.ActionQueued(BattleSide.Player, a.ToString()));
        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, ToFeedbackAction(a));
        GameEvents.OnBattleStateChanged?.Invoke();
    }
    // ─────────────────────────────────────────────────────────────
    // Queued Action (Manual Turns) – used by UI to feel instant
    // ─────────────────────────────────────────────────────────────
    public bool HasQueuedPlayerAction => pendingAction != PlayerAction.None;

    /// <summary>
    /// Returns a stable code so UI doesn't need access to the private enum.
    /// 0=None, 1=Attack, 2=Defend, 3=Focus, 4=Swap, 5=Run
    /// </summary>
    public int QueuedPlayerActionCode
    {
        get
        {
            switch (pendingAction)
            {
                case PlayerAction.Attack: return 1;
                case PlayerAction.Defend: return 2;
                case PlayerAction.Focus:  return 3;
                case PlayerAction.Swap:   return 4;
                case PlayerAction.Run:    return 5;
                default: return 0;
            }
        }
    }




    private BattleFeedbackManager.BattleFeedbackAction ToFeedbackAction(PlayerAction a)
    {
        switch (a)
        {
            case PlayerAction.Attack: return BattleFeedbackManager.BattleFeedbackAction.Attack;
            case PlayerAction.Defend: return BattleFeedbackManager.BattleFeedbackAction.Defend;
            case PlayerAction.Focus:  return BattleFeedbackManager.BattleFeedbackAction.Focus;
            case PlayerAction.Run:    return BattleFeedbackManager.BattleFeedbackAction.Run;
            case PlayerAction.Swap:   return BattleFeedbackManager.BattleFeedbackAction.Swap;
            default: return BattleFeedbackManager.BattleFeedbackAction.Attack;
        }
    }

private static void HardResetIconVisual(Image img)
{
    if (!img) return;

    // Cancel any lingering feedback tweens from the previous encounter/battle.
    LeanTween.cancel(img.gameObject);

    var c = img.color;
    c.a = 1f;
    img.color = c;

    img.canvasRenderer.SetAlpha(1f);

    var cg = img.GetComponent<CanvasGroup>();
    if (cg) cg.alpha = 1f;
}

private IEnumerator SayKO(string displayName)
{
    if (string.IsNullOrWhiteSpace(displayName)) yield break;
    BattleLogger.AddKeyMoment($"KO: {displayName}");
    yield return Say($"{displayName} KO'ed!", BattleLineTag.Result);
}

private IEnumerator MaybeSayKO_Player(string victimName, float preHP, float postHP)
{
    if (preHP > 0.01f && postHP <= 0.01f)
        yield return SayKO(victimName);
}

private IEnumerator MaybeSayKO_Wild(string victimName, float preHP, float postHP)
{
    if (preHP > 0.01f && postHP <= 0.01f)
        yield return SayKO(victimName);
}

    public void BeginBattle(MonsterDataSO wild, int level, Action<BattleResult> onEnded)
    {
        Begin(wild, level, onEnded);
    }

    public void Begin(MonsterDataSO wild, int level, Action<BattleResult> onEnded)
    {
        var roster = SaveManager.Data.team;
        if (roster == null || roster.Count == 0) { ForceEndBattleEarly(false); return; }

        // Reset per-battle deterministic RNG state.
        _rng.ResetForBegin();
        playerNoDmgTurns = 0;
        playerNoCritTurns = 0;
        runAttempts = 0;

        _totalCritsThisBattle = 0;
        _totalDamageTakenThisBattle = 0;
        _totalDamageDealtThisBattle = 0;

        defendConsecutiveUses = 0;
        currentDefendSuccess = defendFirstUseSuccess;

        wildDefendConsecutiveUses = 0;
        wildDefendCurrentSuccess = defendFirstUseSuccess;
        wildDefendActiveThisRound = false;
        wildShieldHP = 0f;
        wildPendingGuardShield = 0f;
        wildChargedNextAttack = false;

        inBattle = false;
        onEnd = onEnded;

        wildDef = wild;
        wildLevel = Mathf.Max(1, level);

        float wHpBase = BattleCalc.CalcHP(wildDef, wildLevel);
        float wAtkBase = BattleCalc.CalcBaseAttack(wildDef, wildLevel, 0, 0);

        wildMaxHP = Mathf.Max(1f, wHpBase * encounterThreatScalar);
        wildHP = wildMaxHP;

        wildAttackPerTurn = Mathf.Max(1f, wAtkBase * encounterThreatScalar);

        // IMPORTANT: clear any lingering icon tweens/alphas from the prior battle.
        if (feedback != null) feedback.ResetIconVisuals();
        HardResetIconVisual(playerIcon);
        HardResetIconVisual(wildIcon);

        // ─────────────────────────────────────────────────────────────
        // Shiny encounter state (spawn-time), driven by EncounterManager
        // ─────────────────────────────────────────────────────────────
        bool shinyWild = (EncounterManager.I != null) && EncounterManager.I.CurrentWildIsShiny;

        bool isAuto = (EncounterManager.I != null) && EncounterManager.I.IsAutoMode;

        // If you want BattleManager rules to also snap to auto:
        ConfigureForAuto(isAuto);


        // Wild icon: use shiny icon if shiny encounter and one exists.
        if (wildIcon)
        {
            if (shinyWild && wildDef && wildDef.shinyIcon) wildIcon.sprite = wildDef.shinyIcon;
            else wildIcon.sprite = wildDef ? wildDef.icon : null;
            HardResetIconVisual(wildIcon);
        }


        // Wild name: MUST apply formatter so we literally see * and italics.
        // Ensure MonsterNameFormatter.Format returns "*<i>Name</i>*" when isShiny=true.
        if (wildNameText)
        {
            if (wildDef)
                wildNameText.text = MonsterNameFormatter.Format(wildDef, shinyWild);
            else
                wildNameText.text = "Wild";
        }

        // If this encounter spawned as shiny, play the shiny name sparkle feedback.
        if (shinyWild && feedback != null)
        {
            feedback.PlayShinyNameSparkle(wildNameText);
        }

        if (wildLevelText) wildLevelText.text = $"Lv {wildLevel}";
        if (wildHPBar) { wildHPBar.maxValue = wildMaxHP; wildHPBar.value = wildHP; }

        UpdateWildInfoUI();

        teamCount = Mathf.Min(3, roster.Count);
        if (teamCount <= 0) { inBattle = false; return; }

        teamDefs = new MonsterDataSO[teamCount];
        teamLevels = new int[teamCount];
        teamMaxHP = new float[teamCount];
        teamHP = new float[teamCount];
        teamIds = new string[teamCount];

        for (int i = 0; i < teamCount; i++)
        {
            var owned = roster[i];
            teamIds[i] = owned != null ? owned.monsterId : null;

            var def = (owned != null && !string.IsNullOrEmpty(owned.monsterId))
                ? MonsterLibraryLocator.GetById(owned.monsterId)
                : null;

            if (!def)
            {
                teamDefs[i] = null;
                teamLevels[i] = 1;
                teamMaxHP[i] = 1f;
                teamHP[i] = 0f;
                BattleLogger.Log($"[Battle] WARNING: team slot {i} has missing MonsterData for id '{teamIds[i]}'. Marking as KO/unusable.", LogScope.Battle);
                continue;
            }

            teamDefs[i] = def;
            teamLevels[i] = owned.level;

            GetProgressionTotalsForIndex(i, out int totalHP, out _, out _, out _, out _);
            float finalMax = Mathf.Max(1f, totalHP);
            teamMaxHP[i] = finalMax;

            int savedHP = owned.currentHP;
            teamHP[i] = (savedHP >= 0)
                ? Mathf.Clamp(savedHP, 0, Mathf.RoundToInt(finalMax))
                : finalMax;
        }

        jobCtx = new JobBattlePassives.Ctx[teamCount];
        shieldHP = new float[teamCount];
        teamPendingBuffPct = new float[teamCount];
        teamPendingBuffTurns = new int[teamCount];

        slotDamageBuffPct = new float[teamCount];
        slotDamageBuffTurns = new int[teamCount];

        pendingGuardShield = new float[teamCount];
        chargedNextAttack = new bool[teamCount];

        for (int i = 0; i < teamCount; i++)
        {
            var owned = SaveManager.Data.team[i];
            var (job, hours) = JobManager.I ? JobManager.I.GetCurrentJobAndHours(owned.monsterId) : (JobType.None, 0f);
            jobCtx[i] = JobBattlePassives.Build(job, hours);

            if (jobCtx[i].maxHpBonusPct > 0f)
            {
                float pct = (teamMaxHP[i] > 0.01f) ? (teamHP[i] / teamMaxHP[i]) : 1f;
                teamMaxHP[i] *= 1f + jobCtx[i].maxHpBonusPct;
                teamHP[i] = Mathf.Clamp(teamMaxHP[i] * pct, 0f, teamMaxHP[i]);
            }

            if (jobCtx[i].startShieldPctMaxHp > 0f)
            {
                float curMaxWithTitlesAndConditionals = GetFinalMaxHPForIndex(i);
                shieldHP[i] = curMaxWithTitlesAndConditionals * jobCtx[i].startShieldPctMaxHp;
            }
        }

        playerTookFirstIncomingThisBattle = false;
        playerLandedFirstHitThisBattle = false;

        defendActiveThisRound = false;
        wildDefendActiveThisRound = false;
        pendingAction = PlayerAction.None;
        SetIsPlayerTurn(false);

        activeIndex = -1;
        for (int i = 0; i < teamCount; i++)
            if (teamHP[i] > 0f) { activeIndex = i; break; }

        if (activeIndex < 0) { EndBattle(false); return; }

        ApplyActiveToUI();
        ClampAndPushActiveHP();
        RefreshBenchUI();

        // Booster system: provide runtime hooks so UI boosters can perform actions (e.g., Health booster healing).
        if (BattleBoosterController.I != null)
        {
            BattleBoosterController.I.SetHooks(new BattleRuntimeHooks
            {
                // Heal the currently active team slot. Return actual healed amount.
                HealPlayer = (amount) =>
                {
                    if (teamHP == null || teamHP.Length == 0 || activeIndex < 0 || activeIndex >= teamHP.Length)
                        return 0;

                    float before = teamHP[activeIndex];
                    TryAddHPToActive(amount);
                    float after = teamHP[activeIndex];
                    return Mathf.RoundToInt(Mathf.Max(0f, after - before));
                }
            });
        }


        if (wildPanel) wildPanel.SetActive(true);
        if (playerPanel) playerPanel.SetActive(true);

        CanvasGroup wildCG = null;
        CanvasGroup playerCG = null;

        if (wildPanel)
        {
            wildCG = wildPanel.GetComponent<CanvasGroup>();
            if (!wildCG) wildCG = wildPanel.AddComponent<CanvasGroup>();
            wildCG.alpha = 0f; wildCG.blocksRaycasts = false; wildCG.interactable = false;
        }
        if (playerPanel)
        {
            playerCG = playerPanel.GetComponent<CanvasGroup>();
            if (!playerCG) playerCG = playerPanel.AddComponent<CanvasGroup>();
            playerCG.alpha = 0f; playerCG.blocksRaycasts = false; playerCG.interactable = false;
        }

        if (turnCR != null) StopCoroutine(turnCR);
        turnCR = StartCoroutine(Co_RevealPanelsThenStart(wildCG, playerCG, 0.28f));
        ResetStatusIcons();
    }



    private void EndBattle(bool victory, bool escaped = false)
    {
        if (!inBattle) return;

        inBattle = false;
        SetIsPlayerTurn(false);
        GameEvents.OnBattleStateChanged?.Invoke();

        ConfigureForAuto(false);

        if (benchBtn1) benchBtn1.interactable = false;
        if (benchBtn2) benchBtn2.interactable = false;

        pendingAction = PlayerAction.None;
        defendActiveThisRound = false;
        wildDefendActiveThisRound = false;
        wildChargedNextAttack = false;
        ResetStatusIcons();

        if (turnCR != null) { StopCoroutine(turnCR); turnCR = null; }

        // Restore BattleCalc RNG to default (UnityEngine.Random)
        BattleCalc.ResetRng();
        _rng.ClearAll();
        float survived = Mathf.Max(0f, Time.unscaledTime - startTime);

        int basecredits = 0;
        int finalcredits = 0;
        int creditTitleBonus = 0;

        if (!escaped)
        {
            basecredits = BattleRewards.creditsFor(victory, wildLevel, survived);
            finalcredits = basecredits;

            if (victory && teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            {
                float cm = TitlesAdapter.GetcreditMultOnVictory(teamIds[activeIndex], wildDef, wildLevel);
                if (cm > 0f)
                {
                    finalcredits = Mathf.Max(0, Mathf.RoundToInt(basecredits * cm));
                    creditTitleBonus = Mathf.Max(0, finalcredits - basecredits);
                }
            }

            if (finalcredits < 0) finalcredits = 0;
        }

        int baseCores = Mathf.Max(1, 2 + wildLevel);
        int growthCoreTitleBonus = 0;
        int growthCoreTotal = 0;

        var data = SaveManager.Data;

        if (victory && !escaped)
        {
            var m = (data != null && data.team != null && activeIndex >= 0 && activeIndex < data.team.Count)
                ? data.team[activeIndex]
                : default;

            float shinyMul = ShinySystems.TrainingXpMult(m);
            int baseAfterShiny = Mathf.RoundToInt(baseCores * shinyMul);

            float titleCoreMul = 1f;
            if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
                titleCoreMul = Mathf.Max(0f, TitlesAdapter.GetGrowthCoreMultOnVictory(teamIds[activeIndex], wildDef, wildLevel));

            growthCoreTotal = Mathf.RoundToInt(baseAfterShiny * titleCoreMul);
            growthCoreTitleBonus = Mathf.Max(0, growthCoreTotal - baseAfterShiny);

            // Global tuning knob (progression lever). Safe no-op if GameBalance asset is missing.
            if (GameBalance.TryGet(out var bal))
                growthCoreTotal = Mathf.RoundToInt(growthCoreTotal * Mathf.Max(0f, bal.xpGainMultiplier));

            if (growthCoreTotal > 0)
                ResourceManager.I?.Add(ResourceType.GrowthCore, growthCoreTotal);

            BattleLogger.Log($"Gained {growthCoreTotal} Growth Cores.", LogScope.Battle);
        }

        var teamList = data != null && data.team != null ? data.team : new List<OwnedMonsterData>();
        var ownedList = data != null && data.owned != null ? data.owned : new List<OwnedMonsterData>();
        long nowUnix = SaveManager.NowUnix();

        for (int i = 0; i < teamCount && i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
            int hp = Mathf.CeilToInt(Mathf.Max(0f, teamHP[i]));
            t.currentHP = hp;
            teamList[i] = t;
        }

        // Sync HP back to owned list.
        // Prefer ownedUID matching so shiny/normal variants (same monsterId) don't cross-contaminate.
        for (int i = 0; i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            int idx = -1;

            // 1) Strong match: ownedUID
            if (!string.IsNullOrEmpty(t.ownedUID))
            {
                for (int j = 0; j < ownedList.Count; j++)
                {
                    var o = ownedList[j];
                    if (o != null && !string.IsNullOrEmpty(o.ownedUID) && o.ownedUID == t.ownedUID)
                    {
                        idx = j;
                        break;
                    }
                }
            }

            // 2) Fallback: monsterId only if unique in owned list
            if (idx < 0)
            {
                int count = 0;
                int singleIdx = -1;
                for (int j = 0; j < ownedList.Count; j++)
                {
                    var o = ownedList[j];
                    if (o != null && !string.IsNullOrEmpty(o.monsterId) && o.monsterId == t.monsterId)
                    {
                        count++;
                        singleIdx = j;
                        if (count > 1) break;
                    }
                }

                if (count == 1) idx = singleIdx;
            }

            if (idx >= 0 && idx < ownedList.Count)
            {
                var o = ownedList[idx];
                if (o != null)
                {
                    o.currentHP = Mathf.Max(0, t.currentHP);
                    o.lastHPUnix = nowUnix;
                    ownedList[idx] = o;
                }
            }
        }

        for (int i = 0; i < teamList.Count; i++)
        {
            var e = teamList[i];
            if (e == null || string.IsNullOrEmpty(e.monsterId)) continue;
            e.lastHPUnix = nowUnix;
            teamList[i] = e;
        }

        if (data != null)
        {
            data.owned = ownedList;
            data.team = teamList;
            SaveManager.Save();
        }

        GameEvents.OnTeamChanged?.Invoke();

        BattleTempBuffs.I?.ClearPlayerAtkBonus();
        BattleTempBuffs.I?.ClearPlayerSpeedBonus();
        BattleTempBuffs.I?.ClearPlayerHPBonus();
        BattleTempBuffs.I?.ClearPlayerDefenseBonus();

        string outcomeLabel = escaped ? "Escaped" : (victory ? "Victory" : "Defeat");
        BattleLogger.Log($"Battle ends: {outcomeLabel} (+{finalcredits} credits).", LogScope.Battle);
        BattleLogger.EndBattle(victory);

        var result = new BattleResult
        {
            victory = victory,
            escaped = escaped,
            creditsGained = finalcredits,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = survived,
            critCount = _totalCritsThisBattle,
            turnsSurvived = _turnIndex,
            damageTaken = _totalDamageTakenThisBattle,
            damageDealt = _totalDamageDealtThisBattle,
            gotFirstHit = playerLandedFirstHitThisBattle
        };

        if (!victory && !escaped && AutoResolveActive)
        {
            EncounterManager.I?.NotifyAuto_TeamKO();
        }

        SetPostBattleWinnerVisible(victory, escaped);

        if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            TitlesAdapter.OnBattleEnd(teamIds[activeIndex], victory, wildDef, wildLevel);

        onEnd?.Invoke(result);
        GameEvents.BattleFinished?.Invoke(result);
    }



private void ResolveQueuedSwap()
{
    if (pendingSwapBenchSlot < 0) return;

    FillOtherIndices(_scratchOthers);
    List<int> others = _scratchOthers;

    int benchSlot = pendingSwapBenchSlot;
    pendingSwapBenchSlot = -1;

    if (benchSlot < 0 || benchSlot >= others.Count) return;

    int targetIndex = others[benchSlot];
    if (teamHP[targetIndex] <= 0f) return;

    activeIndex = targetIndex;

    ApplyActiveToUI();
    ClampAndPushActiveHP();
    RefreshBenchUI();

    if (teamPendingBuffPct != null && teamPendingBuffTurns != null &&
        slotDamageBuffPct != null && slotDamageBuffTurns != null &&
        activeIndex >= 0 && activeIndex < teamPendingBuffPct.Length)
    {
        if (teamPendingBuffPct[activeIndex] > 0f)
        {
            slotDamageBuffPct[activeIndex] += teamPendingBuffPct[activeIndex];
            slotDamageBuffTurns[activeIndex] =
                Mathf.Max(slotDamageBuffTurns[activeIndex], teamPendingBuffTurns[activeIndex]);

            BattleLogger.Log($"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.", LogScope.Battle);

            teamPendingBuffPct[activeIndex] = 0f;
            teamPendingBuffTurns[activeIndex] = 0;
        }
    }

    BattleLogger.Log($"Swapped to {GetName(activeIndex)}! (turn consumed)", LogScope.Battle);
    BattleLogger.AddKeyMoment($"SWAP: {GetName(activeIndex)}");                                        Emit(BattleEvent.ActionQueued(BattleSide.Player, "Swap"));
                                        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Player,
                                            BattleFeedbackManager.BattleFeedbackAction.Swap
                                        );
    if (debugTitles && debugTitlesOnSwap)
        Debug_LogActiveTitlesSnapshot("Swap");
}

private bool AutoSwapToAlive()
    {
        for (int i = 0; i < teamCount; i++)
        {
            if (i == activeIndex) continue;
            if (teamHP[i] <= 0f) continue;

            activeIndex = i;

            ApplyActiveToUI();
            ClampAndPushActiveHP();
            RefreshBenchUI();

            BattleLogger.AddKeyMoment($"SWAP: {GetName(activeIndex)}");

            if (teamPendingBuffPct != null && teamPendingBuffTurns != null &&
                slotDamageBuffPct != null && slotDamageBuffTurns != null &&
                activeIndex >= 0 && activeIndex < teamPendingBuffPct.Length)
            {
                if (teamPendingBuffPct[activeIndex] > 0f)
                {
                    slotDamageBuffPct[activeIndex] += teamPendingBuffPct[activeIndex];
                    slotDamageBuffTurns[activeIndex] =
                        Mathf.Max(slotDamageBuffTurns[activeIndex], teamPendingBuffTurns[activeIndex]);

                    BattleLogger.Log($"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.", LogScope.Battle);

                    teamPendingBuffPct[activeIndex] = 0f;
                    teamPendingBuffTurns[activeIndex] = 0;
                }
            }

            BattleLogger.Log($"Auto-swapped to {GetName(activeIndex)}!", LogScope.Battle);
            return true;
        }

        if (debugTitles && debugTitlesOnSwap)
            Debug_LogActiveTitlesSnapshot("Swap");

        return false;
    }

    private bool IsWildKO() => wildHP <= 0.01f;

    private bool IsTeamKO()
    {
        for (int i = 0; i < teamCount; i++) if (teamHP[i] > 0.01f) return false;
        return true;
    }

    private string GetName(int idx)
        => (teamDefs != null && idx >= 0 && idx < teamDefs.Length && teamDefs[idx]) ? teamDefs[idx].displayName : "Ally";

    public void TryAddHPToActive(float amount)
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);
        teamHP[activeIndex] = Mathf.Clamp(teamHP[activeIndex] + amount, 0f, curMax);
        ClampAndPushActiveHP();
    }






    // ─────────────────────────────────────────────────────────────
    // Allocation-free waits
    //
    // WaitForSecondsRealtime allocates per call. In long battles (and especially auto-battles),
    // those tiny allocations can accumulate into GC spikes.
    //
    // Use these helpers in hot coroutines (TurnLoop / PlayerTurn / EnemyTurn / telegraphs).
    // They yield `null` until the target time is reached, so no GC.
    // ─────────────────────────────────────────────────────────────



    // Keep the old API for compatibility (non-hot paths), but prefer CoWaitScaled in loops.



    private TitleStatMods GetTitleModsForIndex(int idx)
    {
        if (teamIds != null && idx >= 0 && idx < teamIds.Length && !string.IsNullOrEmpty(teamIds[idx]))
            return TitlesAdapter.GetBattleStatMods(teamIds[idx]);
        return default;
    }

    private TitleStatMods GetConditionalModsForIndex(int idx)
    {
        if (teamIds == null || teamDefs == null || teamLevels == null) return default;
        if (idx < 0 || idx >= teamIds.Length) return default;
        if (string.IsNullOrEmpty(teamIds[idx]) || teamDefs[idx] == null) return default;

        float curMax = GetActiveMaxHP_NoConditionals(teamMaxHP[idx], idx);

        float curHp = (teamHP != null && idx >= 0 && idx < teamHP.Length) ? teamHP[idx] : curMax;
        float hp01 = curMax > 0.01f ? Mathf.Clamp01(curHp / curMax) : 0f;

        int alliesAlive = 0;
        for (int i = 0; i < teamCount; i++)
            if (i != idx && teamHP != null && i < teamHP.Length && teamHP[i] > 0.01f) alliesAlive++;

        int winStreak = (EncounterManager.I != null) ? EncounterManager.I.CurrentWinStreak : 0;

        TitleContext ctx = TitleContext.Empty;
        ctx.selfHp01 = hp01;
        ctx.alliesAlive = alliesAlive;
        ctx.winStreak = winStreak;

        var def = teamDefs[idx];
        int lvl = teamLevels[idx];
        string ownedId = teamIds[idx];

        TitleStatMods mods = default;
        mods.atkFlat = Mathf.RoundToInt(TitlesAdapter.GetStatValue(ownedId, def, lvl, "atkFlat", ctx, 0f));
        mods.atkPct = TitlesAdapter.GetStatValue(ownedId, def, lvl, "atkPct", ctx, 0f);

        mods.defFlat = Mathf.RoundToInt(TitlesAdapter.GetStatValue(ownedId, def, lvl, "defFlat", ctx, 0f));
        mods.defPct = TitlesAdapter.GetStatValue(ownedId, def, lvl, "defPct", ctx, 0f);

        mods.spdFlat = Mathf.RoundToInt(TitlesAdapter.GetStatValue(ownedId, def, lvl, "spdFlat", ctx, 0f));
        mods.spdPct = TitlesAdapter.GetStatValue(ownedId, def, lvl, "spdPct", ctx, 0f);

        mods.hpPct = TitlesAdapter.GetStatValue(ownedId, def, lvl, "hpPct", ctx, 0f);

        return mods;
    }

    private TitleStatMods GetConditionalModsForActive() =>GetConditionalModsForIndex(activeIndex);

    public float GetActiveMaxHP(float baseMax, int idx = -1)
    {
        float v = Mathf.Max(1f, baseMax);

        if (idx >= 0)
        {
            var tmods = GetTitleModsForIndex(idx);
            if (tmods.hpPct > 0f) v *= 1f + tmods.hpPct;

            var cmods = GetConditionalModsForIndex(idx);
            if (cmods.hpPct > 0f) v *= 1f + Mathf.Max(0f, cmods.hpPct);
        }

        int hpBuff = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        v = Mathf.Max(1f, v + hpBuff);

        return v;
    }

    private float GetFinalMaxHPForIndex(int idx)
    {
        if (teamMaxHP == null || idx < 0 || idx >= teamMaxHP.Length) return 1f;
        return GetActiveMaxHP(teamMaxHP[idx], idx);
    }

    private int GetAlliesAliveNotIncludingActive()
    {
        int alive = 0;
        for (int i = 0; i < teamCount; i++)
            if (i != activeIndex && teamHP[i] > 0.01f) alive++;
        return alive;
    }

    private int GetWinStreakSafe()
    {
        try
        {
            var em = EncounterManager.I;
            if (em == null) return 0;

            var t = em.GetType();
            var p = t.GetProperty("CurrentWinStreak") ?? t.GetProperty("WinStreak");
            if (p != null && p.PropertyType == typeof(int)) return (int)p.GetValue(em);

            var m = t.GetMethod("GetWinStreak", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            if (m != null && m.ReturnType == typeof(int)) return (int)m.Invoke(em, null);
        }
        catch { }
        return 0;
    }

    private TitleContext BuildTitleContextForActive()
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);
        float hpPct = curMax > 0.01f ? Mathf.Clamp01(teamHP[activeIndex] / curMax) : 0f;
        int alliesAlive = GetAlliesAliveNotIncludingActive();
        int streak = GetWinStreakSafe();

        var ctx = new TitleContext
        {
            selfHp01 = hpPct,
            alliesAlive = alliesAlive,
            winStreak = streak,
            isBattle = true
        };
        return ctx;
    }










    /// <summary>
    /// Returns true if a line with the given tags would be suppressed by current settings (condensed/auto-compress).
    /// Use this to avoid constructing strings that would be immediately skipped (GC savings in auto battles).
    /// </summary>



    private IEnumerator Say(string line, BattleLineTag tags = BattleLineTag.None)
    {
        bool condensed = SettingsManager.I != null && SettingsManager.I.GetCondensedBattleText();
        bool autoCompress = SettingsManager.I != null && SettingsManager.I.GetCompressAutoBattleText();

        bool isAuto = AutoResolveActive || !manualTurns;

        if (condensed && (tags & BattleLineTag.Result) == 0)
            yield break;

        if (isAuto && autoCompress && (tags & BattleLineTag.Flavor) != 0)
            yield break;

        BattleLogger.Log(line, LogScope.Battle);

        _narrationLock = true;
        GameEvents.OnBattleStateChanged?.Invoke();

        if (battleTextBox != null)
            yield return battleTextBox.ShowLine(new BattleLine(line, tags), battleSpeed);

        _narrationLock = false;
        GameEvents.OnBattleStateChanged?.Invoke();
    }

    public string ActivePlayerMonsterId
    {
        get
        {
            if (teamIds == null || teamIds.Length == 0) return "";
            if (activeIndex < 0 || activeIndex >= teamIds.Length) return "";
            return teamIds[activeIndex];
        }
    }

    public string ActiveWildMonsterId => wildDef ? wildDef.id : "";

    private float GetActiveMaxHP_NoConditionals(float baseMax, int idx)
    {
        float v = Mathf.Max(1f, baseMax);

        var tmods = GetTitleModsForIndex(idx);
        if (tmods.hpPct > 0f) v *= (1f + tmods.hpPct);

        int hpBuff = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        v = Mathf.Max(1f, v + hpBuff);

        return v;
    }



    private List<TitleSO> GetTitlesForOwnedIdSafe(string ownedId)
    {
        if (string.IsNullOrEmpty(ownedId)) return null;

        try
        {
            return TitleManager.I?.GetTitlesForMonster(ownedId);
        }
        catch { }

        return null;
    }

    private void Debug_LogActiveTitlesSnapshot(string reason)
    {
        if (!debugTitles) return;
        if (activeIndex < 0) return;
        if (teamDefs == null || teamLevels == null || teamIds == null) return;
        if (activeIndex >= teamDefs.Length || activeIndex >= teamLevels.Length || activeIndex >= teamIds.Length) return;

        string ownedId = teamIds[activeIndex];
        var def = teamDefs[activeIndex];
        int lvl = teamLevels[activeIndex];

        if (string.IsNullOrEmpty(ownedId) || def == null) return;

        var titles = GetTitlesForOwnedIdSafe(ownedId);

        Debug.Log($"[Titles][{reason}] Turn={_turnIndex} OwnedId={ownedId} Monster={def.displayName} Lv={lvl}");

        if (titles == null)
        {
            Debug.Log("[Titles] Title list unavailable (TitleManager.I.GetTitlesForMonster not reachable).");
        }
        else if (titles.Count == 0)
        {
            Debug.Log("[Titles] (No titles found)");
        }
        else
        {
            for (int i = 0; i < titles.Count; i++)
            {
                var t = titles[i];
                if (!t) continue;

                string id = "";
                try { id = t.titleId; } catch { }

                string extra = "";
                if (t is BattleStartFlatTitleSO bsf)
                    extra = $" stat={bsf.stat} flatAmount={bsf.flatAmount} durationTurns={bsf.durationTurns}";

                Debug.Log($"  • [{i}] {id} {t.name} ({t.GetType().Name}){extra}");
            }
        }
    }

    private static bool LooksLikeLegacyTrainingWasMirroredIntoFlat(int flatAtkBonus, int trainingAtk)
    {
        return trainingAtk > 0 && flatAtkBonus >= trainingAtk;
    }



    /// <summary>
    /// Call after swaps / at round boundaries to reflect the CURRENT logical status.
    /// (Guard = defending this round, Charge = has charged next attack queued)
    /// </summary>


    
    // ─────────────────────────────────────────────────────────────────────────────
    // Progression Totals Helpers
    // Baseline Totals = (SpeciesBase + LevelGrowth + TrainingBonus) + PermanentFlat (flatAtkBonus only)
    // Titles/equipment/temp/conditionals stack elsewhere.
    // ─────────────────────────────────────────────────────────────────────────────

    private bool TryGetOwnedAtIndex(int idx, out OwnedMonsterData om)
    {
        om = null;
        var roster = SaveManager.Data?.team;
        if (roster == null) return false;
        if (idx < 0 || idx >= roster.Count) return false;
        om = roster[idx];
        return om != null;
    }

    private void GetProgressionTotalsForIndex(
        int idx,
        out int totalHP,
        out int totalATK,
        out int totalDEF,
        out int totalSPD,
        out int flatAtkBonusOnly)
    {
        totalHP = totalATK = totalDEF = totalSPD = 0;
        flatAtkBonusOnly = 0;

        if (teamDefs == null || teamLevels == null) return;
        if (idx < 0 || idx >= teamDefs.Length) return;
        var def = teamDefs[idx];
        if (!def) return;

        int lvl = Mathf.Max(1, teamLevels[idx]);

        // SpeciesBase + LevelGrowth
        int hpBase  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        int atkBase = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
        int defBase = BattleCalc.CalcDefense(def, lvl);
        int spdBase = BattleCalc.CalcSpeed(def, lvl);

        // Training (EV-like)
        int tHp = 0, tAtk = 0, tDef = 0, tSpd = 0;

        // Permanent flat (separate system)
        int flatAtk = 0;

        if (TryGetOwnedAtIndex(idx, out var om))
        {
            tHp  = Mathf.Max(0, om.trainingBonus.hp);
            tAtk = Mathf.Max(0, om.trainingBonus.atk);
            tDef = Mathf.Max(0, om.trainingBonus.def);
            tSpd = Mathf.Max(0, om.trainingBonus.spd);

            flatAtk = Mathf.Max(0, om.flatAtkBonus);
        }

        // Baseline totals
        totalHP  = Mathf.Max(1, hpBase + tHp);
        totalDEF = Mathf.Max(0, defBase + tDef);
        totalSPD = Mathf.Max(1, spdBase + tSpd);

        // ATK baseline includes training + flatAtkBonus, with legacy guard:
        int atkTrainingPlusFlat = tAtk + flatAtk;
        if (LooksLikeLegacyTrainingWasMirroredIntoFlat(flatAtk, tAtk))
            atkTrainingPlusFlat = Mathf.Max(0, flatAtk); 

        totalATK = Mathf.Max(1, atkBase + atkTrainingPlusFlat);

        flatAtkBonusOnly = flatAtk;
    }

    private int GetProgressionTotalSPDForIndex(int idx)
    {
        GetProgressionTotalsForIndex(idx, out _, out _, out _, out int spd, out _);
        return Mathf.Max(1, spd);
    }





}
