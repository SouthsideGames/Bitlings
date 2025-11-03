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

    private bool inBattle;
    private Action<BattleResult> onEnd;
    private float startTime;
    private Coroutine turnCR;

    private int roundIndex = 0;
    private int playerAttacksThisTurn = 0;
    private int enemyAttacksThisTurn  = 0;
    private bool playerActsFirstThisRound = true;
    private bool playerDidFirstAttackThisBattle = false;
    private bool playerTookFirstIncomingThisBattle = false;
    private bool playerLandedFirstHitThisBattle = false;

    private int   tempDmgBuffTurns = 0;
    private float tempDmgBuffPct   = 0f;
    private int   playerNoDmgTurns = 0;
    private int   playerNoCritTurns = 0;
    private int wildWeakenTurns = 0; // retained for future systems; unused now
    private float wildWeakenPct = 0f;

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

            teamIds[i]   = owned.monsterId;
            teamDefs[i]  = def;
            teamLevels[i]= owned.level;

            teamMaxHP[i] = BattleCalc.CalcHP(def, owned.level);
            int savedHP = owned.currentHP;
            teamHP[i] = (savedHP >= 0)
                ? Mathf.Clamp(savedHP, 0, (int)teamMaxHP[i])
                : teamMaxHP[i];
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

        roundIndex = 0;
        playerAttacksThisTurn = 0;
        enemyAttacksThisTurn  = 0;
        playerActsFirstThisRound = true;
        playerDidFirstAttackThisBattle = false;
        playerTookFirstIncomingThisBattle = false;
        playerLandedFirstHitThisBattle = false;

        tempDmgBuffTurns = 0;
        tempDmgBuffPct   = 0f;

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

            round++;
            roundIndex = round;
            playerAttacksThisTurn = 0;
            enemyAttacksThisTurn  = 0;

            BattleLogger.Log($"— Round {round} —", LogScope.Battle);

            yield return Wait(beginRoundDelay);

            // ── Speed / Initiative (job + titles + temp buffs)
            int pSpeed = BattleCalc.CalcSpeed(teamDefs[activeIndex], teamLevels[activeIndex], teamIds[activeIndex]);
            if (BattleTempBuffs.I != null)
                pSpeed = BattleTempBuffs.I.ApplyPlayerSpeedBonus(pSpeed);

            var ctxSpeed = jobCtx != null ? jobCtx[activeIndex] : null;
            if (ctxSpeed != null && ctxSpeed.speedBuffTurns > 0 && ctxSpeed.speedBonusPctFirstTurns != 0f)
                pSpeed = Mathf.Max(1, Mathf.RoundToInt(pSpeed * (1f + ctxSpeed.speedBonusPctFirstTurns)));

            // Conditional Title boost via router (explicit "SPD")
            var titleCtx = BuildTitleContextForActive();
            float spdF = TitlesAdapter.GetStatValue(teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex], "SPD", titleCtx, pSpeed);
            pSpeed = Mathf.Max(1, Mathf.RoundToInt(spdF));

            int wSpeed = BattleCalc.CalcSpeed(wildDef, wildLevel);

            bool playerFirst = pSpeed >= wSpeed;
            playerActsFirstThisRound = playerFirst;
            if (playerFirst) BattleLogger.Log($"{GetName(activeIndex)} acts first!", LogScope.Battle);
            else             BattleLogger.Log($"{(wildDef ? wildDef.displayName : "Foe")} acts first!", LogScope.Battle);

            if (playerFirst)
            {
                if (!IsWildKO() && !IsTeamKO()) { yield return PlayerTurn(); if (CheckEnd()) break; yield return Wait(hitPause); }
                if (!IsWildKO() && !IsTeamKO()) { yield return EnemyTurn();  if (CheckEnd()) break; yield return Wait(hitPause); }
            }
            else
            {
                if (!IsWildKO() && !IsTeamKO()) { yield return EnemyTurn();  if (CheckEnd()) break; yield return Wait(hitPause); }
                if (!IsWildKO() && !IsTeamKO()) { yield return PlayerTurn(); if (CheckEnd()) break; yield return Wait(hitPause); }
            }

            if (!IsWildKO() && !IsTeamKO())
            {
                if (jobCtx != null && jobCtx[activeIndex] != null)
                {
                    if (jobCtx[activeIndex].speedBuffTurns > 0)      jobCtx[activeIndex].speedBuffTurns--;
                    if (jobCtx[activeIndex].critBuffTurns > 0)       jobCtx[activeIndex].critBuffTurns--;
                    if (jobCtx[activeIndex].critResistBuffTurns > 0) jobCtx[activeIndex].critResistBuffTurns--;
                    if (jobCtx[activeIndex].dmgReduceBuffTurns > 0)  jobCtx[activeIndex].dmgReduceBuffTurns--;
                }

                yield return Wait(endRoundDelay);
            }
        }

        turnCR = null;
    }

    private IEnumerator PlayerTurn()
    {
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive()) yield break;

        playerAttacksThisTurn++;

        // --- Base ATK (no active boosts here) ---
        int flat = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count) flat = Mathf.Max(0, roster[activeIndex].flatAtkBonus);

        int tempFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;
        float atkBase = BattleCalc.CalcBaseAttack(teamDefs[activeIndex], teamLevels[activeIndex], flat, tempFlat);

        // Conditional Title ATK via router (explicit "ATK"); still part of base, not an "active" boost
        var titleCtx = BuildTitleContextForActive();
        float atkNoActives = TitlesAdapter.GetStatValue(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex], "ATK", titleCtx, atkBase
        );

        // Crit chance (job buffs)
        var ctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float playerCrit = critChancePlayer;
        if (ctx != null)
        {
            playerCrit += ctx.critChanceFlat;
            if (ctx.critBuffTurns > 0) playerCrit += ctx.critChanceBonusFirstTurns;
        }
        playerCrit = Mathf.Clamp01(playerCrit);

        // Resolve using BASE (no actives, no job damage scalers yet)
        int defenseIgnore = 0;
        var dr = BattleCalc.ResolveHit(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            null, wildDef, wildLevel,
            atkNoActives, playerCrit, critMultiplier, -defenseIgnore
        );

        // ─────────────────────────────────────────────────────────────
        // Order: Type effectiveness (inside ResolveHit) → Job passives → Title boosts → Active boosts
        // ─────────────────────────────────────────────────────────────

        // (1) JOB PASSIVES → as damage scalers (not in base ATK)
        if (ctx != null && ctx.attackBonusPct > 0f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + ctx.attackBonusPct)));

        if (ctx != null && !ctx.usedFirstOutgoing && ctx.firstOutgoingBonus > 0f)
        {
            ctx.usedFirstOutgoing = true;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + ctx.firstOutgoingBonus)));
        }

        // (2) TITLE BOOSTS → effectiveness mods (Multiply first, then Add)
        float effMul = TitlesAdapter.GetEffectivenessMult(teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]);
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

        float effAdd = TitlesAdapter.GetEffectivenessAdd(teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]);
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

        // (3) ACTIVE BOOSTS → Tap + Temp LAST
        float tap = TapBoost.I ? TapBoost.I.CurrentMultiplier : 1f;
        if (tap > 1f)
        {
            int before = dr.damage;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * tap));
            if (debugEffectivenessOutgoing)
            {
                string msg = $"[ActiveBoost] Tap x{tap:0.00}: {before} → {dr.damage}";
                try { BattleLogger.Log(msg, LogScope.Battle); } catch { }
                Debug.Log(msg);
            }
        }

        if (tempDmgBuffTurns > 0 && tempDmgBuffPct > 0f)
        {
            int before = dr.damage;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + tempDmgBuffPct)));
            BattleLogger.Log($"+{Mathf.RoundToInt(tempDmgBuffPct * 100f)}% damage buff active.", LogScope.Battle);
            tempDmgBuffTurns--;
            if (tempDmgBuffTurns <= 0) tempDmgBuffPct = 0f;

            if (debugEffectivenessOutgoing)
                Debug.Log($"[ActiveBoost] TempDmg applied: {before} → {dr.damage}");
        }

        // Apply damage
        bool lethal = dr.damage >= wildHP;
        wildHP = Mathf.Max(0f, wildHP - dr.damage);
        PushHPBars();

        if (!playerLandedFirstHitThisBattle && dr.damage > 0)
            playerLandedFirstHitThisBattle = true;

        BattleLogger.Log($"{GetName(activeIndex)} attacks for {dr.damage}!", LogScope.Battle);

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) BattleLogger.Log("It's super effective!", LogScope.Battle);
            else if (dr.effectiveness < 0.85f) BattleLogger.Log("It's not very effective...", LogScope.Battle);
        }
        if (dr.crit) BattleLogger.Log("Critical hit!", LogScope.Battle);

        if (!playerDidFirstAttackThisBattle) playerDidFirstAttackThisBattle = true;

        // End-turn regen (jobs)
        var jCtx2 = (jobCtx != null) ? jobCtx[activeIndex] : null;
        if (jCtx2 != null && jCtx2.endTurnHealPct > 0f)
        {
            bool canHeal = (jCtx2.regenTurns == int.MaxValue) || (jCtx2.regenTurns > 0);
            if (canHeal)
            {
                float healAmt = GetFinalMaxHPForIndex(activeIndex) * jCtx2.endTurnHealPct;
                TryAddHPToActive(healAmt);
                if (jCtx2.regenTurns != int.MaxValue) jCtx2.regenTurns--;
                BattleLogger.Log($"{GetName(activeIndex)} regenerates {Mathf.RoundToInt(healAmt)} HP.", LogScope.Battle);
            }
        }

        Punch(playerIcon);
        FirePlayerEndTurnTicks(dealtDamageThisTurn: dr.damage > 0, critThisTurn: dr.crit);
        yield break;
    }


    private IEnumerator EnemyTurn()
    {
        if (debugIncomingMitigation) Debug.Log("[Mitigation] EnemyTurn started — debugIncomingMitigation = TRUE");

        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive()) yield break;

        enemyAttacksThisTurn++;

        float atk = Mathf.Max(1f, wildAttackPerTurn);

        int flatDefBonus = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;
        var ctx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        float preHP = teamHP[activeIndex];

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

        // Resolve (defender ID present)
        var dr = BattleCalc.ResolveHit(
            null, wildDef, wildLevel,
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            atk, wildCritChance, critMultiplier, flatDefBonus
        );

        // ── DEBUG capture BEFORE any filters
        int  baseRawDamage      = dr.damage;
        bool critRolled         = dr.crit;
        bool critNegatedByTitle = false;

        // If runtime forced "cannot be crit" and RNG still flagged crit (edge), re-resolve without crit.
        if (df.cannotBeCrit && dr.crit)
        {
            critNegatedByTitle = true;
            dr = BattleCalc.ResolveHit(
                null, wildDef, wildLevel,
                teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
                atk, 0f, critMultiplier, flatDefBonus
            );
        }

        // Incoming damage scalar (ConditionalBooster + Jobs)
        float incomingScalar = 1f;

        // ConditionalBooster: DEF% mitigation
        var cmods = GetConditionalModsForActive();
        if (cmods.defPct > 0f) incomingScalar *= (1f - Mathf.Clamp01(cmods.defPct));

        // Job-based reductions
        if (ctx != null && !ctx.usedFirstIncoming && ctx.firstIncomingReduce > 0f)
        { ctx.usedFirstIncoming = true; incomingScalar *= (1f - ctx.firstIncomingReduce); }

        if (ctx != null && ctx.baseDamageReducePct > 0f) incomingScalar *= (1f - ctx.baseDamageReducePct);
        if (ctx != null && ctx.defenseBonusPct    > 0f) incomingScalar *= (1f - ctx.defenseBonusPct);
        if (ctx != null && ctx.dmgReduceBuffTurns > 0 && ctx.dmgReduceFirstTurns > 0f)
            incomingScalar *= (1f - ctx.dmgReduceFirstTurns);

        // Optional weaken remains
        if (wildWeakenTurns > 0 && wildWeakenPct > 0f)
        {
            incomingScalar *= (1f - wildWeakenPct);
            BattleLogger.Log($"{(wildDef ? wildDef.displayName : "Foe")} is weakened (-{Mathf.RoundToInt(wildWeakenPct * 100f)}% dmg).", LogScope.Battle);
            wildWeakenTurns--;
            if (wildWeakenTurns <= 0) wildWeakenPct = 0f;
        }

        // ── Stage 1: apply job/conditional scalar first
        int dmg_afterScalar = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));

        // ── Stage 1.5: incoming effectiveness titles (defensive) BEFORE %/flat filters
        float incomingEffMul = TitleManager.I
            ? Mathf.Max(0f, TitleManager.I.GetIncomingEffectivenessMultiplier(teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex]))
            : 1f;
        if (!Mathf.Approximately(incomingEffMul, 1f))
        {
            int beforeInc = dmg_afterScalar;
            dmg_afterScalar = Mathf.Max(1, Mathf.RoundToInt(dmg_afterScalar * incomingEffMul));
            if (debugIncomingMitigation)
                MitLog($"  • After Title Incoming-Effectiveness x{incomingEffMul:0.00}: {beforeInc} → {dmg_afterScalar}");
        }

        // ── Stage 2: Titles — % reduce then flat reduce
        float percentReduce = Mathf.Clamp01(df.percentReduce);
        int   flatReduce    = Mathf.Max(0, df.flatReduce);

        int dmg_afterPercent = (percentReduce > 0f)
            ? Mathf.Max(1, Mathf.RoundToInt(dmg_afterScalar * (1f - percentReduce)))
            : dmg_afterScalar;

        int dmg_afterFlat = (flatReduce > 0)
            ? Mathf.Max(1, dmg_afterPercent - flatReduce)
            : dmg_afterPercent;

        // ── Stage 3: Shield (absorbs after titles’ reductions)
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

        // ── Apply to HP
        teamHP[activeIndex] = Mathf.Max(0f, teamHP[activeIndex] - dmg_final);
        ClampAndPushActiveHP();

        string foeName = wildDef ? wildDef.displayName : "Foe";
        BattleLogger.Log($"{foeName} hits {GetName(activeIndex)} for {dmg_final}!", LogScope.Battle);

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) BattleLogger.Log("It's super effective!", LogScope.Battle);
            else if (dr.effectiveness < 0.85f) BattleLogger.Log("It's not very effective...", LogScope.Battle);
        }
        if (dr.crit) BattleLogger.Log("Critical hit!", LogScope.Battle);

        if (!playerTookFirstIncomingThisBattle) playerTookFirstIncomingThisBattle = true;

        // ── DEBUG: structured mitigation breakdown
        if (debugIncomingMitigation)
        {
            MitLogOncePerTurnHeader(critRolled, critNegatedByTitle);

            int dmg_noTitlesNoJobs = baseRawDamage;
            int dmg_afterJobsOnly  = Mathf.Max(1, Mathf.RoundToInt(baseRawDamage * incomingScalar));
            int shieldAbsInt       = Mathf.RoundToInt(shieldAbsorbF);
            int jobsPctOff         = Mathf.RoundToInt((1f - incomingScalar) * 100f);
            int titlePctOff        = Mathf.RoundToInt(percentReduce * 100f);
            int shieldBeforeInt    = Mathf.RoundToInt(shieldBefore);

            var text =
                $"  • Base: {dmg_noTitlesNoJobs}\n" +
                $"  • After Job/Conditional scalar ({jobsPctOff}% off): {dmg_afterJobsOnly}\n" +
                (Mathf.Approximately(incomingEffMul, 1f) ? "" :
                $"  • After Title Incoming-Effectiveness x{incomingEffMul:0.00}: {dmg_afterJobsOnly} → {dmg_afterScalar}\n") +
                $"  • After Title % ({titlePctOff}% off): {dmg_afterPercent}\n" +
                $"  • After Title Flat (-{flatReduce}): {dmg_afterFlat}\n" +
                $"  • Shield Absorb (-{shieldAbsInt}, was {shieldBeforeInt}): {dmg_afterFlat - shieldAbsInt}\n" +
                $"  ⇒ Final Applied: {dmg_final}";

            MitLog(text);
        }

        // Rescue heal (job-based)
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

        if (turnCR != null) { StopCoroutine(turnCR); turnCR = null; }

        float survived = Mathf.Max(0f, Time.unscaledTime - startTime);

        // ── COINS: base, then apply Title multiplier (title-only bonus)
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

        // ── XP: compute base vs shiny/training vs title
        int xpBaseTimesTraining = 0;
        int xpTitleBonus = 0;
        int xpTotal = 0;

        if (victory)
        {
            // Base raw XP per your rules (5 + 2*wildLevel), then shiny/training mult, then title mult
            int baseRaw = Mathf.Max(0, 5 + 2 * wildLevel); // mirrors BattleRewards base
            var data = SaveManager.Data;
            var m = (data != null && data.team != null && activeIndex >= 0 && activeIndex < data.team.Count) ? data.team[activeIndex] : default;

            float shinyMul = ShinySystems.TrainingXpMult(m); // same factor BattleRewards uses
            int baseAfterShiny = Mathf.RoundToInt(baseRaw * shinyMul);

            float titleXPMul = 1f;
            if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
                titleXPMul = Mathf.Max(0f, TitlesAdapter.GetXPMultOnVictory(teamIds[activeIndex], wildDef, wildLevel));

            xpTotal = Mathf.RoundToInt(baseAfterShiny * titleXPMul);
            xpBaseTimesTraining = baseAfterShiny;
            xpTitleBonus = Mathf.Max(0, xpTotal - baseAfterShiny);

            // Now actually grant & save through the normal path (persists XP/level)
            float passMulToRewards = titleXPMul; // Rewards already applies shiny/training inside
            BattleRewards.GrantVictoryXPAndEvo(activeIndex, wildLevel, MonsterLibraryLocator.Lib, Mathf.Max(0f, passMulToRewards));
        }

        // Write HP back to save
       var teamList  = SaveManager.Data.team ?? new List<OwnedMonsterData>();
        var ownedList = SaveManager.Data.owned ?? new List<OwnedMonsterData>();

        // 1) Persist post-battle HP into TEAM entries
        for (int i = 0; i < teamCount && i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            int hp = Mathf.CeilToInt(Mathf.Max(0f, teamHP[i]));
            t.currentHP = hp;
            teamList[i] = t;
        }

        // 2) Mirror those TEAM HP values back into OWNED collection
        long nowUnix = SaveManager.NowUnix();

        for (int i = 0; i < teamList.Count; i++)
        {
            var t = teamList[i];
            if (t == null || string.IsNullOrEmpty(t.monsterId)) continue;

            // find matching owned entry by monsterId
            for (int j = 0; j < ownedList.Count; j++)
            {
                var o = ownedList[j];
                if (!string.IsNullOrEmpty(o.monsterId) && o.monsterId == t.monsterId)
                {
                    o.currentHP  = Mathf.Max(0, t.currentHP); // <-- the fix: OWNED gets true post-battle HP
                    o.lastHPUnix = nowUnix;                    // for regen timing
                    ownedList[j] = o;
                    break;
                }
            }
        }

        // 3) Timestamp team copies and remove KO’d from active team (still owned at 0 HP)
        for (int i = 0; i < teamList.Count; i++)
        {
            var e = teamList[i];
            if (e == null || string.IsNullOrEmpty(e.monsterId)) continue;

            e.lastHPUnix = nowUnix;

            if (e.currentHP <= 0)
            {
                // Remove from team (kept in owned with 0 HP)
                teamList[i] = new OwnedMonsterData();
            }
            else
            {
                teamList[i] = e;
            }
        }

        SaveManager.Data.owned = ownedList;
        SaveManager.Data.team  = teamList;
        SaveManager.Save();
        GameEvents.OnTeamChanged?.Invoke();

        // Clear temps
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

        // Build optional XP detail line for the active monster
        var xpLines = new List<string>();
        try
        {
            var data = SaveManager.Data;
            if (data != null && data.team != null && activeIndex >= 0 && activeIndex < data.team.Count)
            {
                var def = teamDefs[activeIndex];
                var owned = data.team[activeIndex];

                // We don’t know the exact before/after snapshot here without tracking pre-battle values,
                // but we can still show a nice single-line breakdown with title bonus in green:
                // e.g. "Cindrax Lv5 → +36 (<color=#3CDE74>+6</color>) XP"
                string nm = def ? def.displayName : (owned.monsterId ?? "Ally");
                xpLines.Add($"{nm} Lv{owned.level} → +{Mathf.Max(0, xpBaseTimesTraining + xpTitleBonus)} (<color=#3CDE74>+{xpTitleBonus}</color>) XP");
            }
        }
        catch { /* non-fatal UI sugar */ }

        bool isAuto = EncounterManager.I && EncounterManager.I.IsAutoMode;
        PostBattleSummaryManager.I?.NotifyBattleEnd(
            result,
            isAuto,
            xpTotal,
            monstersLeveledUp: 0,
            captured: false,
            capturedMonsterId: null,
            capturedLevel: 0,
            levelUpSummaries: null,
            coinsBase: baseCoins,
            coinsTitleBonus: coinTitleBonus,
            xpBase: xpBaseTimesTraining,
            xpTitleBonus: xpTitleBonus,
            xpDetailLines: xpLines
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

        List<int> others = new();
        for (int i = 0; i < teamCount; i++) if (i != activeIndex) others.Add(i);
        if (benchSlot < 0 || benchSlot >= others.Count) return;

        int targetIndex = others[benchSlot];
        if (teamHP[targetIndex] <= 0f) return;

        (teamDefs[activeIndex], teamDefs[targetIndex])       = (teamDefs[targetIndex], teamDefs[activeIndex]);
        (teamLevels[activeIndex], teamLevels[targetIndex])   = (teamLevels[targetIndex], teamLevels[activeIndex]);
        (teamMaxHP[activeIndex], teamMaxHP[targetIndex])     = (teamMaxHP[targetIndex], teamMaxHP[activeIndex]);
        (teamHP[activeIndex], teamHP[targetIndex])           = (teamHP[targetIndex], teamHP[activeIndex]);
        (teamIds[activeIndex], teamIds[targetIndex])         = (teamIds[targetIndex], teamIds[activeIndex]);

        var t = SaveManager.Data.team[activeIndex];
        SaveManager.Data.team[activeIndex] = SaveManager.Data.team[targetIndex];
        SaveManager.Data.team[targetIndex] = t;
        SaveManager.Save();

        ApplyActiveToUI();
        ClampAndPushActiveHP();
        RefreshBenchUI();

        if (teamPendingBuffPct != null && teamPendingBuffTurns != null)
        {
            if (teamPendingBuffPct[activeIndex] > 0f)
            {
                tempDmgBuffPct   += teamPendingBuffPct[activeIndex];
                tempDmgBuffTurns  = Mathf.Max(tempDmgBuffTurns, teamPendingBuffTurns[activeIndex]);
                BattleLogger.Log($"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.", LogScope.Battle);
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
            if (teamHP[i] > 0f)
            {
                (teamDefs[activeIndex], teamDefs[i])       = (teamDefs[i], teamDefs[activeIndex]);
                (teamLevels[activeIndex], teamLevels[i])   = (teamLevels[i], teamLevels[activeIndex]);
                (teamMaxHP[activeIndex], teamMaxHP[i])     = (teamMaxHP[i], teamMaxHP[activeIndex]);
                (teamHP[activeIndex], teamHP[i])           = (teamHP[i], teamHP[activeIndex]);
                (teamIds[activeIndex], teamIds[i])         = (teamIds[i], teamIds[activeIndex]);

                var t = SaveManager.Data.team[activeIndex];
                SaveManager.Data.team[activeIndex] = SaveManager.Data.team[i];
                SaveManager.Data.team[i] = t;
                SaveManager.Save();

                ApplyActiveToUI();
                ClampAndPushActiveHP();
                RefreshBenchUI();

                if (teamPendingBuffPct != null && teamPendingBuffPct[activeIndex] > 0f)
                {
                    tempDmgBuffPct   += teamPendingBuffPct[activeIndex];
                    tempDmgBuffTurns  = Mathf.Max(tempDmgBuffTurns, teamPendingBuffTurns[activeIndex]);
                    BattleLogger.Log($"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.", LogScope.Battle);
                    teamPendingBuffPct[activeIndex] = 0f;
                    teamPendingBuffTurns[activeIndex] = 0;
                }

                BattleLogger.Log($"Auto-swapped to {GetName(activeIndex)}!", LogScope.Battle);
                return true;
            }
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

        // PURE BASE
        int baseHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        int baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
        int baseDEF = BattleCalc.CalcDefense(def, lvl);
        int baseSPD = BattleCalc.CalcSpeed(def, lvl);

        // TEMP / EQUIP FLATS
        int tempHPFlat  = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus()        : 0;
        int tempATKFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus()       : 0;
        int tempDEFFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus()   : 0;
        int tempSPDFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0;

        int equippedFlatATK = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
            equippedFlatATK = Mathf.Max(0, roster[activeIndex].flatAtkBonus);

        // Title context (conditionals)
        var ctx = TitleContext.Empty;
        ctx.ownedId = (teamIds != null && activeIndex < teamIds.Length) ? teamIds[activeIndex] : "";

        float maxHPForCtx = Mathf.Max(1f, baseHP + tempHPFlat);
        float currentHP   = (teamHP != null && activeIndex < teamHP.Length) ? teamHP[activeIndex] : maxHPForCtx;
        ctx.selfHp01 = Mathf.Clamp01(currentHP / maxHPForCtx);

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
        int hpBaseForDisplay  = Mathf.Max(1, baseHP + tempHPFlat);
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

        int hpBuff = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        v = Mathf.Max(1f, v + hpBuff);
 
        if (idx >= 0)
        {
            var tmods = GetTitleModsForIndex(idx);
            if (tmods.hpPct > 0f) v *= (1f + tmods.hpPct);
        }
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

    // Shows conditional-only contribution as a suffix, e.g. " {cond +12}"
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
}