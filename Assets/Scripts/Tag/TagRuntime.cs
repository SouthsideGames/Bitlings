using System;
using System.Collections.Generic;
using UnityEngine;

public static class TagRuntime
{
    // ======================================================================
    //  GLOBAL TAG DEBUGGER
    //  Logs every time a tag effect contributes or is consumed.
    // ======================================================================
    public static class TagDebugger
    {
        public static bool Enabled = true;
        public static bool ShowAll = true;
        public static Action<string> OutputSink;

        public static HashSet<TagTrigger> OnlyTriggers = null;
        public static HashSet<string> OnlyIds = null;

        static void Emit(string msg)
        {
            if (OutputSink != null) OutputSink(msg);
            else Debug.Log(msg);
        }

        static bool PassesFilters(string id, TagTrigger trig, float contributionIsNeutral)
        {
            if (!Enabled) return false;
            if (!ShowAll && Mathf.Approximately(contributionIsNeutral, 0f)) return false;
            if (OnlyTriggers != null && !OnlyTriggers.Contains(trig)) return false;
            if (!string.IsNullOrEmpty(id) && OnlyIds != null && !OnlyIds.Contains(id)) return false;
            return true;
        }

        public static void Log(
            string id,
            TagTrigger trig,
            float addPct,
            in TagContext ctx,
            TagSO tag = null,
            string extra = null)
        {
            if (!PassesFilters(id, trig, addPct)) return;

            string msg = $"[TAG] {trig}{(tag ? $"[{tag.name}]" : "")}"
                       + $" id={id ?? "-"}"
                       + $" t={ctx.turnIndex} bT={ctx.battleTurnsElapsed}"
                       + $" hp={ctx.selfHp01:0.##} eHP={ctx.enemyHp01:0.##} boss={ctx.enemyIsBoss}"
                       + $" addPct={addPct:0.###}"
                       + (string.IsNullOrEmpty(extra) ? "" : $" | {extra}");
            Emit(msg);
        }

        public static void LogNote(string id, TagTrigger trig, string note, in TagContext ctx, TagSO tag = null)
        {
            if (!PassesFilters(id, trig, 1f)) return;
            string msg = $"[TAG] {trig}{(tag ? $"[{tag.name}]" : "")}"
                       + $" id={id ?? "-"}"
                       + $" t={ctx.turnIndex} bT={ctx.battleTurnsElapsed}"
                       + $" hp={ctx.selfHp01:0.##} eHP={ctx.enemyHp01:0.##} boss={ctx.enemyIsBoss}"
                       + $" | {note}";
            Emit(msg);
        }

        public static void Enable(Action<string> sink = null, bool showAll = true,
                                  IEnumerable<TagTrigger> onlyTriggers = null,
                                  IEnumerable<string> onlyIds = null)
        {
            OutputSink = sink;
            ShowAll = showAll;
            OnlyTriggers = onlyTriggers != null ? new HashSet<TagTrigger>(onlyTriggers) : null;
            OnlyIds = onlyIds != null ? new HashSet<string>(onlyIds) : null;
            Enabled = true;
            Emit("[TAG] Debug ENABLED");
        }

        public static void Disable()
        {
            Enabled = false;
            OnlyTriggers = null;
            OnlyIds = null;
            OutputSink = null;
            Debug.Log("[TAG] Debug DISABLED");
        }
    }
    // ======================================================================

    public struct TagContext
    {
        public int turnIndex;
        public int battleTurnsElapsed;
        public bool actsFirstThisRound;
        public bool isFirstAttackThisBattle;
        public bool isFirstHitThisBattle;
        public bool isFirstIncomingThisBattle;
        public bool allyJustKOd;
        public bool enemyJustKOd;
        public bool tookCritThisTurn;
        public bool blockedOrResistedThisTurn;
        public int attacksThisTurn;
        public float selfHp01;
        public float enemyHp01;
        public bool enemyIsBoss;
        public bool hasStatusAny;
        public JobType siteJob;
        public bool workingHere;
        public int roundsSurvived;
        public int everyNthTurnN;
    }

