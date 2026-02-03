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


public class BattleManager : MonoBehaviour
{
    private enum PlayerAction { None, Attack, Defend, Focus, Swap, Run }
    private enum EnemyAction { Attack, Defend, Focus, Run }

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
    private Action<BattleResult> onEnd;
    private float startTime;
    private Coroutine turnCR;

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
    private System.Random _enemyRng = new System.Random();

    private int runAttempts = 0;
    private bool wildChargedNextAttack = false;

    private int _totalCritsThisBattle = 0;
    private int _totalDamageTakenThisBattle = 0;
    private int _totalDamageDealtThisBattle = 0;

    private static readonly Color StatNeutral = Color.white;
    private static readonly Color StatBuff = new Color(0.35f, 1f, 0.35f);
    private static readonly Color StatNerf = new Color(1f, 0.35f, 0.35f);

    private bool _playerQueuedSwap;
    private int _playerSwapToIndex = -1;

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
        GameEvents.OnBattleStateChanged?.Invoke();
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
    yield return Say($"{displayName} KO'ed!");
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

    private IEnumerator Co_RevealPanelsThenStart(CanvasGroup wildCG, CanvasGroup playerCG, float duration)
    {
        if (feedback != null)
            yield return feedback.Co_RevealPanels(wildCG, playerCG, duration);
        else
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, duration));

        if (wildCG) { wildCG.alpha = 1f; wildCG.blocksRaycasts = true; wildCG.interactable = true; }
        if (playerCG) { playerCG.alpha = 1f; playerCG.blocksRaycasts = true; playerCG.interactable = true; }

        yield return Co_StartBattleNow();
    }

    private IEnumerator Co_StartBattleNow()
    {
        _turnIndex = 0;
        inBattle = true;
        startTime = Time.unscaledTime;

        var vsName = wildDef ? $"{wildDef.displayName} (Lv {wildLevel})" : "Unknown";
        BattleLogger.BeginBattle(vsName);

        if (wildDef)
            BattleLogger.Log($"A wild {wildDef.displayName} (Lv {wildLevel}) appeared!", LogScope.Battle);
        else
            BattleLogger.Log("A wild foe appeared!", LogScope.Battle);

        string personalityLabel = GetWildPersonalityLabel();
        if (!string.IsNullOrEmpty(personalityLabel) && wildDef && wildDef.Personality != null)
        {
            if (!string.IsNullOrEmpty(wildDef.Personality.description))
                BattleLogger.Log($"Personality: {personalityLabel} – {wildDef.Personality.description}", LogScope.Battle);
            else
                BattleLogger.Log($"Personality: {personalityLabel}.", LogScope.Battle);
        }

        PostBattleSummaryManager.I?.NotifyBattleStart();

        if (activeIndex >= 0 && teamIds != null && activeIndex < teamIds.Length)
            TitlesAdapter.OnBattleStart(teamIds[activeIndex], wildDef, wildLevel);

        Debug_LogActiveTitlesSnapshot("BattleStart");

        UpdateHPTextUI();

        ResetStatusIcons();
        RefreshStatusIconsFromState();

        if (turnCR != null) StopCoroutine(turnCR);
        turnCR = StartCoroutine(TurnLoop());
        yield break;
    }

    private IEnumerator TurnLoop()
    {
        int round = 0;
        yield return Wait(0.4f);

        while (inBattle)
        {
            bool swappedFromKO = false;

            if (teamHP[activeIndex] <= 0.01f)
            {
                if (!AutoSwapToAlive())
                {
                    BattleLogger.Log("Your team is unable to battle!", LogScope.Battle);
                    EndBattle(false);
                    break;
                }
                swappedFromKO = true;
            }

            // Apply any stored guard shields (from last round)
            ApplyPendingGuardShieldForActive();
            ApplyPendingGuardShieldForWild();

            // New round: clear defend stances (they are "this round only")
            defendActiveThisRound = false;
            wildDefendActiveThisRound = false;

            // Sync status icons after round reset + shield application
            RefreshStatusIconsFromState();

            BattleLogger.Log($"— Round {round} —", LogScope.Battle);
            yield return Wait(beginRoundDelay);

            _turnIndex++;
            TitlesAdapter.OnTurnAdvanced(_turnIndex);
            GameEvents.RaiseBattleStatsChanged();

            if (debugTitles && debugTitlesEveryTurn)
                Debug_LogActiveTitlesSnapshot("TurnAdvanced");

            if (swappedFromKO)
            {
                ClampAndPushActiveHP();
                ApplyActiveToUI();
                RefreshBenchUI();

                // Swap can change which slot has charge queued
                RefreshStatusIconsFromState();
            }

            if (IsWildKO() || IsTeamKO())
            {
                if (CheckEnd()) break;
                round++;
                continue;
            }

            int pSpeedBase = GetProgressionTotalSPDForIndex(activeIndex);

            var jSpeed = (jobCtx != null && activeIndex >= 0 && activeIndex < jobCtx.Length) ? jobCtx[activeIndex] : null;
            if (jSpeed != null && jSpeed.speedBuffTurns > 0 && jSpeed.speedBonusPctFirstTurns != 0f)
                pSpeedBase = Mathf.Max(1, Mathf.RoundToInt(pSpeedBase * (1f + jSpeed.speedBonusPctFirstTurns)));

            var titleCtx = BuildTitleContextForActive();
            float pSpeedAfterTitlesF = TitlesAdapter.GetStatValue(
                teamIds[activeIndex],
                teamDefs[activeIndex],
                teamLevels[activeIndex],
                "SPD",
                titleCtx,
                pSpeedBase
            );
            int pSpeedAfterTitles = Mathf.Max(1, Mathf.RoundToInt(pSpeedAfterTitlesF));

            var cmods = GetConditionalModsForActive();
            float pSpeedWithConditionalsF =
                (pSpeedAfterTitles + Mathf.Max(0, cmods.spdFlat)) *
                (1f + Mathf.Max(0f, cmods.spdPct));
            int pSpeedWithConditionals = Mathf.Max(1, Mathf.RoundToInt(pSpeedWithConditionalsF));

            int tempSPDFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0;
            int pSpeed = Mathf.Max(1, pSpeedWithConditionals + Mathf.Max(0, tempSPDFlat));

            int wSpeed = BattleCalc.CalcSpeed(wildDef, wildLevel);

            bool playerFirst;
            if (pSpeed > wSpeed) playerFirst = true;
            else if (pSpeed < wSpeed) playerFirst = false;
            else playerFirst = UnityEngine.Random.value < 0.5f;

            EnemyAction wildChoice = ChooseEnemyAction();

            if (playerFirst)
            {
                if (wildChoice == EnemyAction.Defend)
                {
                    ApplyWildDefendStance();
                    RefreshStatusIconsFromState();
                }

                if (!IsWildKO() && !IsTeamKO())
                {
                    if (manualTurns) yield return WaitForPlayerChoiceAndResolve();
                    else yield return PlayerTurn();

                    RefreshStatusIconsFromState();

                    if (CheckEnd()) break;
                    yield return Wait(hitPause);

                    if (!IsTeamKO() && teamHP[activeIndex] <= 0.01f)
                    {
                        AutoSwapToAlive();
                        RefreshStatusIconsFromState();
                    }
                }

                if (!IsWildKO() && !IsTeamKO())
                {
                    if (wildChoice != EnemyAction.Defend)
                    {
                        yield return EnemyTurn(wildChoice);

                        RefreshStatusIconsFromState();

                        if (CheckEnd()) break;
                        yield return Wait(hitPause);
                    }
                }
            }
            else
            {
                if (!IsWildKO() && !IsTeamKO())
                {
                    PlayerAction queuedChoice = PlayerAction.Attack;

                    if (manualTurns)
                    {
                        SetIsPlayerTurn(true);
                        pendingAction = PlayerAction.None;

                        float choiceStart = Time.unscaledTime;

                        while (inBattle && pendingAction == PlayerAction.None)
                        {
                            if (enableAutoQueueAttack && autoQueueAttackAfterSeconds > 0f && !_narrationLock)
                            {
                                if (Time.unscaledTime - choiceStart >= autoQueueAttackAfterSeconds)
                                {
                                    pendingAction = PlayerAction.Attack;
                                    BattleLogger.Log($"[Battle] Failsafe: auto-queued Attack after {autoQueueAttackAfterSeconds:0}s idle.", LogScope.Battle);
                                    break;
                                }
                            }

                            yield return null;
                        }

                        queuedChoice = pendingAction;
                        pendingAction = PlayerAction.None;
                        GameEvents.OnBattleStateChanged?.Invoke();
                        SetIsPlayerTurn(false);

                        if (queuedChoice == PlayerAction.Defend)
                        {
                            string name = GetName(activeIndex);
                            bool success = RollDefendSuccess();

                            defendActiveThisRound = success;

                            if (feedback)
                            {
                                feedback.PlayDefendResult(BattleFeedbackManager.BattleFeedbackSide.Player, success);
                            }

                            // Defend icon should only show if success
                            RefreshStatusIconsFromState();

                            if (success)
                            {
                                BattleLogger.Log($"{name} is defending.", LogScope.Battle);
                                BattleLogger.Log($"{name} will reduce the next hit and convert it into a shield for the following round.", LogScope.Battle);
                            }
                            else
                            {
                                BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
                            }
                        }
                        else
                        {
                            ResetDefendStreak();
                            defendActiveThisRound = false;
                            RefreshStatusIconsFromState();
                        }
                    }

                    // ─────────────────────────────────────────────────────────
                    // Swap priority (Pokemon-style):
                    // If the player queued a Swap during the enemy-first branch,
                    // resolve the swap BEFORE the wild executes its action so the
                    // incoming hit targets the swapped-in monster.
                    // (Swap consumes the player's action for the round.)
                    // ─────────────────────────────────────────────────────────
                    if (manualTurns && queuedChoice == PlayerAction.Swap)
                    {
                        // If the current active was KO'ed somehow before swap resolution,
                        // the queued action is lost (handled below after enemy turn).
                        if (teamHP[activeIndex] > 0.01f)
                        {
                            ResetDefendStreak();
                            ResolveQueuedSwap();
                            RefreshStatusIconsFromState();
                        }

                        // Swap consumes the turn.
                        queuedChoice = PlayerAction.None;
                    }

                    // In the enemy-first branch, if the wild is defending this round,
                    // apply the defend stance BEFORE its action would execute.
                    // (We do NOT call EnemyTurn(Defend) to avoid double-rolling.)
                    if (wildChoice == EnemyAction.Defend)
                    {
                        ApplyWildDefendStance();
                        RefreshStatusIconsFromState();
                    }
                    else
                    {
                        yield return EnemyTurn(wildChoice);
                    }

                    // Enemy turn can set defend/charge/consume charge
                    RefreshStatusIconsFromState();

                    if (CheckEnd()) break;
                    yield return Wait(hitPause);

                    // If the wild KO'ed our active slot, we must auto-swap (if possible) and the queued action is lost.
                    if (!IsTeamKO() && teamHP[activeIndex] <= 0.01f)
                    {
                        AutoSwapToAlive();
                        queuedChoice = PlayerAction.None;
                        RefreshStatusIconsFromState();
                    }

                    if (!IsWildKO() && !IsTeamKO())
                    {
                        if (manualTurns)
                        {
                            switch (queuedChoice)
                            {
                                case PlayerAction.Attack:
                                    // If the active slot was KO'ed by the wild acting first, the player's queued action is lost.
                                    if (teamHP[activeIndex] > 0.01f)
                                        yield return PlayerTurn();
                                    RefreshStatusIconsFromState();
                                    break;

                                case PlayerAction.Focus:
                                    {
                                        // If the active slot was KO'ed by the wild acting first, the player's queued action is lost.
                                        if (teamHP[activeIndex] <= 0.01f)
                                        {
                                            RefreshStatusIconsFromState();
                                            break;
                                        }

                                        ResetDefendStreak();

                                        if (chargedNextAttack != null &&
                                            activeIndex >= 0 &&
                                            activeIndex < chargedNextAttack.Length)
                                        {
                                            chargedNextAttack[activeIndex] = true;
                                        }

                                        BattleLogger.Log($"{GetName(activeIndex)} is charging.", LogScope.Battle);
                                        BattleLogger.Log($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.", LogScope.Battle);

                                        if (feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Player,
                                            BattleFeedbackManager.BattleFeedbackAction.Focus
                                        );

                                        RefreshStatusIconsFromState();
                                        break;
                                    }

                                case PlayerAction.Swap:
                                    {
                                        // If the active slot was KO'ed by the wild acting first, the player's queued action is lost.
                                        if (teamHP[activeIndex] <= 0.01f)
                                        {
                                            RefreshStatusIconsFromState();
                                            break;
                                        }

                                        ResetDefendStreak();
                                        ResolveQueuedSwap();
                                        RefreshStatusIconsFromState();
                                        break;
                                    }

                                case PlayerAction.Run:
                                    {
                                        // If the active slot was KO'ed by the wild acting first, the player's queued action is lost.
                                        if (teamHP[activeIndex] <= 0.01f)
                                        {
                                            RefreshStatusIconsFromState();
                                            break;
                                        }

                                        ResetDefendStreak();

                                        float chance = ComputeRunChance();
                                        bool escaped = UnityEngine.Random.value < chance;

                                        string name = GetName(activeIndex);

                                        if (feedback) feedback.PlayActionQueued(
                                            BattleFeedbackManager.BattleFeedbackSide.Player,
                                            BattleFeedbackManager.BattleFeedbackAction.Run
                                        );

                                        // Run does not affect guard/charge, but keep icons correct anyway
                                        RefreshStatusIconsFromState();

                                        if (escaped)
                                        {
                                            BattleLogger.Log($"{name} has fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                                            EndBattle(false, true);
                                            yield break;
                                        }
                                        else
                                        {
                                            runAttempts++;
                                            BattleLogger.Log($"Couldn't escape! (Run chance was {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                                        }
                                        break;
                                    }

                                case PlayerAction.Defend:
                                default:
                                    // Defend is resolved before the wild acts in the enemy-first branch.
                                    break;
                            }
                        }
                        else
                        {
                            yield return PlayerTurn();
                            RefreshStatusIconsFromState();
                        }

                        if (CheckEnd()) break;
                        yield return Wait(hitPause);
                    }
                }
            }

            if (!IsWildKO() && !IsTeamKO())
            {
                if (jobCtx != null && activeIndex >= 0 && activeIndex < jobCtx.Length && jobCtx[activeIndex] != null)
                {
                    if (jobCtx[activeIndex].speedBuffTurns > 0) jobCtx[activeIndex].speedBuffTurns--;
                    if (jobCtx[activeIndex].critBuffTurns > 0) jobCtx[activeIndex].critBuffTurns--;
                    if (jobCtx[activeIndex].critResistBuffTurns > 0) jobCtx[activeIndex].critResistBuffTurns--;
                    if (jobCtx[activeIndex].dmgReduceBuffTurns > 0) jobCtx[activeIndex].dmgReduceBuffTurns--;
                }

                yield return Wait(endRoundDelay);
            }

            defendActiveThisRound = false;
            wildDefendActiveThisRound = false;
            RefreshStatusIconsFromState();

            round++;
        }

        turnCR = null;
    }


    private IEnumerator WaitForPlayerChoiceAndResolve()
    {
        SetIsPlayerTurn(true);

        float choiceStart = Time.unscaledTime;

        while (inBattle && pendingAction == PlayerAction.None)
        {
            if (enableAutoQueueAttack && autoQueueAttackAfterSeconds > 0f && !_narrationLock)
            {
                if (Time.unscaledTime - choiceStart >= autoQueueAttackAfterSeconds)
                {
                    pendingAction = PlayerAction.Attack;
                    BattleLogger.Log($"[Battle] Failsafe: auto-queued Attack after {autoQueueAttackAfterSeconds:0}s idle.", LogScope.Battle);
                    break;
                }
            }

            yield return null;
        }

        var choice = pendingAction;
        pendingAction = PlayerAction.None;
        GameEvents.OnBattleStateChanged?.Invoke();
        SetIsPlayerTurn(false);

        switch (choice)
        {
            case PlayerAction.Attack:
                ResetDefendStreak();
                yield return PlayerTurn();
                break;

            case PlayerAction.Defend:
                {
                    string name = GetName(activeIndex);
                    bool success = RollDefendSuccess();

                    defendActiveThisRound = success;

                    if (feedback)
                    {
                        feedback.PlayDefendResult(BattleFeedbackManager.BattleFeedbackSide.Player, success);
                    }

                    if (success)
                    {
                        BattleLogger.Log($"{name} is defending.", LogScope.Battle);
                        BattleLogger.Log($"{name} will reduce the next hit and convert it into a shield for the following round.", LogScope.Battle);
                    }
                    else
                    {
                        BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
                    }
                    break;
                }

            case PlayerAction.Focus:
                {
                    ResetDefendStreak();

                    if (chargedNextAttack != null && activeIndex >= 0 && activeIndex < chargedNextAttack.Length)
                        chargedNextAttack[activeIndex] = true;

                    BattleLogger.Log($"{GetName(activeIndex)} is charging.", LogScope.Battle);
                    BattleLogger.Log($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.", LogScope.Battle);

                    if (feedback) feedback.PlayActionQueued(
                        BattleFeedbackManager.BattleFeedbackSide.Player,
                        BattleFeedbackManager.BattleFeedbackAction.Focus
                    );
                    break;
                }

            case PlayerAction.Swap:
                {
                    ResetDefendStreak();
                    ResolveQueuedSwap();
                    break;
                }

            case PlayerAction.Run:
                {
                    ResetDefendStreak();

                    float chance = ComputeRunChance();
                    bool escaped = UnityEngine.Random.value < chance;

                    string name = GetName(activeIndex);

                    if (feedback) feedback.PlayActionQueued(
                        BattleFeedbackManager.BattleFeedbackSide.Player,
                        BattleFeedbackManager.BattleFeedbackAction.Run
                    );

                    if (escaped)
                    {
                        BattleLogger.Log($"{name} has fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                        EndBattle(false, true);
                        yield break;
                    }
                    else
                    {
                        runAttempts++;
                        BattleLogger.Log($"Couldn't escape! (Run chance was {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                    }
                    break;
                }

            default:
                break;
        }
    }

    private IEnumerator PlayerTurn()
    {
        if (isResolvingPlayerTurn)
            yield break;

        isResolvingPlayerTurn = true;

        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
        {
            isResolvingPlayerTurn = false;
            yield break;
        }

        var playerDef = teamDefs[activeIndex];
        string attacker = GetName(activeIndex);
        string move = GetBasicMoveName(playerDef);
        string foeName = wildDef ? wildDef.displayName : "Foe";

        yield return Say($"{attacker} used {move}!");
        if (feedback) feedback.PlayAttackWindup(BattleFeedbackManager.BattleFeedbackSide.Player);

        if (feedback)
            feedback.SpawnBasicAttackVfx(isPlayerSide: true, playerDef: playerDef, wildDef: wildDef);

        yield return Wait(0.10f);

        // Baseline TOTAL ATK (SpeciesBase + LevelGrowth + Training + flatAtkBonus w/ legacy guard)
        GetProgressionTotalsForIndex(activeIndex, out _, out int atkBaseTotal, out _, out _, out _);

        // Conditionals apply on top of baseline totals
        var cond = GetConditionalModsForActive();
        int atkWithCondFlat = Mathf.Max(1, atkBaseTotal + Mathf.Max(0, cond.atkFlat));
        int atkForResolve = Mathf.Max(1, Mathf.RoundToInt(atkWithCondFlat * (1f + Mathf.Max(0f, cond.atkPct))));

        // Temp boosters are additive flat on top (do NOT recalc BattleCalc to create a multiplier)
        int tempFlatFromBoosters = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;
        if (tempFlatFromBoosters > 0)
            atkForResolve = Mathf.Max(1, atkForResolve + Mathf.Max(0, tempFlatFromBoosters));


        var jctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float playerCrit = critChancePlayer;
        if (jctx != null)
        {
            playerCrit += jctx.critChanceFlat;
            if (jctx.critBuffTurns > 0)
                playerCrit += jctx.critChanceBonusFirstTurns;
        }
        playerCrit = Mathf.Clamp01(playerCrit);

        var dr = BattleCalc.ResolveHit(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            null, wildDef, wildLevel,
            atkForResolve,
            playerCrit,
            critMultiplier,
            0
        );

        TitlesAdapter.OnAttackLanded(teamIds[activeIndex], dr.crit);
        if (dr.crit) _totalCritsThisBattle++;

        if (jctx != null && jctx.attackBonusPct > 0f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.attackBonusPct)));

        if (jctx != null && jctx.usedFirstOutgoing == false && jctx.firstOutgoingBonus > 0f)
        {
            jctx.usedFirstOutgoing = true;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.firstOutgoingBonus)));
        }

        if (jctx != null && jctx.surgeApplied && jctx.surgeAtkBonusPct > 0f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.surgeAtkBonusPct)));

        if (slotDamageBuffPct != null && slotDamageBuffTurns != null &&
            activeIndex >= 0 && activeIndex < slotDamageBuffPct.Length &&
            slotDamageBuffTurns[activeIndex] > 0 &&
            slotDamageBuffPct[activeIndex] > 0f)
        {
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + slotDamageBuffPct[activeIndex])));
            yield return Say($"+{Mathf.RoundToInt(slotDamageBuffPct[activeIndex] * 100f)}% damage buff active.");

            slotDamageBuffTurns[activeIndex]--;
            if (slotDamageBuffTurns[activeIndex] <= 0)
                slotDamageBuffPct[activeIndex] = 0f;
        }

        if (chargedNextAttack != null &&
            activeIndex >= 0 &&
            activeIndex < chargedNextAttack.Length &&
            chargedNextAttack[activeIndex] &&
            chargeBonusPct > 0f)
        {
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + chargeBonusPct)));
            chargedNextAttack[activeIndex] = false;

            yield return Say($"{GetName(activeIndex)} unleashes a charged attack (+{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage)!");
        }

        float preventedByWildGuard = 0f;
        int dmgToApply = dr.damage;

        if (wildDefendActiveThisRound && defendReducePct > 0f)
        {
            float guardPct = Mathf.Clamp01(defendReducePct);
            int before = dmgToApply;
            int after = Mathf.Max(1, Mathf.RoundToInt(dmgToApply * (1f - guardPct)));
            preventedByWildGuard = Mathf.Max(0, before - after);
            dmgToApply = after;

            if (preventedByWildGuard > 0f && feedback)
                feedback.PlayDefendShieldFX(isPlayer: false);
        }

        if (wildShieldHP > 0f && dmgToApply > 0)
        {
            float absorb = Mathf.Min(wildShieldHP, dmgToApply);
            wildShieldHP = Mathf.Max(0f, wildShieldHP - absorb);
            dmgToApply = Mathf.Max(0, dmgToApply - Mathf.RoundToInt(absorb));

            if (absorb > 0f)
                yield return Say($"{foeName}'s shield absorbed {Mathf.RoundToInt(absorb)}!");
        }

        if (preventedByWildGuard > 0f && guardConvertPct > 0f)
        {
            float gain = preventedByWildGuard * guardConvertPct;
            wildPendingGuardShield += gain;
            yield return Say($"{foeName} stores {Mathf.RoundToInt(gain)} damage as a guard shield for the next round.");
        }

        float preWildHP = wildHP;
        wildHP = Mathf.Max(0f, wildHP - dmgToApply);
        _totalDamageDealtThisBattle += Mathf.Max(0, dmgToApply);
        PushHPBars();

        float wRatio = wildMaxHP > 0.01f ? (float)dmgToApply / wildMaxHP : 0f;
        if (feedback) feedback.PlayHitReaction(BattleFeedbackManager.BattleFeedbackSide.Wild, dr.crit, wRatio);

        if (!playerLandedFirstHitThisBattle && dr.damage > 0)
            playerLandedFirstHitThisBattle = true;

        yield return Say($"{attacker} hits {foeName} for {dmgToApply}!");

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) yield return Say("It's super effective!");
            else if (dr.effectiveness < 0.85f) yield return Say("It's not very effective...");
        }
        if (dr.crit) yield return Say("Critical hit!");

        // Centralized KO messaging (fires only on HP crossing >0 → 0)
        yield return MaybeSayKO_Wild(foeName, preWildHP, wildHP);

        if (jctx != null && jctx.endTurnHealPct > 0f)
        {
            bool canHeal = (jctx.regenTurns == int.MaxValue) || (jctx.regenTurns > 0);
            if (canHeal)
            {
                float healAmt = GetFinalMaxHPForIndex(activeIndex) * jctx.endTurnHealPct;
                TryAddHPToActive(healAmt);
                if (jctx.regenTurns != int.MaxValue) jctx.regenTurns--;
                yield return Say($"{GetName(activeIndex)} regenerates {Mathf.RoundToInt(healAmt)} HP.");
            }
        }

        FirePlayerEndTurnTicks(dealtDamageThisTurn: dr.damage > 0, critThisTurn: dr.crit);

        isResolvingPlayerTurn = false;
    }

    private IEnumerator EnemyTurn(EnemyAction choice)
    {
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
            yield break;

        if (choice != EnemyAction.Defend)
            yield return Wait(0.15f);

        if (choice != EnemyAction.Defend)
            ResetEnemyDefendStreak();

        if (choice == EnemyAction.Defend)
        {
            string name = wildDef ? wildDef.displayName : "Foe";
            bool success = RollEnemyDefendSuccess();

            wildDefendActiveThisRound = success;

            if (feedback)
            {
                feedback.PlayDefendResult(BattleFeedbackManager.BattleFeedbackSide.Wild, success);
            }

            if (success)
            {
                yield return Say($"{name} is defending.");
                yield return Say($"{name} will reduce the next hit and convert it into a shield for the following round.");
            }
            else
            {
                yield return Say($"{name} tried to defend, but it failed!");
            }

            yield break;
        }

        if (choice == EnemyAction.Focus)
        {
            wildChargedNextAttack = true;

            string name = wildDef ? wildDef.displayName : "Foe";
            yield return Say($"{name} is charging up.");
            yield return Say($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.");

            if (feedback) feedback.PlayActionQueued(
                BattleFeedbackManager.BattleFeedbackSide.Wild,
                BattleFeedbackManager.BattleFeedbackAction.Swap
            );

            yield break;
        }

        if (choice == EnemyAction.Run)
        {
            string name = wildDef ? wildDef.displayName : "Foe";
            float chance = ComputeEnemyRunChance();
            bool fled = UnityEngine.Random.value < chance;

            if (fled)
            {
                yield return Say($"{name} fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)");
                EndBattle(false, escaped: true);
                yield break;
            }
            else
            {
                yield return Say($"{name} tried to flee, but couldn't! (Run chance {Mathf.RoundToInt(chance * 100f)}%)");
                yield break;
            }
        }

        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
            yield break;

        string attackerName = wildDef ? wildDef.displayName : "Foe";
        string move = GetBasicMoveName(wildDef);

        yield return Say($"{attackerName} used {move}!");
        if (feedback) feedback.PlayAttackWindup(BattleFeedbackManager.BattleFeedbackSide.Wild);

        if (feedback)
            feedback.SpawnBasicAttackVfx(isPlayerSide: false, playerDef: teamDefs[activeIndex], wildDef: wildDef);

        yield return Wait(0.10f);

        int enemyAtk = Mathf.Max(1, Mathf.RoundToInt(wildAttackPerTurn));
        int defFlatBooster = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;

        var ctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float preHP = teamHP[activeIndex];


        var cmods = GetConditionalModsForActive();

        var df = TitlesAdapter.GetDamageFilter(teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]);

        float playerCritResist = 0f;
        if (ctx != null)
        {
            playerCritResist += ctx.critResistFlat;
            if (ctx.critResistBuffTurns > 0)
                playerCritResist += ctx.critResistBonusFirstTurns;
        }

        float wildCritChance = df.cannotBeCrit ? 0f : Mathf.Clamp01(critChanceWild - playerCritResist);

        // Baseline TOTAL DEF (SpeciesBase + LevelGrowth + Training)
        GetProgressionTotalsForIndex(activeIndex, out _, out _, out int defBaseTotal, out _, out _);

        // Flat defense sources stack onto DEF as a STAT (boosters + conditional flat)
        int defenderEffectiveDefenseStat =
            Mathf.Max(0, defBaseTotal + Mathf.Max(0, defFlatBooster) + Mathf.Max(0, cmods.defFlat));

        var dr = BattleCalc.ResolveHit(
            null, wildDef, wildLevel,
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            enemyAtk, wildCritChance, critMultiplier,
            defenderFlatDefenseBonus: 0,
            defenderEffectiveDefenseStat: defenderEffectiveDefenseStat
        );


        if (wildChargedNextAttack && chargeBonusPct > 0f)
        {
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + chargeBonusPct)));
            wildChargedNextAttack = false;

            yield return Say($"{attackerName} unleashes a charged attack (+{Mathf.RoundToInt(chargeBonusPct * 100f)}% dmg)!");
        }

        float incomingScalar = 1f;

        if (cmods.defPct > 0f)
            incomingScalar *= 1f - Mathf.Clamp01(cmods.defPct);

        if (ctx != null && !ctx.usedFirstIncoming && ctx.firstIncomingReduce > 0f)
        {
            ctx.usedFirstIncoming = true;
            incomingScalar *= 1f - ctx.firstIncomingReduce;
        }

        if (ctx != null && ctx.baseDamageReducePct > 0f)
            incomingScalar *= 1f - ctx.baseDamageReducePct;

        if (ctx != null && ctx.defenseBonusPct > 0f)
            incomingScalar *= 1f - ctx.defenseBonusPct;

        if (ctx != null && ctx.dmgReduceBuffTurns > 0 && ctx.dmgReduceFirstTurns > 0f)
            incomingScalar *= 1f - ctx.dmgReduceFirstTurns;

        float scalarBeforeGuard = incomingScalar;
        float preventedByGuardRaw = 0f;

        if (defendActiveThisRound && defendReducePct > 0f)
        {
            float guardPct = Mathf.Clamp01(defendReducePct);
            incomingScalar *= (1f - guardPct);

            float dmgBeforeGuard = dr.damage * scalarBeforeGuard;
            float dmgAfterGuard = dr.damage * incomingScalar;
            preventedByGuardRaw = Mathf.Max(0f, dmgBeforeGuard - dmgAfterGuard);

            if (preventedByGuardRaw > 0f && feedback)
                feedback.PlayDefendShieldFX(isPlayer: true);
        }

        int dmg_afterScalar = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));

        float shieldBefore = (shieldHP != null && shieldHP.Length > activeIndex) ? shieldHP[activeIndex] : 0f;
        float shieldAbsorbF = 0f;

        int dmg_final = dmg_afterScalar;
        if (shieldBefore > 0f && dmg_final > 0)
        {
            shieldAbsorbF = Mathf.Min(shieldBefore, dmg_final);
            shieldHP[activeIndex] = Mathf.Max(0f, shieldBefore - shieldAbsorbF);
            dmg_final = Mathf.Max(0, dmg_final - Mathf.RoundToInt(shieldAbsorbF));

            if (shieldAbsorbF > 0f)
                yield return Say($"{GetName(activeIndex)}'s shield absorbed {Mathf.RoundToInt(shieldAbsorbF)}!");
        }

        string victimName = GetName(activeIndex);
        float prePlayerHP = teamHP[activeIndex];
        teamHP[activeIndex] = Mathf.Max(0f, teamHP[activeIndex] - dmg_final);
        ClampAndPushActiveHP();

        float maxHP = GetFinalMaxHPForIndex(activeIndex);
        float ratio = maxHP > 0.01f ? (float)dmg_final / maxHP : 0f;
        if (feedback) feedback.PlayHitReaction(BattleFeedbackManager.BattleFeedbackSide.Player, dr.crit && !df.cannotBeCrit, ratio);

        if (preventedByGuardRaw > 0f &&
            pendingGuardShield != null &&
            activeIndex >= 0 &&
            activeIndex < pendingGuardShield.Length &&
            guardConvertPct > 0f)
        {
            float shieldGain = preventedByGuardRaw * guardConvertPct;
            pendingGuardShield[activeIndex] += shieldGain;
            yield return Say($"{GetName(activeIndex)} stores {Mathf.RoundToInt(shieldGain)} damage as a guard shield for the next round.");
        }

        TitlesAdapter.OnHitTaken(teamIds[activeIndex], dmg_final, dr.crit && !df.cannotBeCrit);

        yield return Say($"{attackerName} hits {GetName(activeIndex)} for {dmg_final}!");

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) yield return Say("It's super effective!");
            else if (dr.effectiveness < 0.85f) yield return Say("It's not very effective...");
        }

        if (dr.crit && !df.cannotBeCrit)
        {
            yield return Say("Critical hit!");
            _totalCritsThisBattle++;
        }

        // Centralized KO messaging (fires only on HP crossing >0 → 0)
        yield return MaybeSayKO_Player(victimName, prePlayerHP, teamHP[activeIndex]);

        _totalDamageTakenThisBattle += dmg_final;

        if (!playerTookFirstIncomingThisBattle)
            playerTookFirstIncomingThisBattle = true;

        if (ctx != null && !ctx.rescueUsed && ctx.rescueHealPct > 0f && teamHP[activeIndex] > 0f)
        {
            float curMax = GetFinalMaxHPForIndex(activeIndex);
            float thresholdHP = curMax * (ctx.rescueThreshold > 0f ? ctx.rescueThreshold : 0.4f);
            if (preHP > thresholdHP && teamHP[activeIndex] <= thresholdHP)
            {
                ctx.rescueUsed = true;
                float healAmt = curMax * ctx.rescueHealPct;
                TryAddHPToActive(healAmt);
                yield return Say($"{GetName(activeIndex)} triage heals {Mathf.RoundToInt(healAmt)} HP!");
                AudioManager.I?.PlaySfx(SfxType.Heal);
            }
        }

        if (ctx != null && !ctx.surgeApplied)
        {
            float curMax = GetFinalMaxHPForIndex(activeIndex);
            if (teamHP[activeIndex] <= curMax * 0.5f && ctx.surgeAtkBonusPct > 0f)
            {
                ctx.surgeApplied = true;
                ctx.attackBonusPct += ctx.surgeAtkBonusPct;
                yield return Say($"{GetName(activeIndex)} becomes enraged (+{Mathf.RoundToInt(ctx.surgeAtkBonusPct * 100f)}% ATK)!");
                AudioManager.I?.PlaySfx(SfxType.Clutch);
            }
        }
    }

    private bool CheckEnd()
    {
        if (IsWildKO())
        {
            BattleLogger.Log("Wild monster fainted!", LogScope.Battle);
            AudioManager.I?.PlaySfx(SfxType.KO);
            if (feedback) feedback.PlayKO(BattleFeedbackManager.BattleFeedbackSide.Wild);
            EndBattle(true);
            return true;
        }
        if (IsTeamKO())
        {
            BattleLogger.Log("Your team is unable to battle!", LogScope.Battle);
            AudioManager.I?.PlaySfx(SfxType.KO);
            if (feedback) feedback.PlayKO(BattleFeedbackManager.BattleFeedbackSide.Player);
            EndBattle(false);
            return true;
        }
        return false;
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

        if (!victory && !escaped && EncounterManager.I != null && EncounterManager.I.IsAutoMode)
        {
            EncounterManager.I?.NotifyAuto_TeamKO();
        }

        SetPostBattleWinnerVisible(victory, escaped);

        if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            TitlesAdapter.OnBattleEnd(teamIds[activeIndex], victory, wildDef, wildLevel);

        onEnd?.Invoke(result);
        GameEvents.BattleFinished?.Invoke(result);
    }

    private void ClampAndPushActiveHP()
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);
        teamHP[activeIndex] = Mathf.Min(teamHP[activeIndex], curMax);

        if (feedback != null)
        {
            feedback.SetHPBars(
                playerCur: teamHP[activeIndex],
                playerMax: curMax,
                wildCur: wildHP,
                wildMax: wildMaxHP
            );
        }
        else
        {
            if (playerHPBar)
            {
                playerHPBar.maxValue = curMax;
                playerHPBar.value = Mathf.Clamp(teamHP[activeIndex], 0f, curMax);
            }
            if (wildHPBar)
            {
                wildHPBar.maxValue = wildMaxHP;
                wildHPBar.value = Mathf.Clamp(wildHP, 0f, wildMaxHP);
            }
        }

        UpdatePlayerInfoUI();
        UpdateHPTextUI();
    }

    private void PushHPBars()
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);

        if (feedback != null)
        {
            feedback.SetHPBars(
                playerCur: teamHP[activeIndex],
                playerMax: curMax,
                wildCur: wildHP,
                wildMax: wildMaxHP
            );
        }
        else
        {
            if (wildHPBar)
            {
                wildHPBar.maxValue = wildMaxHP;
                wildHPBar.value = Mathf.Clamp(wildHP, 0f, wildMaxHP);
            }
            if (playerHPBar)
            {
                playerHPBar.maxValue = curMax;
                playerHPBar.value = Mathf.Clamp(teamHP[activeIndex], 0f, curMax);
            }
        }

        UpdatePlayerInfoUI();
        UpdateHPTextUI();
    }


    private void UpdateHPTextUI()
    {
        float playerMax = GetFinalMaxHPForIndex(activeIndex);
        playerMax = Mathf.Max(1f, playerMax);

        float playerCur =
            (teamHP != null && activeIndex >= 0 && activeIndex < teamHP.Length)
                ? teamHP[activeIndex]
                : playerMax;

        playerCur = Mathf.Clamp(playerCur, 0f, playerMax);

        float wildMax = Mathf.Max(1f, wildMaxHP);
        float wildCur = Mathf.Clamp(wildHP, 0f, wildMax);

        if (feedback != null && feedback.HasHPTextWired)
        {
            feedback.SetHPTexts(
                playerCur: playerCur,
                playerMax: playerMax,
                wildCur: wildCur,
                wildMax: wildMax
            );
            return;
        }

        int pCurI = Mathf.CeilToInt(playerCur);
        int pMaxI = Mathf.CeilToInt(playerMax);
        int wCurI = Mathf.CeilToInt(wildCur);
        int wMaxI = Mathf.CeilToInt(wildMax);

        if (playerHPText)
        {
            playerHPText.text = $"HP: {pCurI}/{pMaxI}";
            playerHPText.color = StatNeutral;
        }

        if (wildHPText)
        {
            wildHPText.text = $"HP: {wCurI}/{wMaxI}";
            wildHPText.color = StatNeutral;
        }

        if (playerHPBar)
        {
            playerHPBar.maxValue = playerMax;
            playerHPBar.value = playerCur;
        }

        if (wildHPBar)
        {
            wildHPBar.maxValue = wildMax;
            wildHPBar.value = wildCur;
        }
    }


    private void RefreshBenchUI()
    {
        List<int> others = new();
        for (int i = 0; i < teamCount; i++) if (i != activeIndex) others.Add(i);

        if (benchImg1)
        {
            if (others.Count > 0)
            {
                benchImg1.enabled = true;
                benchImg1.sprite = teamDefs[others[0]]?.icon;
                benchImg1.color = teamHP[others[0]] > 0 ? Color.white : new Color(1, 1, 1, 0.35f);
            }
            else benchImg1.enabled = false;
        }
        if (benchBtn1) benchBtn1.interactable = others.Count > 0 && teamHP[others[0]] > 0f;

        if (benchHPText1)
        {
            if (others.Count > 0) SetBenchHP(benchHPText1, others[0]);
            else benchHPText1.gameObject.SetActive(false);
        }

        if (benchImg2)
        {
            if (others.Count > 1)
            {
                benchImg2.enabled = true;
                benchImg2.sprite = teamDefs[others[1]]?.icon;
                benchImg2.color = teamHP[others[1]] > 0 ? Color.white : new Color(1, 1, 1, 0.35f);
            }
            else benchImg2.enabled = false;
        }
        if (benchBtn2) benchBtn2.interactable = others.Count > 1 && teamHP[others[1]] > 0f;

        if (benchHPText2)
        {
            if (others.Count > 1) SetBenchHP(benchHPText2, others[1]);
            else benchHPText2.gameObject.SetActive(false);
        }
    }

    private void ClickBench(int benchSlot)
{
    if (!inBattle) return;
    if (!manualTurns) return; // swapping is a manual-turn action
    if (!IsPlayerTurn) return;
    if (isResolvingPlayerTurn) return;
    if (_narrationLock) return;

    // Lock swapping once an action is queued.
    if (pendingAction != PlayerAction.None) return;

    // Queue a Swap action (swapping costs the turn).
    pendingSwapBenchSlot = benchSlot;
    pendingAction = PlayerAction.Swap;
    GameEvents.OnBattleStateChanged?.Invoke();
}

