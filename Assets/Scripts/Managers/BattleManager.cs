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

    private bool  firstKOTakenProcessed = false;
    private int   tempDmgBuffTurns = 0;
    private float tempDmgBuffPct   = 0f;
    private int   playerNoDmgTurns = 0;
    private int   playerNoCritTurns = 0;
    private int wildWeakenTurns = 0;
    private float wildWeakenPct = 0f;

    private bool firstKODealtProcessed = false;

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

        TagRuntime.ResetBattleState();

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

        firstKOTakenProcessed = false;
        firstKODealtProcessed = false;
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

        // Prep battle-start one-turn buff now (safe before loop)
        {
            var ctx = new TagRuntime.TagContext
            {
                turnIndex = Mathf.Max(1, roundIndex),
                battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                selfHp01 = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
                enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                enemyIsBoss = (wildDef && wildDef.isBoss),
            };
            float mul = TagRuntime.EvaluateConditionalMultiplier(
                teamIds[activeIndex],
                new[] { TagTrigger.OnBattleStart },
                ctx,
                teamDefs[activeIndex],
                wildDef
            );
            if (mul > 1f)
            {
                float addPct = Mathf.Max(0f, mul - 1f);
                tempDmgBuffPct += addPct;
                tempDmgBuffTurns = Math.Max(tempDmgBuffTurns, 1);
                BattleLogger.Log($"{GetName(activeIndex)} focuses at battle start (+{Mathf.RoundToInt(addPct * 100f)}% dmg next turn).", LogScope.Battle);
            }
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

            // True round-start tick → OnEachRound > 1 gives 1-turn damage buff
            {
                var ctx = new TagRuntime.TagContext
                {
                    turnIndex = Mathf.Max(1, roundIndex),
                    battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                    selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
                    enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                    enemyIsBoss = (wildDef && wildDef.isBoss),
                };

                float roundMul = TagRuntime.EvaluateConditionalMultiplier(
                    teamIds[activeIndex],
                    new[] { TagTrigger.OnEachRound },
                    ctx,
                    teamDefs[activeIndex],
                    wildDef
                );
                if (roundMul > 1f)
                {
                    float addPct = roundMul - 1f;
                    tempDmgBuffPct  += addPct;
                    tempDmgBuffTurns = Math.Max(tempDmgBuffTurns, 1);
                    BattleLogger.Log($"{GetName(activeIndex)} rallies at round start (+{Mathf.RoundToInt(addPct*100f)}% dmg this turn).", LogScope.Battle);
                }
            }

            // OnBattleLength at round start → one-turn damage buff
            {
                var ctx = new TagRuntime.TagContext
                {
                    turnIndex = Mathf.Max(1, roundIndex),
                    battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                    selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
                    enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                    enemyIsBoss = (wildDef && wildDef.isBoss),
                };
                float mul = TagRuntime.EvaluateConditionalMultiplier(
                    teamIds[activeIndex],
                    new[] { TagTrigger.OnBattleLength },
                    ctx,
                    teamDefs[activeIndex],
                    wildDef
                );
                if (mul > 1f)
                {
                    float addPct = Mathf.Max(0f, mul - 1f);
                    tempDmgBuffPct  += addPct;
                    tempDmgBuffTurns = Math.Max(tempDmgBuffTurns, 1);
                    BattleLogger.Log($"{GetName(activeIndex)} adapts to the battle length (+{Mathf.RoundToInt(addPct*100f)}% dmg next turn).", LogScope.Battle);
                }
            }

            yield return Wait(beginRoundDelay);

            // ── Speed / Initiative (tag-aware via OnSpeedCheck)
            int pSpeed = BattleCalc.CalcSpeed(teamDefs[activeIndex], teamLevels[activeIndex]);
            if (BattleTempBuffs.I != null)
                pSpeed = BattleTempBuffs.I.ApplyPlayerSpeedBonus(pSpeed);

            var ctxSpeed = jobCtx != null ? jobCtx[activeIndex] : null;
            if (ctxSpeed != null && ctxSpeed.speedBuffTurns > 0 && ctxSpeed.speedBonusPctFirstTurns != 0f)
                pSpeed = Mathf.Max(1, Mathf.RoundToInt(pSpeed * (1f + ctxSpeed.speedBonusPctFirstTurns)));

            int wSpeed = BattleCalc.CalcSpeed(wildDef, wildLevel);

            // Tag-driven speed check bonuses (apply % to computed speed)
            {
                var spdCtx = new TagRuntime.TagContext
                {
                    turnIndex = Mathf.Max(1, roundIndex),
                    battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                    selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
                    enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                    enemyIsBoss = (wildDef && wildDef.isBoss),
                };
                pSpeed = TagRuntime.ApplySpeedCheckBonus(teamIds[activeIndex], spdCtx, teamDefs[activeIndex], wildDef, pSpeed);
                wSpeed = TagRuntime.ApplySpeedCheckBonus(null, spdCtx, wildDef, teamDefs[activeIndex], wSpeed);
            }

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

                // Survive-round coin drip (tag-scaled internally)
                int gained = TagRuntime.CoinsForSurviveRounds(teamIds[activeIndex], roundsSurvived: Mathf.Max(0, roundIndex));
                if (gained > 0)
                {
                    ResourceManager.I.Add(ResourceType.Coins, gained);
                    BattleLogger.Log($"+{gained} coins for surviving {roundIndex} rounds!", LogScope.Battle);
                }

                TagRuntime.TickEndOfRound(teamIds[activeIndex]);

                yield return Wait(endRoundDelay);
            }
        }

        turnCR = null;
    }

    private IEnumerator PlayerTurn()
    {
        if (teamHP[activeIndex] <= 0.01f && !AutoSwapToAlive()) yield break;

        playerAttacksThisTurn++;

        // Every 3rd player turn → one-turn damage buff (OnEvery3Turns)
        if (((roundIndex + 1) % 3) == 0)
        {
            var cadenceCtx = new TagRuntime.TagContext
            {
                turnIndex = Mathf.Max(1, roundIndex),
                battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
                enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                enemyIsBoss = (wildDef && wildDef.isBoss),
                everyNthTurnN = 3
            };
            float tMul = TagRuntime.EvaluateConditionalMultiplier(
                teamIds[activeIndex],
                new[] { TagTrigger.OnEvery3Turns },
                cadenceCtx,
                teamDefs[activeIndex],
                wildDef
            );
            if (tMul > 1f)
            {
                float addPct = Mathf.Max(0f, tMul - 1f);
                tempDmgBuffPct  += addPct;
                tempDmgBuffTurns = Math.Max(tempDmgBuffTurns, 1);
                BattleLogger.Log($"{GetName(activeIndex)} is charged (+{Mathf.RoundToInt(addPct*100f)}% dmg this turn).", LogScope.Battle);
            }
        }

        var ctx = (jobCtx != null) ? jobCtx[activeIndex] : null;

        int flat = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count) flat = Mathf.Max(0, roster[activeIndex].flatAtkBonus);

        int temp = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;

        float atk = BattleCalc.CalcBaseAttack(teamDefs[activeIndex], teamLevels[activeIndex], flat, temp);
        if (ctx != null && ctx.attackBonusPct > 0f) atk *= (1f + ctx.attackBonusPct);

        float tap = TapBoost.I ? TapBoost.I.CurrentMultiplier : 1f;
        atk *= Mathf.Max(1f, tap);

        float playerCrit = critChancePlayer;
        if (ctx != null)
        {
            playerCrit += ctx.critChanceFlat;
            if (ctx.critBuffTurns > 0) playerCrit += ctx.critChanceBonusFirstTurns;
        }

        var tagCtx = new TagRuntime.TagContext
        {
            turnIndex = Mathf.Max(1, roundIndex),
            battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
            actsFirstThisRound = playerActsFirstThisRound,
            isFirstAttackThisBattle = !playerDidFirstAttackThisBattle,
            isFirstHitThisBattle = !playerLandedFirstHitThisBattle,
            isFirstIncomingThisBattle = !playerTookFirstIncomingThisBattle,
            allyJustKOd = false,
            enemyJustKOd = false,
            tookCritThisTurn = false,
            blockedOrResistedThisTurn = false,
            attacksThisTurn = playerAttacksThisTurn,
            selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, teamMaxHP[activeIndex]),
            enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
            enemyIsBoss = (wildDef && wildDef.isBoss),
            hasStatusAny = false,
            siteJob = (ctx != null) ? ctx.job : JobType.None,
            workingHere = (ctx != null && ctx.job != JobType.None),
            roundsSurvived = Mathf.Max(0, roundIndex - 1),
            everyNthTurnN = 3
        };

        playerCrit += TagRuntime.GetOutgoingCritChanceBonus(
            teamIds[activeIndex],
            tagCtx,
            teamDefs[activeIndex],
            wildDef
        );
        playerCrit = Mathf.Clamp01(playerCrit);

        if (TagRuntime.ForbidOutgoingCrits(teamIds[activeIndex], tagCtx, teamDefs[activeIndex], wildDef))
            playerCrit = 0f;

        float playerMomentMul = TagRuntime.EvaluateConditionalMultiplier(
            teamIds[activeIndex],
            new[]
            {
                TagTrigger.OnAttack,
                TagTrigger.OnFirst2Turns,
                TagTrigger.OnFirst3Turns,
                TagTrigger.OnActFirst,
                TagTrigger.OnEvery3rdAttack,
                TagTrigger.OnFirstAttack,
                TagTrigger.OnFirstHit,
                TagTrigger.OnEveryOtherTurn,
                TagTrigger.OnBattleCondition,
                TagTrigger.OnOutgoingDamage,
                TagTrigger.OnEachRound,
                TagTrigger.OnEveryOddTurn,
                TagTrigger.OnHP,
                TagTrigger.OnEnemyBelow50,
                TagTrigger.OnEnemyBelow20,
            },
            tagCtx,
            teamDefs[activeIndex],
            wildDef
        );

        if (tempDmgBuffTurns > 0 && tempDmgBuffPct > 0f)
        {
            playerMomentMul *= (1f + tempDmgBuffPct);
            BattleLogger.Log($"+{Mathf.RoundToInt(tempDmgBuffPct * 100f)}% damage buff active.", LogScope.Battle);
            tempDmgBuffTurns--;
            if (tempDmgBuffTurns <= 0) tempDmgBuffPct = 0f;
        }

        float momentumBonus = TagRuntime.GetConsecutiveHitDamageBonus(
            teamIds[activeIndex],
            tagCtx,
            teamDefs[activeIndex],
            wildDef
        );
        if (momentumBonus > 0f)
        {
            atk *= (1f + momentumBonus);
            BattleLogger.Log($"{GetName(activeIndex)} gains momentum (+{Mathf.RoundToInt(momentumBonus * 100f)}% dmg)!", LogScope.Battle);
        }

        int defenseIgnore = TagRuntime.GetDefenseIgnoreFlat(
            teamIds[activeIndex],
            tagCtx,
            teamDefs[activeIndex],
            wildDef
        );

        var dr = BattleCalc.ResolveHit(
            teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
            null, wildDef, wildLevel,
            atk, playerCrit, critMultiplier, -defenseIgnore
        );

        if (dr.crit)
        {
            float critBonusPct = TagRuntime.GetCritDealtDamageBonus(
                teamIds[activeIndex], tagCtx, teamDefs[activeIndex], wildDef);
            if (critBonusPct > 0f)
            {
                dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + critBonusPct)));
            }
        }

        if (playerMomentMul != 1f)
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * playerMomentMul));

        var jCtx = (jobCtx != null) ? jobCtx[activeIndex] : null;
        if (jCtx != null && !jCtx.usedFirstOutgoing && jCtx.firstOutgoingBonus > 0f)
        {
            jCtx.usedFirstOutgoing = true;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f + jCtx.firstOutgoingBonus)));
        }

        bool lethal = dr.damage >= wildHP;

        wildHP = Mathf.Max(0f, wildHP - dr.damage);
        PushHPBars();

        bool landed = dr.damage > 0;
        TagRuntime.RegisterHitResult(teamIds[activeIndex], landed);

        if (!playerLandedFirstHitThisBattle && dr.damage > 0)
        {
            playerLandedFirstHitThisBattle = true;

            var firstHitCtx = new TagRuntime.TagContext
            {
                turnIndex = Mathf.Max(1, roundIndex),
                battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                actsFirstThisRound = playerActsFirstThisRound,
                selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, teamMaxHP[activeIndex]),
                enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                enemyIsBoss = (wildDef && wildDef.isBoss),
                attacksThisTurn = playerAttacksThisTurn,
                isFirstHitThisBattle = true
            };

            TagRuntime.EvaluateConditionalMultiplier(
                teamIds[activeIndex],
                new[] { TagTrigger.OnFirstHit },
                firstHitCtx,
                teamDefs[activeIndex],
                wildDef
            );
        }

        if (dr.damage > 0)
        {
            float lsPct = TagRuntime.GetLifestealPct(
                teamIds[activeIndex], tagCtx, teamDefs[activeIndex], wildDef);
            if (lsPct > 0f)
            {
                float heal = Mathf.RoundToInt(dr.damage * lsPct);
                TryAddHPToActive(heal);
                if (heal > 0) BattleLogger.Log($"{GetName(activeIndex)} lifesteals {Mathf.RoundToInt(lsPct * 100f)}% (+{Mathf.RoundToInt(heal)} HP)", LogScope.Battle);
            }
        }

        if (dr.crit)
        {
            int wTurns;
            float wPct = TagRuntime.GetWeakenOnCritPct(
                teamIds[activeIndex],
                tagCtx,
                teamDefs[activeIndex],
                wildDef,
                out wTurns
            );
            if (wPct > 0f)
            {
                wildWeakenPct = Mathf.Max(wildWeakenPct, wPct);
                wildWeakenTurns = Math.Max(wildWeakenTurns, (wTurns <= 0 ? 1 : wTurns));
                BattleLogger.Log($"{(wildDef ? wildDef.displayName : "Foe")} is hexed: -{Mathf.RoundToInt(wPct * 100f)}% damage for {wildWeakenTurns} turn{(wildWeakenTurns>1?"s":"")}.", LogScope.Battle);
            }
        }

        // ── Multi-hit follow-ups (OnMultiHit) ───────────────────────────────────
        {
            var mhCtx = new TagRuntime.TagContext
            {
                turnIndex = Mathf.Max(1, roundIndex),
                battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                actsFirstThisRound = playerActsFirstThisRound,
                selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
                enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                enemyIsBoss = (wildDef && wildDef.isBoss),
                attacksThisTurn = playerAttacksThisTurn,
            };

            float hitBudget = TagRuntime.EvaluateConditionalMultiplier(
                teamIds[activeIndex],
                new[] { TagTrigger.OnMultiHit },
                mhCtx,
                teamDefs[activeIndex],
                wildDef
            );

            if (hitBudget >= 2f && wildHP > 0f)
            {
                int fullExtraHits = Mathf.FloorToInt(hitBudget) - 1;
                float frac = hitBudget - Mathf.Floor(hitBudget);

                System.Action<float> doExtra = (scalar) =>
                {
                    if (wildHP <= 0f) return;

                    playerAttacksThisTurn++;

                    var perHitCtx = new TagRuntime.TagContext
                    {
                        turnIndex = Mathf.Max(1, roundIndex),
                        battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                        actsFirstThisRound = playerActsFirstThisRound,
                        selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
                        enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                        enemyIsBoss = (wildDef && wildDef.isBoss),
                        attacksThisTurn = playerAttacksThisTurn,
                    };

                    float perHitMul = TagRuntime.EvaluateConditionalMultiplier(
                        teamIds[activeIndex],
                        new[]
                        {
                            TagTrigger.OnAttack,
                            TagTrigger.OnEvery3rdAttack,
                            TagTrigger.OnEveryOtherTurn,
                            TagTrigger.OnOutgoingDamage,
                            TagTrigger.OnHP,
                            TagTrigger.OnEnemyBelow50,
                            TagTrigger.OnEnemyBelow20,
                        },
                        perHitCtx,
                        teamDefs[activeIndex],
                        wildDef
                    );

                    float atkThisHit = Mathf.Max(1f, atk) * Mathf.Max(0f, scalar) * Mathf.Max(0.0001f, perHitMul);

                    float pc = Mathf.Clamp01(playerCrit);
                    if (TagRuntime.ForbidOutgoingCrits(teamIds[activeIndex], perHitCtx, teamDefs[activeIndex], wildDef)) pc = 0f;

                    int defenseIgnore2 = TagRuntime.GetDefenseIgnoreFlat(teamIds[activeIndex], perHitCtx, teamDefs[activeIndex], wildDef);

                    var dr2 = BattleCalc.ResolveHit(
                        teamIds[activeIndex], teamDefs[activeIndex], teamLevels[activeIndex],
                        null, wildDef, wildLevel,
                        atkThisHit, pc, critMultiplier, -defenseIgnore2
                    );

                    bool lethal2 = dr2.damage >= wildHP;

                    if (dr2.damage > 0)
                    {
                        float lsPct2 = TagRuntime.GetLifestealPct(teamIds[activeIndex], perHitCtx, teamDefs[activeIndex], wildDef);
                        if (lsPct2 > 0f)
                        {
                            float heal2 = Mathf.RoundToInt(dr2.damage * lsPct2);
                            TryAddHPToActive(heal2);
                            if (heal2 > 0) BattleLogger.Log($"{GetName(activeIndex)} lifesteals {Mathf.RoundToInt(lsPct2*100f)}% (+{Mathf.RoundToInt(heal2)} HP).", LogScope.Battle);
                        }
                    }

                    wildHP = Mathf.Max(0f, wildHP - dr2.damage);
                    PushHPBars();

                    BattleLogger.Log($"{GetName(activeIndex)} follows up for {dr2.damage}!", LogScope.Battle);
                    if (showEffectivenessText)
                    {
                        if (dr2.effectiveness > 1.25f) BattleLogger.Log("It's super effective!", LogScope.Battle);
                        else if (dr2.effectiveness < 0.85f) BattleLogger.Log("It's not very effective...", LogScope.Battle);
                    }
                    if (dr2.crit) BattleLogger.Log("Critical hit!", LogScope.Battle);

                    if (lethal2)
                    {
                        var koCtx2 = perHitCtx; koCtx2.enemyJustKOd = true;
                        float healPct2 = TagRuntime.GetSelfHealPctOnEnemyKO(teamIds[activeIndex], koCtx2, teamDefs[activeIndex], wildDef);
                        if (healPct2 > 0f)
                        {
                            float healAmt2 = GetActiveMaxHP(teamMaxHP[activeIndex]) * healPct2;
                            TryAddHPToActive(healAmt2);
                            BattleLogger.Log($"{GetName(activeIndex)} heals {Mathf.RoundToInt(healPct2 * 100f)}% on KO!", LogScope.Battle);
                        }
                    }
                };

                for (int i = 0; i < fullExtraHits && wildHP > 0f; i++) doExtra(1f);
                if (frac > 0.001f && wildHP > 0f) doExtra(frac);
            }
        }
        // ─────────────────────────────────────────────────────────────────────

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

        var tagCtxIncoming = new TagRuntime.TagContext
        {
            turnIndex = Mathf.Max(1, roundIndex),
            battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
            actsFirstThisRound = false,
            isFirstAttackThisBattle   = !playerDidFirstAttackThisBattle,
            isFirstHitThisBattle      = !playerLandedFirstHitThisBattle,
            isFirstIncomingThisBattle = !playerTookFirstIncomingThisBattle,
            tookCritThisTurn = dr.crit,
            blockedOrResistedThisTurn = (dr.effectiveness < 1f),
            attacksThisTurn = enemyAttacksThisTurn,
            selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, teamMaxHP[activeIndex]),
            enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
            enemyIsBoss = (wildDef && wildDef.isBoss),
            siteJob = (ctx != null) ? ctx.job : JobType.None,
            workingHere = (ctx != null && ctx.job != JobType.None),
            roundsSurvived = Mathf.Max(0, roundIndex - 1),
            everyNthTurnN = 3
        };

        // Negate incoming crit (tag-driven)
        if (dr.crit && TagRuntime.TryConsumeNegateIncomingCrit(teamIds[activeIndex], tagCtxIncoming, wildDef, teamDefs[activeIndex]))
        {
            dr.crit = false;
            dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage / Mathf.Max(1e-3f, critMultiplier)));
            BattleLogger.Log($"{GetName(activeIndex)} negated the critical hit!", LogScope.Battle);
        }

        if (dr.crit)
        {
            float critReduce = TagRuntime.GetIncomingCritDamageReducePct(teamIds[activeIndex], tagCtxIncoming, wildDef, teamDefs[activeIndex]);
            if (critReduce > 0f)
                dr.damage = Mathf.Max(1, Mathf.RoundToInt(dr.damage * (1f - critReduce)));
        }

        float incomingMomentMul = TagRuntime.EvaluateConditionalMultiplier(
            teamIds[activeIndex],
            new[]
            {
                TagTrigger.OnIncomingDamage,
                TagTrigger.OnFirstIncoming,
                TagTrigger.OnIncomingCrit,
                TagTrigger.OnFirst3Turns,
                TagTrigger.OnEveryOtherTurn,
                TagTrigger.OnBlockOrResist,
                TagTrigger.OnHP
            },
            tagCtxIncoming,
            attackerDef: wildDef,
            defenderDef: teamDefs[activeIndex]
        );

        float incomingScalar = incomingMomentMul;

        if (ctx != null && !ctx.usedFirstIncoming && ctx.firstIncomingReduce > 0f)
        { ctx.usedFirstIncoming = true; incomingScalar *= (1f - ctx.firstIncomingReduce); }

        if (ctx != null && ctx.baseDamageReducePct > 0f) incomingScalar *= (1f - ctx.baseDamageReducePct);
        if (ctx != null && ctx.defenseBonusPct > 0f)     incomingScalar *= (1f - ctx.defenseBonusPct);
        if (ctx != null && ctx.dmgReduceBuffTurns > 0 && ctx.dmgReduceFirstTurns > 0f)
            incomingScalar *= (1f - ctx.dmgReduceFirstTurns);
        {
            float swapDef = TagRuntime.GetSwapDefenseBonusPct(teamIds[activeIndex]);
            if (swapDef > 0f) incomingScalar *= (1f - swapDef);
        }

        // Apply Hex Consultant weaken to enemy damage (if active), then tick down
        if (wildWeakenTurns > 0 && wildWeakenPct > 0f)
        {
            incomingScalar *= (1f - wildWeakenPct);
            BattleLogger.Log($"{(wildDef ? wildDef.displayName : "Foe")} is weakened (-{Mathf.RoundToInt(wildWeakenPct * 100f)}% dmg).", LogScope.Battle);
            wildWeakenTurns--;
            if (wildWeakenTurns <= 0) wildWeakenPct = 0f;
        }

        int dmg = Mathf.Max(1, Mathf.RoundToInt(dr.damage * incomingScalar));

        int flatReduce = TagRuntime.GetIncomingDamageFlatReduce(
            teamIds[activeIndex],
            tagCtxIncoming,
            wildDef,
            teamDefs[activeIndex]
        );
        if (flatReduce != 0)
        {
            dmg = Mathf.Max(1, dmg - flatReduce);
            if (flatReduce > 0)
                BattleLogger.Log($"{GetName(activeIndex)} blocks {flatReduce} damage!", LogScope.Battle);
        }

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

        // Arm Resolve (OnFirstKOTaken) if we just went to 0
        if (teamHP[activeIndex] <= 0.01f && !firstKOTakenProcessed)
        {
            var koCtx = new TagRuntime.TagContext
            {
                allyJustKOd = true,
                turnIndex = Mathf.Max(1, roundIndex),
                battleTurnsElapsed = Mathf.Max(0, roundIndex - 1)
            };
            float mul = TagRuntime.EvaluateConditionalMultiplier(
                teamIds[activeIndex],
                new[] { TagTrigger.OnFirstKOTaken },
                koCtx,
                teamDefs[activeIndex],
                wildDef
            );
            if (mul > 1f)
            {
                tempDmgBuffPct   = Mathf.Max(0f, mul - 1f);
                tempDmgBuffTurns = 1;
                firstKOTakenProcessed = true;
                BattleLogger.Log($"Resolve readied: +{Mathf.RoundToInt(tempDmgBuffPct * 100f)}% damage next turn.", LogScope.Battle);
            }

            // Trigger OnAllyKO for surviving teammates + OnDeath heal payload
            TriggerOnAllyKO_ForSurvivors(activeIndex);
        }

        // HP threshold (crisis heal)
        if (teamHP[activeIndex] > 0f)
        {
            var ctxHP = new TagRuntime.TagContext
            {
                selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
                enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                turnIndex = Mathf.Max(1, roundIndex),
                battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
            };
            float healPct = TagRuntime.TryConsumeHpThresholdHealPct(teamIds[activeIndex], ctxHP);
            if (healPct > 0f)
            {
                float healAmt = GetActiveMaxHP(teamMaxHP[activeIndex]) * healPct;
                TryAddHPToActive(healAmt);
                BattleLogger.Log($"{GetName(activeIndex)} crisis heals {Mathf.RoundToInt(healPct * 100f)}% HP!", LogScope.Battle);
            }
        }

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

    private void TriggerOnAllyKO_ForSurvivors(int fallenIdx)
    {
        // Fallen ally may carry an OnDeath heal payload for the team.
        // Interpret mul > 1 as (mul - 1) % Max HP heal to all surviving allies.
        {
            var deathCtx = new TagRuntime.TagContext
            {
                turnIndex = Mathf.Max(1, roundIndex),
                battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                selfHp01  = 0f,
                enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                enemyIsBoss = (wildDef && wildDef.isBoss),
            };

            float deathMul = TagRuntime.EvaluateConditionalMultiplier(
                teamIds[fallenIdx],
                new[] { TagTrigger.OnDeath },
                deathCtx,
                teamDefs[fallenIdx],
                wildDef
            );

            if (deathMul > 1f)
            {
                float healPct = deathMul - 1f;
                for (int i = 0; i < teamCount; i++)
                {
                    if (i == fallenIdx) continue;
                    if (teamHP[i] <= 0f) continue;

                    float maxI = (i == activeIndex) ? GetActiveMaxHP(teamMaxHP[i]) : teamMaxHP[i];
                    float heal  = Mathf.Max(0f, maxI * healPct);

                    if (i == activeIndex)
                        TryAddHPToActive(heal);
                    else
                        teamHP[i] = Mathf.Min(teamMaxHP[i], teamHP[i] + heal);

                    BattleLogger.Log($"{GetName(i)} is healed {Mathf.RoundToInt(healPct*100f)}% by {GetName(fallenIdx)}'s sacrifice.", LogScope.Battle);
                }
                ClampAndPushActiveHP();
                PushHPBars();
            }
        }

        // Now fire OnAllyKO on surviving teammates (buffs; store on bench)
        var ctx = new TagRuntime.TagContext
        {
            turnIndex = Mathf.Max(1, roundIndex),
            battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
            selfHp01  = 0f,
            enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
            enemyIsBoss = (wildDef && wildDef.isBoss),
            allyJustKOd = true
        };

        for (int i = 0; i < teamCount; i++)
        {
            if (i == fallenIdx) continue;
            if (teamHP[i] <= 0f) continue;

            float mul = TagRuntime.EvaluateConditionalMultiplier(
                teamIds[i],
                new[] { TagTrigger.OnAllyKO },
                ctx,
                teamDefs[i],
                wildDef
            );
            if (mul > 1f)
            {
                float addPct = Mathf.Max(0f, mul - 1f);
                if (i == activeIndex)
                {
                    tempDmgBuffPct  += addPct;
                    tempDmgBuffTurns = Math.Max(tempDmgBuffTurns, 1);
                }
                else
                {
                    teamPendingBuffPct[i]  += addPct;
                    teamPendingBuffTurns[i] = Math.Max(teamPendingBuffTurns[i], 1);
                }
                BattleLogger.Log($"{GetName(i)} steels themselves (+{Mathf.RoundToInt(addPct*100f)}% dmg next action) after an ally falls.", LogScope.Battle);
            }
        }
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
        float coinMul = TagRuntime.GetCoinsGainedMultiplier(teamIds);
        coins = Mathf.RoundToInt(coins * Mathf.Max(0f, coinMul));

        if (victory)
        {
            float xpMul = TagRuntime.GetBattleXPMultiplier(teamIds);
            BattleRewards.GrantVictoryXPAndEvo(activeIndex, wildLevel, MonsterLibraryLocator.Lib, xpMul);
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

        TagRuntime.NotifySwapIn(teamIds[activeIndex]);

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

        var swapCtx = new TagRuntime.TagContext
        {
            turnIndex = Mathf.Max(1, roundIndex),
            battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
            selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
            enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
            enemyIsBoss = (wildDef && wildDef.isBoss),
        };

        // Only evaluate the healing trigger here (not OnSwapIn) so DEF-on-swap tags don't become heals
        float swapMul = TagRuntime.EvaluateConditionalMultiplier(
            teamIds[activeIndex],
            new[] { TagTrigger.OnRescueHealBelow40 },
            swapCtx,
            teamDefs[activeIndex],
            wildDef
        );

        if (swapMul > 1f)
        {
            float healPct = swapMul - 1f;
            float healAmt = GetActiveMaxHP(teamMaxHP[activeIndex]) * healPct;
            TryAddHPToActive(healAmt);
            if (healAmt > 0) BattleLogger.Log($"{GetName(activeIndex)} rallies on swap (+{Mathf.RoundToInt(healPct * 100f)}% HP)", LogScope.Battle);
        }

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

                TagRuntime.NotifySwapIn(teamIds[activeIndex]);

                if (teamPendingBuffPct != null && teamPendingBuffPct[activeIndex] > 0f)
                {
                    tempDmgBuffPct   += teamPendingBuffPct[activeIndex];
                    tempDmgBuffTurns  = Math.Max(tempDmgBuffTurns, teamPendingBuffTurns[activeIndex]);
                    BattleLogger.Log($"{GetName(activeIndex)} carries over +{Mathf.RoundToInt(teamPendingBuffPct[activeIndex] * 100f)}% damage from bench.", LogScope.Battle);
                    teamPendingBuffPct[activeIndex] = 0f;
                    teamPendingBuffTurns[activeIndex] = 0;
                }

                BattleLogger.Log($"Auto-swapped to {GetName(activeIndex)}!", LogScope.Battle);

                var swapCtx = new TagRuntime.TagContext
                {
                    turnIndex = Mathf.Max(1, roundIndex),
                    battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
                    selfHp01  = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
                    enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
                    enemyIsBoss = (wildDef && wildDef.isBoss),
                };

                float swapMul = TagRuntime.EvaluateConditionalMultiplier(
                    teamIds[activeIndex],
                    new[] { TagTrigger.OnRescueHealBelow40 },
                    swapCtx,
                    teamDefs[activeIndex],
                    wildDef
                );

                if (swapMul > 1f)
                {
                    float healPct = swapMul - 1f;
                    float healAmt = GetActiveMaxHP(teamMaxHP[activeIndex]) * healPct;
                    TryAddHPToActive(healAmt);
                    if (healAmt > 0) BattleLogger.Log($"{GetName(activeIndex)} rallies on swap (+{Mathf.RoundToInt(healPct * 100f)}% HP)", LogScope.Battle);
                }

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

        var ctx = new TagRuntime.TagContext
        {
            turnIndex = Mathf.Max(1, roundIndex),
            battleTurnsElapsed = Mathf.Max(0, roundIndex - 1),
            actsFirstThisRound = playerActsFirstThisRound,
            selfHp01 = teamHP[activeIndex] / Mathf.Max(1f, GetActiveMaxHP(teamMaxHP[activeIndex])),
            enemyHp01 = wildHP / Mathf.Max(1f, wildMaxHP),
            enemyIsBoss = (wildDef && wildDef.isBoss),
        };

        float endMul = TagRuntime.EvaluateConditionalMultiplier(
             teamIds[activeIndex],
             new[] { TagTrigger.OnEndTurn },
             ctx,
             teamDefs[activeIndex],
             wildDef
         );

        // Convert OnEndTurn > 1 into a one-turn outgoing damage buff for next turn.
        if (endMul > 1f)
        {
            float addPct = Mathf.Max(0f, endMul - 1f);
            tempDmgBuffPct += addPct;
            tempDmgBuffTurns = Math.Max(tempDmgBuffTurns, 1);
            BattleLogger.Log($"{GetName(activeIndex)} readies +{Mathf.RoundToInt(addPct * 100f)}% damage next turn (End Turn).", LogScope.Battle);
        }

        float regenMul = TagRuntime.EvaluateConditionalMultiplier(
            teamIds[activeIndex],
            new[] { TagTrigger.OnEndTurnRegen },
            ctx,
            teamDefs[activeIndex],
            wildDef
        );

        float regenPct = Mathf.Max(0f, (regenMul - 1f));
        if (regenPct > 0f)
        {
            float healAmt = GetActiveMaxHP(teamMaxHP[activeIndex]) * regenPct;
            TryAddHPToActive(healAmt);
            BattleLogger.Log($"{GetName(activeIndex)} regenerates +{Mathf.RoundToInt(regenPct * 100f)}% HP at end of turn.", LogScope.Battle);
        }

        if (playerNoDmgTurns >= 2)
        {
            TagRuntime.EvaluateConditionalMultiplier(
                teamIds[activeIndex],
                new[] { TagTrigger.OnNoDamageDealt2T },
                ctx,
                teamDefs[activeIndex],
                wildDef
            );
            playerNoDmgTurns = 0;
        }

        if (playerNoCritTurns >= 2)
        {
            TagRuntime.EvaluateConditionalMultiplier(
                teamIds[activeIndex],
                new[] { TagTrigger.OnNoCritsFor2Turns },
                ctx,
                teamDefs[activeIndex],
                wildDef
            );
            playerNoCritTurns = 0;
        }
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

        int lvl = teamLevels[activeIndex];

        // Base stats
        int baseHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
        int baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
        int baseDEF = BattleCalc.CalcDefense(def, lvl);
        int baseSPD = BattleCalc.CalcSpeed(def, lvl);

        // Temp buffs (flat)
        int tempHPFlat   = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerHPBonus() : 0;
        int tempATKFlat  = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerAtkBonus() : 0;
        int tempDEFFlat  = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerDefenseBonus() : 0;
        int tempSPDFlat  = BattleTempBuffs.I ? BattleTempBuffs.I.GetPlayerSpeedFlatBonus() : 0; // ← your API

        // Optional timers
        string tAtk  = (BattleTempBuffs.I && BattleTempBuffs.I.IsAtkBonusActive())  ? $" {BattleTempBuffs.I.GetAtkBonusRemainingSeconds():0.0}s"  : "";
        string tHP   = (BattleTempBuffs.I && BattleTempBuffs.I.IsHPBonusActive())   ? $" {BattleTempBuffs.I.GetHPBonusRemainingSeconds():0.0}s"   : "";
        string tDef  = (BattleTempBuffs.I && BattleTempBuffs.I.IsDefenseBonusActive()) ? $" {BattleTempBuffs.I.GetDefenseBonusRemainingSeconds():0.0}s" : "";
        string tSpd  = (BattleTempBuffs.I && BattleTempBuffs.I.IsSpeedBonusActive())   ? $" {BattleTempBuffs.I.GetSpeedBonusRemainingSeconds():0.0}s"   : "";
        bool resistOn = BattleTempBuffs.I && BattleTempBuffs.I.IsTypeResistActive();

        // Equipped flat ATK bonus
        int equippedFlatATK = 0;
        var roster = SaveManager.Data?.team;
        if (roster != null && activeIndex < roster.Count)
            equippedFlatATK = Mathf.Max(0, roster[activeIndex].flatAtkBonus);

        // Job context
        var jc = (jobCtx != null && activeIndex < jobCtx.Length) ? jobCtx[activeIndex] : null;

        // Current/max HP with existing job % already baked into teamMaxHP
        float maxHPBase = teamMaxHP != null ? teamMaxHP[activeIndex] : baseHP;
        float curMaxHP  = GetActiveMaxHP(maxHPBase); // adds temp flat HP
        int curHPDisp   = Mathf.RoundToInt(Mathf.Clamp(teamHP[activeIndex], 0f, curMaxHP));

        // Helpers
        string SegIfFlat(string label, int v, string time)   => (v != 0) ? $" [{label}+{v}{time}]" : "";
        string SegIfPct (string label, float v, string tail) => (v != 0f) ? $" [{label}+{Mathf.RoundToInt(v*100f)}%{tail}]" : "";
        string MinusPct (float v)                            => (v > 0f) ? $" [−{Mathf.RoundToInt(v*100f)}% dmg]" : "";

        // Identity
        if (playerIdText)     playerIdText.text     = $"ID: {def.id}";
        if (playerTypeText)   playerTypeText.text   = $"TYPE: {def.type}";
        if (playerRarityText) playerRarityText.text = $"RARITY: {def.rarity}";
        if (playerLevelText)  playerLevelText.text  = $"LVL: {lvl}";

        // HP line
        float gymPct = (jc != null) ? jc.maxHpBonusPct : 0f; // already applied, badge for clarity
        string hpLine = $"HP: {curHPDisp}/{Mathf.RoundToInt(curMaxHP)}";
        hpLine += SegIfFlat("Temp", tempHPFlat, tHP);
        hpLine += SegIfPct ("Gym",  gymPct, "");
        if (playerHPText) playerHPText.text = hpLine;

        // ATK line
        float jobAtkPct = (jc != null) ? jc.attackBonusPct : 0f;
        float turnBuffPct = (tempDmgBuffTurns > 0 && tempDmgBuffPct > 0f) ? tempDmgBuffPct : 0f;
        int atkShown = Mathf.Max(1, Mathf.RoundToInt(baseATK + equippedFlatATK + tempATKFlat));
        string atkLine = $"ATK: {atkShown}";
        atkLine += SegIfFlat("Equip", equippedFlatATK, "");
        atkLine += SegIfFlat("Temp",  tempATKFlat, tAtk);
        atkLine += SegIfPct ("Job",   jobAtkPct, "");
        atkLine += SegIfPct ("Turn",  turnBuffPct, ""); // one-turn buff from tags
        if (playerATKText) playerATKText.text = atkLine;

        // DEF line (+ total dmg reduction badges)
        float dmgReducePct = 0f;
        if (jc != null)
        {
            dmgReducePct += Mathf.Max(0f, jc.baseDamageReducePct);
            if (jc.dmgReduceBuffTurns > 0 && jc.dmgReduceFirstTurns > 0f)
                dmgReducePct += jc.dmgReduceFirstTurns;
            if (jc.defenseBonusPct > 0f)
                dmgReducePct += jc.defenseBonusPct;
        }
        int defShown = Mathf.Max(0, baseDEF + tempDEFFlat);
        string defLine = $"DEF: {defShown}";
        defLine += SegIfFlat("Temp", tempDEFFlat, tDef);
        defLine += MinusPct(dmgReducePct);
        if (playerDEFText) playerDEFText.text = defLine;

        // SPD line (flat temp + first-turn job speed with remaining turns)
        int spdShown = Mathf.Max(1, baseSPD + tempSPDFlat);
        string spdLine = $"SPD: {spdShown}";
        spdLine += SegIfFlat("Temp", tempSPDFlat, tSpd);
        if (jc != null && jc.speedBuffTurns > 0 && jc.speedBonusPctFirstTurns > 0f)
            spdLine += SegIfPct("Job", jc.speedBonusPctFirstTurns, $" ({jc.speedBuffTurns}t)");
        if (playerSPDText) playerSPDText.text = spdLine;

        // Optional active tags line (append to rarity or type if you prefer)
        if (resistOn && playerRarityText)
        {
            // Append a compact badge so we don’t add more fields to your layout
            playerRarityText.text += " [Resist]";
        }
    }


    private void ApplyActiveToUI()
    {
        var def = teamDefs[activeIndex];
        var lvl = teamLevels[activeIndex];
        if (playerIcon)      playerIcon.sprite = def ? (def.backIcon ? def.backIcon : def.icon) : null;
        if (playerNameText)  playerNameText.text = def ? def.displayName : "";
        if (playerLevelText) playerLevelText.text = $"Lv {lvl}";
        UpdatePlayerInfoUI(); // ← add this line
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

        if (turnCR != null) StopCoroutine(turnCR);
        turnCR = StartCoroutine(TurnLoop());
        yield break;
    }

}