    static IEnumerable<TagSO> EquippedTags(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) yield break;
        var rec = TagSave.GetOrCreate(monsterId);
        if (rec == null || rec.equippedTagIds == null) yield break;
        foreach (var id in rec.equippedTagIds)
        {
            var tag = TagLibrarySO.I?.GetById(id);
            if (tag != null) yield return tag;
        }
    }

    static bool PassesGate(in TagEffect e, in TagContext ctx)
    {
        switch (e.gateType)
        {
            case TagGateType.None: return true;
            case TagGateType.SelfHPBelow01: return ctx.selfHp01 <= Mathf.Clamp01(e.gateValueF);
            case TagGateType.EnemyHPBelow01: return ctx.enemyHp01 <= Mathf.Clamp01(e.gateValueF);
            case TagGateType.FirstNTurns: return ctx.turnIndex > 0 && ctx.turnIndex <= Mathf.Max(1, e.gateValueI);
            case TagGateType.EveryNthTurn:
                int n = Mathf.Max(1, e.gateValueI);
                return (ctx.turnIndex > 0) && (ctx.turnIndex % n == 0);
            case TagGateType.ActFirstThisRound: return ctx.actsFirstThisRound == e.gateBool;
            case TagGateType.EnemyIsBoss: return ctx.enemyIsBoss == e.gateBool;
            case TagGateType.AttacksThisTurnAtLeast: return ctx.attacksThisTurn >= Mathf.Max(1, e.gateValueI);
            case TagGateType.BattleTurnsAtLeast: return ctx.battleTurnsElapsed >= Mathf.Max(0, e.gateValueI);
            case TagGateType.RoundsSurvivedAtLeast: return ctx.roundsSurvived >= Mathf.Max(0, e.gateValueI);
            case TagGateType.TimeIsNight: return IsNightNow();
            default: return true;
        }
    }

    // NOTE: EvaluateMultiplier is a legacy/site-only path that lacks full TagContext.
    // We add logging in EvaluateMultiplierFor instead (which knows monsterId).
    static float EvaluateMultiplier(
        IEnumerable<TagSO> tags,
        TagTrigger trigger,
        MonsterDataSO attackerDef = null,
        MonsterDataSO defenderDef = null,
        JobType siteScope = 0,
        bool onlyWhenAtSite = false)
    {
        float mul = 1f;
        if (tags == null) return mul;
        foreach (var tag in tags)
        {
            var effects = tag.effects;
            if (effects == null) continue;
            for (int i = 0; i < effects.Length; i++)
            {
                var e = effects[i];
                if (e.trigger != trigger) continue;
                if (onlyWhenAtSite && !e.onlyWhenAtSite) { }
                if (e.onlyWhenAtSite && !onlyWhenAtSite) continue;
                if (e.siteScope != 0 && e.siteScope != siteScope) continue;
                if (e.gateByAttackerType && (attackerDef == null || attackerDef.type != e.attackerType)) continue;
                if (e.gateByDefenderType && (defenderDef == null || defenderDef.type != e.defenderType)) continue;
                mul += e.addPct;
            }
        }
        return mul;
    }

    static float EvaluateMultiplierFor(
        string monsterId,
        TagTrigger trigger,
        MonsterDataSO attackerDef = null,
        MonsterDataSO defenderDef = null,
        JobType siteScope = 0,
        bool onlyWhenAtSite = false)
    {
        float mul = 1f;
        var tags = EquippedTags(monsterId);
        if (tags == null) return mul;

        var ctx = new TagContext
        {
            turnIndex = 0,
            battleTurnsElapsed = 0,
            selfHp01 = 0f,
            enemyHp01 = 0f,
            siteJob = siteScope,
            workingHere = onlyWhenAtSite
        };

        foreach (var tag in tags)
        {
            var effects = tag.effects;
            if (effects == null) continue;

            for (int i = 0; i < effects.Length; i++)
            {
                var e = effects[i];
                if (e.trigger != trigger) continue;
                if (onlyWhenAtSite && !e.onlyWhenAtSite) { }
                if (e.onlyWhenAtSite && !onlyWhenAtSite) continue;
                if (e.siteScope != 0 && e.siteScope != siteScope) continue;
                if (e.gateByAttackerType && (attackerDef == null || attackerDef.type != e.attackerType)) continue;
                if (e.gateByDefenderType && (defenderDef == null || defenderDef.type != e.defenderType)) continue;

                mul += e.addPct;

                // Log each contributing effect
                TagDebugger.Log(monsterId, trigger, e.addPct, ctx, tag);
            }
        }
        return mul;
    }

    static readonly Dictionary<TagTrigger, Func<TagContext, bool>> TriggerChecks = new()
    {
        { TagTrigger.OnBattleStart,        ctx => ctx.turnIndex == 1 },
        { TagTrigger.OnBattleEnd,          ctx => true },
        { TagTrigger.OnFirst2Turns,        ctx => ctx.turnIndex <= 2 },
        { TagTrigger.OnFirst3Turns,        ctx => ctx.turnIndex <= 3 },
        { TagTrigger.OnActFirst,           ctx => ctx.actsFirstThisRound },
        { TagTrigger.OnFirstAttack,        ctx => ctx.isFirstAttackThisBattle },
        { TagTrigger.OnFirstHit,           ctx => ctx.isFirstHitThisBattle },
        { TagTrigger.OnFirstIncoming,      ctx => ctx.isFirstIncomingThisBattle },
        { TagTrigger.OnEveryOtherTurn,     ctx => (ctx.turnIndex % 2) == 1 },
        { TagTrigger.OnEvery3Turns,        ctx => (ctx.turnIndex % 3) == 0 },
        { TagTrigger.OnBattleLength,       ctx => true },
        { TagTrigger.OnSurviveRounds,      ctx => true },
        { TagTrigger.OnAllyKO,             ctx => ctx.allyJustKOd },
        { TagTrigger.OnKill,               ctx => ctx.enemyJustKOd },
        { TagTrigger.OnBlock,              ctx => ctx.blockedOrResistedThisTurn },
        { TagTrigger.OnBlockOrResist,      ctx => ctx.blockedOrResistedThisTurn },
        { TagTrigger.OnIncomingCrit,       ctx => ctx.tookCritThisTurn },
        { TagTrigger.OnBurnChance,         ctx => true },
        { TagTrigger.OnFreezeChance,       ctx => true },
        { TagTrigger.OnShockChance,        ctx => true },
        { TagTrigger.OnStatus,             ctx => ctx.hasStatusAny },
        { TagTrigger.OnStatusTaken,        ctx => true },
        { TagTrigger.OnStatusInflict,      ctx => true },
        { TagTrigger.OnHP,                 ctx => true },
        { TagTrigger.OnHPThreshold,        ctx => true },
        { TagTrigger.OnEnemyBelow50,       ctx => ctx.enemyHp01 <= 0.50f },
        { TagTrigger.OnEnemyBelow20,       ctx => ctx.enemyHp01 <= 0.20f },
        { TagTrigger.OnAttack,             ctx => true },
        { TagTrigger.OnMultiHit,           ctx => ctx.attacksThisTurn > 1 },
        { TagTrigger.OnEvery3rdAttack,     ctx => (ctx.attacksThisTurn % 3) == 0 },
        { TagTrigger.OnNoCritsFor2Turns,   ctx => true },
        { TagTrigger.OnNoDamageDealt2T,    ctx => true },
        { TagTrigger.OnEnemyBoss,          ctx => ctx.enemyIsBoss },
        { TagTrigger.OnEndTurn,            ctx => true },
        { TagTrigger.OnEndTurnRegen,       ctx => true },
        { TagTrigger.OnJobOutput,          ctx => true },
        { TagTrigger.OnJobCoins,           ctx => true },
        { TagTrigger.OnJobEnergy,          ctx => true },
        { TagTrigger.OnJobMedkits,         ctx => true },
        { TagTrigger.OnJobMaterials,       ctx => true },
        { TagTrigger.OnStorageCap,         ctx => true },
        { TagTrigger.OnCoinsGained,        ctx => true },
        { TagTrigger.OnShopRerollCost,     ctx => true },
        { TagTrigger.OnShopPrice,          ctx => true },
        { TagTrigger.OnShopRefresh,        ctx => true },
        { TagTrigger.OnOutgoingDamage,     ctx => true },
        { TagTrigger.OnIncomingDamage,     ctx => true },
        { TagTrigger.OnEnemyKO,            ctx => ctx.enemyJustKOd },
        { TagTrigger.OnCritLogic,          ctx => true },
        { TagTrigger.OnCritChance,         ctx => true },
        { TagTrigger.OnDefenseIgnore,      ctx => true },
        { TagTrigger.OnIncomingDamageFlat, ctx => true },
        { TagTrigger.OnConsecutiveHits,    ctx => true },
        { TagTrigger.OnFirstKOTaken,       ctx => ctx.allyJustKOd },
        { TagTrigger.OnEachRound,          ctx => true },
        { TagTrigger.OnEveryOddTurn,       ctx => (ctx.turnIndex % 2) == 1 },
        { TagTrigger.OnSpeedCheck,         ctx => true },
        { TagTrigger.OnDamage,             ctx => true },
        { TagTrigger.OnHitByEnemy,         ctx => true },
        { TagTrigger.OnDefense,            ctx => true },
        { TagTrigger.OnLifesteal,          ctx => true },
        { TagTrigger.OnCritDealt,          ctx => true },
        { TagTrigger.OnFirstKODealt,       ctx => ctx.enemyJustKOd },
        { TagTrigger.OnSwapIn,             ctx => true },
        { TagTrigger.OnRescueHealBelow40,  ctx => ctx.selfHp01 <= 0.40f },
        { TagTrigger.OnFatigueDecay,       ctx => true },
        { TagTrigger.OnDropChance,         ctx => true },
    };

    public static float EvaluateConditionalMultiplier(
        string monsterId,
        IEnumerable<TagTrigger> triggersNow,
        TagContext ctx,
        MonsterDataSO attackerDef = null,
        MonsterDataSO defenderDef = null)
    {
        float mul = 1f;
        if (triggersNow == null) return mul;
        var tags = EquippedTags(monsterId);
        foreach (var tag in tags)
        {
            var effects = tag.effects;
            if (effects == null) continue;
            for (int i = 0; i < effects.Length; i++)
            {
                var e = effects[i];
                if (!HasTriggerFast(triggersNow, e.trigger)) continue;
                if (TriggerChecks.TryGetValue(e.trigger, out var pred) && !pred(ctx)) continue;
                if (ctx.workingHere) { if (e.onlyWhenAtSite == false) { } } else if (e.onlyWhenAtSite) continue;
                if (e.siteScope != 0 && e.siteScope != ctx.siteJob) continue;
                if (e.gateByAttackerType && (attackerDef == null || attackerDef.type != e.attackerType)) continue;
                if (e.gateByDefenderType && (defenderDef == null || defenderDef.type != e.defenderType)) continue;
                if (!PassesGate(e, ctx)) continue;

                float add = e.addPct;
                if (e.trigger == TagTrigger.OnBattleLength)
                {
                    int rounds = Mathf.Max(0, ctx.battleTurnsElapsed);
                    float cap = (e.gateValueF > 0f) ? e.gateValueF : float.MaxValue;
                    add = Mathf.Min(rounds * Mathf.Max(0f, e.addPct), cap);
                }
                mul += add;

                TagDebugger.Log(monsterId, e.trigger, e.addPct, ctx, tag);
            }
        }
        return mul;
    }

    static bool HasTriggerFast(IEnumerable<TagTrigger> list, TagTrigger needle)
    {
        foreach (var t in list) if (t == needle) return true;
        return false;
    }

    // ---------- Math helpers with legacy (context-less) paths ----------
    public static float ApplyOutgoingDamage(string attackerId, MonsterDataSO attackerDef, MonsterDataSO defenderDef, float baseDmg)
    {
        float m1 = EvaluateMultiplierFor(attackerId, TagTrigger.OnOutgoingDamage, attackerDef, defenderDef);
        float m2 = EvaluateMultiplierFor(attackerId, TagTrigger.OnDamage, attackerDef, defenderDef);
        return baseDmg * m1 * m2;
    }

    public static float ApplyIncomingDamage(string defenderId, MonsterDataSO defenderDef, MonsterDataSO attackerDef, float baseDmg)
    {
        float m1 = EvaluateMultiplierFor(defenderId, TagTrigger.OnIncomingDamage, attackerDef, defenderDef);
        float m2 = EvaluateMultiplierFor(defenderId, TagTrigger.OnDamage, attackerDef, defenderDef);
        float m3 = EvaluateMultiplierFor(defenderId, TagTrigger.OnHitByEnemy, attackerDef, defenderDef);
        return baseDmg * m1 * m2 * m3;
    }

    // ---------- Jobs / Storage / Shop ----------
    public static float GetJobOutputMultiplier(string workerId, JobType site, bool workingHere)
    {
        if (string.IsNullOrEmpty(workerId)) return 1f;

        var ctx = new TagContext
        {
            siteJob = site,
            workingHere = workingHere,
        };

        return EvaluateConditionalMultiplier(
            workerId,
            new[] { TagTrigger.OnJobOutput },
            ctx,
            attackerDef: null,
            defenderDef: null
        );
    }
    public static float GetStorageCapMultiplier(JobType siteJob, IEnumerable<string> workerIds)
    {
        float mul = 1f;
        if (workerIds == null) return mul;
        foreach (var id in workerIds)
            mul += EvaluateMultiplierFor(id, TagTrigger.OnStorageCap, siteScope: siteJob, onlyWhenAtSite: true) - 1f;
        return mul;
    }

    public static float GetRerollCostMultiplier(IEnumerable<string> teamIds)
    {
        float mul = 1f;
        if (teamIds == null) return mul;
        foreach (var id in teamIds)
            mul += EvaluateMultiplierFor(id, TagTrigger.OnShopRerollCost) - 1f;
        return mul;
    }

    public static float GetShopPriceMultiplier(IEnumerable<string> teamIds)
    {
        float mul = 1f;
        if (teamIds == null) return mul;
        foreach (var id in teamIds)
            mul += EvaluateMultiplierFor(id, TagTrigger.OnShopPrice) - 1f;
        return mul;
    }

    public static float GetShopRefreshCooldownMultiplier(IEnumerable<string> teamIds)
    {
        float mul = 1f;
        if (teamIds == null) return mul;
        foreach (var id in teamIds)
            mul += EvaluateMultiplierFor(id, TagTrigger.OnShopRefresh) - 1f;
        return mul;
    }

    public static float GetJobCoinsMultiplier(string monsterId, JobType job, bool here)
        => EvaluateMultiplierFor(monsterId, TagTrigger.OnJobCoins, siteScope: job, onlyWhenAtSite: here);

    public static float GetJobEnergyMultiplier(string monsterId, JobType job, bool here)
        => EvaluateMultiplierFor(monsterId, TagTrigger.OnJobEnergy, siteScope: job, onlyWhenAtSite: here);

    public static float GetJobMedkitsMultiplier(string monsterId, JobType job, bool here)
        => EvaluateMultiplierFor(monsterId, TagTrigger.OnJobMedkits, siteScope: job, onlyWhenAtSite: here);

    public static float GetJobMaterialsMultiplier(string monsterId, JobType job, bool here)
        => EvaluateMultiplierFor(monsterId, TagTrigger.OnJobMaterials, siteScope: job, onlyWhenAtSite: here);

    public static float GetCoinsGainedMultiplier(IEnumerable<string> teamIds)
    {
        float mul = 1f;
        if (teamIds == null) return mul;

        var ctx = new TagContext(); // neutral
        foreach (var id in teamIds)
        {
            float before = mul;
            float add = EvaluateMultiplierFor(id, TagTrigger.OnCoinsGained) - 1f;
            mul += add;
            if (add != 0f)
                TagDebugger.LogNote(id, TagTrigger.OnCoinsGained, $"team addPct={add:0.###}", ctx);
        }
        return mul;
    }

    // ---------- One-shots / streaks ----------
    static readonly HashSet<string> _consumedOneShots = new HashSet<string>();
    static readonly HashSet<string> _negateCritUsed = new HashSet<string>();
    static readonly Dictionary<string, int> consecutiveHitStacks = new Dictionary<string, int>();
    static readonly HashSet<string> _swapOnce = new HashSet<string>();
    static readonly Dictionary<string, int> _swapDefTurns = new Dictionary<string, int>();
    static readonly Dictionary<string, float> _swapDefPct = new Dictionary<string, float>();


    public static float TryConsumeHpThresholdHealPct(string monsterId, TagContext ctx, TagEffect[] cacheEffects = null)
    {
        if (string.IsNullOrEmpty(monsterId)) return 0f;
        float totalHealPct = 0f;
        var tags = EquippedTags(monsterId);
        foreach (var tag in tags)
        {
            var effects = cacheEffects ?? tag.effects;
            if (effects == null) continue;
            for (int i = 0; i < effects.Length; i++)
            {
                var e = effects[i];
                if (e.trigger != TagTrigger.OnHPThreshold) continue;
                if (!PassesGate(e, ctx)) continue;
                string key = monsterId + "|HPTHR|" + tag.id + "|" + i.ToString();
                if (!ConsumeOnce(key)) continue;
                totalHealPct += Mathf.Max(0f, e.addPct);

                TagDebugger.Log(monsterId, TagTrigger.OnHPThreshold, Mathf.Max(0f, e.addPct), ctx, tag, "consumed");
            }
        }
        return totalHealPct;
    }

    public static void ResetBattleState()
    {
        _consumedOneShots.Clear();
        _negateCritUsed.Clear();
        consecutiveHitStacks.Clear();
        _swapOnce.Clear();
        _swapDefTurns.Clear();
        _swapDefPct.Clear();

    }

    static bool ConsumeOnce(string key)
    {
        if (_consumedOneShots.Contains(key)) return false;
        _consumedOneShots.Add(key);
        return true;
    }

    static bool IsNightNow()
    {
        int h = DateTime.Now.Hour;
        return (h >= 18 || h < 6);
    }

    public static bool TryConsumeNegateFirstIncomingCrit(string defenderId)
    {
        if (string.IsNullOrEmpty(defenderId)) return false;
        foreach (var tag in EquippedTags(defenderId))
        {
            if (tag.effects == null) continue;
            for (int i = 0; i < tag.effects.Length; i++)
            {
                var e = tag.effects[i];
                if (e.trigger != TagTrigger.OnIncomingCrit) continue;
                string key = defenderId + "|NEGCRIT|" + tag.id + "|" + i.ToString();
                if (ConsumeOnce(key))
                {
                    var ctx = new TagContext();
                    TagDebugger.LogNote(defenderId, TagTrigger.OnIncomingCrit, "consumed (legacy)", ctx, tag);
                    return true;
                }
            }
        }
        return false;
    }

    public static bool TryConsumeNegateIncomingCrit(string defenderId, TagContext ctx, MonsterDataSO attackerDef, MonsterDataSO defenderDef)
    {
        if (string.IsNullOrEmpty(defenderId)) return false;
        if (_negateCritUsed.Contains(defenderId)) return false;
        foreach (var tag in EquippedTags(defenderId))
        {
            if (tag.effects == null) continue;
            foreach (var e in tag.effects)
            {
                if (e.trigger != TagTrigger.OnIncomingCrit) continue;
                if (!ctx.workingHere && e.onlyWhenAtSite) continue;
                if (e.siteScope != 0 && e.siteScope != ctx.siteJob) continue;
                if (e.gateByAttackerType && (attackerDef == null || attackerDef.type != e.attackerType)) continue;
                if (e.gateByDefenderType && (defenderDef == null || defenderDef.type != e.defenderType)) continue;
                if (!PassesGate(e, ctx)) continue;
                if (!e.negateCritOnce) continue;
                _negateCritUsed.Add(defenderId);

                TagDebugger.LogNote(defenderId, TagTrigger.OnIncomingCrit, "consumed", ctx, tag);
                return true;
            }
        }
        return false;
    }

    public static float GetOnDeathHealAlliesPct(string fallenId, MonsterDataSO fallenDef)
    {
        float best = 0f;
        foreach (var tag in EquippedTags(fallenId))
        {
            if (tag.effects == null) continue;
            foreach (var e in tag.effects)
            {
                if (e.trigger != TagTrigger.OnDeath) continue;
                if (e.healAlliesPctMaxHp > best) best = e.healAlliesPctMaxHp;
            }
        }
        // No context here; emit a raw note if non-zero
        if (best > 0f)
        {
            var ctx = new TagContext();
            TagDebugger.LogNote(fallenId, TagTrigger.OnDeath, $"healAlliesPct={best:0.###}", ctx);
        }
        return Mathf.Clamp01(best);
    }

    public static int CoinsForSurviveRounds(string monsterId, int roundsSurvived)
    {
        if (roundsSurvived <= 0) return 0;
        int coins = 0;
        foreach (var tag in EquippedTags(monsterId))
        {
            if (tag.effects == null) continue;
            foreach (var e in tag.effects)
            {
                if (e.trigger != TagTrigger.OnSurviveRounds) continue;
                if (e.everyNRounds <= 0 || e.coinsOnSurvive == 0) continue;
                if (roundsSurvived % e.everyNRounds == 0)
                {
                    coins += e.coinsOnSurvive;

                    var ctx = new TagContext { roundsSurvived = roundsSurvived };
                    TagDebugger.LogNote(monsterId, TagTrigger.OnSurviveRounds, $"+{e.coinsOnSurvive} coins (every {e.everyNRounds})", ctx, tag);
                }
            }
        }
        return coins;
    }

    public static float GetJobFatigueMultiplier(string workerId, JobType site)
    {
        float mul = 1f;
        foreach (var tag in EquippedTags(workerId))
        {
            if (tag.effects == null) continue;
            foreach (var e in tag.effects)
            {
                if (e.trigger != TagTrigger.OnJobOutput) continue;
                if (e.onlyWhenAtSite && e.siteScope != site) continue;
                if (e.extraFatiguePct > 0f)
                {
                    mul *= (1f + e.extraFatiguePct);

                    var ctx = new TagContext { siteJob = site, workingHere = true };
                    TagDebugger.Log(workerId, TagTrigger.OnJobOutput, e.extraFatiguePct, ctx, tag, "fatigue");
                }
            }
        }
        return Mathf.Max(0f, mul);
    }

    public static bool ForbidOutgoingCrits(string attackerId, TagContext ctx, MonsterDataSO attackerDef = null, MonsterDataSO defenderDef = null)
    {
        if (string.IsNullOrEmpty(attackerId)) return false;
        foreach (var tag in EquippedTags(attackerId))
        {
            if (tag.effects == null) continue;
            foreach (var e in tag.effects)
            {
                if (e.trigger != TagTrigger.OnCritLogic) continue;
                if (!ctx.workingHere && e.onlyWhenAtSite) continue;
                if (e.siteScope != 0 && e.siteScope != ctx.siteJob) continue;
                if (e.gateByAttackerType && (attackerDef == null || attackerDef.type != e.attackerType)) continue;
                if (e.gateByDefenderType && (defenderDef == null || defenderDef.type != e.defenderType)) continue;
                if (!PassesGate(e, ctx)) continue;
                if (e.noCrits)
                {
                    TagDebugger.LogNote(attackerId, TagTrigger.OnCritLogic, "noCrits=true", ctx, tag);
                    return true;
                }
            }
        }
        return false;
    }

    public static float GetIncomingCritDamageReducePct(string defenderId, TagContext ctx, MonsterDataSO attackerDef, MonsterDataSO defenderDef)
    {
        float reduce = 0f;
        if (string.IsNullOrEmpty(defenderId)) return 0f;
        foreach (var tag in EquippedTags(defenderId))
        {
            if (tag.effects == null) continue;
            foreach (var e in tag.effects)
            {
                if (e.trigger != TagTrigger.OnCritLogic) continue;
                if (!ctx.workingHere && e.onlyWhenAtSite) continue;
                if (e.siteScope != 0 && e.siteScope != ctx.siteJob) continue;
                if (e.gateByAttackerType && (attackerDef == null || attackerDef.type != e.attackerType)) continue;
                if (e.gateByDefenderType && (defenderDef == null || defenderDef.type != e.defenderType)) continue;
                if (!PassesGate(e, ctx)) continue;
                if (e.critTakenReducePct > 0f) reduce = Mathf.Max(reduce, e.critTakenReducePct);
            }
        }
        if (reduce > 0f) TagDebugger.Log(defenderId, TagTrigger.OnCritLogic, -reduce, ctx, null, "crit reduce");
        return Mathf.Clamp01(reduce);
    }

    public static float GetOutgoingCritChanceBonus(string attackerId, TagContext ctx, MonsterDataSO attackerDef = null, MonsterDataSO defenderDef = null)
    {
        float bonus = 0f;
        var tags = EquippedTags(attackerId);
        foreach (var tag in tags)
        {
            var effects = tag.effects;
            if (effects == null) continue;
            for (int i = 0; i < effects.Length; i++)
            {
                var e = effects[i];
                if (e.trigger != TagTrigger.OnCritChance) continue;
                if (ctx.workingHere)
                {
                    if (e.onlyWhenAtSite == false) { }
                    else if (e.siteScope != 0 && e.siteScope != ctx.siteJob) continue;
                }
                if (e.gateByAttackerType && (attackerDef == null || attackerDef.type != e.attackerType)) continue;
                if (e.gateByDefenderType && (defenderDef == null || defenderDef.type != e.defenderType)) continue;
                if (!PassesGate(e, ctx)) continue;
                if (Mathf.Abs(e.addPct) > 0f)
                {
                    bonus += e.addPct;
                    TagDebugger.Log(attackerId, TagTrigger.OnCritChance, e.addPct, ctx, tag);
                }
            }
        }
        return bonus;
    }

    public static int GetDefenseIgnoreFlat(string attackerId, TagContext ctx, MonsterDataSO attackerDef = null, MonsterDataSO defenderDef = null)
    {
        int ignore = 0;
        var tags = EquippedTags(attackerId);
        foreach (var tag in tags)
        {
            var effects = tag.effects;
            if (effects == null) continue;
            foreach (var e in effects)
            {
                if (e.trigger != TagTrigger.OnDefenseIgnore) continue;
                if (!PassesGate(e, ctx)) continue;
                if (e.gateByAttackerType && attackerDef && attackerDef.type != e.attackerType) continue;
                if (e.gateByDefenderType && defenderDef && defenderDef.type != e.defenderType) continue;
                int add = Mathf.RoundToInt(e.addPct);
                if (add != 0)
                {
                    ignore += add;
                    TagDebugger.Log(attackerId, TagTrigger.OnDefenseIgnore, add, ctx, tag, "flat");
                }
            }
        }
        return ignore;
    }

    public static float GetDefenseMultiplier(string defenderId, MonsterDataSO defDef, MonsterDataSO atkDef)
    {
        return EvaluateMultiplierFor(defenderId, TagTrigger.OnDefense, atkDef, defDef);
    }

    public static float GetSelfHealPctOnEnemyKO(string attackerId, TagContext ctx, MonsterDataSO attackerDef = null, MonsterDataSO defenderDef = null)
    {
        float heal = 0f;
        var tags = EquippedTags(attackerId);
        foreach (var tag in tags)
        {
            var effects = tag.effects;
            if (effects == null) continue;
            for (int i = 0; i < effects.Length; i++)
            {
                var e = effects[i];
                if (e.trigger != TagTrigger.OnEnemyKO && e.trigger != TagTrigger.OnKill) continue;
                if (ctx.workingHere) { if (e.onlyWhenAtSite == false) { } } else if (e.onlyWhenAtSite) continue;
                if (e.siteScope != 0 && e.siteScope != ctx.siteJob) continue;
                if (e.gateByAttackerType && (attackerDef == null || attackerDef.type != e.attackerType)) continue;
                if (e.gateByDefenderType && (defenderDef == null || defenderDef.type != e.defenderType)) continue;
                if (!PassesGate(e, ctx)) continue;
                if (Mathf.Abs(e.addPct) > 0f)
                {
                    heal += Mathf.Max(0f, e.addPct);
                    TagDebugger.Log(attackerId, e.trigger, Mathf.Max(0f, e.addPct), ctx, tag, "heal% on KO");
                }
            }
        }
        return heal;
    }

    public static int GetIncomingDamageFlatReduce(string defenderId, TagContext ctx, MonsterDataSO attackerDef = null, MonsterDataSO defenderDef = null)
    {
        int flatReduce = 0;
        var tags = EquippedTags(defenderId);
        foreach (var tag in tags)
        {
            var effects = tag.effects;
            if (effects == null) continue;
            foreach (var e in effects)
            {
                if (e.trigger != TagTrigger.OnIncomingDamageFlat) continue;
                if (e.gateByAttackerType && attackerDef && attackerDef.type != e.attackerType) continue;
                if (e.gateByDefenderType && defenderDef && defenderDef.type != e.defenderType) continue;
                if (!PassesGate(e, ctx)) continue;

                int add = Mathf.RoundToInt(e.addPct);
                if (add != 0)
                {
                    flatReduce += add;
                    TagDebugger.Log(defenderId, TagTrigger.OnIncomingDamageFlat, add, ctx, tag, "flat reduce");
                }
            }
        }
        return flatReduce;
    }

    public static void RegisterHitResult(string attackerId, bool hitLanded)
    {
        if (string.IsNullOrEmpty(attackerId)) return;
        if (hitLanded)
        {
            if (!consecutiveHitStacks.ContainsKey(attackerId))
                consecutiveHitStacks[attackerId] = 1;
            else
                consecutiveHitStacks[attackerId] = Mathf.Min(consecutiveHitStacks[attackerId] + 1, 3);
        }
        else
        {
            consecutiveHitStacks[attackerId] = 0;
        }
    }

    public static float GetConsecutiveHitDamageBonus(string attackerId, TagContext ctx, MonsterDataSO attackerDef = null, MonsterDataSO defenderDef = null)
    {
        int stacks = 0;
        consecutiveHitStacks.TryGetValue(attackerId, out stacks);
        if (stacks <= 0) return 0f;

        float perStack = 0f;
        var tags = EquippedTags(attackerId);
        foreach (var tag in tags)
        {
            var effects = tag.effects;
            if (effects == null) continue;
            foreach (var e in effects)
            {
                if (e.trigger != TagTrigger.OnConsecutiveHits) continue;
                if (!PassesGate(e, ctx)) continue;
                perStack += e.addPct;
            }
        }

        float total = perStack * stacks;
        if (total != 0f)
            TagDebugger.Log(attackerId, TagTrigger.OnConsecutiveHits, total, ctx, null, $"stacks={stacks}");
        return total;
    }

    public static float GetBattleXPMultiplier(IEnumerable<string> teamIds)
    {
        float mul = 1f;
        if (teamIds == null) return mul;
        var ctx = new TagContext();
        foreach (var id in teamIds)
        {
            float add = EvaluateMultiplierFor(id, TagTrigger.OnBattleEnd) - 1f;
            if (add != 0f)
                TagDebugger.LogNote(id, TagTrigger.OnBattleEnd, $"team addPct={add:0.###}", ctx);
            mul += add;
        }
        return mul;
    }

    public static float GetCritDealtDamageBonus(string attackerId, TagContext ctx, MonsterDataSO attackerDef, MonsterDataSO defenderDef)
    {
        float bonus = 0f;
        foreach (var tag in EquippedTags(attackerId))
        {
            if (tag.effects == null) continue;
            foreach (var e in tag.effects)
            {
                if (e.trigger != TagTrigger.OnCritDealt) continue;
                if (!PassesGate(e, ctx)) continue;
                if (Mathf.Abs(e.addPct) > 0f)
                {
                    bonus += e.addPct;
                    TagDebugger.Log(attackerId, TagTrigger.OnCritDealt, e.addPct, ctx, tag);
                }
            }
        }
        return bonus;
    }

    public static float GetLifestealPct(string attackerId, TagContext ctx, MonsterDataSO attackerDef, MonsterDataSO defenderDef)
    {
        float ls = 0f;
        foreach (var tag in EquippedTags(attackerId))
        {
            if (tag.effects == null) continue;
            foreach (var e in tag.effects)
            {
                if (e.trigger != TagTrigger.OnLifesteal) continue;
                if (!PassesGate(e, ctx)) continue;
                if (e.addPct > 0f)
                {
                    ls += e.addPct;
                    TagDebugger.Log(attackerId, TagTrigger.OnLifesteal, e.addPct, ctx, tag);
                }
            }
        }
        return Mathf.Max(0f, ls);
    }

    public static int ApplySpeedCheckBonus(string monsterId, TagContext ctx, MonsterDataSO selfDef, MonsterDataSO enemyDef, int baseSpeed)
    {
        float mul = EvaluateMultiplierFor(monsterId, TagTrigger.OnSpeedCheck, selfDef, enemyDef);
        int result = Mathf.Max(1, Mathf.RoundToInt(baseSpeed * Mathf.Max(0f, mul)));
        if (!Mathf.Approximately(mul, 1f))
            TagDebugger.Log(monsterId, TagTrigger.OnSpeedCheck, mul - 1f, ctx, null, $"spd {baseSpeed}→{result}");
        return result;
    }

    public static float GetDropChanceMultiplier(IEnumerable<string> teamIds)
    {
        float mul = 1f;
        if (teamIds == null) return mul;
        var ctx = new TagContext();
        foreach (var id in teamIds)
        {
            float add = EvaluateMultiplierFor(id, TagTrigger.OnDropChance) - 1f;
            if (add != 0f) TagDebugger.LogNote(id, TagTrigger.OnDropChance, $"team addPct={add:0.###}", ctx);
            mul += add;
        }
        return mul;
    }

    public static float GetFatigueDecayMultiplierForSite(JobType site, IEnumerable<string> teamIds)
    {
        float mul = 1f;
        if (teamIds == null) return mul;
        var ctx = new TagContext { siteJob = site };
        foreach (var id in teamIds)
        {
            float add = EvaluateMultiplierFor(id, TagTrigger.OnFatigueDecay, siteScope: site) - 1f;
            if (add != 0f) TagDebugger.LogNote(id, TagTrigger.OnFatigueDecay, $"team addPct={add:0.###}", ctx);
            mul += add;
        }
        return Mathf.Max(0f, mul);
    }

    public static void NotifySwapIn(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return;

        foreach (var tag in EquippedTags(monsterId))
        {
            if (tag.effects == null) continue;
            for (int i = 0; i < tag.effects.Length; i++)
            {
                var e = tag.effects[i];
                if (e.trigger != TagTrigger.OnSwapIn) continue;

                string onceKey = monsterId + "|SWAP_ONCE|" + tag.id + "|" + i.ToString();
                if (_swapOnce.Contains(onceKey)) continue;
                _swapOnce.Add(onceKey);

                float pct = Mathf.Max(0f, e.addPct);
                if (pct > 0f)
                {
                    _swapDefPct[monsterId] = pct;
                    _swapDefTurns[monsterId] = 1;
                }
            }
        }
    }

    public static float GetSwapDefenseBonusPct(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return 0f;
        if (_swapDefTurns.TryGetValue(monsterId, out int t) && t > 0 &&
            _swapDefPct.TryGetValue(monsterId, out float p))
            return Mathf.Clamp01(p);
        return 0f;
    }

    public static void TickEndOfRound(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return;
        if (_swapDefTurns.TryGetValue(monsterId, out int t) && t > 0)
        {
            t--;
            if (t <= 0)
            {
                _swapDefTurns.Remove(monsterId);
                _swapDefPct.Remove(monsterId);
            }
            else
            {
                _swapDefTurns[monsterId] = t;
            }
        }
    }

    public static float GetWeakenOnCritPct(
        string attackerId,
        TagContext ctx,
        MonsterDataSO attackerDef,
        MonsterDataSO defenderDef,
        out int turns)
    {
        float bestPct = 0f;
        int bestTurns = 0;

        var tags = EquippedTags(attackerId);
        foreach (var tag in tags)
        {
            var effects = tag.effects;
            if (effects == null) continue;

            foreach (var e in effects)
            {
                if (e.trigger != TagTrigger.OnCritDealt) continue;
                if (!ctx.workingHere && e.onlyWhenAtSite) continue;
                if (e.siteScope != 0 && e.siteScope != ctx.siteJob) continue;
                if (e.gateByAttackerType && (attackerDef == null || attackerDef.type != e.attackerType)) continue;
                if (e.gateByDefenderType && (defenderDef == null || defenderDef.type != e.defenderType)) continue;
                if (!PassesGate(e, ctx)) continue;

                if (e.weakenEnemyDamagePct > bestPct)
                {
                    bestPct = Mathf.Clamp01(e.weakenEnemyDamagePct);
                    bestTurns = Mathf.Max(bestTurns, (e.durationTurns <= 0 ? 1 : e.durationTurns));
                }
            }
        }

        turns = bestTurns;
        return Mathf.Clamp01(bestPct);
    }

    public static float GetExtraDropChanceAdd(string id, TagContext ctx, MonsterDataSO selfOrAttacker, MonsterDataSO defenderOrEnemy)
    {
        if (string.IsNullOrEmpty(id)) return 0f;
        float mul = EvaluateConditionalMultiplier(
            id,
            new[] { TagTrigger.OnDropChance },
            ctx,
            selfOrAttacker,
            defenderOrEnemy
        );
        // convert multiplier to additive chance: (1 + 0.10) -> +0.10
        return Mathf.Max(0f, mul - 1f);
    }

    public static float GetExtraDropChanceAdd(IList<string> ids, TagContext ctx, MonsterDataSO selfOrAttacker, MonsterDataSO defenderOrEnemy)
    {
        if (ids == null || ids.Count == 0) return 0f;
        float mul = 1f;
        foreach (var id in ids)
        {
            mul += EvaluateConditionalMultiplier(
                id,
                new[] { TagTrigger.OnDropChance },
                ctx,
                selfOrAttacker,
                defenderOrEnemy
            ) - 1f;
        }
        return Mathf.Max(0f, mul);
    }
    



}
