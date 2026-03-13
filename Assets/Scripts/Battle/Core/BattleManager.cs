using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public partial class BattleManager : MonoBehaviour
{
    private enum PlayerAction { None, Attack, Defend, Focus, Swap, Run }
    private enum EnemyAction { None, Attack, Defend, Focus, Run }

    // Wild combatants must use a synthetic combat id (e.g., "WILD::<...>") so they
    // never collide with a real owned monsterId like "M-039".
    // If they collide, the TitleManager will treat the wild as the player's monster
    // and incorrectly apply the player's equipped titles to the wild.
    private static int _wildCombatSerial = 0;

    private readonly BattleEventBus _eventBus = new BattleEventBus();

    public event Action<BattleEvent> OnBattleEvent
    {
        add { _eventBus.OnEvent += value; }
        remove { _eventBus.OnEvent -= value; }
    }

    private static int _battleSerial;
    private readonly BattleRngService _rng = new BattleRngService();


    
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

    [Header("Auto-Queue Countdown UI (Optional)")]
    [Tooltip("If > 0, the countdown UI is considered 'active' when remaining seconds are <= this value.")]
    [SerializeField, Min(0f)] private float autoQueueCountdownShowAtSeconds = 10f;

     [Header("HUD Rig")]
    [SerializeField] private BattleHudRig defaultHudRig;

    private BattleHudRig _hudRigOverride;
    private BattleHudRig _hudRigActive;

    /// <summary>
    /// Fired while waiting for player input when the auto-queue failsafe is enabled.
    /// float = seconds remaining (clamped >= 0), bool = whether countdown should be shown.
    /// </summary>
    public event Action<float, bool> OnAutoQueueCountdown;

    private bool _autoQueueCountdownShown;
    private int _autoQueueCountdownLastInt = int.MinValue;

    private bool _manualTurnsDefault;
    private bool _enableAutoQueueDefault;
    private float _autoQueueSecondsDefault;
    private bool _autoResolveActive;
    private bool _defaultsCaptured;

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

    // Wild UI (populated from HudRig at battle start)
    private GameObject wildPanel;
    private Slider wildHPBar;
    private Image wildIcon;
    private TextMeshProUGUI wildNameText;
    private TextMeshProUGUI wildLevelText;
    private TextMeshProUGUI wildIdText;
    private TextMeshProUGUI wildTypeText;
    private TextMeshProUGUI wildRarityText;
    private TextMeshProUGUI wildHPText;
    private TextMeshProUGUI wildATKText;
    private TextMeshProUGUI wildDEFText;
    private TextMeshProUGUI wildSPDText;

    // Player UI (populated from HudRig at battle start)
    private GameObject playerPanel;
    private Slider playerHPBar;
    private Image playerIcon;
    private TextMeshProUGUI playerNameText;
    private TextMeshProUGUI playerLevelText;
    private TextMeshProUGUI playerIdText;
    private TextMeshProUGUI playerTypeText;
    private TextMeshProUGUI playerRarityText;
    private TextMeshProUGUI playerHPText;
    private TextMeshProUGUI playerATKText;
    private TextMeshProUGUI playerDEFText;
    private TextMeshProUGUI playerSPDText;

    // Bench UI (populated from HudRig at battle start)
    private Button benchBtn1;
    private Button benchBtn2;
    private Image benchImg1;
    private Image benchImg2;
    private TextMeshProUGUI benchHPText1;
    private TextMeshProUGUI benchHPText2;

    [Header("Turn Pacing (unscaled)")]
    [SerializeField, Min(0.05f)] private float beginRoundDelay = 0.15f;
    [SerializeField, Min(0.05f)] private float hitPause = 0.25f;
    [SerializeField, Min(0.05f)] private float endRoundDelay = 0.60f;

    [Header("Battle Start Pacing (unscaled)")]
    [Tooltip("Delay between the first and second monster reveal/spawn call at battle start.")]
    [SerializeField, Min(0f)] private float spawnDelayBetweenMonsters = 0.20f;

    [Header("Combat Tunables")]
    [Range(0f, 1f)][SerializeField] private float critChancePlayer = 0.10f;
    [Range(0f, 1f)][SerializeField] private float critChanceWild = 0.08f;
    [SerializeField] private float critMultiplier = 1.8f;
    [SerializeField] private bool showEffectivenessText = true;

    [Header("Speed Control")]
    [SerializeField, Min(0.25f)] private float battleSpeed = 1f;
    public float BattleSpeed => battleSpeed;


    // Battle Text Box (populated from HudRig at battle start)
    private BattleTextBoxUI battleTextBox;
    private BattleSwitchToggle _bottomToggle;

    [Header("Encounter Tuning")]
    [SerializeField, Range(0.5f, 2.0f)] private float encounterThreatScalar = 1.0f;

    // Feedback (populated from HudRig at battle start)
    private BattleFeedbackManager feedback;
    private bool _uiDefaultsCaptured;
    private BattleFeedbackManager _defaultFeedback;
    private BattleTextBoxUI _defaultBattleTextBox;
    private BattleSwitchToggle _defaultBottomToggle;
    private bool _runtimeUIOverrideActive;
    private BattleFeedbackManager _runtimeOverrideFeedback;
    private BattleTextBoxUI _runtimeOverrideTextBox;
    private BattleSwitchToggle _runtimeOverrideBottomToggle;
    private readonly Dictionary<TMP_Text, float> _battleStartInfoTargetAlpha = new Dictionary<TMP_Text, float>(16);
    private readonly Dictionary<Graphic, float> _battleStartHpBarTargetAlpha = new Dictionary<Graphic, float>(16);
    private readonly Dictionary<Graphic, float> _battleStartCoreIconTargetAlpha = new Dictionary<Graphic, float>(4);

    [Header("Status + Synergy (Battle Start)")]
    [Tooltip("Icons + default durations/magnitudes for StatusType.")]
    [SerializeField] private StatusLibrarySO statusLibrary;

    [Tooltip("Synergy mapping: MonsterType + tier -> StatusType + scope.")]
    [SerializeField] private SynergyLibrarySO synergyLibrary;

    [Header("Debug - Synergy/Status")]
    [Tooltip("For testing only: bypass unlocks/counts and force at least one synergy to apply so you can verify UI + logging.")]
    [SerializeField] private bool debugForceSynergyApply = false;

    [Tooltip("For testing only: tier used when debugForceSynergyApply is enabled.")]
    [SerializeField] private SynergyTier debugForcePlayerSynergyTier = SynergyTier.Tier2;

    [Tooltip("For testing only: also force a wild synergy tier (ignores difficulty/unlock).")]
    [SerializeField] private bool debugForceWildSynergyTier = false;

    [SerializeField] private SynergyTier debugWildSynergyTier = SynergyTier.Tier2;

    [Tooltip("Logs why synergies/statuses did (or did not) apply at battle start.")]
    [SerializeField] private bool debugSynergyLogs = true;

    public bool NarrationLocked => _narrationLock;
    private bool _narrationLock;

    public BattleRules Rules => _rules;

    public MonsterDataSO WildDef => wildDef;
    public int WildLevel => wildLevel;

    private MonsterDataSO wildDef;
    private int wildLevel;
    private float wildBaseMaxHP;
    private float wildBaseAttackPerTurn;
    private float wildMaxHP, wildHP;
    private float wildAttackPerTurn;

    private int teamCount, activeIndex;
    private MonsterDataSO[] teamDefs;
    private int[] teamLevels;
    private float[] teamMaxHP, teamHP;
    private string[] teamIds;
    private string[] teamTitleIds;
    private IBattleRosterProvider _rosterProvider;
    private IBattleContext _battleContext;
    private BattleRules _rules = BattleRules.Default;
    private OwnedMonsterData[] teamOwnedEffective;
    private string[] teamOwnedUidEffective;

    private JobBattlePassives.Ctx[] jobCtx;

    private float[] shieldHP;
    private float[] titleShieldHP;
    private float wildTitleShieldHP = 0f;
    private float[] pendingGuardShield;
    private bool[] chargedNextAttack;

    private float[] teamPendingBuffPct;
    private int[] teamPendingBuffTurns;

    private float[] slotDamageBuffPct;
    private int[] slotDamageBuffTurns;

    private StatusType[] teamStatus;
    private int[] teamStatusTurns;
    private float[] teamStatusMagnitude;
    private bool[] teamStatusPersistent;

    private StatusType wildStatus = StatusType.None;
    private int wildStatusTurns = 0;
    private float wildStatusMagnitude = 0f;
    private bool wildStatusPersistent = false;

    private float _cachedCreditMult = 1f;

    [Header("Debug - Titles")]
    [SerializeField] private bool debugTitles = false;
    [SerializeField] private bool debugTitlesEveryTurn = true;
    [SerializeField] private bool debugTitlesOnSwap = true;

    private int _turnIndex = 0;
    private bool inBattle;
    public bool InBattle => inBattle;

    private Action<BattleResult> onEnd;
    private float startTime;
    private Coroutine turnCR;



    // ─────────────────────────────────────────────────────────────
    // GC / Allocation Scratch (mobile smoothness)
    // ─────────────────────────────────────────────────────────────
    private readonly List<int> _scratchOthers = new List<int>(4);
    private readonly BattleLogBuffer _logBuffer = new BattleLogBuffer();

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
    private bool _wildEscapedThisBattle = false;
    private bool wildChargedNextAttack = false;

    private int _totalCritsThisBattle = 0;
    private int _totalDamageTakenThisBattle = 0;
    private int _totalDamageDealtThisBattle = 0;

    private static readonly Color StatNeutral = Color.white;
    private static readonly Color StatBuff = new Color(0.35f, 1f, 0.35f);
    private static readonly Color StatNerf = new Color(1f, 0.35f, 0.35f);

    // ─────────────────────────────────────────────────────────────
    // UI baseline snapshots (Adjusted stats without Titles)
    // Captured once at battle start so buff/debuff coloring persists
    // until effects truly expire (or battle ends).
    // ─────────────────────────────────────────────────────────────
    private int[] _uiBaseAtk;
    private int[] _uiBaseDef;
    private int[] _uiBaseSpd;
    private int[] _uiBaseMaxHp;

    private int _uiBaseWildAtk;
    private int _uiBaseWildDef;
    private int _uiBaseWildSpd;
    private int _uiBaseWildMaxHp;

    // Wild Titles routing id (set from EncounterManager if available).
    private string _wildCombatIdForTitles;

    // ─────────────────────────────────────────────────────────────
    // Battle Stats System (centralized stat pipeline)
    // ─────────────────────────────────────────────────────────────
    private BattleStatsSystem _stats;
    public BattleStatsSystem Stats => _stats;

    // ─────────────────────────────────────────────────────────────
    // Battle stat rebuild contract
    // - One entrypoint for any "stats may have changed" situation.
    // - Centralizes maxHP % preservation + UI refresh ordering.
    // - Call RequestBattleStatRebuild(...) instead of sprinkling
    //   SyncEffectiveMaxHPFromStats / RaiseBattleStatsChanged / UI refresh.
    // ─────────────────────────────────────────────────────────────
    [Flags]
    public enum BattleStatRebuildReason
    {
        None          = 0,
        BattleStart   = 1 << 0,
        TurnAdvanced  = 1 << 1,
        Swap          = 1 << 2,
        HPChanged     = 1 << 3,
        Boosters      = 1 << 4,
        Titles        = 1 << 5,
        ExternalEvent = 1 << 6,
        Force         = 1 << 7,
    }

    private BattleStatRebuildReason _pendingStatRebuildReasons = BattleStatRebuildReason.None;
    private bool _isRebuildingStats;
    private bool _ignoreNextBattleStatsEvent;

    /// <summary>
    /// Single, strict entrypoint for "effective stats may have changed".
    /// This method:
    /// - rebuilds adjusted baselines when needed
    /// - preserves HP% when maxHP changes
    /// - refreshes UI (HP bars + stat labels)
    ///
    /// IMPORTANT:
    /// Prefer calling this from BattleManager / battle loop instead of calling
    /// GameEvents.RaiseBattleStatsChanged() directly.
    /// External systems may still raise GameEvents.BattleStatsChanged; the
    /// handler routes here with ExternalEvent.
    /// </summary>
    public void RequestBattleStatRebuild(BattleStatRebuildReason reason, bool forceRebuildAdjusted = false)
    {
        if (!inBattle) return;
        _pendingStatRebuildReasons |= reason;
        RebuildBattleStatsInternal(forceRebuildAdjusted);

        // Notify any external listeners (UI panels, debug overlays, etc.) that subscribe to GameEvents.
        // We ignore the immediate callback in our own handler to avoid recursion.
        _ignoreNextBattleStatsEvent = true;
        GameEvents.RaiseBattleStatsChanged();
    }

    private void RebuildBattleStatsInternal(bool forceRebuildAdjusted)
    {
        if (!inBattle) return;
        if (_isRebuildingStats) return;

        _isRebuildingStats = true;
        try
        {
            // If anything about baselines could have changed, rebuild adjusted caches.
            // Titles/boosters/conditionals are applied on top of adjusted and do not require
            // adjusted rebuilds, but battle start / roster swaps can.
            bool wantsAdjusted = forceRebuildAdjusted || (_pendingStatRebuildReasons & (BattleStatRebuildReason.BattleStart | BattleStatRebuildReason.Swap | BattleStatRebuildReason.Force)) != 0;

            if (_stats != null)
            {
                if (wantsAdjusted)
                {
                    _stats.MarkDirtyAll();
                    _stats.RebuildAdjustedBaselines();
                }
            }

            // Stats can change max HP (titles/boosters/conditionals). Preserve HP% then refresh UI.
            SyncEffectiveMaxHPFromStats(force: (_pendingStatRebuildReasons & BattleStatRebuildReason.BattleStart) != 0);

            // Keep legacy fields in sync where required.
            RefreshWildEffectiveStatsFromTitles();

            // UI sync contract.
            PushHPBars();
            UpdatePlayerInfoUI();
            UpdateWildInfoUI();
        }
        finally
        {
            _pendingStatRebuildReasons = BattleStatRebuildReason.None;
            _isRebuildingStats = false;
        }
    }

    // Effective MaxHP cache (so when max HP changes from titles/boosters we can preserve HP%).
    private float[] _effMaxHpCache;
    private float _wildEffMaxHpCache;

    // Exposed read-only hooks for BattleStatsSystem (keep BattleManager internals private)
    public int TeamCountSafe => teamCount;
    public float WildBaseMaxHP => wildBaseMaxHP;
    public float WildBaseAttackPerTurn => wildBaseAttackPerTurn;
    public string WildCombatIdForTitles => _wildCombatIdForTitles;

    public MonsterDataSO GetTeamDefSafe(int idx)
        => (teamDefs != null && idx >= 0 && idx < teamDefs.Length) ? teamDefs[idx] : null;

    public string GetTeamIdSafe(int idx)
        => (teamIds != null && idx >= 0 && idx < teamIds.Length) ? teamIds[idx] : null;


    public string GetTeamTitleIdSafe(int idx)
        => (teamTitleIds != null && idx >= 0 && idx < teamTitleIds.Length) ? teamTitleIds[idx] : GetTeamIdSafe(idx);

    public int GetTeamLevelSafe(int idx)
        => (teamLevels != null && idx >= 0 && idx < teamLevels.Length) ? Mathf.Max(1, teamLevels[idx]) : 1;

    public JobBattlePassives.Ctx GetJobCtxSafe(int idx)
        => (jobCtx != null && idx >= 0 && idx < jobCtx.Length) ? jobCtx[idx] : null;

    public TitleStatMods GetConditionalModsForIndexSafe(int idx)
        => GetConditionalModsForIndex(idx);

    public TitleContext BuildTitleContextForIndexSafe(int idx)
    {
        // Mirrors BuildTitleContextForActive but for any index.
        float curMax = GetFinalMaxHPForIndex(idx);
        float hpPct = (curMax > 0.01f && teamHP != null && idx >= 0 && idx < teamHP.Length)
            ? Mathf.Clamp01(teamHP[idx] / curMax)
            : 0f;

        int alliesAlive = 0;
        for (int i = 0; i < teamCount; i++)
        {
            if (i == idx) continue;
            if (teamHP != null && i >= 0 && i < teamHP.Length && teamHP[i] > 0.01f)
                alliesAlive++;
        }

        int streak = GetWinStreakSafe();
        return new TitleContext
        {
            selfHp01 = hpPct,
            alliesAlive = alliesAlive,
            winStreak = streak,
            isBattle = true,
            ownedId = GetTeamTitleIdSafe(idx)
        };
    }

    /// <summary>
    /// Safe TitleContext builder that does NOT call GetFinalMaxHPForIndex.
    /// Use this from BattleStatsSystem to avoid recursion (effective stats -> title ctx -> effective stats).
    /// maxHpForContext should be the caller's current working max HP (typically adjusted + job, before titles).
    /// </summary>
    public TitleContext BuildTitleContextForIndexUsingMaxSafe(int idx, float maxHpForContext)
    {
        float curMax = Mathf.Max(1f, maxHpForContext);
        float hpPct = (teamHP != null && idx >= 0 && idx < teamHP.Length)
            ? Mathf.Clamp01(teamHP[idx] / curMax)
            : 0f;

        int alliesAlive = 0;
        for (int i = 0; i < teamCount; i++)
        {
            if (i == idx) continue;
            if (teamHP != null && i >= 0 && i < teamHP.Length && teamHP[i] > 0.01f)
                alliesAlive++;
        }

        int streak = GetWinStreakSafe();
        return new TitleContext
        {
            selfHp01 = hpPct,
            alliesAlive = alliesAlive,
            winStreak = streak,
            isBattle = true,
            ownedId = GetTeamTitleIdSafe(idx)
        };
    }

    /// <summary>
    /// Safe TitleContext builder for the wild combatant that does NOT depend on wildMaxHP.
    /// This is required so conditional titles (e.g., Clutch Booster at HP &lt;= 25%)
    /// evaluate against the effective max HP the caller is working with.
    /// </summary>
    public TitleContext BuildTitleContextForWildUsingMaxSafe(float maxHpForContext)
    {
        float curMax = Mathf.Max(1f, maxHpForContext);
        float hpPct = curMax > 0.01f ? Mathf.Clamp01(wildHP / curMax) : 0f;

        return new TitleContext
        {
            selfHp01 = hpPct,
            alliesAlive = 0,
            winStreak = 0,
            isBattle = true,
            ownedId = _wildCombatIdForTitles
        };
    }

    /// <summary>
    /// Wild HP% helper against a caller-provided max HP.
    /// Used by BattleStatsSystem to apply conditional title mods correctly.
    /// </summary>
    public float GetWildHp01UsingMaxSafe(float maxHpForContext)
    {
        float curMax = Mathf.Max(1f, maxHpForContext);
        return curMax > 0.01f ? Mathf.Clamp01(wildHP / curMax) : 0f;
    }


    void Start()
    {
        RebindBenchButtons();

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
        UnbindBenchButtons();
    }

    private void RebindBenchButtons()
    {
        UnbindBenchButtons();

        if (benchBtn1) benchBtn1.onClick.AddListener(() => ClickBench(0));
        if (benchBtn2) benchBtn2.onClick.AddListener(() => ClickBench(1));
    }

    private void UnbindBenchButtons()
    {
        if (benchBtn1) benchBtn1.onClick.RemoveAllListeners();
        if (benchBtn2) benchBtn2.onClick.RemoveAllListeners();
    }

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

    public void RegisterBattleEventConsumer() => _eventBus.RegisterConsumer();
    public void UnregisterBattleEventConsumer() => _eventBus.UnregisterConsumer();
    private bool HasBattleEventConsumers => _eventBus.HasConsumers;

    private void Emit(BattleEvent e) => _eventBus.Emit(e);

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

    private string BuildFallbackWildCombatId(MonsterDataSO def)
    {
        _wildCombatSerial++;
        string baseId = (def != null && !string.IsNullOrEmpty(def.id)) ? def.id : "UNKNOWN";
        return $"WILD::{baseId}::{_wildCombatSerial}";
    }

    /// <summary>
    /// Called by EncounterManager at battle start.
    /// When true, the battle resolves in automatic mode (no manual input waits),
    /// and text/pace can be accelerated in UI scripts that query this flag.
    /// </summary>
    public bool AutoResolveActive => _autoResolveActive;
    public bool AutoQueueFailsafeEnabled => inBattle && manualTurns && enableAutoQueueAttack && autoQueueAttackAfterSeconds > 0f;
    public bool IsAutoQueuePausedByReviewUI => ShouldPauseAutoQueueAttack();

    public void ConfigureForAuto(bool isAuto)
    {
        CaptureDefaults();
        _autoResolveActive = isAuto;

        if (isAuto)
        {
            manualTurns = false;
            enableAutoQueueAttack = false;
            autoQueueAttackAfterSeconds = 0.25f;
        }
        else
        {
            manualTurns = _manualTurnsDefault;
            enableAutoQueueAttack = _enableAutoQueueDefault;
            autoQueueAttackAfterSeconds = _autoQueueSecondsDefault;
        }
    }

    private void EmitAutoQueueCountdown(float remainingSeconds, bool show)
    {
        if (remainingSeconds < 0f) remainingSeconds = 0f;
        int displayInt = Mathf.CeilToInt(remainingSeconds);

        if (!show)
        {
            if (_autoQueueCountdownShown)
            {
                _autoQueueCountdownShown = false;
                _autoQueueCountdownLastInt = int.MinValue;
                OnAutoQueueCountdown?.Invoke(0f, false);
            }
            return;
        }

        if (!_autoQueueCountdownShown || _autoQueueCountdownLastInt != displayInt)
        {
            _autoQueueCountdownShown = true;
            _autoQueueCountdownLastInt = displayInt;
            OnAutoQueueCountdown?.Invoke(remainingSeconds, true);
        }
    }

    private bool ShouldPauseAutoQueueAttack()
    {
        bool tutorialOpen = TutorialOverlayPanel.IsAnyOverlayOpen;
        bool loggerOpen = BattleLogPanelUI.IsAnyOpen;

        if (!loggerOpen && UIManager.I != null)
            loggerOpen = UIManager.I.IsOpen(PanelId.Log);

        return tutorialOpen || loggerOpen;
    }

    /// <summary>
    /// Overrides the BattleManager's UI targets at runtime (used by Iron Career).
    /// Additive only: does not remove any existing features.
    /// </summary>
    public void SetUIOverride(BattleFeedbackManager overrideFeedback, BattleTextBoxUI overrideTextBox, BattleSwitchToggle overrideBottomToggle)
    {
        if (!_uiDefaultsCaptured)
        {
            _uiDefaultsCaptured = true;
            _defaultFeedback = feedback;
            _defaultBattleTextBox = battleTextBox;
            _defaultBottomToggle = _bottomToggle;
        }

        if (overrideFeedback || overrideTextBox || overrideBottomToggle)
        {
            _runtimeUIOverrideActive = true;
            if (overrideFeedback) _runtimeOverrideFeedback = overrideFeedback;
            if (overrideTextBox) _runtimeOverrideTextBox = overrideTextBox;
            if (overrideBottomToggle) _runtimeOverrideBottomToggle = overrideBottomToggle;
        }

        if (overrideFeedback)
        {
            feedback = overrideFeedback;
            feedback.BindToBattleManager(this);
        }
        if (overrideTextBox) battleTextBox = overrideTextBox;
        if (overrideBottomToggle) _bottomToggle = overrideBottomToggle;
    }

    /// <summary>
    /// Restores UI references back to their original inspector values.
    /// </summary>
    public void ClearUIOverride()
    {
        if (!_uiDefaultsCaptured) return;
        feedback = _defaultFeedback;
        battleTextBox = _defaultBattleTextBox;
        _bottomToggle = _defaultBottomToggle;
        _runtimeUIOverrideActive = false;
        _runtimeOverrideFeedback = null;
        _runtimeOverrideTextBox = null;
        _runtimeOverrideBottomToggle = null;
    }

    private void ReapplyRuntimeUIOverrideIfAny()
    {
        if (!_runtimeUIOverrideActive) return;
        SetUIOverride(_runtimeOverrideFeedback, _runtimeOverrideTextBox, _runtimeOverrideBottomToggle);
    }

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

    // Read-only UI helpers (shield pools)
    // NOTE: Title battle-start shields should be displayed as (+X) shield, but should NOT change the HP number.
    public int GetActivePlayerShieldTotal()
    {
        int s = 0;
        if (shieldHP != null && activeIndex >= 0 && activeIndex < shieldHP.Length)
            s += Mathf.RoundToInt(Mathf.Max(0f, shieldHP[activeIndex]));
        if (titleShieldHP != null && activeIndex >= 0 && activeIndex < titleShieldHP.Length)
            s += Mathf.RoundToInt(Mathf.Max(0f, titleShieldHP[activeIndex]));
        return Mathf.Max(0, s);
    }

    public int GetWildShieldTotal()
    {
        int s = 0;
        s += Mathf.RoundToInt(Mathf.Max(0f, wildShieldHP));
        s += Mathf.RoundToInt(Mathf.Max(0f, wildTitleShieldHP));
        return Mathf.Max(0, s);
    }

    private void FillOtherIndices(List<int> dst)
    {
        if (dst == null) return;
        dst.Clear();
        for (int i = 0; i < teamCount; i++)
            if (i != activeIndex) dst.Add(i);
    }

    private void SetIsPlayerTurn(bool value)
    {
        if (_isPlayerTurn == value) return;
        _isPlayerTurn = value;
        OnPlayerTurnChanged?.Invoke(_isPlayerTurn);

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
        if (IsActivePlayerFrozen()) return;
        if (isResolvingPlayerTurn) return;
        if (_narrationLock) return;
        if (pendingAction != PlayerAction.None) return;

        if (IsActivePlayerSundered())
        {
            if (a == PlayerAction.Defend || a == PlayerAction.Run)
            {
                SayInstant($"{GetName(activeIndex)} is Sundered! Cannot Defend or Run.");
                return;
            }
        }

        if (IsActivePlayerWyrmFury())
        {
            if (a == PlayerAction.Focus)
            {
                SayInstant($"{GetName(activeIndex)} is consumed by Wyrm Fury! Cannot Focus.");
                return;
            }
        }

        pendingAction = a;
        Emit(BattleEvent.ActionQueued(BattleSide.Player, a.ToString()));
        if (!HasBattleEventConsumers && feedback) feedback.PlayActionQueued(BattleFeedbackManager.BattleFeedbackSide.Player, ToFeedbackAction(a));
        GameEvents.OnBattleStateChanged?.Invoke();
    }

    private void SayInstant(string line, BattleLineTag tags = BattleLineTag.None)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        BattleLogger.Log(line, LogScope.Battle);
        EnsureBattleTextBoxBound();

#if UNITY_EDITOR
        if (battleTextBox == null)
            DevLog.Log("[IronTextTrace] SayInstant: battleTextBox is NULL after EnsureBattleTextBoxBound().");
#endif

        if (battleTextBox != null)
            battleTextBox.ShowLineInstant(line, tags, battleSpeed);
    }

    private void EnsureBattleTextBoxBound()
    {
        if (battleTextBox != null && battleTextBox.HasRenderableTarget)
            return;

        if (_runtimeOverrideTextBox != null && _runtimeOverrideTextBox.HasRenderableTarget)
        {
            battleTextBox = _runtimeOverrideTextBox;
            return;
        }

        if (_hudRigActive != null && _hudRigActive.battleTextBox != null && _hudRigActive.battleTextBox.HasRenderableTarget)
        {
            battleTextBox = _hudRigActive.battleTextBox;
            return;
        }

        var any = FindObjectsByType<BattleTextBoxUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < any.Length; i++)
        {
            var tb = any[i];
            if (!tb || !tb.HasRenderableTarget) continue;

            battleTextBox = tb;
            if (_runtimeUIOverrideActive && _runtimeOverrideTextBox == null)
                _runtimeOverrideTextBox = tb;
            return;
        }

        Debug.LogWarning("[BattleManager] No valid BattleTextBoxUI found. Battle text cannot be displayed.");
    }
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

    private void CaptureUiBaselines_NoTitles()
    {
        if (teamCount <= 0 || teamDefs == null) return;

        _uiBaseAtk = new int[teamCount];
        _uiBaseDef = new int[teamCount];
        _uiBaseSpd = new int[teamCount];
        _uiBaseMaxHp = new int[teamCount];

        // Prefer the centralized stats system so baseline snapshots always match combat.
        // (Adjusted baselines = level/training/threat; excludes titles/conditionals/boosters.)
        if (_stats != null)
        {
            _stats.MarkDirtyAll();
            _stats.RebuildAdjustedBaselines();

            for (int i = 0; i < teamCount; i++)
            {
                if (teamDefs[i] == null) continue;
                var adj = _stats.GetAdjustedPlayer(i);
                _uiBaseMaxHp[i] = Mathf.Max(1, adj.maxHP);
                _uiBaseAtk[i]   = Mathf.Max(1, adj.atk);
                _uiBaseDef[i]   = Mathf.Max(0, adj.def);
                _uiBaseSpd[i]   = Mathf.Max(1, adj.spd);
            }

            var wadj = _stats.GetAdjustedWild();
            _uiBaseWildMaxHp = Mathf.Max(1, wadj.maxHP);
            _uiBaseWildAtk   = Mathf.Max(1, wadj.atk);
            _uiBaseWildDef   = Mathf.Max(0, wadj.def);
            _uiBaseWildSpd   = Mathf.Max(1, wadj.spd);
        }
        else
        {
            // Fallback to legacy computation.
            for (int i = 0; i < teamCount; i++)
            {
                if (teamDefs[i] == null) continue;

                GetProgressionTotalsForIndex(i, out int hp, out int atk, out int def, out int spd, out _);
                if (teamMaxHP != null && i < teamMaxHP.Length)
                    hp = Mathf.RoundToInt(Mathf.Max(1f, teamMaxHP[i]));

                _uiBaseMaxHp[i] = Mathf.Max(1, hp);
                _uiBaseAtk[i]   = Mathf.Max(1, atk);
                _uiBaseDef[i]   = Mathf.Max(0, def);
                _uiBaseSpd[i]   = Mathf.Max(1, spd);
            }

            _uiBaseWildMaxHp = Mathf.RoundToInt(Mathf.Max(1f, wildBaseMaxHP));
            _uiBaseWildAtk = Mathf.RoundToInt(Mathf.Max(1f, wildBaseAttackPerTurn));
            _uiBaseWildDef = (wildDef != null) ? BattleCalc.CalcDefense(wildDef, wildLevel) : 0;
            _uiBaseWildSpd = (wildDef != null) ? BattleCalc.CalcSpeed(wildDef, wildLevel) : 1;
        }
    }

    public void BeginBattle(MonsterDataSO wild, int level, Action<BattleResult> onEnded)
    {
        Begin(wild, level, onEnded, null, null);
    }

    public void Begin(MonsterDataSO wild, int level, Action<BattleResult> onEnded)
    {
        Begin(wild, level, onEnded, null, null);
    }

    public void Begin(MonsterDataSO wild, int level, Action<BattleResult> onEnded, IBattleRosterProvider rosterProvider, IBattleContext battleContext)
    {
        _rosterProvider = rosterProvider;
        _battleContext = battleContext;
        _rules = (battleContext != null) ? battleContext.Rules : BattleRules.Default;

        if (IronCareerRuntime.IsActive && rosterProvider == null)
        {
            Debug.LogError("[BattleManager] IronCareerRuntime.IsActive but Begin(...) was called without a rosterProvider. Forfeiting battle.");
            ForceEndBattleEarly(false);
            return;
        }

        var injectedTeam = (rosterProvider != null) ? rosterProvider.GetPlayerTeam() : null;
        bool usingInjected = (injectedTeam != null && injectedTeam.Count > 0);

        var roster = usingInjected ? null : SaveManager.Data?.team;

        if (!usingInjected)
        {
            if (roster == null || roster.Count == 0) { ForceEndBattleEarly(false); return; }
        }

        _rng.ResetForBegin();
        playerNoDmgTurns = 0;
        playerNoCritTurns = 0;
        runAttempts = 0;
        _wildEscapedThisBattle = false;

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

        ApplyHudRigForThisBattle();

        TryApplyBattleBackgroundFromWild();

        _wildCombatIdForTitles = null;

        if (rosterProvider != null)
        {
            try
            {
                var injectedWild = rosterProvider.GetWild();
                if (injectedWild != null && !string.IsNullOrEmpty(injectedWild.combatantId))
                    _wildCombatIdForTitles = injectedWild.combatantId;
            }
            catch { }
        }

        if (string.IsNullOrEmpty(_wildCombatIdForTitles))
            _wildCombatIdForTitles = (EncounterManager.I != null) ? EncounterManager.I.WildCombatId : null;

        if (string.IsNullOrEmpty(_wildCombatIdForTitles) || !_wildCombatIdForTitles.StartsWith("WILD::", StringComparison.OrdinalIgnoreCase))
            _wildCombatIdForTitles = BuildFallbackWildCombatId(wildDef);

        float wHpBase = BattleCalc.CalcHP(wildDef, wildLevel);
        float wAtkBase = BattleCalc.CalcBaseAttack(wildDef, wildLevel, 0, 0);

        wildBaseMaxHP = Mathf.Max(1f, wHpBase * encounterThreatScalar);
        wildBaseAttackPerTurn = Mathf.Max(1f, wAtkBase * encounterThreatScalar);

        wildMaxHP = wildBaseMaxHP;
        wildHP = wildMaxHP;
        wildAttackPerTurn = wildBaseAttackPerTurn;

        // Clear stale effective-HP caches so SyncEffectiveMaxHPFromStats
        // doesn't compute hp% against the previous battle's wild.
        _wildEffMaxHpCache = 0f;
        _effMaxHpCache = null;

        if (feedback != null) feedback.ResetIconVisuals();
        HardResetIconVisual(playerIcon);
        HardResetIconVisual(wildIcon);

        bool shinyWild = (EncounterManager.I != null) && EncounterManager.I.CurrentWildIsShiny;

        bool isAuto = _rules.allowAutoBattle && (EncounterManager.I != null) && EncounterManager.I.IsAutoMode;

        ConfigureForAuto(isAuto);

        if (wildIcon)
        {
            if (shinyWild && wildDef != null && wildDef.shinyIcon != null) wildIcon.sprite = wildDef.shinyIcon;
            else wildIcon.sprite = (wildDef != null) ? wildDef.icon : null;
            HardResetIconVisual(wildIcon);
        }

        if (wildNameText)
        {
            if (wildDef != null)
                wildNameText.text = MonsterNameFormatter.Format(wildDef, shinyWild);
            else
                wildNameText.text = "Wild";
        }

        if (shinyWild && feedback != null)
        {
            feedback.PlayShinyNameSparkle(wildNameText);
        }

        if (wildLevelText) wildLevelText.text = $"Lv {wildLevel}";
        if (wildHPBar) { wildHPBar.maxValue = wildMaxHP; wildHPBar.value = wildHP; }

        UpdateWildInfoUI();

        teamCount = Mathf.Min(3, (injectedTeam != null) ? injectedTeam.Count : roster.Count);
        if (teamCount <= 0) { inBattle = false; return; }

        teamDefs = new MonsterDataSO[teamCount];
        teamLevels = new int[teamCount];
        teamMaxHP = new float[teamCount];
        teamHP = new float[teamCount];
        teamIds = new string[teamCount];
        teamTitleIds = new string[teamCount];

        teamOwnedEffective = new OwnedMonsterData[teamCount];
        teamOwnedUidEffective = new string[teamCount];

        teamStatus = new StatusType[teamCount];
        teamStatusTurns = new int[teamCount];
        teamStatusMagnitude = new float[teamCount];
        teamStatusPersistent = new bool[teamCount];

        EnsureShieldGrantPools();
        if (_ironShieldCarrySlots != null) Array.Clear(_ironShieldCarrySlots, 0, _ironShieldCarrySlots.Length);

        wildStatus = StatusType.None;
        wildStatusTurns = 0;
        wildStatusMagnitude = 0f;
        wildStatusPersistent = false;

        if (injectedTeam != null)
        {
            for (int i = 0; i < teamCount; i++)
            {
                var c = injectedTeam[i];

                teamTitleIds[i] = !string.IsNullOrEmpty(c.combatantId) ? c.combatantId : $"IRON::P::{i}";
                teamDefs[i] = c.def;
                teamLevels[i] = Mathf.Max(1, c.level);
                teamIds[i] = (c.def != null) ? c.def.id : null;

                if (c.def == null)
                {
                    teamMaxHP[i] = 1f;
                    teamHP[i] = 0f;
                    BattleLogger.Log($"[Iron] WARNING: injected team slot {i} has missing MonsterData. Marking as KO/unusable.", LogScope.Battle);
                    continue;
                }

                float baseMax = Mathf.Max(1f, BattleCalc.CalcHP(c.def, teamLevels[i]));
                teamMaxHP[i] = baseMax;
                teamHP[i] = Mathf.Clamp(c.hp, 0, Mathf.RoundToInt(baseMax));

                if (_rules.allowTitles && c.lockedTitle != null)
                {
                    TitlesAdapter.SetLocalTitles(teamTitleIds[i], new[] { c.lockedTitle });
                    TitlesAdapter.RegisterBattleContext(teamTitleIds[i], c.def, teamLevels[i]);
                }
            }
        }
        else
        {
            for (int i = 0; i < teamCount; i++)
        {
            var slotOwned = roster[i];
            var owned = ResolveEffectiveTeamOwnedForBattle(i, slotOwned);

            teamIds[i] = owned != null ? owned.monsterId : null;
            teamTitleIds[i] = teamIds[i];

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
        }

        jobCtx = new JobBattlePassives.Ctx[teamCount];
        shieldHP = new float[teamCount];
        titleShieldHP = new float[teamCount];
        wildTitleShieldHP = 0f;
        teamPendingBuffPct = new float[teamCount];
        teamPendingBuffTurns = new int[teamCount];

        slotDamageBuffPct = new float[teamCount];
        slotDamageBuffTurns = new int[teamCount];

        pendingGuardShield = new float[teamCount];
        chargedNextAttack = new bool[teamCount];

        for (int i = 0; i < teamCount; i++)
        {
            var owned = (teamOwnedEffective != null && i >= 0 && i < teamOwnedEffective.Length) ? teamOwnedEffective[i] : null;

            if (!_rules.allowJobPassives)
            {
                jobCtx[i] = default;
                continue;
            }

            string ownedMonsterId = (owned != null) ? owned.monsterId : null;
            JobType job = JobType.None;
            float hours = 0f;
            if (JobManager.I != null && !string.IsNullOrEmpty(ownedMonsterId))
            {
                var jh = JobManager.I.GetCurrentJobAndHours(ownedMonsterId);
                job = jh.Item1;
                hours = jh.Item2;
            }
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

        _stats = new BattleStatsSystem(this);
        _stats.RebuildAdjustedBaselines();

        playerTookFirstIncomingThisBattle = false;
        playerLandedFirstHitThisBattle = false;

        defendActiveThisRound = false;
        wildDefendActiveThisRound = false;
        pendingAction = PlayerAction.None;
        SetIsPlayerTurn(false);

        activeIndex = -1;
        for (int i = 0; i < teamCount; i++)
            if (teamHP[i] > 0f) { activeIndex = i; break; }

        if (activeIndex < 0) { EndBattleRouted(false); return; }

        CaptureUiBaselines_NoTitles();

        ApplyBattleStartTitles();

        if (_stats != null) _stats.MarkDirtyAll();
        SyncEffectiveMaxHPFromStats(force: true);

        ApplyBattleStartSynergies();

        ApplyActiveToUI();
        ClampAndPushActiveHP();
        RefreshBenchUI();

        if (_rules.allowBoosters && BattleBoosterController.I != null)
        {
            BattleBoosterController.I.SetHooks(new BattleRuntimeHooks
            {
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
            if (wildCG)
                wildCG.alpha = 0f;
        }
        if (playerPanel)
        {
            playerCG = playerPanel.GetComponent<CanvasGroup>();
            if (playerCG)
                playerCG.alpha = 0f;
        }

        if (feedback != null)
        {
            feedback.SetIconAlphaImmediate(BattleFeedbackManager.BattleFeedbackSide.Player, 0f);
            feedback.SetIconAlphaImmediate(BattleFeedbackManager.BattleFeedbackSide.Wild, 0f);
        }

        if (turnCR != null) StopCoroutine(turnCR);
        turnCR = StartCoroutine(Co_RevealPanelsThenStart(wildCG, playerCG, 0.28f));
        ResetStatusIcons();
    }

    private IEnumerator ResolveQueuedSwap()
    {
        if (pendingSwapBenchSlot < 0) yield break;

        FillOtherIndices(_scratchOthers);
        List<int> others = _scratchOthers;

        int benchSlot = pendingSwapBenchSlot;
        pendingSwapBenchSlot = -1;

        if (benchSlot < 0 || benchSlot >= others.Count) yield break;

        int targetIndex = others[benchSlot];
        if (teamHP[targetIndex] <= 0f) yield break;

        if (feedback)
            feedback.SetIconAlphaImmediate(BattleFeedbackManager.BattleFeedbackSide.Player, 0f);

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
        BattleLogger.AddKeyMoment($"SWAP: {GetName(activeIndex)}");
        Emit(BattleEvent.ActionQueued(BattleSide.Player, "Swap"));

        if (!HasBattleEventConsumers && feedback)
            feedback.PlayActionQueued(
                BattleFeedbackManager.BattleFeedbackSide.Player,
                BattleFeedbackManager.BattleFeedbackAction.Swap
            );

        if (debugTitles && debugTitlesOnSwap)
            Debug_LogActiveTitlesSnapshot("Swap");

        if (feedback)
            yield return feedback.Co_FadeInIcon(
                BattleFeedbackManager.BattleFeedbackSide.Player,
                teamDefs[activeIndex],
                onSpawnAnnounce: Co_AnnounceSpawnLine);
        else
            yield return Co_AnnounceSpawnLine(teamDefs[activeIndex]);
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


    /// <summary>
    /// When max HP changes due to titles/boosters, preserve current HP percent and update max caches.
    /// Call this whenever BattleStatsSystem becomes dirty (battle start, turn advance, combatant turn end).
    /// </summary>
    private void SyncEffectiveMaxHPFromStats(bool force = false)
    {
        if (_stats == null) return;

        {
            float newMax = Mathf.Max(1f, _stats.GetEffectiveWild().maxHP);
            float oldMax = (_wildEffMaxHpCache > 0.01f) ? _wildEffMaxHpCache : Mathf.Max(1f, wildMaxHP);

            if (force || Mathf.Abs(newMax - oldMax) > 0.01f)
            {
                float hp01 = oldMax > 0.01f ? Mathf.Clamp01(wildHP / oldMax) : 1f;
                wildMaxHP = newMax;
                wildHP = Mathf.Clamp(newMax * hp01, 0f, newMax);
                _wildEffMaxHpCache = newMax;
            }
        }

        if (teamCount <= 0 || teamHP == null || teamHP.Length == 0) return;

        if (_effMaxHpCache == null || _effMaxHpCache.Length != teamCount)
            _effMaxHpCache = new float[teamCount];

        for (int i = 0; i < teamCount; i++)
        {
            float newMax = Mathf.Max(1f, _stats.GetEffectivePlayer(i).maxHP);

            float oldMax = _effMaxHpCache[i];
            if (oldMax <= 0.01f)
                oldMax = Mathf.Max(1f, (teamMaxHP != null && i < teamMaxHP.Length) ? teamMaxHP[i] : 1f);

            if (force || Mathf.Abs(newMax - oldMax) > 0.01f)
            {
                float hp01 = oldMax > 0.01f ? Mathf.Clamp01(teamHP[i] / oldMax) : 1f;
                teamHP[i] = Mathf.Clamp(newMax * hp01, 0f, newMax);
                _effMaxHpCache[i] = newMax;
            }
        }
    }

    









    private IEnumerator Say(string line, BattleLineTag tags = BattleLineTag.None)
    {
        bool condensed = SettingsManager.I != null && SettingsManager.I.GetCondensedBattleText();
        bool autoCompress = SettingsManager.I != null && SettingsManager.I.GetCompressAutoBattleText();

        bool isAuto = AutoResolveActive || !manualTurns;

        if (condensed && (tags & BattleLineTag.Result) == 0)
        {
    #if UNITY_EDITOR
            DevLog.Log($"[IronTextTrace] Say suppressed by condensed text. tags={tags} line='{line}'");
    #endif
            yield break;
        }

        if (isAuto && autoCompress && (tags & BattleLineTag.Flavor) != 0)
        {
    #if UNITY_EDITOR
            DevLog.Log($"[IronTextTrace] Say suppressed by auto-compress flavor filter. tags={tags} line='{line}'");
    #endif
            yield break;
        }

        BattleLogger.Log(line, LogScope.Battle);

        _narrationLock = true;
        GameEvents.OnBattleStateChanged?.Invoke();

        EnsureBattleTextBoxBound();

#if UNITY_EDITOR
        if (battleTextBox == null)
            DevLog.Log("[IronTextTrace] Say: battleTextBox is NULL after EnsureBattleTextBoxBound().");
        else
            DevLog.Log($"[IronTextTrace] Say rendering on textbox='{battleTextBox.name}' tags={tags} auto={isAuto} condensed={condensed} autoCompress={autoCompress}");
#endif

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

    public string ActivePlayerTitleOwnerId
    {
        get
        {
            if (activeIndex < 0) return "";
            return GetTeamTitleIdSafe(activeIndex) ?? "";
        }
    }

    public string ActiveWildMonsterId => wildDef ? wildDef.id : "";

    private static bool LooksLikeLegacyTrainingWasMirroredIntoFlat(int flatAtkBonus, int trainingAtk)
    {
        return trainingAtk > 0 && flatAtkBonus >= trainingAtk;
    }

    private OwnedMonsterData ResolveEffectiveTeamOwnedForBattle(int teamIndex, OwnedMonsterData teamSlotOwned)
    {
        if (!_rules.allowPreferredVariants)
        {
            if (teamOwnedEffective != null && teamIndex >= 0 && teamIndex < teamOwnedEffective.Length)
            {
                teamOwnedEffective[teamIndex] = teamSlotOwned;
                teamOwnedUidEffective[teamIndex] = teamSlotOwned != null ? teamSlotOwned.ownedUID : null;
            }
            return teamSlotOwned;
        }

        if (teamSlotOwned == null || string.IsNullOrEmpty(teamSlotOwned.monsterId))
            return teamSlotOwned;

        if (!string.IsNullOrEmpty(teamSlotOwned.ownedUID))
        {
            var resolved = XPManager.Resolve(teamSlotOwned) ?? teamSlotOwned;
            if (teamOwnedEffective != null && teamIndex >= 0 && teamIndex < teamOwnedEffective.Length)
            {
                teamOwnedEffective[teamIndex] = resolved;
                teamOwnedUidEffective[teamIndex] = resolved.ownedUID;
            }
            return resolved;
        }

        var preferred = MonsterVariantPreference.GetPreferredOwned(teamSlotOwned.monsterId);
        if (preferred != null && preferred.monsterId == teamSlotOwned.monsterId)
        {
            if (teamOwnedEffective != null && teamIndex >= 0 && teamIndex < teamOwnedEffective.Length)
            {
                teamOwnedEffective[teamIndex] = preferred;
                teamOwnedUidEffective[teamIndex] = preferred.ownedUID;
            }
            return preferred;
        }

        if (teamOwnedEffective != null && teamIndex >= 0 && teamIndex < teamOwnedEffective.Length)
        {
            teamOwnedEffective[teamIndex] = teamSlotOwned;
            teamOwnedUidEffective[teamIndex] = teamSlotOwned.ownedUID;
        }
        return teamSlotOwned;
    }

    private bool TryGetOwnedAtIndex(int idx, out OwnedMonsterData om)
    {
        om = null;

        if (teamOwnedEffective != null && idx >= 0 && idx < teamOwnedEffective.Length)
        {
            om = teamOwnedEffective[idx];
            return om != null;
        }

        if (IronCareerRuntime.IsActive) return false;

        var roster = SaveManager.Data?.team;
        if (roster == null) return false;
        if (idx < 0 || idx >= roster.Count) return false;
        om = roster[idx];
        return om != null;
    }

    internal void GetProgressionTotalsForIndex(
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

        int hpBase  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        int atkBase = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
        int defBase = BattleCalc.CalcDefense(def, lvl);
        int spdBase = BattleCalc.CalcSpeed(def, lvl);

        int tHp = 0, tAtk = 0, tDef = 0, tSpd = 0;
        int flatAtk = 0;

        if (TryGetOwnedAtIndex(idx, out var om))
        {
            tHp  = Mathf.Max(0, om.trainingBonus.hp);
            tAtk = Mathf.Max(0, om.trainingBonus.atk);
            tDef = Mathf.Max(0, om.trainingBonus.def);
            tSpd = Mathf.Max(0, om.trainingBonus.spd);

            flatAtk = Mathf.Max(0, om.flatAtkBonus);
        }

        totalHP  = Mathf.Max(1, hpBase + tHp);
        totalDEF = Mathf.Max(0, defBase + tDef);
        totalSPD = Mathf.Max(1, spdBase + tSpd);

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

    private void AddBattleLine(string line, BattleLineTag tags = BattleLineTag.None)
    {
        if (!isActiveAndEnabled) return;
        StartCoroutine(Say(line, tags));
    }
        public void SetHudRigOverride(BattleHudRig rig)
    {
        _hudRigOverride = rig;
    }

    public void ClearHudRigOverride()
    {
        _hudRigOverride = null;
    }

    private void ApplyHudRigForThisBattle()
    {
        _hudRigActive = (_hudRigOverride != null) ? _hudRigOverride : defaultHudRig;

        if (_hudRigActive == null)
        {
            Debug.LogWarning("[BattleManager] No HUD rig assigned (defaultHudRig missing). Using existing serialized references.");
            return;
        }

        wildPanel = _hudRigActive.wildPanel;
        playerPanel = _hudRigActive.playerPanel;

        wildHPBar = _hudRigActive.wildHPBar;
        wildIcon = _hudRigActive.wildIcon;
        wildNameText = _hudRigActive.wildNameText;
        wildLevelText = _hudRigActive.wildLevelText;
        wildIdText = _hudRigActive.wildIdText;
        wildTypeText = _hudRigActive.wildTypeText;
        wildRarityText = _hudRigActive.wildRarityText;
        wildHPText = _hudRigActive.wildHPText;
        wildATKText = _hudRigActive.wildATKText;
        wildDEFText = _hudRigActive.wildDEFText;
        wildSPDText = _hudRigActive.wildSPDText;

        playerHPBar = _hudRigActive.playerHPBar;
        playerIcon = _hudRigActive.playerIcon;
        playerNameText = _hudRigActive.playerNameText;
        playerLevelText = _hudRigActive.playerLevelText;
        playerIdText = _hudRigActive.playerIdText;
        playerTypeText = _hudRigActive.playerTypeText;
        playerRarityText = _hudRigActive.playerRarityText;
        playerHPText = _hudRigActive.playerHPText;
        playerATKText = _hudRigActive.playerATKText;
        playerDEFText = _hudRigActive.playerDEFText;
        playerSPDText = _hudRigActive.playerSPDText;

        benchBtn1 = _hudRigActive.benchBtn1;
        benchBtn2 = _hudRigActive.benchBtn2;
        benchImg1 = _hudRigActive.benchImg1;
        benchImg2 = _hudRigActive.benchImg2;
        benchHPText1 = _hudRigActive.benchHPText1;
        benchHPText2 = _hudRigActive.benchHPText2;

        // Apply hud-rig values as defaults directly — do NOT route through
        // SetUIOverride, which would clobber _runtimeOverrideTextBox and
        // destroy any Iron Career override that was already registered.
        if (_hudRigActive.feedback)
        {
            feedback = _hudRigActive.feedback;
            feedback.BindToBattleManager(this);
        }
        if (_hudRigActive.battleTextBox)
            battleTextBox = _hudRigActive.battleTextBox;
        if (_hudRigActive.bottomToggle)
            _bottomToggle = _hudRigActive.bottomToggle;

        if (!_uiDefaultsCaptured)
        {
            _uiDefaultsCaptured = true;
            _defaultFeedback = feedback;
            _defaultBattleTextBox = battleTextBox;
            _defaultBottomToggle = _bottomToggle;
        }

        ReapplyRuntimeUIBindingsOverrideIfAny();
        ReapplyRuntimeUIOverrideIfAny();
    }
}
