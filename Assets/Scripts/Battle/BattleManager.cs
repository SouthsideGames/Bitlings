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
    public int creditsBase;
    public int creditsTitleBonus;
    public float creditsMultiplier;

    public int growthCoresGained;
    public int growthCoresBase;
    public int growthCoresTitleBonus;

    public string activeMonsterOwnedId;  
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
    private enum EnemyAction { None, Attack, Defend, Focus, Run }

    // Wild combatants must use a synthetic combat id (e.g., "WILD::<...>") so they
    // never collide with a real owned monsterId like "M-039".
    // If they collide, the TitleManager will treat the wild as the player's monster
    // and incorrectly apply the player's equipped titles to the wild.
    private static int _wildCombatSerial = 0;

    private string BuildFallbackWildCombatId(MonsterDataSO def)
    {
        _wildCombatSerial++;
        string baseId = (def != null && !string.IsNullOrEmpty(def.id)) ? def.id : "UNKNOWN";
        return $"WILD::{baseId}::{_wildCombatSerial}";
    }


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

    // Tracks last emitted state to avoid spamming listeners.
    private bool _autoQueueCountdownShown;
    private int _autoQueueCountdownLastInt = int.MinValue;

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

    private void EmitAutoQueueCountdown(float remainingSeconds, bool show)
    {
        // Clamp for safety.
        if (remainingSeconds < 0f) remainingSeconds = 0f;

        // If no one is listening, still keep internal state coherent.
        // Only emit on state changes or when the displayed integer would change.
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

    // ─────────────────────────────────────────────────────────────
    // UI routing overrides (Iron Career)
    // ─────────────────────────────────────────────────────────────
    private bool _uiDefaultsCaptured;
    private BattleFeedbackManager _defaultFeedback;
    private BattleTextBoxUI _defaultBattleTextBox;
    private BattleSwitchToggle _defaultBottomToggle;

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

        if (overrideFeedback) feedback = overrideFeedback;
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
    }

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
    // Wild baseline (adjusted by level + encounterThreatScalar). Titles/conditionals stack on top.
    private float wildBaseMaxHP;
    private float wildBaseAttackPerTurn;

    // Wild current effective (may include title-driven max HP; HP is tracked against this).
    private float wildMaxHP, wildHP;
    private float wildAttackPerTurn;

    private int teamCount, activeIndex;
    private MonsterDataSO[] teamDefs;
    private int[] teamLevels;
    private float[] teamMaxHP, teamHP;
    private string[] teamIds;
    // Title routing ids (normal: same as teamIds; Iron: per-instance combatant ids)
    private string[] teamTitleIds;

    // Iron / custom battle context (optional)
    private IBattleRosterProvider _rosterProvider;
    private IBattleContext _battleContext;
    private BattleRules _rules = BattleRules.Default;

    // Preferred-variant aware "effective" owned data used for battle.
    // This allows players to choose shiny/non-shiny display & usage without having to rearrange the team list.
    private OwnedMonsterData[] teamOwnedEffective;
    private string[] teamOwnedUidEffective;

    private JobBattlePassives.Ctx[] jobCtx;

    private float[] shieldHP;
    private float[] titleShieldHP; // Title battle-start shield (separate from job/guard shield)
    private float wildTitleShieldHP = 0f;
    private float[] pendingGuardShield;
    private bool[] chargedNextAttack;

    private float[] teamPendingBuffPct;
    private int[] teamPendingBuffTurns;

    private float[] slotDamageBuffPct;
    private int[] slotDamageBuffTurns;

    // ─────────────────────────────────────────────────────────────
    // Status runtime (Phase 3: apply-only; Phase 4 will add ticking)
    // One status per unit. No overwrites.
    // ─────────────────────────────────────────────────────────────
    private StatusType[] teamStatus;
    private int[] teamStatusTurns;
    private float[] teamStatusMagnitude;
    private bool[] teamStatusPersistent;

    private StatusType wildStatus = StatusType.None;
    private int wildStatusTurns = 0;
    private float wildStatusMagnitude = 0f;
    private bool wildStatusPersistent = false;

    // Cached title multipliers computed at battle start to avoid timing/order issues.
    private float _cachedCreditMult = 1f;

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
        if (IsActivePlayerFrozen()) return;
        if (isResolvingPlayerTurn) return;
        if (_narrationLock) return;
        if (pendingAction != PlayerAction.None) return;

        // Status: Sundering blocks Defend/Run.
        if (IsActivePlayerSundered())
        {
            if (a == PlayerAction.Defend || a == PlayerAction.Run)
            {
                SayInstant($"{GetName(activeIndex)} is Sundered! Cannot Defend or Run.");
                return;
            }
        }

        // Status: Wyrm Fury blocks Focus/Charge (Focus action).
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
        if (battleTextBox != null)
            battleTextBox.ShowLineInstant(line, tags, battleSpeed);
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

    
    // ─────────────────────────────────────────────────────────────
    // UI Baseline Snapshot (Adjusted stats without Titles)
    // ─────────────────────────────────────────────────────────────
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
            _uiBaseWildAtk   = Mathf.RoundToInt(Mathf.Max(1f, wildBaseAttackPerTurn));
	        	// Use explicit null checks (avoids CS0126 if MonsterDataSO is not a UnityEngine.Object).
	        	_uiBaseWildDef   = (wildDef != null) ? BattleCalc.CalcDefense(wildDef, wildLevel) : 0;
	        	_uiBaseWildSpd   = (wildDef != null) ? BattleCalc.CalcSpeed(wildDef, wildLevel) : 1;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Battle-start Titles application
    // ─────────────────────────────────────────────────────────────
    private void ApplyBattleStartTitles()
    {
        // Player (active slot)
        try
        {
            if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            {
                string ownedId = GetTeamTitleIdSafe(activeIndex);
                if (!string.IsNullOrEmpty(ownedId))
                {
                    TitlesAdapter.OnBattleStart(ownedId, wildDef, wildLevel);

                        // Cache credit multiplier for the active monster at battle start.
                        try
                        {
                            _cachedCreditMult = Mathf.Max(0f, TitlesAdapter.GetCreditMultOnVictory(ownedId, wildDef, wildLevel));
                        }
                        catch (Exception ex)
                        {
                            Debug.Log($"[BattleManager] Failed to cache credit multiplier: {ex.Message}");
                            _cachedCreditMult = 1f;
                        }

                    if (titleShieldHP != null && activeIndex < titleShieldHP.Length)
                        titleShieldHP[activeIndex] = Mathf.Max(0f, TitlesAdapter.GetBattleStartShieldRemaining(ownedId));
                }
            }
        }
        catch (Exception ex)
        {
            BattleLogger.Log($"[Titles] OnBattleStart(player) exception: {ex.Message}", LogScope.Battle);
        }

        // Wild
        try
        {
            if (string.IsNullOrEmpty(_wildCombatIdForTitles) || !_wildCombatIdForTitles.StartsWith("WILD::", StringComparison.OrdinalIgnoreCase))
                _wildCombatIdForTitles = (EncounterManager.I != null) ? EncounterManager.I.WildCombatId : null;

            if (string.IsNullOrEmpty(_wildCombatIdForTitles) || !_wildCombatIdForTitles.StartsWith("WILD::", StringComparison.OrdinalIgnoreCase))
                _wildCombatIdForTitles = BuildFallbackWildCombatId(wildDef);

            if (!string.IsNullOrEmpty(_wildCombatIdForTitles))
            {
                TitlesAdapter.OnBattleStart(_wildCombatIdForTitles, wildDef, wildLevel);
                wildTitleShieldHP = Mathf.Max(0f, TitlesAdapter.GetBattleStartShieldRemaining(_wildCombatIdForTitles));
            }

            RefreshWildEffectiveStatsFromTitles();
        }
        catch (Exception ex)
        {
            BattleLogger.Log($"[Titles] OnBattleStart(wild) exception: {ex.Message}", LogScope.Battle);
        }

        // Unified stat/UI sync contract.
        RequestBattleStatRebuild(BattleStatRebuildReason.BattleStart, forceRebuildAdjusted: true);
    }

        // Status + Synergy logic moved to Core/BattleManager.Statuses.cs (Phase 4)