private void ResolveQueuedSwap()
{
    if (pendingSwapBenchSlot < 0) return;

    List<int> others = new();
    for (int i = 0; i < teamCount; i++)
        if (i != activeIndex) others.Add(i);

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

    if (feedback) feedback.PlayActionQueued(
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

    private void FirePlayerEndTurnTicks(bool dealtDamageThisTurn, bool critThisTurn)
    {
        playerNoDmgTurns = dealtDamageThisTurn ? 0 : Mathf.Min(playerNoDmgTurns + 1, 99);
        playerNoCritTurns = critThisTurn ? 0 : Mathf.Min(playerNoCritTurns + 1, 99);
    }

    private void UpdateWildInfoUI()
    {
        if (!wildDef) return;

        int baseHP = Mathf.RoundToInt(BattleCalc.CalcHP(wildDef, wildLevel));
        int baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(wildDef, wildLevel, 0, 0));
        int baseDEF = BattleCalc.CalcDefense(wildDef, wildLevel);
        int baseSPD = BattleCalc.CalcSpeed(wildDef, wildLevel);

        int effHP = Mathf.RoundToInt(wildMaxHP);
        int effATK = Mathf.RoundToInt(wildAttackPerTurn);

        int effDEF = baseDEF;
        int effSPD = baseSPD;

        if (wildIdText) wildIdText.text = $"ID: {wildDef.id}";
        if (wildTypeText) wildTypeText.text = $"TYPE: {wildDef.type}";
        if (wildRarityText) wildRarityText.text = $"RARITY: {wildDef.rarity}";
        if (wildLevelText) wildLevelText.text = $"LVL: {wildLevel}";

        if (wildHPText) SetStatRowColorAndText(wildHPText, "HP", baseHP, effHP, minFinal: 1);
        if (wildATKText) SetStatRowColorAndText(wildATKText, "ATK", baseATK, effATK, minFinal: 1);
        if (wildDEFText) SetStatRowColorAndText(wildDEFText, "DEF", baseDEF, effDEF, minFinal: 0);
        if (wildSPDText) SetStatRowColorAndText(wildSPDText, "SPD", baseSPD, effSPD, minFinal: 1);
    }

    private void UpdatePlayerInfoUI()
    {
        if (activeIndex < 0 || teamDefs == null || activeIndex >= teamDefs.Length) return;

        var def = teamDefs[activeIndex];
        if (!def) return;

        int lvl = (teamLevels != null && activeIndex < teamLevels.Length) ? teamLevels[activeIndex] : 1;

        // ─────────────────────────────────────────────────────────────────────────
        // Baseline TOTALS (SpeciesBase + LevelGrowth + TrainingBonus + flatAtkBonus*)
        // *flatAtkBonus is treated as permanent progression ATK bonus and included in total ATK.
        // Legacy guard: if old saves mirrored training into flatAtkBonus, we avoid double counting.
        // ─────────────────────────────────────────────────────────────────────────
        GetProgressionTotalsForIndex(
            activeIndex,
            out int baseTotalHP,
            out int baseTotalATK,
            out int baseTotalDEF,
            out int baseTotalSPD,
            out _ 
        );

        int tempHPFlat  = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        int tempATKFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;
        int tempDEFFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;
        int tempSPDFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0;

        var ctx = TitleContext.Empty;
        ctx.ownedId = (teamIds != null && activeIndex < teamIds.Length) ? teamIds[activeIndex] : "";

        float maxNoConds = GetActiveMaxHP_NoConditionals(teamMaxHP[activeIndex], activeIndex);
        maxNoConds = Mathf.Max(1f, maxNoConds);

        float currentHP = (teamHP != null && activeIndex < teamHP.Length) ? teamHP[activeIndex] : maxNoConds;
        ctx.selfHp01 = Mathf.Clamp01(currentHP / maxNoConds);


        ctx.alliesAlive = GetAlliesAliveNotIncludingActive();
        ctx.winStreak = GetWinStreakSafe();

        var cmods = GetConditionalModsForActive();

        // Header rows
        if (playerIdText) playerIdText.text = $"ID: {def.id}";
        if (playerTypeText) playerTypeText.text = $"TYPE: {def.type}";
        if (playerRarityText) playerRarityText.text = $"RARITY: {def.rarity}";
        if (playerLevelText) playerLevelText.text = $"LVL: {lvl}";

        // ─────────────────────────────────────────────────────────────────────────
        // HP
        // Base for display = baseline totals + temp HP
        // Titles first, then conditionals (for coloring and delta)
        // ─────────────────────────────────────────────────────────────────────────
        int hpBaseForDisplay = Mathf.RoundToInt(maxNoConds);
        float hpFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "HP", ctx, hpBaseForDisplay);
        int hpTitleFinal = Mathf.Max(1, Mathf.RoundToInt(hpFinalF));

        if (playerHPText)
        {
            SetPlayerStatRowWithConditionals(
                playerHPText, "HP",
                hpBaseForDisplay, hpTitleFinal,
                condFlat: 0, condPct: cmods.hpPct,
                minFinal: 1
            );
        }

        // ─────────────────────────────────────────────────────────────────────────
        // ATK
        // Base for display = baseline totals (already includes training + flatAtkBonus w/ legacy guard) + temp ATK
        // Titles first, then conditionals
        // ─────────────────────────────────────────────────────────────────────────
        int atkBaseForDisplay = Mathf.Max(1, baseTotalATK + tempATKFlat);
        float atkFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Attack", ctx, atkBaseForDisplay);
        int atkTitleFinal = Mathf.Max(1, Mathf.RoundToInt(atkFinalF));

        if (playerATKText)
        {
            SetPlayerStatRowWithConditionals(
                playerATKText, "ATK",
                atkBaseForDisplay, atkTitleFinal,
                condFlat: cmods.atkFlat, condPct: cmods.atkPct,
                minFinal: 1
            );
        }

        // ─────────────────────────────────────────────────────────────────────────
        // DEF
        // Base for display = baseline totals + temp DEF
        // Titles first, then conditionals
        // ─────────────────────────────────────────────────────────────────────────
        int defBaseForDisplay = Mathf.Max(0, baseTotalDEF + tempDEFFlat);
        float defFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Defense", ctx, defBaseForDisplay);
        int defTitleFinal = Mathf.Max(0, Mathf.RoundToInt(defFinalF));

        if (playerDEFText)
        {
            SetPlayerStatRowWithConditionals(
                playerDEFText, "DEF",
                defBaseForDisplay, defTitleFinal,
                condFlat: cmods.defFlat, condPct: cmods.defPct,
                minFinal: 0
            );
        }

        // ─────────────────────────────────────────────────────────────────────────
        // SPD
        // Base for display = baseline totals + temp SPD
        // Titles first, then conditionals
        // ─────────────────────────────────────────────────────────────────────────
        int spdBaseForDisplay = Mathf.Max(1, baseTotalSPD + tempSPDFlat);
        float spdFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Speed", ctx, spdBaseForDisplay);
        int spdTitleFinal = Mathf.Max(1, Mathf.RoundToInt(spdFinalF));

        if (playerSPDText)
        {
            SetPlayerStatRowWithConditionals(
                playerSPDText, "SPD",
                spdBaseForDisplay, spdTitleFinal,
                condFlat: cmods.spdFlat, condPct: cmods.spdPct,
                minFinal: 1
            );
        }

        bool resistOn = BattleTempBuffs.I && BattleTempBuffs.I.IsTypeResistActive();
        if (resistOn && playerRarityText) playerRarityText.text += " [Resist]";
    }


    private void ApplyActiveToUI()
    {
        var def = teamDefs[activeIndex];
        var lvl = teamLevels[activeIndex];

        // Shiny-aware display (team entries carry isShiny/shinyTier)
        bool isShiny = false;
        var data = SaveManager.Data;
        if (data != null && data.team != null && activeIndex >= 0 && activeIndex < data.team.Count)
        {
            var om = data.team[activeIndex];
            isShiny = om != null && (om.isShiny || om.shinyTier > 0);
        }

        if (playerIcon)
        {
            if (def)
            {
                var s = MonsterNameFormatter.GetIcon(def, isShiny, backIcon: true);
                playerIcon.sprite = s ? s : (def.backIcon ? def.backIcon : def.icon);
            }
            else
            {
                playerIcon.sprite = null;
            }
        }

        if (playerNameText) playerNameText.text = def ? MonsterNameFormatter.Format(def, isShiny) : "";
        if (playerLevelText) playerLevelText.text = $"Lv {lvl}";
        UpdatePlayerInfoUI();
        UpdateHPTextUI();
    }


    private WaitForSecondsRealtime Wait(float t)
    {
        float scaled = Mathf.Max(0.01f, t / Mathf.Max(0.01f, battleSpeed));
        return new WaitForSecondsRealtime(scaled);
    }

    public void SetBattleSpeed(float s)
    {
        battleSpeed = Mathf.Clamp(s, 0.25f, 5f);
        if (SaveManager.Data != null && SaveManager.Data.settings != null)
        {
            SaveManager.Data.settings.battleSpeed = battleSpeed;
            SaveManager.Save();
        }
    }

    public void CycleBattleSpeed()
    {
        if (battleSpeed < 1.5f) SetBattleSpeed(2f);
        else if (battleSpeed < 2.5f) SetBattleSpeed(3f);
        else SetBattleSpeed(1f);
    }

    private void SetBenchHP(TextMeshProUGUI label, int teamIdx)
    {
        if (!label) return;
        if (teamIdx < 0 || teamIdx >= teamCount) { label.gameObject.SetActive(false); return; }

        float cur = Mathf.Max(0f, teamHP[teamIdx]);
        float max = Mathf.Max(1f, GetFinalMaxHPForIndex(teamIdx));
        int icur = Mathf.CeilToInt(cur);
        int imax = Mathf.CeilToInt(max);

        label.gameObject.SetActive(true);
        label.text = $"{icur}/{imax}";
        label.alpha = cur > 0f ? 1f : 0.35f;
    }

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

    private void HandleBattleFinishedUIRefresh(BattleResult _)
    {
        if (playerPanel != null && playerPanel.activeInHierarchy)
        {
            ApplyActiveToUI();
            ClampAndPushActiveHP();
            RefreshBenchUI();
        }
    }

    private int GetPlayerEffectiveSpeedForRun()
    {
        if (activeIndex < 0 || teamDefs == null || activeIndex >= teamDefs.Length || teamDefs[activeIndex] == null)
            return 1;

        int spd = GetProgressionTotalSPDForIndex(activeIndex);

        var j = (jobCtx != null) ? jobCtx[activeIndex] : null;
        if (j != null && j.speedBuffTurns > 0 && j.speedBonusPctFirstTurns != 0f)
            spd = Mathf.Max(1, Mathf.RoundToInt(spd * (1f + j.speedBonusPctFirstTurns)));

        var cmods = GetConditionalModsForActive();
        spd = Mathf.Max(1, Mathf.RoundToInt((spd + Mathf.Max(0, cmods.spdFlat)) * (1f + Mathf.Max(0f, cmods.spdPct))));

        return Mathf.Max(1, spd);
    }

    private int GetWildEffectiveSpeedForRun()
    {
        if (!wildDef) return 1;
        return Mathf.Max(1, BattleCalc.CalcSpeed(wildDef, wildLevel));
    }

    private float ComputeRunChance()
    {
        int pSpd = GetPlayerEffectiveSpeedForRun();
        int wSpd = GetWildEffectiveSpeedForRun();
        float speedTerm = (pSpd + wSpd) > 0 ? (float)pSpd / (pSpd + wSpd) : 0.5f;

        float hp01 = 1f - (wildHP / Mathf.Max(1f, wildMaxHP));
        float attemptsBonus = runAttemptBonus * Mathf.Max(0, runAttempts);

        float chance =
            runBaseChance +
            runSpeedWeight * (speedTerm - 0.5f) +
            runHpWeight * hp01 +
            attemptsBonus;

        return Mathf.Clamp(chance, runMinChance, runMaxChance);
    }

    private void ApplyPendingGuardShieldForActive()
    {
        if (pendingGuardShield == null || shieldHP == null) return;
        if (activeIndex < 0 || activeIndex >= pendingGuardShield.Length) return;

        float gain = pendingGuardShield[activeIndex];
        if (gain <= 0.01f) return;

        shieldHP[activeIndex] += gain;
        pendingGuardShield[activeIndex] = 0f;

        BattleLogger.Log($"{GetName(activeIndex)} gains a guard shield of {Mathf.RoundToInt(gain)}!", LogScope.Battle);
        ClampAndPushActiveHP();
    }

    private string GetWildPersonalityLabel()
    {
        if (!wildDef || wildDef.Personality == null) return null;
        return wildDef.Personality.group.ToString();
    }

    private string GetBasicMoveName(MonsterDataSO def)
    {
        if (!def) return "Attack";
        return !string.IsNullOrEmpty(def.basicAttackName) ? def.basicAttackName : "Attack";
    }

    private bool RollDefendSuccess()
    {
        float chance = Mathf.Clamp01(currentDefendSuccess);
        bool ok = UnityEngine.Random.value <= chance;

        if (ok)
        {
            defendConsecutiveUses++;
            float next = defendFirstUseSuccess * Mathf.Pow(defendRepeatMultiplier, defendConsecutiveUses);
            currentDefendSuccess = Mathf.Max(defendMinSuccess, next);
        }
        else
        {
            defendConsecutiveUses = 0;
            currentDefendSuccess = defendFirstUseSuccess;
        }

        return ok;
    }

    private void ResetDefendStreak()
    {
        defendConsecutiveUses = 0;
        currentDefendSuccess = defendFirstUseSuccess;
    }

    private EnemyAction ChooseEnemyAction()
    {
        if (!wildDef || wildMaxHP <= 0.01f)
            return EnemyAction.Attack;

        float hpRatio = Mathf.Clamp01(wildHP / Mathf.Max(1f, wildMaxHP));
        BattleAction action = BattleAction.Attack;

        if (wildDef.Personality != null)
        {
            var ctx = new PersonalityContext
            {
                selfHpRatio = hpRatio,
                hasSuperEffectiveMove = false,
                isBadlyMatched = false,
                turnNumber = Mathf.Max(1, _turnIndex + 1)
            };

            action = wildDef.Personality.ChooseAction(in ctx, _enemyRng);
        }

        EnemyAction Fallback()
        {
            if (hpRatio < 0.25f && UnityEngine.Random.value < 0.40f)
                return EnemyAction.Run;
            if (hpRatio < 0.50f && UnityEngine.Random.value < 0.30f)
                return EnemyAction.Defend;
            if (UnityEngine.Random.value < 0.15f)
                return EnemyAction.Focus;
            return EnemyAction.Attack;
        }

        switch (action)
        {
            case BattleAction.Attack: return EnemyAction.Attack;
            case BattleAction.Defend: return EnemyAction.Defend;
            case BattleAction.Focus: return EnemyAction.Focus;
            case BattleAction.Run: return EnemyAction.Run;
            default: return Fallback();
        }
    }

    private float ComputeEnemyRunChance()
    {
        if (!wildDef || wildMaxHP <= 0.01f)
            return 0f;

        float hpLost01 = 1f - Mathf.Clamp01(wildHP / wildMaxHP);
        float baseChance = 0.05f;
        float hpBonus = hpLost01 * 0.70f;

        string groupName = null;
        if (wildDef.Personality != null)
        {
            try { groupName = wildDef.Personality.group.ToString(); }
            catch { groupName = null; }
        }

        if (groupName == "Evasive")
            hpBonus *= 1.3f;

        float chance = baseChance + hpBonus;
        return Mathf.Clamp01(chance);
    }

    private void ApplyPendingGuardShieldForWild()
    {
        if (wildPendingGuardShield <= 0.01f) return;

        string name = wildDef ? wildDef.displayName : "Foe";
        float gain = wildPendingGuardShield;
        wildShieldHP += gain;
        wildPendingGuardShield = 0f;

        BattleLogger.Log($"{name} gains a guard shield of {Mathf.RoundToInt(gain)}!", LogScope.Battle);
    }

    private bool RollEnemyDefendSuccess()
    {
        float chance = Mathf.Clamp01(wildDefendCurrentSuccess);
        bool ok = UnityEngine.Random.value <= chance;

        if (ok)
        {
            wildDefendConsecutiveUses++;
            float next = defendFirstUseSuccess * Mathf.Pow(defendRepeatMultiplier, wildDefendConsecutiveUses);
            wildDefendCurrentSuccess = Mathf.Max(defendMinSuccess, next);
        }
        else
        {
            wildDefendConsecutiveUses = 0;
            wildDefendCurrentSuccess = defendFirstUseSuccess;
        }

        return ok;
    }

    private void ResetEnemyDefendStreak()
    {
        wildDefendConsecutiveUses = 0;
        wildDefendCurrentSuccess = defendFirstUseSuccess;
    }

    private void ApplyWildDefendStance()
    {
        string name = wildDef ? wildDef.displayName : "Foe";
        bool success = RollEnemyDefendSuccess();

        wildDefendActiveThisRound = success;

        if (feedback)
        {
            feedback.PlayDefendResult(BattleFeedbackManager.BattleFeedbackSide.Wild, success);
        }

        if (success)
        {
            BattleLogger.Log($"{name} is defending.", LogScope.Battle);
            BattleLogger.Log($"{name} will reduce the next hit and convert it into a shield for the following round.", LogScope.Battle);
        }
        else
        {
            BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
        }
    }

    private void SetPostBattleWinnerVisible(bool victory, bool escaped)
    {
        if (escaped)
        {
            if (playerPanel) playerPanel.SetActive(true);
            if (wildPanel) wildPanel.SetActive(false);
            return;
        }

        if (victory)
        {
            if (playerPanel) playerPanel.SetActive(true);
            if (wildPanel) wildPanel.SetActive(false);
        }
        else
        {
            if (playerPanel) playerPanel.SetActive(false);
            if (wildPanel) wildPanel.SetActive(true);
        }
    }

    private void ForceEndBattleEarly(bool victory, bool escaped = false)
    {
        SetIsPlayerTurn(false);
        pendingAction = PlayerAction.None;
        ResetStatusIcons();

        if (benchBtn1) benchBtn1.interactable = false;
        if (benchBtn2) benchBtn2.interactable = false;

        var result = new BattleResult
        {
            victory = victory,
            escaped = escaped,
            creditsGained = 0,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = 0f,
            critCount = 0,
            turnsSurvived = 0,
            damageTaken = 0,
            damageDealt = 0,
            gotFirstHit = false
        };

        onEnd?.Invoke(result);
        GameEvents.BattleFinished?.Invoke(result);
    }

    private IEnumerator Say(string line, BattleLineTag tags = BattleLineTag.None)
    {
        bool condensed = SettingsManager.I != null && SettingsManager.I.GetCondensedBattleText();
        bool autoCompress = SettingsManager.I != null && SettingsManager.I.GetCompressAutoBattleText();

        bool isAuto = (EncounterManager.I != null && EncounterManager.I.IsAutoMode) || !manualTurns;

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

    private void SetStatRowColorAndText(TextMeshProUGUI label, string statName, int baseVal, int finalVal, int minFinal = 1)
    {
        if (!label) return;

        finalVal = Mathf.Max(minFinal, finalVal);
        baseVal = Mathf.Max(minFinal, baseVal);

        int delta = finalVal - baseVal;

        if (delta > 0) label.color = StatBuff;
        else if (delta < 0) label.color = StatNerf;
        else label.color = StatNeutral;

        if (delta == 0)
            label.text = $"{statName}: {finalVal}";
        else
            label.text = $"{statName}: {finalVal} ({(delta > 0 ? "+" : "")}{delta})";
    }

    private void SetPlayerStatRowWithConditionals(
        TextMeshProUGUI label,
        string statName,
        int baseVal,
        int titleFinalVal,
        int condFlat,
        float condPct,
        int minFinal = 1)
    {
        int condDelta = Mathf.RoundToInt(condFlat + (baseVal * condPct));
        int combinedFinal = titleFinalVal + condDelta;
        combinedFinal = Mathf.Max(minFinal, combinedFinal);

        SetStatRowColorAndText(label, statName, baseVal, combinedFinal, minFinal);
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

    private void ResetStatusIcons()
    {
        if (!feedback) return;

        feedback.SetGuard(BattleFeedbackManager.BattleFeedbackSide.Player, false);
        feedback.SetGuard(BattleFeedbackManager.BattleFeedbackSide.Wild, false);

        feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Player, false);
        feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Wild, false);
    }

    /// <summary>
    /// Call after swaps / at round boundaries to reflect the CURRENT logical status.
    /// (Guard = defending this round, Charge = has charged next attack queued)
    /// </summary>
    private void RefreshStatusIconsFromState()
    {
        if (!feedback) return;

        // Guard status (this round only)
        feedback.SetGuard(BattleFeedbackManager.BattleFeedbackSide.Player, defendActiveThisRound);
        feedback.SetGuard(BattleFeedbackManager.BattleFeedbackSide.Wild, wildDefendActiveThisRound);

        // Charge status (persists until spent)
        bool playerCharged =
            chargedNextAttack != null &&
            activeIndex >= 0 &&
            activeIndex < chargedNextAttack.Length &&
            chargedNextAttack[activeIndex];

        feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Player, playerCharged);
        feedback.SetCharge(BattleFeedbackManager.BattleFeedbackSide.Wild, wildChargedNextAttack);
    }
    
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

    private void HandleBattleStatsChanged()
    {
        if (!inBattle) return;

        UpdatePlayerInfoUI();
        UpdateWildInfoUI();
    }


}
