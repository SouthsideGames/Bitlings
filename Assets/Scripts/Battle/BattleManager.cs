using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public struct BattleResult
{
    public bool victory;
    public int coinsGained;
    public MonsterDataSO wildDef;
    public int wildLevel;
    public float secondsSurvived;
    public int critCount;
    public int turnsSurvived;
    public int damageTaken;
}

public class BattleManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────────
    // Manual-turn settings (new)
    // ─────────────────────────────────────────────────────────────────────────────
    private enum PlayerAction { None, Attack, Defend, Focus, Run }

    [Header("Manual Turn Settings")]
    [SerializeField] private bool manualTurns = true;
    [SerializeField, Range(0f,1f)] private float defendReducePct = 0.50f;
    [SerializeField, Range(0f,1f)] private float focusBuffPct   = 0.50f;
    [SerializeField, Min(1)]       private int   focusBuffTurns  = 1;

    [SerializeField, Range(0f,1f)] private float runBaseChance   = 0.25f; // base floor
    [SerializeField, Range(0f,1f)] private float runMinChance    = 0.05f; // never lower than this
    [SerializeField, Range(0f,1f)] private float runMaxChance    = 0.95f; // never higher than this
    [SerializeField, Range(0f,1f)] private float runSpeedWeight  = 0.50f; // how much (playerSPD/(playerSPD+wildSPD) - 0.5) matters
    [SerializeField, Range(0f,1f)] private float runAttemptBonus = 0.10f; // +10% per failed attempt
    [SerializeField, Range(0f,1f)] private float runHpWeight     = 0.25f; // easier to run when wild is hurt


    public bool IsPlayerTurn { get; private set; }
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
    [SerializeField] private bool pauseForFirstDecision = true;

    [Header("Speed Control")]
    [SerializeField, Min(0.25f)] private float battleSpeed = 1f; // 1x, 2x, 3x
    public float BattleSpeed => battleSpeed;

    [Header("Debug")]
    [SerializeField] private bool debugIncomingMitigation = false;
    [SerializeField] private bool debugEffectivenessOutgoing = false;

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

    // Pending one-turn damage buffs for benched allies (applied on swap-in)
    private float[] teamPendingBuffPct;
    private int[]   teamPendingBuffTurns;
    private int _turnIndex = 0;
    private bool inBattle;
    private Action<BattleResult> onEnd;
    private float startTime;
    private Coroutine turnCR;

    private bool playerTookFirstIncomingThisBattle = false;
    private bool playerLandedFirstHitThisBattle = false;

    private int   tempDmgBuffTurns = 0;
    private float tempDmgBuffPct   = 0f;
    private int   playerNoDmgTurns = 0;
    private int   playerNoCritTurns = 0;
    private int   wildWeakenTurns = 0; // retained for future systems; unused now
    private float wildWeakenPct = 0f;

    private int runAttempts = 0;

    private static readonly Color StatNeutral = Color.white;
    private static readonly Color StatBuff    = new Color(0.35f, 1f, 0.35f);
    private static readonly Color StatNerf    = new Color(1f, 0.35f, 0.35f);

    void Start()
    {
        if (benchBtn1) benchBtn1.onClick.AddListener(() => ClickBench(0));
        if (benchBtn2) benchBtn2.onClick.AddListener(() => ClickBench(1));

        if (SaveManager.Data != null && SaveManager.Data.settings != null)
            battleSpeed = Mathf.Clamp(SaveManager.Data.settings.battleSpeed, 0.25f, 5f);

        SetCombatPanels(false);
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

    // ─────────────────────────────────────────────────────────────────────────────
    // Public API for action bar (hook buttons to these)
    // ─────────────────────────────────────────────────────────────────────────────
    public void SetPlayerActionAttack() { TryQueueAction(PlayerAction.Attack); }
    public void SetPlayerActionDefend() { TryQueueAction(PlayerAction.Defend); }
    public void SetPlayerActionFocus()  { TryQueueAction(PlayerAction.Focus);  }
    public void SetPlayerActionRun()    { TryQueueAction(PlayerAction.Run);    }

    private void TryQueueAction(PlayerAction a)
    {
        if (!inBattle || !manualTurns) return;
        if (!IsPlayerTurn) return;           
        if (isResolvingPlayerTurn) return;     
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
        if (roster == null || roster.Count == 0) return;

        playerNoDmgTurns = 0;
        playerNoCritTurns = 0;
        wildWeakenTurns = 0;
        wildWeakenPct = 0f;
        runAttempts = 0;

        inBattle = false;
        onEnd = onEnded;

        wildDef = wild;
        wildLevel = Mathf.Max(1, level);
        wildMaxHP = BattleCalc.CalcHP(wildDef, wildLevel) * 0.9f;
        wildHP    = wildMaxHP;
        wildAttackPerTurn = BattleCalc.CalcBaseAttack(wildDef, wildLevel, 0, 0) * 0.9f;

        if (wildIcon) wildIcon.sprite = wildDef ? wildDef.icon : null;
        if (wildNameText) wildNameText.text = wildDef ? wildDef.displayName : "Wild";
        if (wildLevelText) wildLevelText.text = $"Lv {wildLevel}";
        if (wildHPBar) { wildHPBar.maxValue = wildMaxHP; wildHPBar.value = wildHP; }

        UpdateWildInfoUI();
        teamCount = Mathf.Min(3, roster.Count);
        if (teamCount <= 0) { inBattle = false; return; }

        teamDefs  = new MonsterDataSO[teamCount];
        teamLevels= new int[teamCount];
        teamMaxHP = new float[teamCount];
        teamHP    = new float[teamCount];
        teamIds   = new string[teamCount];

        for (int i = 0; i < teamCount; i++)
        {
            var owned = roster[i];
            var def = MonsterLibraryLocator.GetById(owned.monsterId);
            if (!def) continue;

            teamIds[i]    = owned.monsterId;
            teamDefs[i]   = def;
            teamLevels[i] = owned.level;

            // --- base max HP from curve ---
            float baseMax = BattleCalc.CalcHP(def, owned.level);

            // --- permanent training HP bonus (saved) ---
            int bonusHP = Mathf.Max(0, owned.trainingBonus.hp);

            float finalMax = Mathf.Max(1f, baseMax + bonusHP);
            teamMaxHP[i] = finalMax;

            int savedHP = owned.currentHP;
            teamHP[i] = (savedHP >= 0)
                ? Mathf.Clamp(savedHP, 0, Mathf.RoundToInt(finalMax))
                : finalMax;
        }


        jobCtx  = new JobBattlePassives.Ctx[teamCount];
        shieldHP= new float[teamCount];
        teamPendingBuffPct  = new float[teamCount];
        teamPendingBuffTurns= new int[teamCount];

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

        tempDmgBuffTurns = 0;
        tempDmgBuffPct   = 0f;
        defendActiveThisRound = false;
        pendingAction = PlayerAction.None;
        IsPlayerTurn = false;

        activeIndex = -1;
        for (int i = 0; i < teamCount; i++)
            if (teamHP[i] > 0f) { activeIndex = i; break; }

        if (activeIndex < 0) { EndBattle(false); return; }

        ApplyActiveToUI();
        ClampAndPushActiveHP();
        RefreshBenchUI();

        if (wildPanel)   wildPanel.SetActive(true);
        if (playerPanel) playerPanel.SetActive(true);

        CanvasGroup wildCG   = null;
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
            if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
            {
                BattleLogger.Log("Your team is unable to battle!", LogScope.Battle);
                EndBattle(false);
                break;
            }

            BattleLogger.Log($"— Round {round} —", LogScope.Battle);
            yield return Wait(beginRoundDelay);

            _turnIndex++;
            TitlesAdapter.OnTurnAdvanced(_turnIndex);

            // ────────────────────────────────────────────────────────────────
            // SPEED CALCULATION (Player)
            // ────────────────────────────────────────────────────────────────
            int pSpeedBase = BattleCalc.CalcSpeed(teamDefs[activeIndex], teamLevels[activeIndex]);

            // Training speed bonus
            var roster = SaveManager.Data?.team;
            if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
                pSpeedBase += Mathf.Max(0, roster[activeIndex].trainingBonus.spd);

            // Job buff (first X turns)
            var jSpeed = jobCtx != null ? jobCtx[activeIndex] : null;
            if (jSpeed != null && jSpeed.speedBuffTurns > 0 && jSpeed.speedBonusPctFirstTurns != 0f)
                pSpeedBase = Mathf.Max(1, Mathf.RoundToInt(pSpeedBase * (1f + jSpeed.speedBonusPctFirstTurns)));

            // Titles
            var titleCtx = BuildTitleContextForActive();
            float pSpeedAfterTitlesF = TitlesAdapter.GetStatValue(
                teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex], "SPD", titleCtx, pSpeedBase
            );
            int pSpeedAfterTitles = Mathf.Max(1, Mathf.RoundToInt(pSpeedAfterTitlesF));

            // Boosters (flat)
            int tempSPDFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0;
            int pSpeed = Mathf.Max(1, pSpeedAfterTitles + Mathf.Max(0, tempSPDFlat));

            // Enemy Speed
            int wSpeed = BattleCalc.CalcSpeed(wildDef, wildLevel);

            // ────────────────────────────────────────────────────────────────
            // ** UPDATED INITIATIVE LOGIC **
            // Pokémon-style turn order with true speed tie 50/50
            // ────────────────────────────────────────────────────────────────

            bool playerFirst;

            if (pSpeed > wSpeed)
            {
                playerFirst = true;
            }
            else if (pSpeed < wSpeed)
            {
                playerFirst = false;
            }
            else
            {
                // True speed tie → random
                playerFirst = UnityEngine.Random.value < 0.5f;

                string tieName = playerFirst
                    ? GetName(activeIndex)
                    : (wildDef ? wildDef.displayName : "Foe");

                BattleLogger.Log($"Speed tie! {tieName} moves first!", LogScope.Battle);
            }

            // Only log normal message when it wasn't a tie
            if (pSpeed != wSpeed)
            {
                if (playerFirst)
                    BattleLogger.Log($"{GetName(activeIndex)} acts first!", LogScope.Battle);
                else
                    BattleLogger.Log($"{(wildDef ? wildDef.displayName : "Foe")} acts first!", LogScope.Battle);
            }

            // ────────────────────────────────────────────────────────────────
            // ROUND ORDER (NEVER MORE THAN 1 ACTION EACH)
            // ────────────────────────────────────────────────────────────────
            if (playerFirst)
            {
                // Player → Enemy
                if (!IsWildKO() && !IsTeamKO())
                {
                    if (manualTurns) yield return WaitForPlayerChoiceAndResolve();
                    else             yield return PlayerTurn();
                    if (CheckEnd()) break;
                    yield return Wait(hitPause);
                }

                if (!IsWildKO() && !IsTeamKO())
                {
                    yield return EnemyTurn();
                    if (CheckEnd()) break;
                    yield return Wait(hitPause);
                }
            }
            else
            {
                // Enemy → Player
                if (!IsWildKO() && !IsTeamKO())
                {
                    yield return EnemyTurn();
                    if (CheckEnd()) break;
                    yield return Wait(hitPause);
                }

                if (!IsWildKO() && !IsTeamKO())
                {
                    if (manualTurns) yield return WaitForPlayerChoiceAndResolve();
                    else             yield return PlayerTurn();
                    if (CheckEnd()) break;
                    yield return Wait(hitPause);
                }
            }

            // ────────────────────────────────────────────────────────────────
            // End Round Cleanup
            // ────────────────────────────────────────────────────────────────
            if (!IsWildKO() && !IsTeamKO())
            {
                if (jobCtx != null && jobCtx[activeIndex] != null)
                {
                    if (jobCtx[activeIndex].speedBuffTurns > 0) jobCtx[activeIndex].speedBuffTurns--;
                    if (jobCtx[activeIndex].critBuffTurns > 0) jobCtx[activeIndex].critBuffTurns--;
                    if (jobCtx[activeIndex].critResistBuffTurns > 0) jobCtx[activeIndex].critResistBuffTurns--;
                    if (jobCtx[activeIndex].dmgReduceBuffTurns > 0) jobCtx[activeIndex].dmgReduceBuffTurns--;
                }

                yield return Wait(endRoundDelay);
            }

            defendActiveThisRound = false;
            round++;
        }

        turnCR = null;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Manual Turn: wait for UI choice, then apply effect
    // ─────────────────────────────────────────────────────────────────────────────
    private IEnumerator WaitForPlayerChoiceAndResolve()
    {
        // Do not clear pendingAction here; allow pre-queued first choice.
        IsPlayerTurn = true;

        // If nothing is chosen yet, wait for a button press.
        while (inBattle && pendingAction == PlayerAction.None)
            yield return null;

        var choice = pendingAction;       // consume it
        pendingAction = PlayerAction.None;
        IsPlayerTurn = false;

        switch (choice)
        {
            case PlayerAction.Attack:
                yield return PlayerTurn();
                break;

            case PlayerAction.Defend:
                defendActiveThisRound = true;
                BattleLogger.Log($"{GetName(activeIndex)} braces for impact (−{Mathf.RoundToInt(defendReducePct*100f)}% incoming this round).", LogScope.Battle);
                Punch(playerIcon);
                break;

            case PlayerAction.Focus:
                tempDmgBuffPct   = Mathf.Max(tempDmgBuffPct, focusBuffPct);
                tempDmgBuffTurns = Mathf.Max(tempDmgBuffTurns, focusBuffTurns);
                BattleLogger.Log($"{GetName(activeIndex)} focuses (+{Mathf.RoundToInt(focusBuffPct*100f)}% ATK for {focusBuffTurns} turn).", LogScope.Battle);
                Punch(playerIcon);
                break;

            case PlayerAction.Run:
            {
                float chance = ComputeRunChance();
                bool escaped = UnityEngine.Random.value < chance;
                if (escaped)
                {
                    BattleLogger.Log($"Got away safely! (Run chance {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                    EndBattle(false);
                    yield break; // stop resolving this turn
                }
                else
                {
                    runAttempts++;
                    BattleLogger.Log($"Couldn't escape! (Run chance was {Mathf.RoundToInt(chance * 100f)}%)", LogScope.Battle);
                }
                break;
            }

            default:
                // no-op if somehow None
                break;
        }
    }


    private IEnumerator PlayerTurn()
    {
        // Prevent multiple calls caused by button mashing or UI spam
        if (isResolvingPlayerTurn)
            yield break;

        // Lock re-entry until this turn is fully resolved
        isResolvingPlayerTurn = true;

        // Safety: ensure there is someone alive to act
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive())
        {
            isResolvingPlayerTurn = false;
            yield break;
        }

        var roster = SaveManager.Data?.team;

        // 1) Permanent flat ATK: equipment + training
        int equipFlat = 0;
        int trainingAtk = 0;

        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
        {
            var om = roster[activeIndex];
            equipFlat   = Mathf.Max(0, om.flatAtkBonus);
            trainingAtk = Mathf.Max(0, om.trainingBonus.atk);
        }

        int permanentFlat = Mathf.Max(0, equipFlat + trainingAtk);

        // Base ATK (no temp/booster flats here)
        float atkBaseF = BattleCalc.CalcBaseAttack(
            teamDefs[activeIndex],
            teamLevels[activeIndex],
            permanentFlat,
            0
        );
        int atkBase = Mathf.Max(1, Mathf.RoundToInt(atkBaseF));

        // Booster flat as multiplier (applied last)
        int tempFlatFromBoosters = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;
        float atkWithBoosterF = BattleCalc.CalcBaseAttack(
            teamDefs[activeIndex],
            teamLevels[activeIndex],
            permanentFlat,
            tempFlatFromBoosters
        );
        float atkBoosterMult = Mathf.Max(0.01f, atkWithBoosterF / Mathf.Max(1f, atkBase));

        // Titles context
        var titleCtx = BuildTitleContextForActive();

        // Crit chance (jobs can add)
        var jctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float playerCrit = critChancePlayer;
        if (jctx != null)
        {
            playerCrit += jctx.critChanceFlat;
            if (jctx.critBuffTurns > 0)
                playerCrit += jctx.critChanceBonusFirstTurns;
        }
        playerCrit = Mathf.Clamp01(playerCrit);

        // Resolve with BASE ONLY (Atk already includes training)
        var dr = BattleCalc.ResolveHit(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            null, wildDef, wildLevel,
            atkBase,
            playerCrit,
            critMultiplier,
            0
        );

        TitlesAdapter.OnAttackLanded(teamIds[activeIndex], dr.crit);

        // 2) Job passives (mults)
        if (jctx != null && jctx.attackBonusPct > 0f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.attackBonusPct)));

        if (jctx != null && !jctx.usedFirstOutgoing && jctx.firstOutgoingBonus > 0f)
        {
            jctx.usedFirstOutgoing = true;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.firstOutgoingBonus)));
        }

        if (jctx != null && jctx.surgeApplied && jctx.surgeAtkBonusPct > 0f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jctx.surgeAtkBonusPct)));

        // 3) Titles (effectiveness mult then add)
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

        // 4) Boosters LAST (tap %, focus %, then ATK flat-as-multiplier)
        float tap = TapBoost.I ? TapBoost.I.CurrentMultiplier : 1f;
        if (!Mathf.Approximately(tap, 1f))
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * tap));

        if (tempDmgBuffTurns > 0 && tempDmgBuffPct > 0f)
        {
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + tempDmgBuffPct)));
            BattleLogger.Log($"+{Mathf.RoundToInt(tempDmgBuffPct * 100f)}% damage buff active.", LogScope.Battle);
            tempDmgBuffTurns--;
            if (tempDmgBuffTurns <= 0) tempDmgBuffPct = 0f;
        }

        if (!Mathf.Approximately(atkBoosterMult, 1f))
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * atkBoosterMult));

        // Apply damage & log
        wildHP = Mathf.Max(0f, wildHP - dr.damage);
        PushHPBars();

        if (!playerLandedFirstHitThisBattle && dr.damage > 0)
            playerLandedFirstHitThisBattle = true;

        string foeName = wildDef ? wildDef.displayName : "Foe";
        BattleLogger.Log($"{GetName(activeIndex)} hits {foeName} for {dr.damage}!", LogScope.Battle);

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) BattleLogger.Log("It's super effective!", LogScope.Battle);
            else if (dr.effectiveness < 0.85f) BattleLogger.Log("It's not very effective...", LogScope.Battle);
        }
        if (dr.crit) BattleLogger.Log("Critical hit!", LogScope.Battle);

        // End-of-player-turn job regen
        var j2 = (jobCtx != null) ? jobCtx[activeIndex] : null;
        if (j2 != null && j2.endTurnHealPct > 0f)
        {
            bool canHeal = (j2.regenTurns == int.MaxValue) || (j2.regenTurns > 0);
            if (canHeal)
            {
                float healAmt = GetFinalMaxHPForIndex(activeIndex) * j2.endTurnHealPct;
                TryAddHPToActive(healAmt);
                if (j2.regenTurns != int.MaxValue) j2.regenTurns--;
                BattleLogger.Log($"{GetName(activeIndex)} regenerates {Mathf.RoundToInt(healAmt)} HP.", LogScope.Battle);
            }
        }

        Punch(playerIcon);
        FirePlayerEndTurnTicks(dealtDamageThisTurn: dr.damage > 0, critThisTurn: dr.crit);

        // Turn is officially finished, allow next action
        isResolvingPlayerTurn = false;
        yield break;
    }


   private IEnumerator EnemyTurn()
    {
        if (debugIncomingMitigation) Debug.Log("[Mitigation] EnemyTurn started — debugIncomingMitigation = TRUE");

        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive()) yield break;

        int enemyAtk = Mathf.Max(1, Mathf.RoundToInt(wildAttackPerTurn));

        // Booster DEF flat APPLIES LAST (not in ResolveHit)
        int defFlatBooster = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;

        var ctx  = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float preHP = teamHP[activeIndex];

        // Training DEF (flat DR per point from trainingBonus.def)
        int trainingFlatDef = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
        {
            // TrainingBonus is a value type; no null-check needed
            trainingFlatDef = Mathf.Max(0, roster[activeIndex].trainingBonus.def);
        }

        // Defender Titles damage filter (cannot be crit, % reduce, flat)
        var df = TitlesAdapter.GetDamageFilter(teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]);

        // Player crit resist (Jobs) + cannotBeCrit (Titles)
        float playerCritResist = 0f;
        if (ctx != null)
        {
            playerCritResist += ctx.critResistFlat;
            if (ctx.critResistBuffTurns > 0) playerCritResist += ctx.critResistBonusFirstTurns;
        }

        float wildCritChance = df.cannotBeCrit ? 0f : Mathf.Clamp01(critChanceWild - playerCritResist);

        // Resolve (defender ID present) with NO flatDefBonus here
        var dr = BattleCalc.ResolveHit(
            null, wildDef, wildLevel,
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            enemyAtk, wildCritChance, critMultiplier, 0
        );

        // ── DEBUG capture BEFORE any filters
        int  baseRawDamage      = dr.damage;
        bool critRolled         = dr.crit;
        bool critNegatedByTitle = false;

        // Retest if cannot be crit
        if (df.cannotBeCrit && dr.crit)
        {
            critNegatedByTitle = true;
            dr = BattleCalc.ResolveHit(
                null, wildDef, wildLevel,
                teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
                enemyAtk, 0f, critMultiplier, 0
            );
        }

        // Incoming scalar: conditionals + jobs
        float incomingScalar = 1f;

        // Conditional DEF% (from Titles)
        var cmods = GetConditionalModsForActive();
        if (cmods.defPct > 0f)
            incomingScalar *= (1f - Mathf.Clamp01(cmods.defPct));

        // Job-based reductions
        if (ctx != null && !ctx.usedFirstIncoming && ctx.firstIncomingReduce > 0f)
        {
            ctx.usedFirstIncoming = true;
            incomingScalar *= (1f - ctx.firstIncomingReduce);
        }

        if (ctx != null && ctx.baseDamageReducePct > 0f)
            incomingScalar *= (1f - ctx.baseDamageReducePct);

        if (ctx != null && ctx.defenseBonusPct > 0f)
            incomingScalar *= (1f - ctx.defenseBonusPct);

        if (ctx != null && ctx.dmgReduceBuffTurns > 0 && ctx.dmgReduceFirstTurns > 0f)
            incomingScalar *= (1f - ctx.dmgReduceFirstTurns);

        // Manual defend (this round)
        if (defendActiveThisRound && defendReducePct > 0f)
            incomingScalar *= (1f - Mathf.Clamp01(defendReducePct));

        // Optional weaken remains
        if (wildWeakenTurns > 0 && wildWeakenPct > 0f)
        {
            incomingScalar *= (1f - wildWeakenPct);
            BattleLogger.Log($"{(wildDef ? wildDef.displayName : "Foe")} is weakened (-{Mathf.RoundToInt(wildWeakenPct * 100f)}% dmg).", LogScope.Battle);
            wildWeakenTurns--;
            if (wildWeakenTurns <= 0) wildWeakenPct = 0f;
        }

        // Stage 1: scalar
        int dmg_afterScalar = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));

        // Stage 1.5: incoming effectiveness titles
        float incomingEffMul = TitlesAdapter.GetIncomingEffectivenessMult(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            wildDef ? wildDef.type : MonsterType.None
        );
        if (!Mathf.Approximately(incomingEffMul, 1f))
        {
            int beforeInc = dmg_afterScalar;
            dmg_afterScalar = Mathf.Max(1, Mathf.RoundToInt(dmg_afterScalar * incomingEffMul));
            if (debugIncomingMitigation)
                MitLog($"  • After Title Incoming-Effectiveness x{incomingEffMul:0.00}: {beforeInc} → {dmg_afterScalar}");
        }

        // Stage 2: Titles — % then flat
        float percentReduce = Mathf.Clamp01(df.percentReduce);
        int   flatReduce    = Mathf.Max(0, df.flatReduce);

        int dmg_afterPercent = (percentReduce > 0f)
            ? Mathf.Max(1, Mathf.RoundToInt(dmg_afterScalar * (1f - percentReduce)))
            : dmg_afterScalar;

        // Stage 3: Booster DEF flat + title flat + training DEF
        int totalFlatDR   = flatReduce + Mathf.Max(0, defFlatBooster) + Mathf.Max(0, trainingFlatDef);
        int dmg_afterFlat = Mathf.Max(1, dmg_afterPercent - totalFlatDR);

        // Stage 4: Shield
        float shieldBefore  = (shieldHP != null && shieldHP.Length > activeIndex) ? shieldHP[activeIndex] : 0f;
        float shieldAbsorbF = 0f;

        int dmg_final = dmg_afterFlat;
        if (shieldBefore > 0f)
        {
            shieldAbsorbF = Mathf.Min(shieldBefore, dmg_final);
            shieldHP[activeIndex] = Mathf.Max(0f, shieldBefore - shieldAbsorbF);
            dmg_final = Mathf.Max(1, dmg_final - Mathf.RoundToInt(shieldAbsorbF));
            if (shieldAbsorbF > 0f)
                BattleLogger.Log($"{GetName(activeIndex)}'s shield absorbed {Mathf.RoundToInt(shieldAbsorbF)}!", LogScope.Battle);
        }

        // Apply to HP
        teamHP[activeIndex] = Mathf.Max(0f, teamHP[activeIndex] - dmg_final);
        ClampAndPushActiveHP();

        TitlesAdapter.OnHitTaken(teamIds[activeIndex], dmg_final, dr.crit && !df.cannotBeCrit);

        string foeName = wildDef ? wildDef.displayName : "Foe";
        BattleLogger.Log($"{foeName} hits {GetName(activeIndex)} for {dmg_final}!", LogScope.Battle);

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) BattleLogger.Log("It's super effective!", LogScope.Battle);
            else if (dr.effectiveness < 0.85f) BattleLogger.Log("It's not very effective...", LogScope.Battle);
        }
        if (dr.crit && !df.cannotBeCrit) BattleLogger.Log("Critical hit!", LogScope.Battle);

        if (!playerTookFirstIncomingThisBattle) playerTookFirstIncomingThisBattle = true;

        if (debugIncomingMitigation)
        {
            MitLogOncePerTurnHeader(critRolled, critNegatedByTitle);

            int shieldAbsInt     = Mathf.RoundToInt(shieldAbsorbF);
            int jobsPctOff       = Mathf.RoundToInt((1f - incomingScalar) * 100f);
            int titlePctOff      = Mathf.RoundToInt(percentReduce * 100f);
            int shieldBeforeInt  = Mathf.RoundToInt(shieldBefore);
            int totalFlatDRDebug = totalFlatDR;

            var text =
                $"  • Base: {baseRawDamage}\n" +
                $"  • After Job/Conditional scalar ({jobsPctOff}% off): {Mathf.Max(1, Mathf.RoundToInt(baseRawDamage * incomingScalar))}\n" +
                (Mathf.Approximately(incomingEffMul, 1f) ? "" :
                $"  • After Title Incoming-Effectiveness x{incomingEffMul:0.00}\n") +
                $"  • After Title % ({titlePctOff}% off): {dmg_afterPercent}\n" +
                $"  • After Flat DR (title + booster + training DEF = -{totalFlatDRDebug}): {dmg_afterFlat}\n" +
                $"  • Shield Absorb (-{shieldAbsInt}, was {shieldBeforeInt})\n" +
                $"  ⇒ Final Applied: {dmg_final}";

            MitLog(text);
        }

        // Rescue heal (job)
        if (ctx != null && !ctx.rescueUsed && ctx.rescueHealPct > 0f && teamHP[activeIndex] > 0f)
        {
            float curMax = GetFinalMaxHPForIndex(activeIndex);
            float thresholdHP = curMax * (ctx.rescueThreshold > 0f ? ctx.rescueThreshold : 0.4f);
            if (preHP > thresholdHP && teamHP[activeIndex] <= thresholdHP)
            {
                ctx.rescueUsed = true;
                float healAmt = curMax * ctx.rescueHealPct;
                TryAddHPToActive(healAmt);
                BattleLogger.Log($"{GetName(activeIndex)} triage heals {Mathf.RoundToInt(healAmt)} HP!", LogScope.Battle);
            }
        }

        // Enrage @ 50% (job)
        if (ctx != null && !ctx.surgeApplied)
        {
            float curMax = GetFinalMaxHPForIndex(activeIndex);
            if (teamHP[activeIndex] <= curMax * 0.5f && ctx.surgeAtkBonusPct > 0f)
            {
                ctx.surgeApplied = true;
                ctx.attackBonusPct += ctx.surgeAtkBonusPct;
                BattleLogger.Log($"{GetName(activeIndex)} becomes enraged (+{Mathf.RoundToInt(ctx.surgeAtkBonusPct * 100f)}% ATK)!", LogScope.Battle);
            }
        }

        Punch(wildIcon);
        yield break;
    }

    private bool CheckEnd()
    {
        if (IsWildKO())
        {
            BattleLogger.Log("Wild monster fainted!", LogScope.Battle);
            EndBattle(true);
            return true;
        }
        if (IsTeamKO())
        {
            BattleLogger.Log("Your team is unable to battle!", LogScope.Battle);
            EndBattle(false);
            return true;
        }
        return false;
    }

    private void EndBattle(bool victory)
    {
        if (!inBattle) return;
        inBattle = false;
        IsPlayerTurn = false;
        pendingAction = PlayerAction.None;
        defendActiveThisRound = false;

        if (turnCR != null) { StopCoroutine(turnCR); turnCR = null; }

        float survived = Mathf.Max(0f, Time.unscaledTime - startTime);

        // COINS
        int baseCoins = BattleRewards.CoinsFor(victory, wildLevel, survived);
        int finalCoins = baseCoins;
        int coinTitleBonus = 0;

        if (victory && teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
        {
            float cm = TitlesAdapter.GetCoinMultOnVictory(teamIds[activeIndex], wildDef, wildLevel);
            if (cm > 0f)
            {
                finalCoins = Mathf.Max(0, Mathf.RoundToInt(baseCoins * cm));
                coinTitleBonus = Mathf.Max(0, finalCoins - baseCoins);
            }
        }

        if (finalCoins < 0) finalCoins = 0;

        // GROWTH CORES
        int baseCores = Mathf.Max(1, 2 + wildLevel);
        int growthCoreTitleBonus = 0;
        int growthCoreTotal = 0;

        // single SaveManager.Data alias for this method
        var data = SaveManager.Data;

        if (victory)
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
                ResourceManager.I?.Add(ResourceType.GrowthCores, growthCoreTotal);

            BattleLogger.Log($"Gained {growthCoreTotal} Growth Cores.", LogScope.Battle);
        }

        // Persist HP results
        var teamList  = data != null && data.team  != null ? data.team  : new List<OwnedMonsterData>();
        var ownedList = data != null && data.owned != null ? data.owned : new List<OwnedMonsterData>();
        long nowUnix = SaveManager.NowUnix();

        // 1) write HP back into team list
        for (int i = 0; i < teamCount && i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;
            int hp = Mathf.CeilToInt(Mathf.Max(0f, teamHP[i]));
            t.currentHP = hp;
            teamList[i] = t;
        }

        // 2) mirror those HP values into owned list
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

        // 3) clear KO’d team slots
        for (int i = 0; i < teamList.Count; i++)
        {
            var e = teamList[i];
            if (e == null || string.IsNullOrEmpty(e.monsterId)) continue;

            e.lastHPUnix = nowUnix;

            if (e.currentHP <= 0)
                teamList[i] = new OwnedMonsterData();
            else
                teamList[i] = e;
        }

        if (data != null)
        {
            data.owned = ownedList;
            data.team  = teamList;
            SaveManager.Save();
        }

        GameEvents.OnTeamChanged?.Invoke();

        // Clear temporary buffs
        BattleTempBuffs.I?.ClearPlayerAtkBonus();
        BattleTempBuffs.I?.ClearPlayerSpeedBonus();
        BattleTempBuffs.I?.ClearPlayerHPBonus();
        BattleTempBuffs.I?.ClearPlayerDefenseBonus();

        BattleLogger.Log($"Battle ends: {(victory ? "Victory" : "Defeat")} (+{finalCoins} coins).", LogScope.Battle);
        BattleLogger.EndBattle(victory);

        var result = new BattleResult
        {
            victory = victory,
            coinsGained = finalCoins,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = survived
        };

        SetCombatPanels(false);

        if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            TitlesAdapter.OnBattleEnd(teamIds[activeIndex], victory, wildDef, wildLevel);

        onEnd?.Invoke(result);

        bool isAuto = EncounterManager.I && EncounterManager.I.IsAutoMode;
        PostBattleSummaryManager.I?.NotifyBattleEnd(
            result,
            isAuto,
            growthCoreTotal,
            monstersLeveledUp: 0,
            captured: false,
            capturedMonsterId: null,
            capturedLevel: 0,
            levelUpSummaries: null,
            coinsBase: baseCoins,
            coinsTitleBonus: coinTitleBonus,
            growthCoresBase: growthCoreTotal - growthCoreTitleBonus, // baseAfterShiny equivalent
            growthCoresTitleBonus: growthCoreTitleBonus,
            growthCoresDetailLines: new List<string> { $"Gained {growthCoreTotal} Growth Cores." }
        );

        GameEvents.BattleFinished?.Invoke(result);
    }


    private void ClampAndPushActiveHP()
    {
        float curMax = GetFinalMaxHPForIndex(activeIndex);
        teamHP[activeIndex] = Mathf.Min(teamHP[activeIndex], curMax);

        if (playerHPBar)
        {
            playerHPBar.maxValue = curMax;
            playerHPBar.value = Mathf.Clamp(teamHP[activeIndex], 0f, curMax);
        }
        if (wildHPBar)
        {
            wildHPBar.maxValue = Mathf.Max(1f, wildMaxHP);
            wildHPBar.value = Mathf.Clamp(wildHP, 0f, wildHPBar.maxValue);
        }

        UpdatePlayerInfoUI();
    }

    private void PushHPBars()
    {
        if (wildHPBar)
        {
            wildHPBar.maxValue = Mathf.Max(1f, wildMaxHP);
            wildHPBar.value = Mathf.Clamp(wildHP, 0f, wildHPBar.maxValue);
        }
        if (playerHPBar)
        {
            float curMax = GetFinalMaxHPForIndex(activeIndex);
            playerHPBar.maxValue = curMax;
            playerHPBar.value = Mathf.Clamp(teamHP[activeIndex], 0f, curMax);
        }

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
                benchImg1.sprite  = teamDefs[others[0]]?.icon;
                benchImg1.color   = teamHP[others[0]] > 0 ? Color.white : new Color(1,1,1,0.35f);
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
                benchImg2.sprite  = teamDefs[others[1]]?.icon;
                benchImg2.color   = teamHP[others[1]] > 0 ? Color.white : new Color(1,1,1,0.35f);
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
        // Optional: only allow swap during player's turn for manual mode
        if (manualTurns && !IsPlayerTurn) return;

        List<int> others = new();
        for (int i = 0; i < teamCount; i++)
            if (i != activeIndex) others.Add(i);

        if (benchSlot < 0 || benchSlot >= others.Count) return;

        int targetIndex = others[benchSlot];
        if (teamHP[targetIndex] <= 0f) return;

        // ─────────────────────────────────────────────────────────────
        // Core team data swap
        // ─────────────────────────────────────────────────────────────
        (teamDefs[activeIndex],   teamDefs[targetIndex])   = (teamDefs[targetIndex],   teamDefs[activeIndex]);
        (teamLevels[activeIndex], teamLevels[targetIndex]) = (teamLevels[targetIndex], teamLevels[activeIndex]);
        (teamMaxHP[activeIndex],  teamMaxHP[targetIndex])  = (teamMaxHP[targetIndex],  teamMaxHP[activeIndex]);
        (teamHP[activeIndex],     teamHP[targetIndex])     = (teamHP[targetIndex],     teamHP[activeIndex]);
        (teamIds[activeIndex],    teamIds[targetIndex])    = (teamIds[targetIndex],    teamIds[activeIndex]);

        // ─────────────────────────────────────────────────────────────
        // CRITICAL: keep runtime battle state aligned with slots
        // ─────────────────────────────────────────────────────────────
        if (jobCtx != null && jobCtx.Length > activeIndex && jobCtx.Length > targetIndex)
        {
            (jobCtx[activeIndex], jobCtx[targetIndex]) =
                (jobCtx[targetIndex], jobCtx[activeIndex]);
        }

        if (shieldHP != null && shieldHP.Length > activeIndex && shieldHP.Length > targetIndex)
        {
            (shieldHP[activeIndex], shieldHP[targetIndex]) =
                (shieldHP[targetIndex], shieldHP[activeIndex]);
        }

        if (teamPendingBuffPct != null && teamPendingBuffPct.Length > activeIndex && teamPendingBuffPct.Length > targetIndex)
        {
            (teamPendingBuffPct[activeIndex], teamPendingBuffPct[targetIndex]) =
                (teamPendingBuffPct[targetIndex], teamPendingBuffPct[activeIndex]);
        }

        if (teamPendingBuffTurns != null && teamPendingBuffTurns.Length > activeIndex && teamPendingBuffTurns.Length > targetIndex)
        {
            (teamPendingBuffTurns[activeIndex], teamPendingBuffTurns[targetIndex]) =
                (teamPendingBuffTurns[targetIndex], teamPendingBuffTurns[activeIndex]);
        }

        // Also keep SaveManager.Data.team order in sync with the swap
        var t = SaveManager.Data.team[activeIndex];
        SaveManager.Data.team[activeIndex] = SaveManager.Data.team[targetIndex];
        SaveManager.Data.team[targetIndex] = t;
        SaveManager.Save();

        ApplyActiveToUI();
        ClampAndPushActiveHP();
        RefreshBenchUI();

        // Apply any pending buff now that this monster is active
        if (teamPendingBuffPct != null && teamPendingBuffTurns != null)
        {
            if (teamPendingBuffPct[activeIndex] > 0f)
            {
                tempDmgBuffPct   += teamPendingBuffPct[activeIndex];
                tempDmgBuffTurns  = Mathf.Max(tempDmgBuffTurns, teamPendingBuffTurns[activeIndex]);

                BattleLogger.Log(
                    $"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.",
                    LogScope.Battle
                );

                teamPendingBuffPct[activeIndex]   = 0f;
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

            int targetIndex = i;

            // ─────────────────────────────────────────────────────────
            // Core team data swap
            // ─────────────────────────────────────────────────────────
            (teamDefs[activeIndex],   teamDefs[targetIndex])   = (teamDefs[targetIndex],   teamDefs[activeIndex]);
            (teamLevels[activeIndex], teamLevels[targetIndex]) = (teamLevels[targetIndex], teamLevels[activeIndex]);
            (teamMaxHP[activeIndex],  teamMaxHP[targetIndex])  = (teamMaxHP[targetIndex],  teamMaxHP[activeIndex]);
            (teamHP[activeIndex],     teamHP[targetIndex])     = (teamHP[targetIndex],     teamHP[activeIndex]);
            (teamIds[activeIndex],    teamIds[targetIndex])    = (teamIds[targetIndex],    teamIds[activeIndex]);

            // ─────────────────────────────────────────────────────────
            // CRITICAL: keep runtime battle state aligned with slots
            // ─────────────────────────────────────────────────────────
            if (jobCtx != null && jobCtx.Length > activeIndex && jobCtx.Length > targetIndex)
            {
                (jobCtx[activeIndex], jobCtx[targetIndex]) =
                    (jobCtx[targetIndex], jobCtx[activeIndex]);
            }

            if (shieldHP != null && shieldHP.Length > activeIndex && shieldHP.Length > targetIndex)
            {
                (shieldHP[activeIndex], shieldHP[targetIndex]) =
                    (shieldHP[targetIndex], shieldHP[activeIndex]);
            }

            if (teamPendingBuffPct != null && teamPendingBuffPct.Length > activeIndex && teamPendingBuffPct.Length > targetIndex)
            {
                (teamPendingBuffPct[activeIndex], teamPendingBuffPct[targetIndex]) =
                    (teamPendingBuffPct[targetIndex], teamPendingBuffPct[activeIndex]);
            }

            if (teamPendingBuffTurns != null && teamPendingBuffTurns.Length > activeIndex && teamPendingBuffTurns.Length > targetIndex)
            {
                (teamPendingBuffTurns[activeIndex], teamPendingBuffTurns[targetIndex]) =
                    (teamPendingBuffTurns[targetIndex], teamPendingBuffTurns[activeIndex]);
            }

            // Keep SaveManager.Data.team in sync
            var t = SaveManager.Data.team[activeIndex];
            SaveManager.Data.team[activeIndex] = SaveManager.Data.team[targetIndex];
            SaveManager.Data.team[targetIndex] = t;
            SaveManager.Save();

            ApplyActiveToUI();
            ClampAndPushActiveHP();
            RefreshBenchUI();

            // Apply any pending buff now that this monster is active
            if (teamPendingBuffPct != null && teamPendingBuffPct[activeIndex] > 0f)
            {
                tempDmgBuffPct   += teamPendingBuffPct[activeIndex];
                tempDmgBuffTurns  = Mathf.Max(tempDmgBuffTurns, teamPendingBuffTurns[activeIndex]);

                BattleLogger.Log(
                    $"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.",
                    LogScope.Battle
                );

                teamPendingBuffPct[activeIndex]   = 0f;
                teamPendingBuffTurns[activeIndex] = 0;
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

        int dispHP  = Mathf.RoundToInt(BattleCalc.CalcHP(wildDef, wildLevel));
        int dispATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(wildDef, wildLevel, 0, 0));
        int dispDEF = BattleCalc.CalcDefense(wildDef, wildLevel);
        int dispSPD = BattleCalc.CalcSpeed(wildDef, wildLevel);

        if (wildIdText)     wildIdText.text     = $"ID: {wildDef.id}";
        if (wildTypeText)   wildTypeText.text   = $"TYPE: {wildDef.type}";
        if (wildRarityText) wildRarityText.text = $"RARITY: {wildDef.rarity}";
        if (wildLevelText)  wildLevelText.text  = $"LVL: {wildLevel}";
        if (wildHPText)     wildHPText.text     = $"HP: {dispHP}";
        if (wildATKText)    wildATKText.text    = $"ATK: {dispATK}";
        if (wildDEFText)    wildDEFText.text    = $"DEF: {dispDEF}";
        if (wildSPDText)    wildSPDText.text    = $"SPD: {dispSPD}";
    }

    private void UpdatePlayerInfoUI()
    {
        if (activeIndex < 0 || teamDefs == null || activeIndex >= teamDefs.Length) return;

        var def = teamDefs[activeIndex];
        if (!def) return;

        int lvl = (teamLevels != null && activeIndex < teamLevels.Length) ? teamLevels[activeIndex] : 1;

        // PURE BASE from curve
        int baseHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        int baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
        int baseDEF = BattleCalc.CalcDefense(def, lvl);
        int baseSPD = BattleCalc.CalcSpeed(def,   lvl);

        // PERMANENT TRAINING BONUSES
        int bonusHP  = 0;
        int bonusATK = 0;
        int bonusDEF = 0;
        int bonusSPD = 0;

        var roster = SaveManager.Data?.team;
       if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
        {
            var tb = roster[activeIndex].trainingBonus;
            bonusHP  = Mathf.Max(0, tb.hp);
            bonusATK = Mathf.Max(0, tb.atk);
            bonusDEF = Mathf.Max(0, tb.def);
            bonusSPD = Mathf.Max(0, tb.spd);
        }

        baseHP  += bonusHP;
        baseATK += bonusATK;
        baseDEF += bonusDEF;
        baseSPD += bonusSPD;

        // TEMP / EQUIP FLATS
        int tempHPFlat  = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus()        : 0;
        int tempATKFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus()       : 0;
        int tempDEFFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus()   : 0;
        int tempSPDFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0;

        int equippedFlatATK = 0;
        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
            equippedFlatATK = Mathf.Max(0, roster[activeIndex].flatAtkBonus);

        // Title context (conditionals)
        var ctx = TitleContext.Empty;
        ctx.ownedId = (teamIds != null && activeIndex < teamIds.Length) ? teamIds[activeIndex] : "";

        int hpBaseForCtx = Mathf.Max(1, baseHP + tempHPFlat);
        float currentHP   = (teamHP != null && activeIndex < teamHP.Length) ? teamHP[activeIndex] : hpBaseForCtx;
        ctx.selfHp01 = Mathf.Clamp01(currentHP / Mathf.Max(1f, hpBaseForCtx));

        ctx.alliesAlive = GetAlliesAliveNotIncludingActive();
        ctx.winStreak   = GetWinStreakSafe();

        // Conditional-only mod snapshot (for {cond ±X} tags)
        var cmods = GetConditionalModsForActive();

        // Identity block
        if (playerIdText)     playerIdText.text     = $"ID: {def.id}";
        if (playerTypeText)   playerTypeText.text   = $"TYPE: {def.type}";
        if (playerRarityText) playerRarityText.text = $"RARITY: {def.rarity}";
        if (playerLevelText)  playerLevelText.text  = $"LVL: {lvl}";

        // HP — Base Max vs Final Max (titles/conditionals applied)
        int hpBaseForDisplay  = hpBaseForCtx;
        float hpFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "HP", ctx, hpBaseForDisplay);
        int hpFinal = Mathf.Max(1, Mathf.RoundToInt(hpFinalF));
        if (playerHPText)
        {
            SetColoredStat(playerHPText, "HP", hpBaseForDisplay, hpFinal);
            playerHPText.text += CondTag(hpBaseForDisplay, 0, cmods.hpPct);
        }

        // ATK — (base + equip + temp) then titles/conditionals
        int atkBaseForDisplay = Mathf.Max(1, baseATK + equippedFlatATK + tempATKFlat);
        float atkFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Attack", ctx, atkBaseForDisplay);
        int atkFinal = Mathf.Max(1, Mathf.RoundToInt(atkFinalF));
        if (playerATKText)
        {
            SetColoredStat(playerATKText, "ATK", atkBaseForDisplay, atkFinal);
            playerATKText.text += CondTag(atkBaseForDisplay, cmods.atkFlat, cmods.atkPct);
        }

        // DEF — (base + temp) then titles/conditionals
        int defBaseForDisplay = Mathf.Max(0, baseDEF + tempDEFFlat);
        float defFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Defense", ctx, defBaseForDisplay);
        int defFinal = Mathf.Max(0, Mathf.RoundToInt(defFinalF));
        if (playerDEFText)
        {
            SetColoredStat(playerDEFText, "DEF", defBaseForDisplay, defFinal);
            playerDEFText.text += CondTag(defBaseForDisplay, cmods.defFlat, cmods.defPct);
        }

        // SPD — (base + temp) then titles/conditionals
        int spdBaseForDisplay = Mathf.Max(1, baseSPD + tempSPDFlat);
        float spdFinalF = TitlesAdapter.GetStatValue(ctx.ownedId, def, lvl, "Speed", ctx, spdBaseForDisplay);
        int spdFinal = Mathf.Max(1, Mathf.RoundToInt(spdFinalF));
        if (playerSPDText)
        {
            SetColoredStat(playerSPDText, "SPD", spdBaseForDisplay, spdFinal);
            playerSPDText.text += CondTag(spdBaseForDisplay, cmods.spdFlat, cmods.spdPct);
        }

        // Optional active resist badge
        bool resistOn = BattleTempBuffs.I && BattleTempBuffs.I.IsTypeResistActive();
        if (resistOn && playerRarityText) playerRarityText.text += " [Resist]";
    }
    private void ApplyActiveToUI()
    {
        var def = teamDefs[activeIndex];
        var lvl = teamLevels[activeIndex];
        if (playerIcon)      playerIcon.sprite = def ? (def.backIcon ? def.backIcon : def.icon) : null;
        if (playerNameText)  playerNameText.text = def ? def.displayName : "";
        if (playerLevelText) playerLevelText.text = $"Lv {lvl}";
        UpdatePlayerInfoUI();
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
                    if (wildCG)   wildCG.alpha = a;
                    if (playerCG) playerCG.alpha = a;
                    yield return null;
                }
            }
        }

        if (wildCG)   { wildCG.alpha = 1f; wildCG.blocksRaycasts = true; wildCG.interactable = true; }
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
        BattleLogger.Log(wildDef ? $"A wild {wildDef.displayName} (Lv {wildLevel}) appeared!" : "A wild foe appeared!", LogScope.Battle);

        for (int i = 0; i < teamCount; i++)
        {
            var summary = BuildPassiveSummary(i);
            if (!string.IsNullOrEmpty(summary))
                BattleLogger.Log(summary, LogScope.Battle);
        }

        PostBattleSummaryManager.I?.NotifyBattleStart();

        if (activeIndex >= 0 && teamIds != null && activeIndex < teamIds.Length)
            TitlesAdapter.OnBattleStart(teamIds[activeIndex], wildDef, wildLevel);

        // >>> NEW: require the player to pick an action before ANYTHING happens.
        if (manualTurns && pauseForFirstDecision)
        {
            IsPlayerTurn = true;
            pendingAction = PlayerAction.None;

            // Wait until they press Attack/Defend/Focus/Run; we DO NOT resolve yet.
            while (inBattle && pendingAction == PlayerAction.None)
                yield return null;

            IsPlayerTurn = false;
        }

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

        // Titles first (percent)
        if (idx >= 0)
        {
            var tmods = GetTitleModsForIndex(idx);
            if (tmods.hpPct > 0f) v *= (1f + tmods.hpPct);
        }

        // Boosters last (flat)
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
        var    def     = teamDefs[activeIndex];
        int    lvl     = teamLevels[activeIndex];

        float curMax = GetFinalMaxHPForIndex(activeIndex);
        float hp01   = curMax > 0.01f ? Mathf.Clamp01(teamHP[activeIndex] / curMax) : 0f;

        int alliesAlive = 0;
        for (int i = 0; i < teamCount; i++)
            if (i != activeIndex && teamHP[i] > 0.01f) alliesAlive++;

        int winStreak = (EncounterManager.I != null) ? EncounterManager.I.CurrentWinStreak : 0;

        TitleContext ctx = TitleContext.Empty;
        ctx.selfHp01  = hp01;
        ctx.alliesAlive = alliesAlive;
        ctx.winStreak = winStreak;

        TitleStatMods mods = default;
        mods.atkFlat = Mathf.RoundToInt(TitlesAdapter.GetStatValue(ownedId, def, lvl, "atkFlat",  ctx, 0f));
        mods.atkPct  = TitlesAdapter.GetStatValue(ownedId, def, lvl, "atkPct",   ctx, 0f);

        mods.defFlat = Mathf.RoundToInt(TitlesAdapter.GetStatValue(ownedId, def, lvl, "defFlat",  ctx, 0f));
        mods.defPct  = TitlesAdapter.GetStatValue(ownedId, def, lvl, "defPct",   ctx, 0f);

        mods.spdFlat = Mathf.RoundToInt(TitlesAdapter.GetStatValue(ownedId, def, lvl, "spdFlat",  ctx, 0f));
        mods.spdPct  = TitlesAdapter.GetStatValue(ownedId, def, lvl, "spdPct",   ctx, 0f);

        mods.hpPct   = TitlesAdapter.GetStatValue(ownedId, def, lvl, "hpPct",    ctx, 0f);

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

    private struct DamageFilterView
    {
        public bool cannotBeCrit;
        public float percentReduce; // 0..1
        public int flatReduce;      // >=0
    }

    private bool TryUnboxDamageFilter(object boxed, out DamageFilterView view)
    {
        view = default;
        if (boxed == null) return false;
        var t = boxed.GetType();

        bool ok = true;
        try
        {
            bool GetBool(string name, bool def)
            {
                var f = t.GetField(name) ?? (object)t.GetProperty(name);
                if (f is System.Reflection.FieldInfo fi) return (bool)fi.GetValue(boxed);
                if (f is System.Reflection.PropertyInfo pi) return (bool)pi.GetValue(boxed);
                return def;
            }
            float GetFloat(string name, float def)
            {
                var f = t.GetField(name) ?? (object)t.GetProperty(name);
                if (f is System.Reflection.FieldInfo fi) return Convert.ToSingle(fi.GetValue(boxed));
                if (f is System.Reflection.PropertyInfo pi) return Convert.ToSingle(pi.GetValue(boxed));
                return def;
            }
            int GetInt(string name, int def)
            {
                var f = t.GetField(name) ?? (object)t.GetProperty(name);
                if (f is System.Reflection.FieldInfo fi) return Convert.ToInt32(fi.GetValue(boxed));
                if (f is System.Reflection.PropertyInfo pi) return Convert.ToInt32(pi.GetValue(boxed));
                return def;
            }

            view.cannotBeCrit  = GetBool("cannotBeCrit", false);
            view.percentReduce = Mathf.Clamp01(GetFloat("percentReduce", 0f));
            view.flatReduce    = Mathf.Max(0, GetInt("flatReduce", 0));
            return true;
        }
        catch { ok = false; }

        return ok;
    }

    private static void UnboxDamageFilter(object box, out bool cannotBeCrit, out float percentReduce, out int flatReduce)
    {
        cannotBeCrit = false;
        percentReduce = 0f;
        flatReduce = 0;

        if (box == null) return;

        var t = box.GetType();
        var f1 = t.GetField("cannotBeCrit"); var p1 = t.GetProperty("cannotBeCrit");
        var f2 = t.GetField("percentReduce"); var p2 = t.GetProperty("percentReduce");
        var f3 = t.GetField("flatReduce"); var p3 = t.GetProperty("flatReduce");

        try
        {
            if (f1 != null) cannotBeCrit = (bool)(f1.GetValue(box) ?? false);
            else if (p1 != null) cannotBeCrit = (bool)(p1.GetValue(box, null) ?? false);
        }
        catch { }

        try
        {
            if (f2 != null) percentReduce = Mathf.Max(0f, (float)(f2.GetValue(box) ?? 0f));
            else if (p2 != null) percentReduce = Mathf.Max(0f, (float)(p2.GetValue(box, null) ?? 0f));
        }
        catch { }

        try
        {
            if (f3 != null) flatReduce = Mathf.Max(0, (int)(f3.GetValue(box) ?? 0));
            else if (p3 != null) flatReduce = Mathf.Max(0, (int)(p3.GetValue(box, null) ?? 0));
        }
        catch { }
    }

    private void SetColoredStat(TextMeshProUGUI label, string name, int baseVal, int finalVal)
    {
        if (!label) return;

        int delta = finalVal - baseVal;
        if (delta == 0)
        {
            label.text  = $"{name}: {finalVal}";
            label.color = StatNeutral;
            return;
        }

        string sign = delta > 0 ? "+" : "";
        label.text  = $"{name}: {finalVal} ({sign}{delta})";
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
        try { BattleLogger.Log(text, LogScope.Battle); } catch { /* ignore */ }
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

        // Training SPD
        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
            spd += Mathf.Max(0, roster[activeIndex].trainingBonus.spd);

        // Job first-turn buff if present
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
        // Speed term: 0..1 centered around 0.5
        int pSpd = GetPlayerEffectiveSpeedForRun();
        int wSpd = GetWildEffectiveSpeedForRun();
        float speedTerm = (pSpd + wSpd) > 0 ? (float)pSpd / (pSpd + wSpd) : 0.5f;

        // HP term: 0..1; higher when wild is lower HP
        float hp01 = 1f - (wildHP / Mathf.Max(1f, wildMaxHP)); // 0 when full, 1 when at 0

        // Attempts: +runAttemptBonus per fail
        float attemptsBonus = runAttemptBonus * Mathf.Max(0, runAttempts);

        float chance =
            runBaseChance +
            runSpeedWeight * (speedTerm - 0.5f) + // ± around base
            runHpWeight    * hp01 +
            attemptsBonus;

        return Mathf.Clamp(chance, runMinChance, runMaxChance);
    }

}