public void BeginBattle(MonsterDataSO wild, int level, Action<BattleResult> onEnded)
    {
        Begin(wild, level, onEnded, null, null);
    }

    // Normal entry point (Save-driven). Iron/custom callers should use the overload that accepts a roster provider + context.
    public void Begin(MonsterDataSO wild, int level, Action<BattleResult> onEnded)
    {
        Begin(wild, level, onEnded, null, null);
    }

    public void Begin(MonsterDataSO wild, int level, Action<BattleResult> onEnded, IBattleRosterProvider rosterProvider, IBattleContext battleContext)
    {
        _rosterProvider = rosterProvider;
        _battleContext = battleContext;
        _rules = (battleContext != null) ? battleContext.Rules : BattleRules.Default;

        // Hard enforcement: Iron battles must NEVER touch SaveManager team/owned.
        if (IronCareerRuntime.IsActive && rosterProvider == null)
        {
            Debug.LogError("[BattleManager] IronCareerRuntime.IsActive but Begin(...) was called without a rosterProvider. Forfeiting battle.");
            ForceEndBattleEarly(false);
            return;
        }

        var injectedTeam = (rosterProvider != null) ? rosterProvider.GetPlayerTeam() : null;
        bool usingInjected = (injectedTeam != null && injectedTeam.Count > 0);

        // Normal battles are Save-driven. Injected battles must NOT read SaveManager.
        var roster = usingInjected ? null : SaveManager.Data.team;

        if (usingInjected)
        {
            // OK
        }
        else
        {
            if (roster == null || roster.Count == 0) { ForceEndBattleEarly(false); return; }
        }

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

        ApplyHudRigForThisBattle();

        // Backgrounds: pick + apply immediately so the reveal anims show the correct scene.
        // Both player and wild use the same background, driven by the wild monster's type.
        TryApplyBattleBackgroundFromWild();

            // Titles routing id for wild (stable across battle)
        // IMPORTANT: must be synthetic (WILD::<...>) so it never collides with a real monster id.
        // In injected (Iron/custom) battles, prefer rosterProvider's wild combatantId so TitlesAdapter context matches.
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

        // Baseline adjusted stats for this encounter (level-scaled + threat scalar).
        wildBaseMaxHP = Mathf.Max(1f, wHpBase * encounterThreatScalar);
        wildBaseAttackPerTurn = Mathf.Max(1f, wAtkBase * encounterThreatScalar);

        // Effective values will be finalized after BattleStart titles are applied.
        wildMaxHP = wildBaseMaxHP;
        wildHP = wildMaxHP;
        wildAttackPerTurn = wildBaseAttackPerTurn;

        // IMPORTANT: clear any lingering icon tweens/alphas from the prior battle.
        if (feedback != null) feedback.ResetIconVisuals();
        HardResetIconVisual(playerIcon);
        HardResetIconVisual(wildIcon);

        // ─────────────────────────────────────────────────────────────
        // Shiny encounter state (spawn-time), driven by EncounterManager
        // ─────────────────────────────────────────────────────────────
        bool shinyWild = (EncounterManager.I != null) && EncounterManager.I.CurrentWildIsShiny;

        bool isAuto = _rules.allowAutoBattle && (EncounterManager.I != null) && EncounterManager.I.IsAutoMode;

        // Auto-battle hard-disable in sealed modes (Iron).
        ConfigureForAuto(isAuto);


        // Wild icon: use shiny icon if shiny encounter and one exists.
        if (wildIcon)
        {
	        	if (shinyWild && wildDef != null && wildDef.shinyIcon != null) wildIcon.sprite = wildDef.shinyIcon;
	        	else wildIcon.sprite = (wildDef != null) ? wildDef.icon : null;
            HardResetIconVisual(wildIcon);
        }


        // Wild name: MUST apply formatter so we literally see * and italics.
        // Ensure MonsterNameFormatter.Format returns "*<i>Name</i>*" when isShiny=true.
        if (wildNameText)
        {
	        	if (wildDef != null)
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

        // Status runtime (one-status-per-unit)
        teamStatus = new StatusType[teamCount];
        teamStatusTurns = new int[teamCount];
        teamStatusMagnitude = new float[teamCount];
        teamStatusPersistent = new bool[teamCount];

        // Iron carry safety: reset imported shield-carry flags each battle.
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

                // Core identity
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

                // Titles: locked per instance (local override). Also register battle context for synthetic ids.
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

        // Central battle stat pipeline.
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

        // Titles can change effective max HP; sync max caches and preserve HP% before first UI render.
        if (_stats != null) _stats.MarkDirtyAll();
        SyncEffectiveMaxHPFromStats(force: true);

        // Synergy-driven statuses apply once at battle start (Phase 3).
        // Deterministic. One-status-per-unit (no overwrites).
        ApplyBattleStartSynergies();

        ApplyActiveToUI();
        ClampAndPushActiveHP();
        RefreshBenchUI();

        // Booster system (optional)
        if (_rules.allowBoosters && BattleBoosterController.I != null)
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

    private void EndBattleRouted(bool victory, bool escaped = false)
    {
        if (IronCareerRuntime.IsActive)
            EndBattle_Iron(victory, escaped);
        else
            EndBattle(victory, escaped);
    }

    private void EndBattle_Iron(bool victory, bool escaped = false)
    {
        if (!inBattle) return;

        inBattle = false;
        SetIsPlayerTurn(false);

        // Iron: do NOT broadcast global battle state events (they can trigger rewards/UI flows).
        ConfigureForAuto(false);

        if (benchBtn1) benchBtn1.interactable = false;
        if (benchBtn2) benchBtn2.interactable = false;

        pendingAction = PlayerAction.None;
        defendActiveThisRound = false;
        wildDefendActiveThisRound = false;
        wildChargedNextAttack = false;
        ResetStatusIcons();

        if (turnCR != null) { StopCoroutine(turnCR); turnCR = null; }

        BattleCalc.ResetRng();
        _rng.ClearAll();
        float survived = Mathf.Max(0f, Time.unscaledTime - startTime);

        // Titles: end for all participants so per-battle stacks clear.
        try
        {
            if (teamTitleIds != null && activeIndex >= 0 && activeIndex < teamTitleIds.Length)
            {
                string tid = teamTitleIds[activeIndex];
                if (!string.IsNullOrEmpty(tid))
                    TitlesAdapter.OnBattleEnd(tid, victory, wildDef, wildLevel);
            }

            if (!string.IsNullOrEmpty(_wildCombatIdForTitles))
                TitlesAdapter.OnBattleEnd(_wildCombatIdForTitles, victory, wildDef, wildLevel);
        }
        catch (Exception ex)
        {
            BattleLogger.Log($"[Titles] OnBattleEnd(Iron) exception: {ex.Message}", LogScope.Battle);
        }

        // Build Iron snapshot (no rewards/persistence).
        ExtractIronCarryFromPlayerField(out var carryStatus, out var carryShield);

        var snap = new IronBattleOutcome
        {
            victory = victory,
            escaped = escaped,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = survived,
            turnsSurvived = _turnIndex,
            teamHP = (teamHP != null) ? (float[])teamHP.Clone() : null,
            teamMaxHP = (teamMaxHP != null) ? (float[])teamMaxHP.Clone() : null,

            // Player-only carryover fields (Phase 1)
            shieldHP = carryShield,
            playerFieldStatus = carryStatus,
        };

        _battleContext?.OnBattleResolved(snap);

        // Also invoke onEnd for local callers that still rely on BattleResult callback.
        var result = new BattleResult
        {
            victory = victory,
            escaped = escaped,
            creditsGained = 0,
            creditsBase = 0,
            creditsTitleBonus = 0,
            creditsMultiplier = 1f,
            growthCoresGained = 0,
            growthCoresBase = 0,
            growthCoresTitleBonus = 0,
            activeMonsterOwnedId = null,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = survived,
            critCount = _totalCritsThisBattle,
            turnsSurvived = _turnIndex,
            damageTaken = _totalDamageTakenThisBattle,
            damageDealt = _totalDamageDealtThisBattle,
            gotFirstHit = playerLandedFirstHitThisBattle
        };

        onEnd?.Invoke(result);
    }

    private IronFieldStatusSnapshot GetPlayerFieldStatusSnapshot()
    {
        // Iron carries a single primary player field-wide status between battles.
        // Current rule: player-only carry; field-wide single status; no between-battle ticking.
        // This snapshot is stored on the battle outcome and re-applied by the Iron run loop.
        var snap = new IronFieldStatusSnapshot
        {
            type = StatusType.None,
            turns = 0,
            magnitude = 0f,
            persistent = false
        };

        if (teamStatus == null || teamStatusTurns == null || teamStatusMagnitude == null || teamStatusPersistent == null)
            return snap;

        int bestIdx = -1;
        int bestTurns = -1;

        for (int i = 0; i < teamStatus.Length; i++)
        {
            var t = teamStatus[i];
            if (t == StatusType.None) continue;

            int turns = (i < teamStatusTurns.Length) ? teamStatusTurns[i] : 0;
            if (turns > bestTurns)
            {
                bestTurns = turns;
                bestIdx = i;
            }
        }

        if (bestIdx >= 0)
        {
            snap.type = teamStatus[bestIdx];
            snap.turns = (bestIdx < teamStatusTurns.Length) ? teamStatusTurns[bestIdx] : 0;
            snap.magnitude = (bestIdx < teamStatusMagnitude.Length) ? teamStatusMagnitude[bestIdx] : 0f;
            snap.persistent = (bestIdx < teamStatusPersistent.Length) && teamStatusPersistent[bestIdx];
        }

        return snap;
    }

    // ─────────────────────────────────────────────────────────────
    // Iron carry-over helpers (Phase 1)
    // Player-only carry. Field-wide single primary status. No between-battle ticking.
    // ShieldHP carries per slot. Imported shields must NOT be auto-removed by Shielded expiry logic.
    // ─────────────────────────────────────────────────────────────
    public void ApplyIronCarryToPlayerField(IronFieldStatusSnapshot snap, float[] shieldBySlot)
    {
        if (!IronCareerRuntime.IsActive) return;

        // Restore field-wide status onto all occupied team slots.
        if (teamStatus != null && teamStatusTurns != null && teamStatusMagnitude != null && teamStatusPersistent != null)
        {
            for (int i = 0; i < teamStatus.Length; i++)
            {
                if (teamDefs != null && i < teamDefs.Length && teamDefs[i] == null) continue;

                teamStatus[i] = snap.type;
                if (i < teamStatusTurns.Length) teamStatusTurns[i] = snap.turns;
                if (i < teamStatusMagnitude.Length) teamStatusMagnitude[i] = snap.magnitude;
                if (i < teamStatusPersistent.Length) teamStatusPersistent[i] = snap.persistent;
            }

            RefreshPrimaryStatusUI();
        }

        // Restore carried shields (player-only).
        if (shieldHP != null && shieldBySlot != null)
        {
            EnsureShieldGrantPools();

            int n = Mathf.Min(shieldHP.Length, shieldBySlot.Length);
            for (int i = 0; i < n; i++)
            {
                shieldHP[i] = Mathf.Max(0f, shieldBySlot[i]);

                // Imported carry should never be removed by grant-pool subtraction.
                if (_shieldedGrantTeam != null && i < _shieldedGrantTeam.Length)
                    _shieldedGrantTeam[i] = 0f;

                if (_ironShieldCarrySlots != null && i < _ironShieldCarrySlots.Length)
                    _ironShieldCarrySlots[i] = (shieldHP[i] > 0f);
            }

            PushHPBars();
        }
    }

    public void ExtractIronCarryFromPlayerField(out IronFieldStatusSnapshot snap, out float[] shieldBySlot)
    {
        snap = GetPlayerFieldStatusSnapshot();
        shieldBySlot = (shieldHP != null) ? (float[])shieldHP.Clone() : null;
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
                string leadId = teamIds[activeIndex];
                float cm = _cachedCreditMult;
                Debug.Log($"[BattleManager] Title mult for lead '{leadId}': {cm} (basecredits={basecredits}) [cached]");
                if (cm > 0f)
                {
                    finalcredits = Mathf.Max(0, Mathf.RoundToInt(basecredits * cm));
                    creditTitleBonus = Mathf.Max(0, finalcredits - basecredits);
                    Debug.Log($"[BattleManager] finalcredits={finalcredits}, creditTitleBonus={creditTitleBonus}");
                }
            }

            if (finalcredits < 0) finalcredits = 0;
        }

        int baseCores = Mathf.Max(1, 2 + wildLevel);
        int growthCoreBaseAfterShiny = 0;
        int growthCoreTitleBonus = 0;
        int growthCoreTotal = 0;

        var data = SaveManager.Data;

         if (victory && !escaped)
        {
            var m = (teamOwnedEffective != null && activeIndex >= 0 && activeIndex < teamOwnedEffective.Length)
                ? teamOwnedEffective[activeIndex]
                : ((data != null && data.team != null && activeIndex >= 0 && activeIndex < data.team.Count) ? data.team[activeIndex] : default);

            float shinyMul = ShinySystems.TrainingXpMult(m);
            int baseAfterShiny = Mathf.RoundToInt(baseCores * shinyMul);
            growthCoreBaseAfterShiny = Mathf.Max(0, baseAfterShiny);

            float titleCoreMul = 1f;
            if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
                titleCoreMul = Mathf.Max(0f, TitlesAdapter.GetGrowthCoreMultOnVictory(teamTitleIds[activeIndex], wildDef, wildLevel));

            int growthCoreAfterTitles = Mathf.Max(0, Mathf.RoundToInt(baseAfterShiny * titleCoreMul));

            float globalMul = 1f;
            if (GameBalance.TryGet(out var bal))
                globalMul = Mathf.Max(0f, bal.xpGainMultiplier);

            growthCoreTotal = Mathf.Max(0, Mathf.RoundToInt(growthCoreAfterTitles * globalMul));

            growthCoreTitleBonus = Mathf.Max(0, growthCoreAfterTitles - growthCoreBaseAfterShiny);

            if (growthCoreTotal > 0)
                ResourceManager.I?.Add(ResourceType.GrowthCore, growthCoreTotal);

            BattleLogger.Log($"Gained {growthCoreTotal} Growth Cores.", LogScope.Battle);
        }

        var teamList = data != null && data.team != null ? data.team : new List<OwnedMonsterData>();
        var ownedList = data != null && data.owned != null ? data.owned : new List<OwnedMonsterData>();
        long nowUnix = SaveManager.NowUnix();

        // If the player has a preferred variant (shiny/non-shiny) for a given monsterId,
        // battles may have been simulated using that preferred OwnedMonsterData (ownedUID).
        // Ensure the team list points at the same owned copy so HP/progression writes back to the correct variant.
        if (teamOwnedUidEffective != null && ownedList != null && teamList != null)
        {
            var uidMap = new Dictionary<string, OwnedMonsterData>(StringComparer.Ordinal);
            for (int j = 0; j < ownedList.Count; j++)
            {
                var o = ownedList[j];
                if (o == null) continue;
                if (string.IsNullOrEmpty(o.ownedUID)) continue;
                if (!uidMap.ContainsKey(o.ownedUID))
                    uidMap.Add(o.ownedUID, o);
            }

            int max = Mathf.Min(teamCount, Mathf.Min(teamList.Count, teamOwnedUidEffective.Length));
            for (int i = 0; i < max; i++)
            {
                string uid = teamOwnedUidEffective[i];
                if (string.IsNullOrEmpty(uid)) continue;

                if (uidMap.TryGetValue(uid, out var preferredOwned) && preferredOwned != null)
                {
                    // Swap the team slot to the preferred owned copy (same monsterId).
                    teamList[i] = preferredOwned;
                }
            }
        }

        for (int i = 0; i < teamCount && i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
            int hp = Mathf.CeilToInt(Mathf.Max(0f, teamHP[i]));

            // AUTO-REMOVE DEAD TEAM MEMBERS:
            // When a team monster hits 0 HP in battle, it is automatically removed from the player's team.
            // We clear the slot (instead of removing list entries) to preserve stable UI slot indices.
            if (hp <= 0)
            {
                // IMPORTANT:
                // Even though we auto-remove dead team members from the TEAM list,
                // we must still persist the KO state back to the OWNED instance so:
                // - cooldown timers can show (OwnedMonsterListItemUI)
                // - battle eligibility correctly blocks 0 HP monsters
                // - healing services can find + heal the KO'd monster

                // Write KO back to owned list using ownedUID first (strong match).
                if (ownedList != null)
                {
                    int ownedIdx = -1;

                    if (!string.IsNullOrEmpty(t.ownedUID))
                    {
                        for (int j = 0; j < ownedList.Count; j++)
                        {
                            var o = ownedList[j];
                            if (o != null && !string.IsNullOrEmpty(o.ownedUID) && o.ownedUID == t.ownedUID)
                            {
                                ownedIdx = j;
                                break;
                            }
                        }
                    }

                    // Fallback: monsterId only if unique.
                    if (ownedIdx < 0)
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
                        if (count == 1) ownedIdx = singleIdx;
                    }

                    if (ownedIdx >= 0 && ownedIdx < ownedList.Count)
                    {
                        var o = ownedList[ownedIdx];
                        if (o != null)
                        {
                            // Centralized HP contract (no Save() here; battle end saves once)
                            SaveManager.SetOwnedMonsterHP(o.ownedUID, 0, stampLastHpUnix: true, nowUnix: nowUnix, save: false, fireEvents: false);
                            // Refresh local list entry from SaveManager in case of clamping/normalization
                            var refreshed = SaveManager.GetOwnedByUid(o.ownedUID);
                            if (refreshed != null) ownedList[ownedIdx] = refreshed;
                        }
                    }
                }

                // Clear slot
                teamList[i] = new OwnedMonsterData { monsterId = null, currentHP = 0 };
                continue;
            }

            // Centralized HP contract: update team slot (syncs owned via ownedUID / unique monsterId).
            SaveManager.SetTeamSlotHP(i, hp, stampLastHpUnix: true, nowUnix: nowUnix, save: false, fireEvents: false);
            // teamList references SaveManager.Data.team, so it is already updated in-place.
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
                                        if (!string.IsNullOrEmpty(o.ownedUID))
                    {
                        SaveManager.SetOwnedMonsterHP(o.ownedUID, Mathf.Max(0, t.currentHP), stampLastHpUnix: true, nowUnix: nowUnix, save: false, fireEvents: false);
                        var refreshed = SaveManager.GetOwnedByUid(o.ownedUID);
                        if (refreshed != null) ownedList[idx] = refreshed;
                    }
                    else
                    {
                        // No ownedUID on owned entry: rely on team-slot HP contract fallback (unique monsterId)
                        // to propagate HP safely without cross-contamination.
                        SaveManager.SetTeamSlotHP(i, Mathf.Max(0, t.currentHP), stampLastHpUnix: true, nowUnix: nowUnix, save: false, fireEvents: false);
                    }
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
            creditsBase = basecredits,
            creditsTitleBonus = creditTitleBonus,
            creditsMultiplier = _cachedCreditMult,

            growthCoresGained = growthCoreTotal,
            growthCoresBase = growthCoreBaseAfterShiny,
            growthCoresTitleBonus = growthCoreTitleBonus,

            activeMonsterOwnedId = (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length) ? teamIds[activeIndex] : null,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = survived,
            critCount = _totalCritsThisBattle,
            turnsSurvived = _turnIndex,
            damageTaken = _totalDamageTakenThisBattle,
            damageDealt = _totalDamageDealtThisBattle,
            gotFirstHit = playerLandedFirstHitThisBattle
        };

        Debug.Log($"[BattleManager] BattleResult: base={result.creditsBase}, bonus={result.creditsTitleBonus}, totalPreScale={result.creditsGained}, active={result.activeMonsterOwnedId}");

        if (!victory && !escaped && AutoResolveActive)
        {
            EncounterManager.I?.NotifyAuto_TeamKO();
        }

        SetPostBattleWinnerVisible(victory, escaped);

        // Titles: make sure BOTH combatants end the session so per-battle stacks/buffs reset.
        // TitleManager registers multiple participants (player + wild) on battle start.
        // If we only call OnBattleEnd for the player, the wild participant remains registered
        // and the session never fully clears.
        try
        {
            if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            {
                string ownedId = GetTeamTitleIdSafe(activeIndex);
                if (!string.IsNullOrEmpty(ownedId))
                    TitlesAdapter.OnBattleEnd(ownedId, victory, wildDef, wildLevel);
            }

            if (!string.IsNullOrEmpty(_wildCombatIdForTitles))
                TitlesAdapter.OnBattleEnd(_wildCombatIdForTitles, victory, wildDef, wildLevel);
        }
        catch (Exception ex)
        {
            BattleLogger.Log($"[Titles] OnBattleEnd exception: {ex.Message}", LogScope.Battle);
        }

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
        BattleLogger.AddKeyMoment($"SWAP: {GetName(activeIndex)}");
        Emit(BattleEvent.ActionQueued(BattleSide.Player, "Swap"));

        if (!HasBattleEventConsumers && feedback)
            feedback.PlayActionQueued(
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


    private TitleStatMods GetTitleModsForIndex(int idx)
    {
        if (teamIds != null && idx >= 0 && idx < teamIds.Length && !string.IsNullOrEmpty(teamIds[idx]))
            return TitlesAdapter.GetBattleStatMods(teamTitleIds[idx]);
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
        string ownedId = GetTeamTitleIdSafe(idx);

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
        if (_stats != null)
            return Mathf.Max(1f, _stats.GetEffectivePlayer(idx).maxHP);

        if (teamMaxHP == null || idx < 0 || idx >= teamMaxHP.Length) return 1f;
        return GetActiveMaxHP(teamMaxHP[idx], idx);
    }

    

    /// <summary>
    /// When max HP changes due to titles/boosters, preserve current HP percent and update max caches.
    /// Call this whenever BattleStatsSystem becomes dirty (battle start, turn advance, combatant turn end).
    /// </summary>
    private void SyncEffectiveMaxHPFromStats(bool force = false)
    {
        if (_stats == null) return;

        // Wild
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

        // Player slots
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

    // ─────────────────────────────────────────────────────────────────────────
    // Conditional Title feedback (battle textbox + BattleLogger)
    // ─────────────────────────────────────────────────────────────────────────

    // Tracks the last conditional-mod snapshot so we only notify on changes.
    // We keep this in BattleManager so it survives across partials and avoids per-frame allocations.
    private bool _condModsInit;
    private int _condModsHashLast;
    private TitleStatMods _condModsLast;

    private static int HashTitleStatMods(in TitleStatMods m)
    {
        unchecked
        {
            int h = 17;
            h = (h * 31) ^ BitConverter.SingleToInt32Bits(m.hpPct);
            h = (h * 31) ^ BitConverter.SingleToInt32Bits(m.atkPct);
            h = (h * 31) ^ BitConverter.SingleToInt32Bits(m.defPct);
            h = (h * 31) ^ BitConverter.SingleToInt32Bits(m.spdPct);
            h = (h * 31) ^ m.atkFlat;
            h = (h * 31) ^ m.defFlat;
            h = (h * 31) ^ m.spdFlat;
            return h;
        }
    }

    private static bool HasAnyConditional(in TitleStatMods m)
    {
        const float EPS = 0.0001f;
        return
            Mathf.Abs(m.hpPct)  > EPS ||
            Mathf.Abs(m.atkPct) > EPS ||
            Mathf.Abs(m.defPct) > EPS ||
            Mathf.Abs(m.spdPct) > EPS ||
            m.atkFlat != 0 || m.defFlat != 0 || m.spdFlat != 0;
    }

    private static string BuildCondSummaryShort(in TitleStatMods m)
    {
        List<string> parts = null;

        void Add(string s)
        {
            parts ??= new List<string>(4);
            parts.Add(s);
        }

        bool anyUp = false;
        bool anyDown = false;

        if (m.atkPct > 0f || m.atkFlat > 0) { Add("ATK↑"); anyUp = true; }
        else if (m.atkPct < 0f || m.atkFlat < 0) { Add("ATK↓"); anyDown = true; }

        if (m.defPct > 0f || m.defFlat > 0) { Add("DEF↑"); anyUp = true; }
        else if (m.defPct < 0f || m.defFlat < 0) { Add("DEF↓"); anyDown = true; }

        if (m.spdPct > 0f || m.spdFlat > 0) { Add("SPD↑"); anyUp = true; }
        else if (m.spdPct < 0f || m.spdFlat < 0) { Add("SPD↓"); anyDown = true; }

        if (m.hpPct > 0f) { Add("HP↑"); anyUp = true; }
        else if (m.hpPct < 0f) { Add("HP↓"); anyDown = true; }

        if (parts == null || parts.Count == 0) return null;

        string prefix = anyUp && !anyDown ? "Title Boost" : (anyDown && !anyUp ? "Title Drag" : "Title Shift");
        return $"{prefix}: {string.Join(" ", parts)}";
    }

    private static string BuildCondSummaryMathy(in TitleStatMods m)
    {
        return $"COND hpPct={m.hpPct:0.###} atkPct={m.atkPct:0.###} defPct={m.defPct:0.###} spdPct={m.spdPct:0.###} atkFlat={m.atkFlat} defFlat={m.defFlat} spdFlat={m.spdFlat}";
    }

    private bool TryConsumeConditionalTitleFeedback(out TitleStatMods mods, out string battleLine, out string logLine)
    {
        mods = default;
        battleLine = null;
        logLine = null;

        string ownedId = GetTeamTitleIdSafe(activeIndex);
        if (string.IsNullOrEmpty(ownedId)) return false;

        float activeHp = Mathf.Max(0f, GetActivePlayerCurHP());
        float baseMax  = (teamMaxHP != null && activeIndex >= 0 && activeIndex < teamMaxHP.Length) ? Mathf.Max(1f, teamMaxHP[activeIndex]) : 1f;
        float maxHp    = Mathf.Max(1f, GetActiveMaxHP(baseMax, activeIndex));
        float hpPct    = Mathf.Clamp01(activeHp / maxHp);
        int alliesAlive = GetAlliesAliveNotIncludingActive();
        int winStreak = GetWinStreakSafe();

        TitleStatMods cond = TitlesAdapter.GetConditionalBattleMods(ownedId, hpPct, alliesAlive, winStreak);
        mods = cond;
        int hash = HashTitleStatMods(cond);

        if (!_condModsInit)
        {
            _condModsInit = true;
            _condModsLast = cond;
            _condModsHashLast = hash;

            if (HasAnyConditional(cond))
            {
                battleLine = BuildCondSummaryShort(cond);
                logLine = BuildCondSummaryMathy(cond);
                return !string.IsNullOrEmpty(battleLine);
            }

            return false;
        }

        if (hash == _condModsHashLast)
            return false;

        bool had = HasAnyConditional(_condModsLast);
        bool has = HasAnyConditional(cond);

        _condModsLast = cond;
        _condModsHashLast = hash;

        if (!had && !has) return false;

        if (!has)
        {
            battleLine = "Title Boost ended";
            logLine = "COND ended";
            return true;
        }

        battleLine = BuildCondSummaryShort(cond);
        logLine = BuildCondSummaryMathy(cond);
        return !string.IsNullOrEmpty(battleLine);
    }

    private void ResetConditionalTitleFeedbackCache()
    {
        _condModsInit = false;
        _condModsHashLast = 0;
        _condModsLast = default;
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
            isBattle = true,
            ownedId = GetTeamTitleIdSafe(activeIndex)
        };
        return ctx;
    }

    internal TitleContext BuildTitleContextForWild()
    {
        float max = Mathf.Max(1f, wildMaxHP);
        float hp01 = max > 0.01f ? Mathf.Clamp01(wildHP / max) : 0f;

        return new TitleContext
        {
            selfHp01 = hp01,
            alliesAlive = 0,
            winStreak = 0,
            isBattle = true,
            ownedId = _wildCombatIdForTitles
        };
    }

    private void RefreshWildEffectiveStatsFromTitles()
    {
        if (!wildDef) return;
        if (string.IsNullOrEmpty(_wildCombatIdForTitles)) return;

        // Preferred path: use centralized stat pipeline so wild titles affect ALL stats consistently.
        // This also ensures conditional titles that depend on HP% evaluate against the effective max HP.
        if (_stats != null)
        {
            // Max HP can change from titles/conditionals; preserve HP%.
            SyncEffectiveMaxHPFromStats();

            // Keep legacy fields in sync for older code paths that still read them.
            wildAttackPerTurn = Mathf.Max(1f, _stats.GetEffectiveWild().atk);
            return;
        }

        // Fallback: legacy title evaluation (HP/ATK only). Kept for safety when _stats is unavailable.
        float prevMax = Mathf.Max(1f, wildMaxHP);
        float hp01 = prevMax > 0.01f ? Mathf.Clamp01(wildHP / prevMax) : 0f;

        var wCtx = BuildTitleContextForWild();

        float wMaxF = TitlesAdapter.GetStatValue(_wildCombatIdForTitles, wildDef, wildLevel, "HP", wCtx, wildBaseMaxHP);
        if (!float.IsNaN(wMaxF) && !float.IsInfinity(wMaxF))
            wildMaxHP = Mathf.Max(1f, wMaxF);

        wildHP = Mathf.Clamp(wildMaxHP * hp01, 0f, wildMaxHP);

        float wAtkF = TitlesAdapter.GetStatValue(_wildCombatIdForTitles, wildDef, wildLevel, "Attack", wCtx, wildBaseAttackPerTurn);
        if (!float.IsNaN(wAtkF) && !float.IsInfinity(wAtkF))
            wildAttackPerTurn = Mathf.Max(1f, wAtkF);
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

        string ownedId = GetTeamTitleIdSafe(activeIndex);
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

        // Prefer the effective owned (preferred shiny/non-shiny variant) used for the current battle.
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
        // Choose active rig
        _hudRigActive = (_hudRigOverride != null) ? _hudRigOverride : defaultHudRig;

        if (_hudRigActive == null)
        {
            Debug.LogWarning("[BattleManager] No HUD rig assigned (defaultHudRig missing). Using existing serialized references.");
            return;
        }

        // Point BattleManager UI refs at the active rig
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

        // Use your existing UI override system too (keeps things consistent)
        SetUIOverride(_hudRigActive.feedback, _hudRigActive.battleTextBox, _hudRigActive.bottomToggle);
    }
}
