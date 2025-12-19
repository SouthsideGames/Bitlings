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

    // NEW
    public int damageDealt;
    public bool gotFirstHit;
}

public class BattleManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Manual-turn settings
    // ─────────────────────────────────────────────────────────────────────────────
    private enum PlayerAction { None, Attack, Defend, Focus, Run }
    private enum EnemyAction { Attack, Defend, Focus, Run }

    [Header("Manual Turn Settings")]
    [SerializeField] private bool manualTurns = true;
    [SerializeField, Range(0f, 1f)] private float defendReducePct = 0.50f;
    [SerializeField, Range(0f, 1f)] private float guardConvertPct = 1.0f;
    [SerializeField, Range(0f, 2f)] private float chargeBonusPct = 0.5f;

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

    [Header("Attack VFX")]
    [SerializeField] private bool spawnAttackPrefabs = true;


    // ─────────────────────────────────────────────────────────────
    // POLISH
    // ─────────────────────────────────────────────────────────────
    [Header("Damage Number FX")]
    [SerializeField] private DamageNumberUI damageNumberPrefab;
    [SerializeField] private RectTransform playerDamageAnchor;
    [SerializeField] private RectTransform wildDamageAnchor;

    [Header("Damage Number Colors")]
    [SerializeField] private Color dmgNormalColor = Color.white;
    [SerializeField] private Color dmgCritColor = new Color(1f, 0.9f, 0.35f);  // gold-ish
    [SerializeField] private Color dmgWeakColor = new Color(0.55f, 0.8f, 1f);   // cyan-ish
    [SerializeField] private Color dmgResistColor = new Color(0.75f, 0.75f, 0.75f);

    [Header("Screen Shake")]
    [SerializeField] private Transform screenShakeRoot;        // Optional. If null, uses Camera.main
    [SerializeField] private float heavyHitShakeMagnitude = 12f;
    [SerializeField] private float heavyHitShakeDuration = 0.15f;
    [SerializeField] private float heavyHitThresholdPct = 0.30f; // 30% HP

    private bool _isPlayerTurn;
    public bool IsPlayerTurn => _isPlayerTurn;
    public event Action<bool> OnPlayerTurnChanged;

    private bool isResolvingPlayerTurn = false;
    private PlayerAction pendingAction = PlayerAction.None;
    private bool defendActiveThisRound = false;

    // ─────────────────────────────────────────────────────────────────────────────
    // Wild UI
    // ─────────────────────────────────────────────────────────────────────────────
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Player UI
    // ─────────────────────────────────────────────────────────────────────────────
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

    [Header("HP Shake Targets")]
    [SerializeField] private RectTransform playerHPShakeRoot;
    [SerializeField] private RectTransform wildHPShakeRoot;

    [Header("HP Shake Settings")]
    [SerializeField] private float hpShakeDuration = 0.25f;
    [SerializeField] private float hpShakeStrength = 8f;


    // ─────────────────────────────────────────────────────────────────────────────
    // Bench UI
    // ─────────────────────────────────────────────────────────────────────────────
    [Header("Bench UI")]
    [SerializeField] private Button benchBtn1;
    [SerializeField] private Button benchBtn2;
    [SerializeField] private Image benchImg1;
    [SerializeField] private Image benchImg2;
    [SerializeField] private TextMeshProUGUI benchHPText1;
    [SerializeField] private TextMeshProUGUI benchHPText2;

    // ─────────────────────────────────────────────────────────────────────────────
    // Pacing / Combat
    // ─────────────────────────────────────────────────────────────────────────────
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

    [Header("Status UI (Player)")]
    [SerializeField] private Image guardIcon;
    [SerializeField] private Image chargeIcon;
    [SerializeField] private TextMeshProUGUI playerShieldText;

    [Header("Status UI (Wild)")]
    [SerializeField] private Image wildGuardIcon;
    [SerializeField] private Image wildChargeIcon;
    [SerializeField] private TextMeshProUGUI wildShieldText;


    [Header("Battle Text Box")]
    [SerializeField] private BattleTextBoxUI battleTextBox;
    [SerializeField] private BattleBottomPanelToggle _bottomToggle;


    [Header("Debug")]
    [SerializeField] private bool debugIncomingMitigation = false;
    [SerializeField] private bool debugEffectivenessOutgoing = false;

    public bool NarrationLocked => _narrationLock;
    private bool _narrationLock;


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

    [Header("HP Bar Animation")]
    [SerializeField] private bool smoothHPBars = true;

    [SerializeField, Min(0.01f)]
    private float hpBarSecondsForFull = 0.6f;

    private Coroutine _playerHPAnimCR;
    private Coroutine _wildHPAnimCR;

    private int _turnIndex = 0;
    private bool inBattle;
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

    void Start()
    {
        if (benchBtn1) benchBtn1.onClick.AddListener(() => ClickBench(0));
        if (benchBtn2) benchBtn2.onClick.AddListener(() => ClickBench(1));

        if (SaveManager.Data != null && SaveManager.Data.settings != null)
            battleSpeed = Mathf.Clamp(SaveManager.Data.settings.battleSpeed, 0.25f, 5f);

        if (guardIcon) guardIcon.enabled = false;
        if (chargeIcon) chargeIcon.enabled = false;
        if (playerShieldText) playerShieldText.gameObject.SetActive(false);

        if (wildGuardIcon) wildGuardIcon.enabled = false;
        if (wildChargeIcon) wildChargeIcon.enabled = false;
        if (wildShieldText) wildShieldText.gameObject.SetActive(false);
    }

    void OnEnable()
    {
        GameEvents.BattleFinished += HandleBattleFinishedUIRefresh;
    }

    void OnDisable()
    {
        GameEvents.BattleFinished -= HandleBattleFinishedUIRefresh;
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
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Public API for action bar
    // ─────────────────────────────────────────────────────────────────────────────
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
    }


    // Legacy alias
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
        wildMaxHP = BattleCalc.CalcHP(wildDef, wildLevel) * 0.9f;
        wildHP = wildMaxHP;
        wildAttackPerTurn = BattleCalc.CalcBaseAttack(wildDef, wildLevel, 0, 0) * 0.9f;

        if (wildIcon) wildIcon.sprite = wildDef ? wildDef.icon : null;
        if (wildNameText) wildNameText.text = wildDef ? wildDef.displayName : "Wild";
        if (wildLevelText) wildLevelText.text = $"Lv {wildLevel}";
        if (wildHPBar) { wildHPBar.maxValue = wildMaxHP; wildHPBar.value = wildHP; }

        UpdateWildStatusUI();
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
            var def = MonsterLibraryLocator.GetById(owned.monsterId);
            if (!def) continue;

            teamIds[i] = owned.monsterId;
            teamDefs[i] = def;
            teamLevels[i] = owned.level;

            float baseMax = BattleCalc.CalcHP(def, owned.level);
            int bonusHP = Mathf.Max(0, owned.trainingBonus.hp);
            float finalMax = Mathf.Max(1f, baseMax + bonusHP);
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
                teamMaxHP[i] *= (1f + jobCtx[i].maxHpBonusPct);
                teamHP[i] = Mathf.Clamp(teamMaxHP[i] * pct, 0f, teamMaxHP[i]);
            }

            if (jobCtx[i].startShieldPctMaxHp > 0f)
            {
                float curMaxWithTitles = GetFinalMaxHPForIndex(i);
                shieldHP[i] = curMaxWithTitles * jobCtx[i].startShieldPctMaxHp;
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

            ApplyPendingGuardShieldForActive();
            ApplyPendingGuardShieldForWild();

            wildDefendActiveThisRound = false;
            UpdateWildStatusUI();

            BattleLogger.Log($"— Round {round} —", LogScope.Battle);
            yield return Wait(beginRoundDelay);

            _turnIndex++;
            TitlesAdapter.OnTurnAdvanced(_turnIndex);

            if (swappedFromKO)
            {
                ClampAndPushActiveHP();
                ApplyActiveToUI();
                RefreshBenchUI();
            }

            if (IsWildKO() || IsTeamKO())
            {
                if (CheckEnd()) break;
                round++;
                continue;
            }

            // SPEED CALC
            int pSpeedBase = BattleCalc.CalcSpeed(teamDefs[activeIndex], teamLevels[activeIndex]);

            var roster = SaveManager.Data?.team;
            if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
                pSpeedBase += Mathf.Max(0, roster[activeIndex].trainingBonus.spd);

            var jSpeed = (jobCtx != null &&
                        activeIndex >= 0 &&
                        activeIndex < jobCtx.Length)
                ? jobCtx[activeIndex]
                : null;

            if (jSpeed != null &&
                jSpeed.speedBuffTurns > 0 &&
                jSpeed.speedBonusPctFirstTurns != 0f)
            {
                pSpeedBase = Mathf.Max(
                    1,
                    Mathf.RoundToInt(pSpeedBase * (1f + jSpeed.speedBonusPctFirstTurns))
                );
            }

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

            int tempSPDFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0;
            int pSpeed = Mathf.Max(1, pSpeedAfterTitles + Mathf.Max(0, tempSPDFlat));
            int wSpeed = BattleCalc.CalcSpeed(wildDef, wildLevel);

            bool playerFirst;
            if (pSpeed > wSpeed) playerFirst = true;
            else if (pSpeed < wSpeed) playerFirst = false;
            else playerFirst = UnityEngine.Random.value < 0.5f;

            defendActiveThisRound = false;
            if (guardIcon) guardIcon.enabled = false;

            // Decide wild action once per round
            EnemyAction wildChoice = ChooseEnemyAction();

            if (playerFirst)
            {
                // TRUE PROTECT PRIORITY FOR WILD DEFEND
                if (wildChoice == EnemyAction.Defend)
                {
                    ApplyWildDefendStance();
                    // Wild will not attack this round; we won't call EnemyTurn for Defend later.
                }

                if (!IsWildKO() && !IsTeamKO())
                {
                    if (manualTurns) yield return WaitForPlayerChoiceAndResolve();
                    else yield return PlayerTurn();
                    if (CheckEnd()) break;
                    yield return Wait(hitPause);
                }

                if (!IsWildKO() && !IsTeamKO())
                {
                    // Only call EnemyTurn if wild did NOT purely Defend this round
                    if (wildChoice != EnemyAction.Defend)
                    {
                        yield return EnemyTurn(wildChoice);
                        if (CheckEnd()) break;
                        yield return Wait(hitPause);
                    }
                }
            }
            else
            {
                // ENEMY FIRST
                // Player still chooses their action BEFORE wild attacks
                if (!IsWildKO() && !IsTeamKO())
                {
                    PlayerAction queuedChoice = PlayerAction.Attack;

                    if (manualTurns)
                    {
                        SetIsPlayerTurn(true);
                        pendingAction = PlayerAction.None;

                        while (inBattle && pendingAction == PlayerAction.None)
                            yield return null;

                        queuedChoice = pendingAction;
                        pendingAction = PlayerAction.None;
                        SetIsPlayerTurn(false);

                        if (queuedChoice == PlayerAction.Defend)
                        {
                            string name = GetName(activeIndex);
                            bool success = RollDefendSuccess();

                            if (success)
                            {
                                defendActiveThisRound = true;
                                if (guardIcon) guardIcon.enabled = true;

                                BattleLogger.Log($"{name} is defending.", LogScope.Battle);
                                BattleLogger.Log(
                                    $"{name} will reduce the next hit and convert it into a shield for the following round.",
                                    LogScope.Battle
                                );

                                PlayDefendShieldFX(isPlayer: true);
                            }
                            else
                            {
                                defendActiveThisRound = false;
                                if (guardIcon) guardIcon.enabled = false;

                                BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
                            }

                            Punch(playerIcon);
                        }
                        else
                        {
                            ResetDefendStreak();
                            defendActiveThisRound = false;
                            if (guardIcon) guardIcon.enabled = false;
                        }
                    }

                    // Wild acts
                    yield return EnemyTurn(wildChoice);
                    if (CheckEnd()) break;
                    yield return Wait(hitPause);

                    if (!IsWildKO() && !IsTeamKO())
                    {
                        if (manualTurns)
                        {
                            switch (queuedChoice)
                            {
                                case PlayerAction.Attack:
                                    yield return PlayerTurn();
                                    break;

                                case PlayerAction.Focus:
                                    {
                                        ResetDefendStreak();

                                        if (chargedNextAttack != null &&
                                            activeIndex >= 0 &&
                                            activeIndex < chargedNextAttack.Length)
                                        {
                                            chargedNextAttack[activeIndex] = true;
                                        }

                                        if (chargeIcon) chargeIcon.enabled = true;

                                        BattleLogger.Log($"{GetName(activeIndex)} is charging.", LogScope.Battle);
                                        BattleLogger.Log(
                                            $"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.",
                                            LogScope.Battle
                                        );
                                        Punch(playerIcon);
                                        break;
                                    }

                                case PlayerAction.Run:
                                    {
                                        ResetDefendStreak();

                                        float chance = ComputeRunChance();
                                        bool escaped = UnityEngine.Random.value < chance;

                                        string name = GetName(activeIndex);

                                        if (escaped)
                                        {
                                            BattleLogger.Log(
                                                $"{name} has fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)",
                                                LogScope.Battle
                                            );
                                            EndBattle(false, true);
                                            yield break;
                                        }
                                        else
                                        {
                                            runAttempts++;
                                            BattleLogger.Log(
                                                $"Couldn't escape! (Run chance was {Mathf.RoundToInt(chance * 100f)}%)",
                                                LogScope.Battle
                                            );
                                        }
                                        break;
                                    }

                                case PlayerAction.Defend:
                                default:
                                    break;
                            }
                        }
                        else
                        {
                            yield return PlayerTurn();
                        }

                        if (CheckEnd()) break;
                        yield return Wait(hitPause);
                    }
                }
            }

            if (!IsWildKO() && !IsTeamKO())
            {
                if (jobCtx != null &&
                    activeIndex >= 0 &&
                    activeIndex < jobCtx.Length &&
                    jobCtx[activeIndex] != null)
                {
                    if (jobCtx[activeIndex].speedBuffTurns > 0) jobCtx[activeIndex].speedBuffTurns--;
                    if (jobCtx[activeIndex].critBuffTurns > 0) jobCtx[activeIndex].critBuffTurns--;
                    if (jobCtx[activeIndex].critResistBuffTurns > 0) jobCtx[activeIndex].critResistBuffTurns--;
                    if (jobCtx[activeIndex].dmgReduceBuffTurns > 0) jobCtx[activeIndex].dmgReduceBuffTurns--;
                }

                yield return Wait(endRoundDelay);
            }

            defendActiveThisRound = false;
            if (guardIcon) guardIcon.enabled = false;
            wildDefendActiveThisRound = false;
            UpdateWildStatusUI();
            round++;
        }

        turnCR = null;
    }

    private IEnumerator WaitForPlayerChoiceAndResolve()
    {
        SetIsPlayerTurn(true);

        while (inBattle && pendingAction == PlayerAction.None)
            yield return null;

        var choice = pendingAction;
        pendingAction = PlayerAction.None;
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

                    if (success)
                    {
                        defendActiveThisRound = true;
                        if (guardIcon) guardIcon.enabled = true;

                        BattleLogger.Log($"{name} is defending.", LogScope.Battle);
                        BattleLogger.Log(
                            $"{name} will reduce the next hit and convert it into a shield for the following round.",
                            LogScope.Battle
                        );

                        PlayDefendShieldFX(isPlayer: true);
                    }
                    else
                    {
                        defendActiveThisRound = false;
                        if (guardIcon) guardIcon.enabled = false;

                        BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
                    }

                    Punch(playerIcon);
                    break;
                }

            case PlayerAction.Focus:
                {
                    ResetDefendStreak();

                    if (chargedNextAttack != null &&
                        activeIndex >= 0 &&
                        activeIndex < chargedNextAttack.Length)
                    {
                        chargedNextAttack[activeIndex] = true;
                    }

                    if (chargeIcon) chargeIcon.enabled = true;

                    BattleLogger.Log($"{GetName(activeIndex)} is charging.", LogScope.Battle);
                    BattleLogger.Log(
                        $"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.",
                        LogScope.Battle
                    );

                    Punch(playerIcon);
                    break;
                }

            case PlayerAction.Run:
                {
                    ResetDefendStreak();

                    float chance = ComputeRunChance();
                    bool escaped = UnityEngine.Random.value < chance;

                    string name = GetName(activeIndex);

                    if (escaped)
                    {
                        BattleLogger.Log(
                            $"{name} has fled! (Run chance {Mathf.RoundToInt(chance * 100f)}%)",
                            LogScope.Battle
                        );
                        EndBattle(false, true);
                        yield break;
                    }
                    else
                    {
                        runAttempts++;
                        BattleLogger.Log(
                            $"Couldn't escape! (Run chance was {Mathf.RoundToInt(chance * 100f)}%)",
                            LogScope.Battle
                        );
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

        // If active slot is KO, try swap before acting
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
        {
            isResolvingPlayerTurn = false;
            yield break;
        }

        // ── Pokémon-style: announce → animate → resolve → results
        var playerDef = teamDefs[activeIndex];
        string attacker = GetName(activeIndex);
        string move = GetBasicMoveName(playerDef);
        string foeName = wildDef ? wildDef.displayName : "Foe";

        yield return Say($"{attacker} used {move}!");
        SpawnBasicAttackVfx(true);
        yield return Wait(0.10f);

        // ── Build ATK base (owned training + equipment flat)
        var roster = SaveManager.Data?.team;

        int equipFlat = 0;
        int trainingAtk = 0;

        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
        {
            var om = roster[activeIndex];
            equipFlat = Mathf.Max(0, om.flatAtkBonus);
            trainingAtk = Mathf.Max(0, om.trainingBonus.atk);
        }

        int permanentFlat = Mathf.Max(0, equipFlat + trainingAtk);

        float atkBaseF = BattleCalc.CalcBaseAttack(
            teamDefs[activeIndex],
            teamLevels[activeIndex],
            permanentFlat,
            0
        );
        int atkBase = Mathf.Max(1, Mathf.RoundToInt(atkBaseF));

        // Booster (temp) attack flat
        int tempFlatFromBoosters = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;
        float atkWithBoosterF = BattleCalc.CalcBaseAttack(
            teamDefs[activeIndex],
            teamLevels[activeIndex],
            permanentFlat,
            tempFlatFromBoosters
        );
        float atkBoosterMult = Mathf.Max(0.01f, atkWithBoosterF / Mathf.Max(1f, atkBase));

        // Titles context for stat calls
        var titleCtx = BuildTitleContextForActive();

        // Crit from base + job ctx
        var jctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float playerCrit = critChancePlayer;
        if (jctx != null)
        {
            playerCrit += jctx.critChanceFlat;
            if (jctx.critBuffTurns > 0)
                playerCrit += jctx.critChanceBonusFirstTurns;
        }
        playerCrit = Mathf.Clamp01(playerCrit);

        // Resolve hit (base damage/crit/effectiveness)
        var dr = BattleCalc.ResolveHit(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            null, wildDef, wildLevel,
            atkBase,
            playerCrit,
            critMultiplier,
            0
        );

        TitlesAdapter.OnAttackLanded(teamIds[activeIndex], dr.crit);
        if (dr.crit) _totalCritsThisBattle++;

        // Job bonuses (attack)
        if (jctx != null && jctx.attackBonusPct > 0f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.attackBonusPct)));

        if (jctx != null && jctx.usedFirstOutgoing == false && jctx.firstOutgoingBonus > 0f)
        {
            jctx.usedFirstOutgoing = true;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.firstOutgoingBonus)));
        }

        if (jctx != null && jctx.surgeApplied && jctx.surgeAtkBonusPct > 0f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.surgeAtkBonusPct)));

        // Titles: outgoing effectiveness adjustments
        float effMul = TitlesAdapter.GetEffectivenessMult(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]
        );
        if (!Mathf.Approximately(effMul, 1f))
        {
            int before = dr.damage;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * effMul));

            if (debugEffectivenessOutgoing)
            {
                string msg = $"[EffectivenessModTitle] MULT x{effMul:0.00}: {before} → {dr.damage}";
                try { BattleLogger.Log(msg, LogScope.Battle); } catch { }
                Debug.Log(msg);
            }
        }

        float effAdd = TitlesAdapter.GetEffectivenessAdd(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]
        );
        if (!Mathf.Approximately(effAdd, 0f) && dr.effectiveness > 0.0001f)
        {
            int before = dr.damage;
            float scale = (dr.effectiveness + effAdd) / dr.effectiveness;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * scale));

            if (debugEffectivenessOutgoing)
            {
                string msg = $"[EffectivenessModTitle] ADD +{effAdd:0.00} (E={dr.effectiveness:0.00}) → x{scale:0.00}: {before} → {dr.damage}";
                try { BattleLogger.Log(msg, LogScope.Battle); } catch { }
                Debug.Log(msg);
            }
        }

        // Tap boost (if used)
        float tap = TapBoost.I ? TapBoost.I.CurrentMultiplier : 1f;
        if (!Mathf.Approximately(tap, 1f))
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * tap));

        // Slot damage buff (temporary)
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

        // Booster multiplier
        if (!Mathf.Approximately(atkBoosterMult, 1f))
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * atkBoosterMult));

        // Player charge (Focus)
        if (chargedNextAttack != null &&
            activeIndex >= 0 &&
            activeIndex < chargedNextAttack.Length &&
            chargedNextAttack[activeIndex] &&
            chargeBonusPct > 0f)
        {
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + chargeBonusPct)));
            chargedNextAttack[activeIndex] = false;

            if (chargeIcon) chargeIcon.enabled = false;

            yield return Say($"{GetName(activeIndex)} unleashes a charged attack (+{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage)!");
        }

        // ── Wild Defend (Guard) reduction + convert to next-round shield
        float preventedByWildGuard = 0f;
        int dmgToApply = dr.damage;

        if (wildDefendActiveThisRound && defendReducePct > 0f)
        {
            float guardPct = Mathf.Clamp01(defendReducePct);
            int before = dmgToApply;
            int after = Mathf.Max(1, Mathf.RoundToInt(dmgToApply * (1f - guardPct)));
            preventedByWildGuard = Mathf.Max(0, before - after);
            dmgToApply = after;

            if (preventedByWildGuard > 0f)
                PlayDefendShieldFX(isPlayer: false);
        }

        // ── Wild shield absorbs first
        if (wildShieldHP > 0f)
        {
            float absorb = Mathf.Min(wildShieldHP, dmgToApply);
            wildShieldHP = Mathf.Max(0f, wildShieldHP - absorb);
            dmgToApply = Mathf.Max(0, dmgToApply - Mathf.RoundToInt(absorb));

            if (absorb > 0f)
                yield return Say($"{foeName}'s shield absorbed {Mathf.RoundToInt(absorb)}!");
        }

        // Convert prevented damage to next-round guard shield
        if (preventedByWildGuard > 0f && guardConvertPct > 0f)
        {
            float gain = preventedByWildGuard * guardConvertPct;
            wildPendingGuardShield += gain;
            yield return Say($"{foeName} stores {Mathf.RoundToInt(gain)} damage as a guard shield for the next round.");
        }

        // Apply damage
        wildHP = Mathf.Max(0f, wildHP - dmgToApply);
        _totalDamageDealtThisBattle += Mathf.Max(0, dmgToApply);
        PushHPBars();

        SpawnDamageNumber(dmgToApply, dr.crit, dr.effectiveness, hitPlayer: false);

        // Heavy hit shake
        float wRatio = wildMaxHP > 0.01f ? (float)dmgToApply / wildMaxHP : 0f;
        if (wRatio >= heavyHitThresholdPct || (dr.crit && wRatio >= heavyHitThresholdPct * 0.5f))
            ScreenShake(heavyHitShakeMagnitude, heavyHitShakeDuration);

        if (!playerLandedFirstHitThisBattle && dr.damage > 0)
            playerLandedFirstHitThisBattle = true;

        // Results narration
        yield return Say($"{attacker} hits {foeName} for {dmgToApply}!");

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) yield return Say("It's super effective!");
            else if (dr.effectiveness < 0.85f) yield return Say("It's not very effective...");
        }
        if (dr.crit) yield return Say("Critical hit!");

        // End-turn job heal
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

        Punch(playerIcon);

        // Your existing tracking tick (no-dmg/no-crit turn counters, etc.)
        FirePlayerEndTurnTicks(dealtDamageThisTurn: dr.damage > 0, critThisTurn: dr.crit);

        isResolvingPlayerTurn = false;
    }


    private IEnumerator EnemyTurn(EnemyAction choice)
    {
        // If our active slot is KO'd, try to autoswap first.
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
            yield break;

        // Non-defend actions get a tiny beat for readability
        if (choice != EnemyAction.Defend)
            yield return Wait(0.15f);

        // If wild does anything other than Defend, reset defend streak
        if (choice != EnemyAction.Defend)
            ResetEnemyDefendStreak();

        // ─────────────────────────────────────────────────────────────
        // DEFEND (Guard)
        // ─────────────────────────────────────────────────────────────
        if (choice == EnemyAction.Defend)
        {
            string name = wildDef ? wildDef.displayName : "Foe";
            bool success = RollEnemyDefendSuccess();

            if (success)
            {
                wildDefendActiveThisRound = true;
                UpdateWildStatusUI();

                yield return Say($"{name} is defending.");
                yield return Say($"{name} will reduce the next hit and convert it into a shield for the following round.");

                PlayDefendShieldFX(isPlayer: false);
            }
            else
            {
                wildDefendActiveThisRound = false;
                UpdateWildStatusUI();

                yield return Say($"{name} tried to defend, but it failed!");
            }

            Punch(wildIcon);
            yield break;
        }

        // ─────────────────────────────────────────────────────────────
        // FOCUS (Charge)
        // ─────────────────────────────────────────────────────────────
        if (choice == EnemyAction.Focus)
        {
            wildChargedNextAttack = true;

            string name = wildDef ? wildDef.displayName : "Foe";
            yield return Say($"{name} is charging up.");
            yield return Say($"Their next attack will deal +{Mathf.RoundToInt(chargeBonusPct * 100f)}% damage.");

            Punch(wildIcon);
            UpdateWildStatusUI();
            yield break;
        }

        // ─────────────────────────────────────────────────────────────
        // RUN
        // ─────────────────────────────────────────────────────────────
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

        // ─────────────────────────────────────────────────────────────
        // ATTACK
        // ─────────────────────────────────────────────────────────────
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
            yield break;

        string attackerName = wildDef ? wildDef.displayName : "Foe";
        string move = GetBasicMoveName(wildDef);
        string targetName = GetName(activeIndex);

        yield return Say($"{attackerName} used {move}!");
        SpawnBasicAttackVfx(false);
        yield return Wait(0.10f);

        int enemyAtk = Mathf.Max(1, Mathf.RoundToInt(wildAttackPerTurn));
        int defFlatBooster = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;

        var ctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float preHP = teamHP[activeIndex];

        // Training DEF contributes to flat DR
        int trainingFlatDef = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
            trainingFlatDef = Mathf.Max(0, roster[activeIndex].trainingBonus.def);

        // Titles damage filter (flat/%/crit immunity)
        var df = TitlesAdapter.GetDamageFilter(teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]);

        // Crit resist from jobs
        float playerCritResist = 0f;
        if (ctx != null)
        {
            playerCritResist += ctx.critResistFlat;
            if (ctx.critResistBuffTurns > 0)
                playerCritResist += ctx.critResistBonusFirstTurns;
        }

        float wildCritChance = df.cannotBeCrit ? 0f : Mathf.Clamp01(critChanceWild - playerCritResist);

        // Base resolve
        var dr = BattleCalc.ResolveHit(
            null, wildDef, wildLevel,
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            enemyAtk, wildCritChance, critMultiplier, 0
        );

        int baseRawDamage = dr.damage;
        bool critRolled = dr.crit;
        bool critNegatedByTitle = false;

        // If titles say "cannot be crit", rebuild without crit
        if (df.cannotBeCrit && dr.crit)
        {
            critNegatedByTitle = true;
            dr = BattleCalc.ResolveHit(
                null, wildDef, wildLevel,
                teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
                enemyAtk, 0f, critMultiplier, 0
            );
        }

        // Wild charge bonus
        if (wildChargedNextAttack && chargeBonusPct > 0f)
        {
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + chargeBonusPct)));
            wildChargedNextAttack = false;
            UpdateWildStatusUI();

            yield return Say($"{attackerName} unleashes a charged attack (+{Mathf.RoundToInt(chargeBonusPct * 100f)}% dmg)!");
        }

        // ── Incoming mitigation scalar (jobs/conditionals/defend)
        float incomingScalar = 1f;

        var cmods = GetConditionalModsForActive();
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

        // Player Defend (Guard)
        if (defendActiveThisRound && defendReducePct > 0f)
        {
            float guardPct = Mathf.Clamp01(defendReducePct);
            incomingScalar *= (1f - guardPct);

            float dmgBeforeGuard = dr.damage * scalarBeforeGuard;
            float dmgAfterGuard = dr.damage * incomingScalar;
            preventedByGuardRaw = Mathf.Max(0f, dmgBeforeGuard - dmgAfterGuard);

            if (preventedByGuardRaw > 0f)
                PlayDefendShieldFX(isPlayer: true);
        }

        int dmg_afterScalar = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));

        // Titles: incoming effectiveness multiplier (if you use it)
        float incomingEffMul = TitlesAdapter.GetIncomingEffectivenessMult(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            wildDef ? wildDef.type : MonsterType.None
        );
        if (!Mathf.Approximately(incomingEffMul, 1f))
            dmg_afterScalar = Mathf.Max(1, Mathf.RoundToInt(dmg_afterScalar * incomingEffMul));

        // Titles filter: % then flat
        float percentReduce = Mathf.Clamp01(df.percentReduce);
        int flatReduce = Mathf.Max(0, df.flatReduce);

        int dmg_afterPercent = (percentReduce > 0f)
            ? Mathf.Max(1, Mathf.RoundToInt(dmg_afterScalar * (1f - percentReduce)))
            : dmg_afterScalar;

        int totalFlatDR = flatReduce + Mathf.Max(0, defFlatBooster) + Mathf.Max(0, trainingFlatDef);
        int dmg_afterFlat = Mathf.Max(1, dmg_afterPercent - totalFlatDR);

        // Shield absorb
        float shieldBefore = (shieldHP != null && shieldHP.Length > activeIndex) ? shieldHP[activeIndex] : 0f;
        float shieldAbsorbF = 0f;

        int dmg_final = dmg_afterFlat;
        if (shieldBefore > 0f)
        {
            shieldAbsorbF = Mathf.Min(shieldBefore, dmg_final);
            shieldHP[activeIndex] = Mathf.Max(0f, shieldBefore - shieldAbsorbF);
            dmg_final = Mathf.Max(1, dmg_final - Mathf.RoundToInt(shieldAbsorbF));

            if (shieldAbsorbF > 0f)
                yield return Say($"{GetName(activeIndex)}'s shield absorbed {Mathf.RoundToInt(shieldAbsorbF)}!");
        }

        // Apply damage
        teamHP[activeIndex] = Mathf.Max(0f, teamHP[activeIndex] - dmg_final);
        ClampAndPushActiveHP();

        SpawnDamageNumber(dmg_final, dr.crit && !df.cannotBeCrit, dr.effectiveness, hitPlayer: true);

        float maxHP = GetFinalMaxHPForIndex(activeIndex);
        float ratio = maxHP > 0.01f ? (float)dmg_final / maxHP : 0f;
        if (ratio >= heavyHitThresholdPct || (dr.crit && ratio >= heavyHitThresholdPct * 0.5f))
            ScreenShake(heavyHitShakeMagnitude, heavyHitShakeDuration);

        // Convert prevented dmg → next-round guard shield
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

        // Results narration
        yield return Say($"{attackerName} hits {GetName(activeIndex)} for {dmg_final}!");

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) yield return Say("It's super effective!");
            else if (dr.effectiveness < 0.85f) yield return Say("It's not very effective...");
        }

        if (dr.crit && !df.cannotBeCrit)
            yield return Say("Critical hit!");
        else if (critRolled && critNegatedByTitle)
            yield return Say("The critical hit was negated!");

        if (dr.crit && !df.cannotBeCrit)
            _totalCritsThisBattle++;

        _totalDamageTakenThisBattle += dmg_final;

        if (!playerTookFirstIncomingThisBattle)
            playerTookFirstIncomingThisBattle = true;

        // Job rescue + surge checks (same as your current logic)
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
                AudioManager.I.PlaySfx(SfxType.Heal);
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
                AudioManager.I.PlaySfx(SfxType.Clutch);
            }
        }

        Punch(wildIcon);
    }


    private bool CheckEnd()
    {
        if (IsWildKO())
        {
            BattleLogger.Log("Wild monster fainted!", LogScope.Battle);
            AudioManager.I.PlaySfx(SfxType.KO);
            EndBattle(true);
            return true;
        }
        if (IsTeamKO())
        {
            BattleLogger.Log("Your team is unable to battle!", LogScope.Battle);
            AudioManager.I.PlaySfx(SfxType.KO);
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
        benchBtn1.interactable = false;
        benchBtn2.interactable = false;
        pendingAction = PlayerAction.None;
        defendActiveThisRound = false;

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

        for (int i = 0; i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            for (int j = 0; j < ownedList.Count; j++)
            {
                var o = ownedList[j];
                if (!string.IsNullOrEmpty(o.monsterId) && o.monsterId == t.monsterId)
                {
                    o.currentHP = Mathf.Max(0, t.currentHP);
                    o.lastHPUnix = nowUnix;
                    ownedList[j] = o;
                    break;
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
            EncounterManager.I.NotifyAuto_TeamKO();
        }

        SetPostBattleWinnerVisible(victory, escaped);


        if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            TitlesAdapter.OnBattleEnd(teamIds[activeIndex], victory, wildDef, wildLevel);

        // Let EncounterManager control summary timing.
        // It already queues PostBattleSummaryManager.NotifyBattleEnd(...) and decides when to Flush.
        onEnd?.Invoke(result);

        GameEvents.BattleFinished?.Invoke(result);
    }



    private void SetHPBarAnimated(
        Slider bar,
        ref Coroutine animCR,
        float targetValue,
        float maxValue
    )
    {
        if (!bar) return;

        // Clamp and set max
        maxValue = Mathf.Max(1f, maxValue);
        bar.maxValue = maxValue;
        targetValue = Mathf.Clamp(targetValue, 0f, maxValue);

        // If animation is disabled or object is not active, just snap
        if (!smoothHPBars || !gameObject.activeInHierarchy)
        {
            if (animCR != null)
            {
                StopCoroutine(animCR);
                animCR = null;
            }
            bar.value = targetValue;
            return;
        }

        float current = bar.value;

        if (current > targetValue)
        {
            if (bar == playerHPBar)
                PlayHPShake(playerHPShakeRoot);
            else if (bar == wildHPBar)
                PlayHPShake(wildHPShakeRoot);
        }

        // If already at target, just snap & clear any old coroutine
        if (Mathf.Approximately(current, targetValue))
        {
            if (animCR != null)
            {
                StopCoroutine(animCR);
                animCR = null;
            }
            bar.value = targetValue;
            return;
        }

        // Restart animation from current -> target
        if (animCR != null) StopCoroutine(animCR);
        animCR = StartCoroutine(Co_AnimateHPBar(bar, current, targetValue));
    }

    private IEnumerator Co_AnimateHPBar(Slider bar, float start, float end)
    {
        if (!bar)
            yield break;

        float max = Mathf.Max(1f, bar.maxValue);
        float distance = Mathf.Abs(end - start);

        // Duration scales with how big the HP change is, just like Pokémon
        float duration = hpBarSecondsForFull * (distance / max);
        duration = Mathf.Max(0.05f, duration); // tiny hits still visible

        float t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / duration;
            float v = Mathf.Lerp(start, end, t);
            bar.value = v;
            yield return null;
        }

        bar.value = end;
    }


    private void ClampAndPushActiveHP()
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);
        teamHP[activeIndex] = Mathf.Min(teamHP[activeIndex], curMax);

        // Animated player bar
        SetHPBarAnimated(
            playerHPBar,
            ref _playerHPAnimCR,
            teamHP[activeIndex],
            curMax
        );

        // Animated wild bar
        SetHPBarAnimated(
            wildHPBar,
            ref _wildHPAnimCR,
            wildHP,
            wildMaxHP
        );

        UpdatePlayerInfoUI();
        UpdateShieldUI();
        UpdateWildStatusUI();


    }


    private void PushHPBars()
    {
        // Wild
        SetHPBarAnimated(
            wildHPBar,
            ref _wildHPAnimCR,
            wildHP,
            wildMaxHP
        );

        // Player
        float curMax = GetFinalMaxHPForIndex(activeIndex);
        SetHPBarAnimated(
            playerHPBar,
            ref _playerHPAnimCR,
            teamHP[activeIndex],
            curMax
        );

        UpdatePlayerInfoUI();
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
        if (manualTurns && !IsPlayerTurn) return;

        List<int> others = new();
        for (int i = 0; i < teamCount; i++)
            if (i != activeIndex) others.Add(i);

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

                BattleLogger.Log(
                    $"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.",
                    LogScope.Battle
                );

                teamPendingBuffPct[activeIndex] = 0f;
                teamPendingBuffTurns[activeIndex] = 0;
            }
        }

        BattleLogger.Log($"Swapped to {GetName(activeIndex)}!", LogScope.Battle);
        Punch(playerIcon);
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

                    BattleLogger.Log(
                        $"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.",
                        LogScope.Battle
                    );

                    teamPendingBuffPct[activeIndex] = 0f;
                    teamPendingBuffTurns[activeIndex] = 0;
                }
            }

            BattleLogger.Log($"Auto-swapped to {GetName(activeIndex)}!", LogScope.Battle);
            return true;
        }

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

    private void Punch(Graphic g)
    {
        if (!g) return;
        var rt = g.rectTransform;
        LeanTween.scale(rt, Vector3.one * 1.06f, 0.08f).setLoopPingPong(1);
    }

    public void TryAddHPToActive(float amount)
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);
        teamHP[activeIndex] = Mathf.Clamp(teamHP[activeIndex] + amount, 0f, curMax);
        ClampAndPushActiveHP();
    }

    private string Pct(float v) => $"{Mathf.RoundToInt(v * 100f)}%";
    private string HpVal(float v) => Mathf.RoundToInt(v).ToString();

    private string BuildPassiveSummary(int idx)
    {
        if (jobCtx == null || idx < 0 || idx >= jobCtx.Length || jobCtx[idx] == null) return null;
        var c = jobCtx[idx];

        List<string> tags = new List<string>();
        switch (c.job)
        {
            case JobType.Gym:
                if (c.maxHpBonusPct > 0f) tags.Add($"Gym +{Pct(c.maxHpBonusPct)} Max HP");
                break;
            case JobType.Forge:
                if (c.firstOutgoingBonus > 0f) tags.Add($"Forge +{Pct(c.firstOutgoingBonus)} first hit");
                break;
            case JobType.PowerPlant:
                if (c.speedBuffTurns > 0 && c.speedBonusPctFirstTurns > 0f) tags.Add($"Power +{Pct(c.speedBonusPctFirstTurns)} SPD (2t)");
                break;
            case JobType.Quarry:
                if (c.defenseBonusPct > 0f) tags.Add($"Quarry −{Pct(c.defenseBonusPct)} dmg taken");
                break;
            case JobType.Grove:
                if (c.endTurnHealPct > 0f) tags.Add($"Grove +{Pct(c.endTurnHealPct)} HP/turn");
                break;
            case JobType.Workshop:
                if (c.critBuffTurns > 0 && c.critChanceBonusFirstTurns > 0f) tags.Add($"Workshop +{Pct(c.critChanceBonusFirstTurns)} crit (2t)");
                break;
            case JobType.Harbor:
                if (c.startShieldPctMaxHp > 0f && teamMaxHP != null && idx < teamMaxHP.Length)
                    tags.Add($"Harbor shield {HpVal(GetFinalMaxHPForIndex(idx) * c.startShieldPctMaxHp)}");
                if (c.endTurnHealPct > 0f && c.regenTurns > 0) tags.Add($"Harbor +{Pct(c.endTurnHealPct)} HP (2t)");
                break;
            case JobType.CryoLab:
                if (c.critResistFlat > 0f) tags.Add($"Cryo +{Pct(c.critResistFlat)} crit resist");
                break;
            case JobType.Observatory:
                if (c.critChanceFlat > 0f) tags.Add($"Observ +{Pct(c.critChanceFlat)} crit");
                if (c.critResistBuffTurns > 0 && c.critResistBonusFirstTurns > 0f) tags.Add($"Observ +{Pct(c.critResistBonusFirstTurns)} crit resist (2t)");
                break;
            case JobType.Containment:
                if (c.baseDamageReducePct > 0f) tags.Add($"Contain −{Pct(c.baseDamageReducePct)} dmg taken");
                break;
            case JobType.WyrmDen:
                if (c.surgeAtkBonusPct > 0f) tags.Add($"Wyrm +{Pct(c.surgeAtkBonusPct)} ATK @ <50% HP");
                break;
            case JobType.ShadowMarket:
                if (c.firstIncomingReduce > 0f) tags.Add($"Shadow −{Pct(c.firstIncomingReduce)} first hit taken");
                break;
            case JobType.Sanctum:
                if (c.startShieldPctMaxHp > 0f && teamMaxHP != null && idx < teamMaxHP.Length)
                    tags.Add($"Sanctum shield {HpVal(GetFinalMaxHPForIndex(idx) * c.startShieldPctMaxHp)}");
                if (c.dmgReduceBuffTurns > 0 && c.dmgReduceFirstTurns > 0f) tags.Add($"Sanctum −{Pct(c.dmgReduceFirstTurns)} dmg (2t)");
                break;
            case JobType.Clinic:
                if (c.rescueHealPct > 0f)
                    tags.Add($"Clinic triage +{Pct(c.rescueHealPct)} @ <{Pct(c.rescueThreshold)} HP");
                break;
            case JobType.Mine:
                if (c.attackBonusPct > 0f) tags.Add($"Mine +{Pct(c.attackBonusPct)} ATK");
                break;
        }

        if (tags.Count == 0) return null;
        string name = GetName(idx);
        return $"{name}: " + string.Join(" • ", tags);
    }

    private void FirePlayerEndTurnTicks(bool dealtDamageThisTurn, bool critThisTurn)
    {
        playerNoDmgTurns = dealtDamageThisTurn ? 0 : Mathf.Min(playerNoDmgTurns + 1, 99);
        playerNoCritTurns = critThisTurn ? 0 : Mathf.Min(playerNoCritTurns + 1, 99);
    }

    private void UpdateWildInfoUI()
    {
        if (!wildDef) return;

        int dispHP = Mathf.RoundToInt(BattleCalc.CalcHP(wildDef, wildLevel));
        int dispATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(wildDef, wildLevel, 0, 0));
        int dispDEF = BattleCalc.CalcDefense(wildDef, wildLevel);
        int dispSPD = BattleCalc.CalcSpeed(wildDef, wildLevel);

        if (wildIdText) wildIdText.text = $"ID: {wildDef.id}";
        if (wildTypeText) wildTypeText.text = $"TYPE: {wildDef.type}";
        if (wildRarityText) wildRarityText.text = $"RARITY: {wildDef.rarity}";
        if (wildLevelText) wildLevelText.text = $"LVL: {wildLevel}";
        if (wildHPText) wildHPText.text = $"HP: {dispHP}";
        if (wildATKText) wildATKText.text = $"ATK: {dispATK}";
        if (wildDEFText) wildDEFText.text = $"DEF: {dispDEF}";
        if (wildSPDText) wildSPDText.text = $"SPD: {dispSPD}";
    }

    private void UpdatePlayerInfoUI()
    {
        if (activeIndex < 0 || teamDefs == null || activeIndex >= teamDefs.Length) return;

        var def = teamDefs[activeIndex];
        if (!def) return;

        int lvl = (teamLevels != null && activeIndex < teamLevels.Length) ? teamLevels[activeIndex] : 1;

        int baseHP = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        int baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
        int baseDEF = BattleCalc.CalcDefense(def, lvl);
        int baseSPD = BattleCalc.CalcSpeed(def, lvl);

        int bonusHP = 0;
        int bonusATK = 0;
        int bonusDEF = 0;
        int bonusSPD = 0;

        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
        {
            var tb = roster[activeIndex].trainingBonus;
            bonusHP = Mathf.Max(0, tb.hp);
            bonusATK = Mathf.Max(0, tb.atk);
            bonusDEF = Mathf.Max(0, tb.def);
            bonusSPD = Mathf.Max(0, tb.spd);
        }

        baseHP += bonusHP;
        baseATK += bonusATK;
        baseDEF += bonusDEF;
        baseSPD += bonusSPD;

        int tempHPFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        int tempATKFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;
        int tempDEFFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;
        int tempSPDFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0;

        int equippedFlatATK = 0;
        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
            equippedFlatATK = Mathf.Max(0, roster[activeIndex].flatAtkBonus);

        var ctx = TitleContext.Empty;
        ctx.ownedId = (teamIds != null && activeIndex < teamIds.Length) ? teamIds[activeIndex] : "";

        int hpBaseForCtx = Mathf.Max(1, baseHP + tempHPFlat);
        float currentHP = (teamHP != null && activeIndex < teamHP.Length) ? teamHP[activeIndex] : hpBaseForCtx;
        ctx.selfHp01 = Mathf.Clamp01(currentHP / Mathf.Max(1f, hpBaseForCtx));

        ctx.alliesAlive = GetAlliesAliveNotIncludingActive();
        ctx.winStreak = GetWinStreakSafe();

        var cmods = GetConditionalModsForActive();

        if (playerIdText) playerIdText.text = $"ID: {def.id}";
        if (playerTypeText) playerTypeText.text = $"TYPE: {def.type}";
        if (playerRarityText) playerRarityText.text = $"RARITY: {def.rarity}";
        if (playerLevelText) playerLevelText.text = $"LVL: {lvl}";

        int hpBaseForDisplay = hpBaseForCtx;
        float hpFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "HP", ctx, hpBaseForDisplay);
        int hpFinal = Mathf.Max(1, Mathf.RoundToInt(hpFinalF));
        if (playerHPText)
        {
            SetColoredStat(playerHPText, "HP", hpBaseForDisplay, hpFinal);
            playerHPText.text += CondTag(hpBaseForDisplay, 0, cmods.hpPct);
        }

        int atkBaseForDisplay = Mathf.Max(1, baseATK + equippedFlatATK + tempATKFlat);
        float atkFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Attack", ctx, atkBaseForDisplay);
        int atkFinal = Mathf.Max(1, Mathf.RoundToInt(atkFinalF));
        if (playerATKText)
        {
            SetColoredStat(playerATKText, "ATK", atkBaseForDisplay, atkFinal);
            playerATKText.text += CondTag(atkBaseForDisplay, cmods.atkFlat, cmods.atkPct);
        }

        int defBaseForDisplay = Mathf.Max(0, baseDEF + tempDEFFlat);
        float defFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Defense", ctx, defBaseForDisplay);
        int defFinal = Mathf.Max(0, Mathf.RoundToInt(defFinalF));
        if (playerDEFText)
        {
            SetColoredStat(playerDEFText, "DEF", defBaseForDisplay, defFinal);
            playerDEFText.text += CondTag(defBaseForDisplay, cmods.defFlat, cmods.defPct);
        }

        int spdBaseForDisplay = Mathf.Max(1, baseSPD + tempSPDFlat);
        float spdFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Speed", ctx, spdBaseForDisplay);
        int spdFinal = Mathf.Max(1, Mathf.RoundToInt(spdFinalF));
        if (playerSPDText)
        {
            SetColoredStat(playerSPDText, "SPD", spdBaseForDisplay, spdFinal);
            playerSPDText.text += CondTag(spdBaseForDisplay, cmods.spdFlat, cmods.spdPct);
        }

        bool resistOn = BattleTempBuffs.I && BattleTempBuffs.I.IsTypeResistActive();
        if (resistOn && playerRarityText) playerRarityText.text += " [Resist]";
    }

    private void ApplyActiveToUI()
    {
        var def = teamDefs[activeIndex];
        var lvl = teamLevels[activeIndex];
        if (playerIcon) playerIcon.sprite = def ? (def.backIcon ? def.backIcon : def.icon) : null;
        if (playerNameText) playerNameText.text = def ? def.displayName : "";
        if (playerLevelText) playerLevelText.text = $"Lv {lvl}";
        UpdatePlayerInfoUI();
        UpdateChargeIconForActive();
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

    private void SetCombatPanels(bool on)
    {
        if (wildPanel) wildPanel.SetActive(on);
        if (playerPanel) playerPanel.SetActive(on);
    }

    CanvasGroup EnsureCanvasGroup(GameObject go)
    {
        if (!go) return null;
        var cg = go.GetComponent<CanvasGroup>();
        if (!cg) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private IEnumerator Co_RevealPanelsThenStart(CanvasGroup wildCG, CanvasGroup playerCG, float duration)
    {
        float dur = Mathf.Max(0f, duration);
        bool usedTween = false;

        if (wildCG)
        {
            LeanTween.alphaCanvas(wildCG, 1f, dur);
            usedTween = true;
        }
        if (playerCG)
        {
            LeanTween.alphaCanvas(playerCG, 1f, dur);
            usedTween = true;
        }

        if (dur > 0f)
        {
            if (usedTween)
                yield return new WaitForSecondsRealtime(dur);
            else
            {
                float t = 0f;
                while (t < dur)
                {
                    t += Time.unscaledDeltaTime;
                    float a = Mathf.Clamp01(t / dur);
                    if (wildCG) wildCG.alpha = a;
                    if (playerCG) playerCG.alpha = a;
                    yield return null;
                }
            }
        }

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

        // Personality flair
        string personalityLabel = GetWildPersonalityLabel();
        if (!string.IsNullOrEmpty(personalityLabel) && wildDef && wildDef.Personality != null)
        {
            if (!string.IsNullOrEmpty(wildDef.Personality.description))
                BattleLogger.Log($"Personality: {personalityLabel} – {wildDef.Personality.description}", LogScope.Battle);
            else
                BattleLogger.Log($"Personality: {personalityLabel}.", LogScope.Battle);
        }

        for (int i = 0; i < teamCount; i++)
        {
            var summary = BuildPassiveSummary(i);
            if (!string.IsNullOrEmpty(summary))
                BattleLogger.Log(summary, LogScope.Battle);
        }

        PostBattleSummaryManager.I?.NotifyBattleStart();

        if (activeIndex >= 0 && teamIds != null && activeIndex < teamIds.Length)
            TitlesAdapter.OnBattleStart(teamIds[activeIndex], wildDef, wildLevel);

        if (turnCR != null) StopCoroutine(turnCR);
        turnCR = StartCoroutine(TurnLoop());
        yield break;
    }

    private TitleStatMods GetTitleModsForIndex(int idx)
    {
        if (teamIds != null && idx >= 0 && idx < teamIds.Length && !string.IsNullOrEmpty(teamIds[idx]))
            return TitlesAdapter.GetBattleStatMods(teamIds[idx]);
        return default;
    }

    public float GetActiveMaxHP(float baseMax, int idx = -1)
    {
        float v = Mathf.Max(1f, baseMax);

        if (idx >= 0)
        {
            var tmods = GetTitleModsForIndex(idx);
            if (tmods.hpPct > 0f) v *= (1f + tmods.hpPct);
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

    private TitleStatMods GetConditionalModsForActive()
    {
        if (teamIds == null || activeIndex < 0 || activeIndex >= teamIds.Length) return default;

        string ownedId = teamIds[activeIndex];
        var def = teamDefs[activeIndex];
        int lvl = teamLevels[activeIndex];

        float curMax = GetFinalMaxHPForIndex(activeIndex);
        float hp01 = curMax > 0.01f ? Mathf.Clamp01(teamHP[activeIndex] / curMax) : 0f;

        int alliesAlive = 0;
        for (int i = 0; i < teamCount; i++)
            if (i != activeIndex && teamHP[i] > 0.01f) alliesAlive++;

        int winStreak = (EncounterManager.I != null) ? EncounterManager.I.CurrentWinStreak : 0;

        TitleContext ctx = TitleContext.Empty;
        ctx.selfHp01 = hp01;
        ctx.alliesAlive = alliesAlive;
        ctx.winStreak = winStreak;

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
            winStreak = streak
        };
        return ctx;
    }

    private void SetColoredStat(TextMeshProUGUI label, string name, int baseVal, int finalVal)
    {
        if (!label) return;

        int delta = finalVal - baseVal;
        if (delta == 0)
        {
            label.text = $"{name}: {finalVal}";
            label.color = StatNeutral;
            return;
        }

        string sign = delta > 0 ? "+" : "";
        label.text = $"{name}: {finalVal} ({sign}{delta})";
        label.color = delta > 0 ? StatBuff : StatNerf;
    }

    private string CondTag(int baseVal, int flat, float pct)
    {
        float raw = flat + (baseVal * pct);
        int delta = Mathf.RoundToInt(raw);
        if (delta == 0) return "";
        string sign = delta > 0 ? "+" : "";
        return $" {{cond {sign}{delta}}}";
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

    private void MitLog(string text)
    {
        try { BattleLogger.Log(text, LogScope.Battle); } catch { }
        Debug.Log(text);
    }

    private void MitLogOncePerTurnHeader(bool critRolled, bool critNegated)
    {
        MitLog($"[Mitigation] {GetName(activeIndex)} | Crit Rolled: {(critRolled ? "Yes" : "No")} | Negated by Title: {(critNegated ? "Yes" : "No")}");
    }

    private int GetPlayerEffectiveSpeedForRun()
    {
        if (activeIndex < 0 || teamDefs == null || activeIndex >= teamDefs.Length || teamDefs[activeIndex] == null)
            return 1;

        int spd = BattleCalc.CalcSpeed(teamDefs[activeIndex], teamLevels[activeIndex]);

        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
            spd += Mathf.Max(0, roster[activeIndex].trainingBonus.spd);

        var j = (jobCtx != null) ? jobCtx[activeIndex] : null;
        if (j != null && j.speedBuffTurns > 0 && j.speedBonusPctFirstTurns != 0f)
            spd = Mathf.Max(1, Mathf.RoundToInt(spd * (1f + j.speedBonusPctFirstTurns)));

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

        BattleLogger.Log(
            $"{GetName(activeIndex)} gains a guard shield of {Mathf.RoundToInt(gain)}!",
            LogScope.Battle
        );

        ClampAndPushActiveHP();
    }

    private void UpdateChargeIconForActive()
    {
        if (!chargeIcon) return;

        bool charged =
            chargedNextAttack != null &&
            activeIndex >= 0 &&
            activeIndex < chargedNextAttack.Length &&
            chargedNextAttack[activeIndex];

        chargeIcon.enabled = charged;
    }

    private void UpdateShieldUI()
    {
        if (!playerShieldText) return;

        float shield = (shieldHP != null &&
                        activeIndex >= 0 &&
                        activeIndex < shieldHP.Length)
            ? shieldHP[activeIndex]
            : 0f;

        if (shield > 0f)
        {
            playerShieldText.gameObject.SetActive(true);
            playerShieldText.text = $"Shield: {Mathf.CeilToInt(shield)}";
        }
        else
        {
            playerShieldText.gameObject.SetActive(false);
        }
    }


    // Helper: get personality label for wild
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

    private void SpawnBasicAttackVfx(bool isPlayerSide)
    {
        if (!spawnAttackPrefabs) return;

        MonsterDataSO def = isPlayerSide
            ? (teamDefs != null && activeIndex >= 0 && activeIndex < teamDefs.Length ? teamDefs[activeIndex] : null)
            : wildDef;

        if (!def || !def.basicAttackPrefab) return;

        Transform spawnRoot = null;
        if (EncounterManager.I != null)
        {
            // Prefab appears on the *target* side
            spawnRoot = isPlayerSide
                ? EncounterManager.I.EnemySpawnPoint
                : EncounterManager.I.PlayerSpawnPoint;
        }

        Vector3 pos = spawnRoot ? spawnRoot.position : Vector3.zero;
        Quaternion rot = spawnRoot ? spawnRoot.rotation : Quaternion.identity;

        var inst = Instantiate(def.basicAttackPrefab, pos, rot);
        if (spawnRoot) inst.transform.SetParent(spawnRoot, worldPositionStays: true);

        float life = Mathf.Max(0f, def.basicAttackPrefabLifetime);
        if (life > 0f) Destroy(inst, life);
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

        // Fallback: if no Personality, use a simple HP-based bias.
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

        float hpLost01 = 1f - Mathf.Clamp01(wildHP / wildMaxHP); // 0 = full HP, 1 = nearly KO
        float baseChance = 0.05f;            // 5% even at full HP
        float hpBonus = hpLost01 * 0.70f; // up to +70% at 1 HP

        // Personality tweak: Evasive has higher success when they *do* try to run
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
        UpdateWildStatusUI();

        BattleLogger.Log(
            $"{name} gains a guard shield of {Mathf.RoundToInt(gain)}!",
            LogScope.Battle
        );
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

        if (success)
        {
            wildDefendActiveThisRound = true;
            UpdateWildStatusUI();
            BattleLogger.Log($"{name} is defending.", LogScope.Battle);
            BattleLogger.Log(
                $"{name} will reduce the next hit and convert it into a shield for the following round.",
                LogScope.Battle
            );
        }
        else
        {
            wildDefendActiveThisRound = false;
            UpdateWildStatusUI();
            BattleLogger.Log($"{name} tried to defend, but it failed!", LogScope.Battle);
        }

        Punch(wildIcon);
    }

    private void SpawnDamageNumber(
        int amount,
        bool isCrit,
        float effectiveness,
        bool hitPlayer
    )
    {
        if (!damageNumberPrefab) return;

        RectTransform anchor = hitPlayer ? playerDamageAnchor : wildDamageAnchor;
        if (!anchor) return;

        var inst = Instantiate(damageNumberPrefab, anchor);
        var color = dmgNormalColor;

        if (isCrit)
        {
            color = dmgCritColor;
        }
        else
        {
            if (effectiveness > 1.25f) color = dmgWeakColor;   // super-effective
            else if (effectiveness < 0.85f) color = dmgResistColor; // not very effective
        }

        inst.Init(amount, color);
    }

    private void PlayDefendShieldFX(bool isPlayer)
    {
        // Re-use your existing icon punch, plus a shimmer on guard icon
        if (isPlayer)
        {
            if (guardIcon)
            {
                // Quick punch scale
                Punch(guardIcon);

                // Shimmer alpha
                var g = guardIcon;
                float startA = g.color.a;
                LeanTween.value(g.gameObject, 0.35f, startA, 0.35f)
                    .setOnUpdate(a =>
                    {
                        var c = g.color;
                        c.a = a;
                        g.color = c;
                    });
            }
        }
        else
        {
            if (wildIcon) Punch(wildIcon);
        }
    }

    private void ScreenShake(float magnitude, float duration)
    {
        Transform target = screenShakeRoot;
        if (!target)
        {
            var cam = Camera.main;
            if (cam) target = cam.transform;
        }
        if (!target) return;

        Vector3 original = target.localPosition;
        float endTime = Time.unscaledTime + duration;

        // Simple manual shake using LeanTween value
        LeanTween.value(gameObject, 0f, magnitude, duration)
            .setOnUpdate(val =>
            {
                if (!target) return;
                float offset = Mathf.Sin(Time.unscaledTime * 80f) * val;
                target.localPosition = original + new Vector3(offset, 0f, 0f);
            })
            .setOnComplete(() =>
            {
                if (target) target.localPosition = original;
            });
    }

    private void PlayHPShake(RectTransform target)
    {
        if (!target) return;

        // Cancel existing shake if still running
        LeanTween.cancel(target.gameObject);

        // Reset to original
        Vector3 originalPos = target.anchoredPosition;

        // Apply shake
        LeanTween.moveX(target, originalPos.x + UnityEngine.Random.Range(-hpShakeStrength, hpShakeStrength), hpShakeDuration)
            .setEasePunch()
            .setOnComplete(() =>
            {
                // Restore original position after shaking
                target.anchoredPosition = originalPos;
            });
    }

    private void SetPostBattleWinnerVisible(bool victory, bool escaped)
    {
        // Escaped = no winner. Keep player visible, hide the wild.
        if (escaped)
        {
            if (playerPanel) playerPanel.SetActive(true);
            if (wildPanel) wildPanel.SetActive(false);
            return;
        }

        // Victory => show player, hide wild
        if (victory)
        {
            if (playerPanel) playerPanel.SetActive(true);
            if (wildPanel) wildPanel.SetActive(false);
        }
        // Defeat => show wild, hide player
        else
        {
            if (playerPanel) playerPanel.SetActive(false);
            if (wildPanel) wildPanel.SetActive(true);
        }
    }


    private void UpdateWildStatusUI()
    {
        if (wildGuardIcon) wildGuardIcon.enabled = wildDefendActiveThisRound;

        if (wildChargeIcon) wildChargeIcon.enabled = wildChargedNextAttack;

        if (!wildShieldText) return;

        if (wildShieldHP > 0.01f)
        {
            wildShieldText.gameObject.SetActive(true);
            wildShieldText.text = $"Shield: {Mathf.CeilToInt(wildShieldHP)}";
        }
        else
        {
            wildShieldText.gameObject.SetActive(false);
        }
    }

    private void ForceEndBattleEarly(bool victory, bool escaped = false)
    {
        // treat as ended even if we never reached inBattle=true
        SetIsPlayerTurn(false);
        pendingAction = PlayerAction.None;

        // disable bench buttons safely
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
        // Settings-driven filtering
        bool condensed = SettingsManager.I != null && SettingsManager.I.GetCondensedBattleText();
        bool autoCompress = SettingsManager.I != null && SettingsManager.I.GetCompressAutoBattleText();

        bool isAuto = (EncounterManager.I != null && EncounterManager.I.IsAutoMode) || !manualTurns;

        // Condensed mode: drop Flavor lines
        if (condensed && (tags & BattleLineTag.Result) == 0)
            yield break;

        // Auto compression: also drop Flavor lines in auto
        if (isAuto && autoCompress && (tags & BattleLineTag.Flavor) != 0)
            yield break;

        BattleLogger.Log(line, LogScope.Battle); // still always logs (history stays complete)

        _narrationLock = true;

        if (battleTextBox != null)
            yield return battleTextBox.ShowLine(new BattleLine(line, tags), battleSpeed);

        _narrationLock = false;
    }



}
