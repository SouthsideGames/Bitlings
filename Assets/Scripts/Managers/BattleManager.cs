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


    void Start()
    {
        if (benchBtn1) benchBtn1.onClick.AddListener(() => ClickBench(0));
        if (benchBtn2) benchBtn2.onClick.AddListener(() => ClickBench(1));

        if (SaveManager.Data != null && SaveManager.Data.settings != null)
            battleSpeed = Mathf.Clamp(SaveManager.Data.settings.battleSpeed, 0.25f, 5f);

        SetCombatPanels(false);
    }
    void OnDestroy()
    {
        if (benchBtn1) benchBtn1.onClick.RemoveAllListeners();
        if (benchBtn2) benchBtn2.onClick.RemoveAllListeners();
    }

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

        // Don't set inBattle yet — we start only after panels reveal.
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
                shieldHP[i] = teamMaxHP[i] * jobCtx[i].startShieldPctMaxHp;
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

        // ─────────────────────────────────────────────────────────────
        // Panels: activate but invisible (fade in first, then start battle)
        // ─────────────────────────────────────────────────────────────
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

        // Fade panels in, then actually start the battle + TurnLoop
        if (turnCR != null) StopCoroutine(turnCR);
        turnCR = StartCoroutine(Co_RevealPanelsThenStart(wildCG, playerCG, 0.28f)); // 0.28s default fade
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

            // ── Speed / Initiative (job and temp-buff aware)
            int pSpeed = BattleCalc.CalcSpeed(teamDefs[activeIndex], teamLevels[activeIndex]);
            if (BattleTempBuffs.I != null)
                pSpeed = BattleTempBuffs.I.ApplyPlayerSpeedBonus(pSpeed);

            var ctxSpeed = jobCtx != null ? jobCtx[activeIndex] : null;
            if (ctxSpeed != null && ctxSpeed.speedBuffTurns > 0 && ctxSpeed.speedBonusPctFirstTurns != 0f)
                pSpeed = Mathf.Max(1, Mathf.RoundToInt(pSpeed * (1f + ctxSpeed.speedBonusPctFirstTurns)));

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
                // Tick down job-limited buffs
                if (jobCtx != null && jobCtx[activeIndex] != null)
                {
                    if (jobCtx[activeIndex].speedBuffTurns > 0)      jobCtx[activeIndex].speedBuffTurns--;
                    if (jobCtx[activeIndex].critBuffTurns > 0)       jobCtx[activeIndex].critBuffTurns--;
                    if (jobCtx[activeIndex].critResistBuffTurns > 0) jobCtx[activeIndex].critResistBuffTurns--;
                    if (jobCtx[activeIndex].dmgReduceBuffTurns > 0)  jobCtx[activeIndex].dmgReduceBuffTurns--;
                }

                // (Removed) tag-driven survive-round coin drip

                yield return Wait(endRoundDelay);
            }
        }

        turnCR = null;
    }

    private IEnumerator PlayerTurn()
    {
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive()) yield break;

        playerAttacksThisTurn++;

        var ctx = (jobCtx != null) ? jobCtx[activeIndex] : null;

        int flat = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count) flat = Mathf.Max(0, roster[activeIndex].flatAtkBonus);

        int temp = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;

        float atk = BattleCalc.CalcBaseAttack(teamDefs[activeIndex], teamLevels[activeIndex], flat, temp);
        if (ctx != null && ctx.attackBonusPct > 0f) atk *= (1f + ctx.attackBonusPct);

        if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
        {
            var tmods = TitlesAdapter.GetBattleStatMods(teamIds[activeIndex]);
            atk = Mathf.Max(1f, atk + Mathf.Max(0, tmods.atkFlat));
            atk *= (1f + Mathf.Max(0f, tmods.atkPct));
        }

        float tap = TapBoost.I ? TapBoost.I.CurrentMultiplier : 1f;
        atk *= Mathf.Max(1f, tap);

        float playerCrit = critChancePlayer;
        if (ctx != null)
        {
            playerCrit += ctx.critChanceFlat;
            if (ctx.critBuffTurns > 0) playerCrit += ctx.critChanceBonusFirstTurns;
        }
        playerCrit = Mathf.Clamp01(playerCrit);

        // One-turn temp damage buff (currently only via other systems; value may be zero)
        if (tempDmgBuffTurns > 0 && tempDmgBuffPct > 0f)
        {
            atk *= (1f + tempDmgBuffPct);
            BattleLogger.Log($"+{Mathf.RoundToInt(tempDmgBuffPct * 100f)}% damage buff active.", LogScope.Battle);
            tempDmgBuffTurns--;
            if (tempDmgBuffTurns <= 0) tempDmgBuffPct = 0f;
        }

        // No tag-based defense ignore; set to 0
        int defenseIgnore = 0;

        var dr = BattleCalc.ResolveHit(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            null, wildDef, wildLevel,
            atk, playerCrit, critMultiplier, -defenseIgnore
        );

        // Job: first outgoing damage bonus (once)
        var jCtx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        if (jCtx != null && !jCtx.usedFirstOutgoing && jCtx.firstOutgoingBonus > 0f)
        {
            jCtx.usedFirstOutgoing = true;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jCtx.firstOutgoingBonus)));
        }

        bool lethal = dr.damage >= wildHP;

        wildHP = Mathf.Max(0f, wildHP - dr.damage);
        PushHPBars();

        if (!playerLandedFirstHitThisBattle && dr.damage > 0)
        {
            playerLandedFirstHitThisBattle = true;
        }

        // (Removed) tag-driven lifesteal, weaken-on-crit, multi-hit, KO heal, etc.

        BattleLogger.Log($"{GetName(activeIndex)} attacks for {dr.damage}!", LogScope.Battle);

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) BattleLogger.Log("It's super effective!", LogScope.Battle);
            else if (dr.effectiveness < 0.85f) BattleLogger.Log("It's not very effective...", LogScope.Battle);
        }
        if (dr.crit) BattleLogger.Log("Critical hit!", LogScope.Battle);

        if (!playerDidFirstAttackThisBattle) playerDidFirstAttackThisBattle = true;

        // End-turn regen from jobs
        var jCtx2 = (jobCtx != null) ? jobCtx[activeIndex] : null;
        if (jCtx2 != null && jCtx2.endTurnHealPct > 0f)
        {
            bool canHeal = (jCtx2.regenTurns == int.MaxValue) || (jCtx2.regenTurns > 0);
            if (canHeal)
            {
                float healAmt = teamMaxHP[activeIndex] * jCtx2.endTurnHealPct;
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
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive()) yield break;

        enemyAttacksThisTurn++;

        float atk = Mathf.Max(1f, wildAttackPerTurn);

        int flatDefBonus = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;
        var ctx = (jobCtx != null) ? jobCtx[activeIndex] : null;

        float preHP = teamHP[activeIndex];

        float playerCritResist = 0f;
        if (ctx != null)
        {
            playerCritResist += ctx.critResistFlat;
            if (ctx.critResistBuffTurns > 0) playerCritResist += ctx.critResistBonusFirstTurns;
        }

        float wildCritChance = Mathf.Clamp01(critChanceWild - playerCritResist);

        var dr = BattleCalc.ResolveHit(
            null, wildDef, wildLevel,
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            atk, wildCritChance, critMultiplier, flatDefBonus
        );

        // Incoming damage scalar (job-based only; tag-based removed)
        float incomingScalar = 1f;

        if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
        {
            var tmods = TitlesAdapter.GetBattleStatMods(teamIds[activeIndex]);
            if (tmods.defPct > 0f) incomingScalar *= (1f - Mathf.Clamp01(tmods.defPct));
        }

        if (ctx != null && !ctx.usedFirstIncoming && ctx.firstIncomingReduce > 0f)
        { ctx.usedFirstIncoming = true; incomingScalar *= (1f - ctx.firstIncomingReduce); }

        if (ctx != null && ctx.baseDamageReducePct > 0f) incomingScalar *= (1f - ctx.baseDamageReducePct);
        if (ctx != null && ctx.defenseBonusPct > 0f)     incomingScalar *= (1f - ctx.defenseBonusPct);
        if (ctx != null && ctx.dmgReduceBuffTurns > 0 && ctx.dmgReduceFirstTurns > 0f)
            incomingScalar *= (1f - ctx.dmgReduceFirstTurns);

        // (Removed) weaken-from-tags; retains fields but unused now
        if (wildWeakenTurns > 0 && wildWeakenPct > 0f)
        {
            incomingScalar *= (1f - wildWeakenPct);
            BattleLogger.Log($"{(wildDef ? wildDef.displayName : "Foe")} is weakened (-{Mathf.RoundToInt(wildWeakenPct * 100f)}% dmg).", LogScope.Battle);
            wildWeakenTurns--;
            if (wildWeakenTurns <= 0) wildWeakenPct = 0f;
        }

        int dmg = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));

        // (Removed) flat damage reduce from tags

        // Shield
        if (shieldHP != null && shieldHP.Length > activeIndex && shieldHP[activeIndex] > 0f)
        {
            float absorbed = Mathf.Min(shieldHP[activeIndex], dmg);
            shieldHP[activeIndex] -= absorbed;
            dmg -= Mathf.RoundToInt(absorbed);
            if (absorbed > 0f) BattleLogger.Log($"{GetName(activeIndex)}'s shield absorbed {Mathf.RoundToInt(absorbed)}!", LogScope.Battle);
        }

        teamHP[activeIndex] = Mathf.Max(0f, teamHP[activeIndex] - dmg);
        ClampAndPushActiveHP();

        // (Removed) tag-based first-KO resolve and OnAllyKO triggers

        // (Removed) HP threshold crisis heal from tags

        string foeName = wildDef ? wildDef.displayName : "Foe";
        BattleLogger.Log($"{foeName} hits {GetName(activeIndex)} for {dmg}!", LogScope.Battle);

        if (showEffectivenessText)
        {
            if (dr.effectiveness > 1.25f) BattleLogger.Log("It's super effective!", LogScope.Battle);
            else if (dr.effectiveness < 0.85f) BattleLogger.Log("It's not very effective...", LogScope.Battle);
        }
        if (dr.crit) BattleLogger.Log("Critical hit!", LogScope.Battle);

        if (!playerTookFirstIncomingThisBattle) playerTookFirstIncomingThisBattle = true;

        // Rescue heal (job-based)
        if (ctx != null && !ctx.rescueUsed && ctx.rescueHealPct > 0f && teamHP[activeIndex] > 0f)
        {
            float curMax = GetActiveMaxHP(teamMaxHP[activeIndex]);
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
            float curMax = GetActiveMaxHP(teamMaxHP[activeIndex]);
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

        int coins = BattleRewards.CoinsFor(victory, wildLevel, survived);

        if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
        {
            float cm = TitlesAdapter.GetCoinMultOnVictory(teamIds[activeIndex], wildDef, wildLevel);
            if (cm > 0f) coins = Mathf.Max(0, Mathf.RoundToInt(coins * cm));
        }

        // coins stays as computed
        if (coins < 0) coins = 0;

        if (victory)
        {
            float xpMul = 1f;
            if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
                xpMul = TitlesAdapter.GetXPMultOnVictory(teamIds[activeIndex], wildDef, wildLevel);

                BattleRewards.GrantVictoryXPAndEvo(activeIndex, wildLevel, MonsterLibraryLocator.Lib, Mathf.Max(0f, xpMul));
        }


        var teamList = SaveManager.Data.team;
        for (int i = 0; i < teamCount && i < teamList.Count; i++)
        {
            var owned = teamList[i];
            owned.currentHP = Mathf.CeilToInt(Mathf.Max(0f, teamHP[i]));
            teamList[i] = owned;
        }

        BattleTempBuffs.I?.ClearPlayerAtkBonus();
        BattleTempBuffs.I?.ClearPlayerSpeedBonus();
        BattleTempBuffs.I?.ClearPlayerHPBonus();
        BattleTempBuffs.I?.ClearPlayerDefenseBonus();

        BattleLogger.Log($"Battle ends: {(victory ? "Victory" : "Defeat")} (+{coins} coins).", LogScope.Battle);
        BattleLogger.EndBattle(victory);

        var result = new BattleResult
        {
            victory = victory,
            coinsGained = coins,
            wildDef = wildDef,
            wildLevel = wildLevel,
            secondsSurvived = survived
        };

        SetCombatPanels(false);
        if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length)
            TitlesAdapter.OnBattleEnd(teamIds[activeIndex], victory, wildDef, wildLevel);

        onEnd?.Invoke(result);

        // Use the actual getter name:
        bool isAuto = EncounterManager.I && EncounterManager.I.IsAutoMode;
        PostBattleSummaryManager.I?.NotifyBattleEnd(result, isAuto);

        GameEvents.BattleFinished?.Invoke(result);
    }

    private void ClampAndPushActiveHP()
    {
        float curMax = GetActiveMaxHP(teamMaxHP[activeIndex]);
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
            float curMax = GetActiveMaxHP(teamMaxHP[activeIndex]);
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

        // (Removed) tag-driven swap-in effects

        if (teamPendingBuffPct != null && teamPendingBuffTurns != null)
        {
            if (teamPendingBuffPct[activeIndex] > 0f)
            {
                tempDmgBuffPct   += teamPendingBuffPct[activeIndex];
                tempDmgBuffTurns  = Math.Max(tempDmgBuffTurns, teamPendingBuffTurns[activeIndex]);
                BattleLogger.Log($"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.", LogScope.Battle);
                teamPendingBuffPct[activeIndex] = 0f;
                teamPendingBuffTurns[activeIndex] = 0;
            }
        }

        BattleLogger.Log($"Swapped to {GetName(activeIndex)}!", LogScope.Battle);

        // (Removed) tag-driven rescue heal on swap
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

                // (Removed) tag-driven swap-in effects

                if (teamPendingBuffPct != null && teamPendingBuffPct[activeIndex] > 0f)
                {
                    tempDmgBuffPct   += teamPendingBuffPct[activeIndex];
                    tempDmgBuffTurns  = Math.Max(tempDmgBuffTurns, teamPendingBuffTurns[activeIndex]);
                    BattleLogger.Log($"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.", LogScope.Battle);
                    teamPendingBuffPct[activeIndex] = 0f;
                    teamPendingBuffTurns[activeIndex] = 0;
                }

                BattleLogger.Log($"Auto-swapped to {GetName(activeIndex)}!", LogScope.Battle);

                // (Removed) tag-driven rescue heal on swap

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

    public float GetActiveMaxHP(float baseMax)
    {
        int hpBuff = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        return Mathf.Max(1f, baseMax + hpBuff);
    }

    public void TryAddHPToActive(float amount)
    {
        float curMax = GetActiveMaxHP(teamMaxHP[activeIndex]);
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
                    tags.Add($"Harbor shield {HpVal(teamMaxHP[idx] * c.startShieldPctMaxHp)}");
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
                    tags.Add($"Sanctum shield {HpVal(teamMaxHP[idx] * c.startShieldPctMaxHp)}");
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

        // (Removed) tag-driven end turn effects and regen
    }

    private void UpdateWildInfoUI()
    {
        if (!wildDef) return;

        // Calculate current stats
        int dispHP  = Mathf.RoundToInt(BattleCalc.CalcHP(wildDef, wildLevel));
        int dispATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(wildDef, wildLevel, 0, 0));
        int dispDEF = BattleCalc.CalcDefense(wildDef, wildLevel);
        int dispSPD = BattleCalc.CalcSpeed(wildDef, wildLevel);

        // Format exactly as you described
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

        int lvl = teamLevels != null && activeIndex < teamLevels.Length ? teamLevels[activeIndex] : 1;

        // ──────────────────────────────────────────────
        // Base stats (pre-mod)
        // ──────────────────────────────────────────────
        int baseHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        int baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
        int baseDEF = BattleCalc.CalcDefense(def, lvl);
        int baseSPD = BattleCalc.CalcSpeed(def, lvl);

        // ──────────────────────────────────────────────
        // Temp buffs (flat)
        // ──────────────────────────────────────────────
        int tempHPFlat  = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus()        : 0;
        int tempATKFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus()       : 0;
        int tempDEFFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus()   : 0;
        int tempSPDFlat = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0;

        // Optional timers (show as suffixes if your API exposes them; safe to leave empty)
        string tHP  = "";
        string tAtk = "";
        string tDef = "";
        string tSpd = "";

        bool resistOn = BattleTempBuffs.I && BattleTempBuffs.I.IsTypeResistActive();

        // ──────────────────────────────────────────────
        // Equipped flat ATK (from roster)
        // ──────────────────────────────────────────────
        int equippedFlatATK = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count && roster[activeIndex] != null)
            equippedFlatATK = Mathf.Max(0, roster[activeIndex].flatAtkBonus);

        // ──────────────────────────────────────────────
        // Job context (already baked into teamMaxHP etc. in your pipeline)
        // ──────────────────────────────────────────────
        var jc = (jobCtx != null && activeIndex < jobCtx.Length) ? jobCtx[activeIndex] : null;

        // ──────────────────────────────────────────────
        // Title mods (flat + %)
        // ──────────────────────────────────────────────
        TitleStatMods tmods = default;
        if (teamIds != null && activeIndex >= 0 && activeIndex < teamIds.Length && !string.IsNullOrEmpty(teamIds[activeIndex]))
            tmods = TitlesAdapter.GetBattleStatMods(teamIds[activeIndex]);

        // Apply Title FLATs to base lines we’ll display
        baseATK = Mathf.Max(1, baseATK + Mathf.Max(0, tmods.atkFlat));
        baseDEF = Mathf.Max(0, baseDEF + Mathf.Max(0, tmods.defFlat));
        baseSPD = Mathf.Max(1, baseSPD + Mathf.Max(0, tmods.spdFlat));
        // Note: HP flat handled by your temp system; Title gives % to max HP below.

        // ──────────────────────────────────────────────
        // Current/max HP with job %, then Title HP%
        // ──────────────────────────────────────────────
        float maxHPBase = (teamMaxHP != null && activeIndex < teamMaxHP.Length) ? teamMaxHP[activeIndex] : baseHP;
        float curMaxHP  = GetActiveMaxHP(maxHPBase); // adds temp flat HP
        if (tmods.hpPct > 0f) curMaxHP *= (1f + tmods.hpPct); // Title % Max HP

        float curHPRaw = (teamHP != null && activeIndex < teamHP.Length) ? teamHP[activeIndex] : curMaxHP;
        int   curHPDisp = Mathf.RoundToInt(Mathf.Clamp(curHPRaw, 0f, curMaxHP));

        // ──────────────────────────────────────────────
        // Small helpers for badge segments
        // ──────────────────────────────────────────────
        string SegIfFlat(string label, int v, string time)   => (v != 0)  ? $" [{label}+{v}{time}]" : "";
        string SegIfPct (string label, float v, string tail) => (v != 0f) ? $" [{label}+{Mathf.RoundToInt(v * 100f)}%{tail}]" : "";
        string MinusPct (float v)                            => (v > 0f)  ? $" [−{Mathf.RoundToInt(v * 100f)}% dmg]" : "";

        // ──────────────────────────────────────────────
        // Identity block
        // ──────────────────────────────────────────────
        if (playerIdText)     playerIdText.text     = $"ID: {def.id}";
        if (playerTypeText)   playerTypeText.text   = $"TYPE: {def.type}";
        if (playerRarityText) playerRarityText.text = $"RARITY: {def.rarity}";
        if (playerLevelText)  playerLevelText.text  = $"LVL: {lvl}";

        // ──────────────────────────────────────────────
        // HP line
        // (Job max HP% already baked; we just show a badge. Then Title %.)
        // ──────────────────────────────────────────────
        float jobHpPct = (jc != null) ? jc.maxHpBonusPct : 0f; // badge only
        string hpLine = $"HP: {curHPDisp}/{Mathf.RoundToInt(curMaxHP)}";
        hpLine += SegIfFlat("Temp",  tempHPFlat, tHP);
        hpLine += SegIfPct ("Job",   jobHpPct,   "");
        hpLine += SegIfPct ("Title", tmods.hpPct, "");
        if (playerHPText) playerHPText.text = hpLine;

        // ──────────────────────────────────────────────
        // ATK line (equip & temp flats; then Job/Turn/Title %)
        // ──────────────────────────────────────────────
        float jobAtkPct   = (jc != null) ? jc.attackBonusPct : 0f;
        float turnBuffPct = 0f; // keep placeholder for your future turn-buff logic

        int atkShown = Mathf.Max(1, Mathf.RoundToInt(baseATK + equippedFlatATK + tempATKFlat));
        // Apply Title % to the displayed number (kept separate as a badge for clarity)
        int atkShownWithTitle = Mathf.Max(1, Mathf.RoundToInt(atkShown * (1f + Mathf.Max(0f, tmods.atkPct))));

        string atkLine = $"ATK: {atkShownWithTitle}";
        atkLine += SegIfFlat("Equip", equippedFlatATK, "");
        atkLine += SegIfFlat("Temp",  tempATKFlat,     tAtk);
        atkLine += SegIfPct ("Job",   jobAtkPct,       "");
        atkLine += SegIfPct ("Turn",  turnBuffPct,     "");
        atkLine += SegIfPct ("Title", tmods.atkPct,    "");
        if (playerATKText) playerATKText.text = atkLine;

        // ──────────────────────────────────────────────
        // DEF line (+ total damage reduction badges)
        // Title DEF% is represented as additional damage reduction badge
        // and we also show flat temp DEF.
        // ──────────────────────────────────────────────
        float dmgReducePct = 0f;
        if (jc != null)
        {
            dmgReducePct += Mathf.Max(0f, jc.baseDamageReducePct);
            if (jc.dmgReduceBuffTurns > 0 && jc.dmgReduceFirstTurns > 0f)
                dmgReducePct += jc.dmgReduceFirstTurns;
            if (jc.defenseBonusPct > 0f)
                dmgReducePct += jc.defenseBonusPct;
        }
        // Treat Title DEF% as mitigation badge for UI clarity
        if (tmods.defPct > 0f) dmgReducePct += tmods.defPct;

        int defShown = Mathf.Max(0, baseDEF + tempDEFFlat);
        string defLine = $"DEF: {defShown}";
        defLine += SegIfFlat("Temp",  tempDEFFlat, tDef);
        defLine += MinusPct(dmgReducePct);
        if (playerDEFText) playerDEFText.text = defLine;

        // ──────────────────────────────────────────────
        // SPD line (temp flat; then Job/Title %)
        // ──────────────────────────────────────────────
        int spdFlatTotal = Mathf.Max(0, tempSPDFlat); // (Title flat already added to baseSPD)
        int spdShown = Mathf.Max(1, baseSPD + spdFlatTotal);
        int spdShownWithTitle = Mathf.Max(1, Mathf.RoundToInt(spdShown * (1f + Mathf.Max(0f, tmods.spdPct))));

        string spdLine = $"SPD: {spdShownWithTitle}";
        spdLine += SegIfFlat("Temp",  tempSPDFlat, tSpd);
        if (jc != null && jc.speedBuffTurns > 0 && jc.speedBonusPctFirstTurns > 0f)
            spdLine += SegIfPct("Job", jc.speedBonusPctFirstTurns, $" ({jc.speedBuffTurns}t)");
        spdLine += SegIfPct("Title", tmods.spdPct, "");
        if (playerSPDText) playerSPDText.text = spdLine;

        // ──────────────────────────────────────────────
        // Optional active resist badge
        // ──────────────────────────────────────────────
        if (resistOn && playerRarityText)
            playerRarityText.text += " [Resist]";
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
        float max = Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[teamIdx]));
        int icur = Mathf.CeilToInt(cur);
        int imax = Mathf.CeilToInt(max);

        label.gameObject.SetActive(true);
        label.text = $"{icur}/{imax}";
        // Optional dim when fainted:
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

        // Tween alpha if LeanTween available
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
        // Mark battle started only after panels are visible
        inBattle  = true;
        startTime = Time.unscaledTime;

        // Centralized logging
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

}
