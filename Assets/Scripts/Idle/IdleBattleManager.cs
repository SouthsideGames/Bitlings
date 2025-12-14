using System;
using System.Collections.Generic;
using UnityEngine;

using random = UnityEngine.Random;

public class IdleBattleManager : MonoBehaviour
{
    public static IdleBattleManager I { get; private set; }

    [SerializeField] private IdleBattleRewardPanelUI rewardPanel;
    [SerializeField] private EncounterManager encounterManager;

    private IdleBattleConfigSO config;

    void Awake()
    {
        I = this;
        if (config == null)
            config = Resources.Load<IdleBattleConfigSO>("IdleBattleConfig");
    }

    void Start()
    {
        if (IsIdleBattleUnlocked())
        {
            ResolveOfflineIfAny();
            TryOpenSummaryIfNeeded();
        }
        else
        {
            // Ensure legacy auto-battle sessions are disabled if the feature is locked.
            var s = IdleBattleStore.Load();
            if (s.autoBattling)
            {
                s.autoBattling = false;
                IdleBattleStore.Save(s);
            }
        }
    }

    void Update()
    {
        if (IsIdleBattleUnlocked())
        {
            TickForegroundAuto();
        }
    }

    // Feature unlock helper
    private bool IsIdleBattleUnlocked()
    {
        // If the FeatureUnlockManager isn't in the scene yet, treat it as unlocked
        // so the game still works in dev / old saves.
        if (FeatureUnlockManager.I == null)
            return true;

        return FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_Basic);
    }

    public void EnableAuto(string biomeId = null)
    {
        if (!IsIdleBattleUnlocked())
        {
            Debug.Log("IdleBattleManager: Idle battles are locked by feature unlocks; ignoring EnableAuto().");
            return;
        }

        var s = IdleBattleStore.Load();
        if (!s.autoBattling)
        {
            s.autoBattling = true;
            s.sessionStartUnix = NowUnix();
            s.lastTickUnix = s.sessionStartUnix;

            // Single source of truth: bank energy
            s.energyAtStart = ResourceBank.Get(ResourceType.Energy);

            s.biomeId = biomeId;
            IdleBattleStore.Save(s);
        }
    }

    public void DisableAuto()
    {
        var s = IdleBattleStore.Load();
        if (s.autoBattling)
        {
            s.autoBattling = false;
            IdleBattleStore.Save(s);
        }
    }

    private void ResolveOfflineIfAny()
    {
        if (SaveManager.Data == null) return;
        if (config == null) return;

        long lastSaved = SaveManager.Data.lastSavedUnix;
        long now = NowUnix();
        float elapsed = Mathf.Max(0, now - lastSaved);
        if (elapsed <= 0.1f) return;

        float clamped = Mathf.Min(elapsed, config.maxOfflineHours * 3600f);
        int timeEnc = Mathf.FloorToInt(clamped / config.secondsPerEncounter);
        if (timeEnc <= 0) return;

        int baseCost = GetEncounterCostSafe();
        int curEnergy = GetEnergySafe();
        int byEnergy = (baseCost <= 0) ? timeEnc : (curEnergy / baseCost);

        int toRun = Mathf.Min(timeEnc, byEnergy);
        if (toRun <= 0) return;

        RunBatchEncounters(toRun);
        ForceOpenSummary();
    }

    private void TickForegroundAuto()
    {
        if (config == null) return;

        var s = IdleBattleStore.Load();
        if (!s.autoBattling) return;

        long now = NowUnix();
        float dt = Mathf.Max(0, now - s.lastTickUnix);
        int canRun = Mathf.FloorToInt(dt / config.secondsPerEncounter);
        if (canRun <= 0) return;

        int baseCost = GetEncounterCostSafe();
        int curEnergy = GetEnergySafe();
        int byEnergy = (baseCost <= 0) ? canRun : (curEnergy / baseCost);

        int toRun = Mathf.Min(canRun, byEnergy);
        if (toRun <= 0) return;

        RunBatchEncounters(toRun);

        s.lastTickUnix = now;
        IdleBattleStore.Save(s);

        // Stop when we can’t afford another encounter
        if (GetEnergySafe() < baseCost)
        {
            DisableAuto();
            ForceOpenSummary();
        }
    }

    private void RunBatchEncounters(int count)
    {
        if (!IsIdleBattleUnlocked() || count <= 0) return;
        if (config == null) return;

        ResourceBank.BeginBatch();

        var s = IdleBattleStore.Load();
        var rng = new System.Random(SeedForSession(s));
        var teamP = JobIdlePassives.ComputeForActiveTeam();

        // Collect up to 3 team monster IDs (lead first)
        var team = SaveManager.Data?.team;
        var teamIds = new List<string>();
        if (team != null)
        {
            int n = Mathf.Min(3, team.Count);
            for (int i = 0; i < n; i++)
            {
                var om = team[i];
                if (om != null && !string.IsNullOrEmpty(om.monsterId))
                    teamIds.Add(om.monsterId);
            }
        }

        // Titles-independent global neutral mul (keep for future if needed)
        float creditMulNeutral = 1f;

        // Feature unlock: Idle Reward Boost (extra credits from idle battles)
        if (FeatureUnlockManager.I != null &&
            FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_RewardBoost))
        {
            float boost = 1.5f; // fallback if config missing
            boost = Mathf.Max(1f, config.rewardBoostMultiplier);

            creditMulNeutral *= boost;
        }

        int baseCost = GetEncounterCostSafe();
        int effectiveCost = Mathf.Max(1, Mathf.RoundToInt(baseCost * Mathf.Clamp(teamP.energyCostMul, 0.5f, 1f)));

        for (int i = 0; i < count; i++)
        {
            if (!SpendEnergy(effectiveCost)) break;

            s.totalEnergySpent += effectiveCost;
            encounterManager?.RequestStateRefresh();

            var wild = encounterManager != null
                ? encounterManager.PickWildConsideringLures()
                : null;
            if (wild == null) continue;

            int wildLevel = RollWildLevel();
            bool shiny = RollShiny(wild, rng);
            int avgLv = GetAverageTeamLevel();

            // ─────────────────────────────────────────────────────────
            // Titles: fold the lead monster’s Title effects into headless odds
            // ─────────────────────────────────────────────────────────
            string leadId = (teamIds.Count > 0) ? teamIds[0] : null;
            MonsterDataSO leadDef = null;
            int leadLevel = 1;

            if (!string.IsNullOrEmpty(leadId))
            {
                leadDef = MonsterLibraryLocator.GetById(leadId);
                var roster = SaveManager.Data?.team;
                if (roster != null && roster.Count > 0 && roster[0] != null && roster[0].monsterId == leadId)
                    leadLevel = Mathf.Max(1, roster[0].level);
            }

            float titleOffMul = 1f;
            float titleDefMul = 1f;

            if (!string.IsNullOrEmpty(leadId))
            {
                var mods = TitlesAdapter.GetBattleStatMods(leadId);
                if (mods.atkPct > 0f) titleOffMul *= (1f + mods.atkPct);
                if (mods.defPct > 0f) titleDefMul *= (1f + mods.defPct);

                try
                {
                    float effMul = TitlesAdapter.GetEffectivenessMult(leadId, leadDef, leadLevel);
                    if (effMul > 0f) titleOffMul *= effMul;
                }
                catch { }

                try
                {
                    var dfBox = TitlesAdapter.GetDamageFilter(leadId, leadDef, leadLevel);
                    DamageFilterView df;
                    if (TryUnboxDamageFilter(dfBox, out df) && df.percentReduce > 0f)
                        titleDefMul *= (1f + Mathf.Clamp01(df.percentReduce));
                }
                catch { }
            }

            var hb = HeadlessBattle.Resolve(new HeadlessBattle.Input
            {
                avgTeamLevel = avgLv,
                wildLevel = wildLevel,
                basecreditPerWin = config.basecreditPerWin,
                rewardMultiplier = config.rewardMultiplier,
                rngSeed = rng.Next(),

                offenseMul = teamP.offenseMul * Mathf.Max(0.1f, titleOffMul),
                defenseMul = teamP.defenseMul * Mathf.Max(0.1f, titleDefMul),

                earlyEdge = teamP.earlyEdge,
                creditMul = teamP.creditMul
            });

            int creditsBase = Mathf.Max(0, Mathf.FloorToInt(hb.credits * Mathf.Max(0f, creditMulNeutral)));

            int awarded = 0;
            if (hb.victory && creditsBase > 0)
            {
                string leadIdForGrant = (teamIds.Count > 0) ? teamIds[0] : null;
                awarded = ResourceManager.I.AddCreditsWithTitles(creditsBase, leadIdForGrant, wild, wildLevel);
            }

            AddToLogMerged(s.log, wild.id, awarded, shiny);

            GameEvents.BattleFinished?.Invoke(new BattleResult
            {
                victory = hb.victory,
                creditsGained = awarded,
                wildDef = wild,
                wildLevel = wildLevel
            });
        }

        TrimLog(s.log, config.encounterLogMaxEntries);
        IdleBattleStore.Save(s);

        encounterManager?.RequestStateRefresh();

        ResourceBank.EndBatch();
    }

    // ─────────────────────────────────────────────────────────────
    // Bank-only energy spend (prefer EncounterManager for timer correctness)
    // ─────────────────────────────────────────────────────────────
    private static bool SpendEnergy(int cost)
    {
        cost = Mathf.Max(1, cost);

        // Prefer EncounterManager so it updates JSON regen timing baseline
        if (EncounterManager.I != null)
        {
            // If effective cost differs from the configured encounterCost,
            // we spend directly from the bank but ALSO update the regen baseline data
            // by calling Add/Spend style baseline updates in EncounterManager where possible.
            //
            // EncounterManager.SpendEnergy() uses encounterCost, so we cannot call it with a custom cost.
            // Instead we:
            //  1) bank spend
            //  2) request EncounterManager to refresh state (it will tick baseline on next update)
            if (ResourceBank.Get(ResourceType.Energy) < cost) return false;
            if (!ResourceBank.TrySpend(ResourceType.Energy, cost)) return false;

            GameEvents.EnergyChanged?.Invoke();
            EncounterManager.I.RequestStateRefresh();
            return true;
        }

        // Fallback (still bank-only)
        if (ResourceBank.Get(ResourceType.Energy) < cost) return false;
        if (!ResourceBank.TrySpend(ResourceType.Energy, cost)) return false;

        GameEvents.EnergyChanged?.Invoke();
        return true;
    }

    private int GetEnergySafe()
    {
        // authoritative: EncounterManager reads bank; this keeps future-proofing
        if (encounterManager != null) return encounterManager.GetEnergyPoints();
        if (EncounterManager.I != null) return EncounterManager.I.GetEnergyPoints();
        return ResourceBank.Get(ResourceType.Energy);
    }

    private int GetEncounterCostSafe()
    {
        if (encounterManager != null) return Mathf.Max(1, encounterManager.GetEncounterCost());
        if (EncounterManager.I != null) return Mathf.Max(1, EncounterManager.I.GetEncounterCost());
        return Mathf.Max(1, SaveManager.Data != null ? SaveManager.Data.encounterCost : 1);
    }

    private static int RollWildLevel()
    {
        var team = SaveManager.Data?.team;
        int avg = 1;
        if (team != null && team.Count > 0)
        {
            int sum = 0;
            for (int i = 0; i < team.Count; i++) sum += team[i].level;
            avg = Mathf.Max(1, Mathf.RoundToInt((float)sum / team.Count));
        }
        return Mathf.Clamp(avg + UnityEngine.Random.Range(-1, 2), 1, 99);
    }

    private static bool RollShiny(MonsterDataSO wild, System.Random rng)
    {
        int baseOdds = 512;
        float mult = 1f;

        var list = SaveManager.Data?.activeShinyBoosts;
        if (list != null && list.Count > 0)
        {
            var cur = list[0];
            long now = SaveManager.NowUnix();
            if (cur != null && cur.expireUnix > now)
                mult = Mathf.Max(1f, cur.bonus);
        }

        int threshold = Mathf.Max(1, Mathf.FloorToInt(baseOdds / Mathf.Max(1f, mult)));
        return rng.Next(threshold) == 0;
    }

    private static int GetAverageTeamLevel()
    {
        var team = SaveManager.Data?.team;
        if (team == null || team.Count == 0) return 1;
        int sum = 0;
        int count = Mathf.Min(3, team.Count);
        for (int i = 0; i < count; i++) sum += Mathf.Max(1, team[i].level);
        return Mathf.Max(1, Mathf.RoundToInt(sum / Mathf.Max(1f, count)));
    }

    private static void AddToLogMerged(List<IdleEncounterLogEntry> log, string monsterId, int credits, bool shiny)
    {
        if (log == null) return;

        var e = log.Find(x => x.monsterId == monsterId);
        if (e == null)
        {
            e = new IdleEncounterLogEntry
            {
                monsterId = monsterId,
                count = 0,
                credits = 0,
                shinySeen = false
            };
            log.Add(e);
        }

        e.count += 1;
        e.credits += Mathf.Max(0, credits);
        e.shinySeen |= shiny;
    }

    private static void TrimLog(List<IdleEncounterLogEntry> log, int max)
    {
        if (log == null || log.Count <= max) return;
        log.RemoveRange(0, log.Count - max);
    }

    private void ForceOpenSummary()
    {
        if (!rewardPanel) return;

        var s = IdleBattleStore.Load();
        var sum = BuildSummary(s);
        if (sum.totalEncounters <= 0 && sum.totalcredits <= 0) return;

        rewardPanel.Open(sum, onCollected: () => IdleBattleStore.ClearLog());
    }

    private void TryOpenSummaryIfNeeded()
    {
        var s = IdleBattleStore.Load();
        if (s.autoBattling && s.log != null && s.log.Count > 0)
            ForceOpenSummary();
    }

    private IdleBattleSummary BuildSummary(IdleBattleSession s)
    {
        var res = new IdleBattleSummary();

        if (s?.log != null)
        {
            foreach (var e in s.log)
            {
                res.totalEncounters += e.count;
                res.totalcredits += e.credits;
                res.mergedLog.Add(new IdleEncounterLogEntry
                {
                    monsterId = e.monsterId,
                    count = e.count,
                    credits = e.credits,
                    shinySeen = e.shinySeen
                });
            }
        }

        res.totalEnergySpent = s.totalEnergySpent;
        res.durationSeconds = EstimateDurationSecondsFromLog(s);
        return res;
    }

    private float EstimateDurationSecondsFromLog(IdleBattleSession s)
    {
        if (s?.log == null) return 0f;

        int encounters = 0;
        for (int i = 0; i < s.log.Count; i++)
            encounters += s.log[i].count;

        return encounters * config.secondsPerEncounter;
    }

    private static int SeedForSession(IdleBattleSession s)
    {
        unchecked
        {
            int seed = 17;
            seed = seed * 31 + (s.biomeId == null ? 0 : s.biomeId.GetHashCode());
            seed = seed * 31 + (int)(s.sessionStartUnix & 0x7fffffff);
            seed = seed * 31 + s.energyAtStart;
            return seed;
        }
    }

    private long NowUnix() => SaveManager.NowUnix();

    // Developer helpers
    public void Dev_RunEncounters(int count)
    {
        RunBatchEncounters(count);
        ForceOpenSummary();
    }

    public void Dev_SimulateOfflineSeconds(int seconds)
    {
        if (seconds <= 0) return;
        SaveManager.Data.lastSavedUnix = (long)Mathf.Max(0, SaveManager.Data.lastSavedUnix - seconds);
        SaveManager.Save();
        ResolveOfflineIfAny();
    }

    public void Dev_OpenSummary() => ForceOpenSummary();
    public void Dev_ClearIdleLog() => IdleBattleStore.ClearLog();

    private struct DamageFilterView
    {
        public bool cannotBeCrit;
        public float percentReduce;
        public int flatReduce;
    }

    private static bool TryUnboxDamageFilter(object boxed, out DamageFilterView view)
    {
        view = default;
        if (boxed == null) return false;

        var t = boxed.GetType();

        var fNoCrit = t.GetField("cannotBeCrit");
        var fPct = t.GetField("percentReduce");
        var fFlat = t.GetField("flatReduce");

        bool ok = true;
        bool noCrit = false;
        float pct = 0f;
        int flat = 0;

        if (fNoCrit != null && fNoCrit.FieldType == typeof(bool)) noCrit = (bool)fNoCrit.GetValue(boxed); else ok = false;
        if (fPct != null && fPct.FieldType == typeof(float)) pct = (float)fPct.GetValue(boxed); else ok = false;
        if (fFlat != null && fFlat.FieldType == typeof(int)) flat = (int)fFlat.GetValue(boxed); else ok = false;

        if (!ok) return false;

        view = new DamageFilterView
        {
            cannotBeCrit = noCrit,
            percentReduce = pct,
            flatReduce = flat
        };
        return true;
    }
}
