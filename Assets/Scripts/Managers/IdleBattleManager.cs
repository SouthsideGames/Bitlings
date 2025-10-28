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
        ResolveOfflineIfAny();
        TryOpenSummaryIfNeeded();
    }

    void Update()
    {
        TickForegroundAuto();
    }

    public void EnableAuto(string biomeId = null)
    {
        var s = IdleBattleStore.Load();
        if (!s.autoBattling)
        {
            s.autoBattling = true;
            s.sessionStartUnix = NowUnix();
            s.lastTickUnix = s.sessionStartUnix;
            s.energyAtStart = SaveManager.Data.encounterPoints;
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
        long lastSaved = SaveManager.Data.lastSavedUnix;
        long now = NowUnix();
        float elapsed = Mathf.Max(0, now - lastSaved);
        if (elapsed <= 0.1f) return;

        float clamped = Mathf.Min(elapsed, config.maxOfflineHours * 3600f);
        int timeEnc = Mathf.FloorToInt(clamped / config.secondsPerEncounter);
        if (timeEnc <= 0) return;

        int cost = Mathf.Max(1, SaveManager.Data.encounterCost);
        int byEnergy = SaveManager.Data.encounterPoints / cost;
        int toRun = Mathf.Min(timeEnc, byEnergy);
        if (toRun <= 0) return;

        RunBatchEncounters(toRun);
        ForceOpenSummary();
    }

    private void TickForegroundAuto()
    {
        var s = IdleBattleStore.Load();
        if (!s.autoBattling) return;

        long now = NowUnix();
        float dt = Mathf.Max(0, now - s.lastTickUnix);
        int canRun = Mathf.FloorToInt(dt / config.secondsPerEncounter);
        if (canRun <= 0) return;

        int cost = Mathf.Max(1, SaveManager.Data.encounterCost);
        int byEnergy = SaveManager.Data.encounterPoints / cost;
        int toRun = Mathf.Min(canRun, byEnergy);
        if (toRun <= 0) return;

        RunBatchEncounters(toRun);

        s.lastTickUnix = now;
        IdleBattleStore.Save(s);

        if (SaveManager.Data.encounterPoints < cost)
        {
            DisableAuto();
            ForceOpenSummary();
        }
    }

    private void RunBatchEncounters(int count)
    {
        ResourceBank.BeginBatch();

        var s     = IdleBattleStore.Load();
        var rng   = new System.Random(SeedForSession(s));
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

        // Neutral global mul (TagRuntime removed; per-site/team multipliers already handled elsewhere)
        float coinMul = 1f;

        int baseCost      = Mathf.Max(1, SaveManager.Data.encounterCost);
        int effectiveCost = Mathf.Max(1, Mathf.RoundToInt(baseCost * Mathf.Clamp(teamP.energyCostMul, 0.5f, 1f)));

        for (int i = 0; i < count; i++)
        {
            if (!SpendEnergyIfPossible(effectiveCost)) break;

            // Live-refresh the encounter/energy UI for each spend
            s.totalEnergySpent += effectiveCost;
            encounterManager?.RequestStateRefresh();

            var wild = encounterManager != null
                ? encounterManager.PickWildConsideringLures()
                : null;
            if (wild == null) continue;

            int  wildLevel = RollWildLevel();
            bool shiny     = RollShiny(wild, rng);
            int  avgLv     = GetAverageTeamLevel();

            var hb = HeadlessBattle.Resolve(new HeadlessBattle.Input
            {
                avgTeamLevel     = avgLv,
                wildLevel        = wildLevel,
                baseCoinPerWin   = config.baseCoinPerWin,
                rewardMultiplier = config.rewardMultiplier,
                rngSeed          = rng.Next(),
                offenseMul       = teamP.offenseMul,
                defenseMul       = teamP.defenseMul,
                earlyEdge        = teamP.earlyEdge,
                coinMul          = teamP.coinMul
            });

            // Base coins from headless result + neutral mul
            int coinsBase = Mathf.Max(0, Mathf.FloorToInt(hb.coins * Mathf.Max(0f, coinMul)));

            // Titles-aware coin grant via ResourceManager (uses lead monster if present)
            int awarded = 0;
            if (hb.victory && coinsBase > 0)
            {
                string leadId = (teamIds.Count > 0) ? teamIds[0] : null;
                awarded = ResourceManager.I.AddCoinsWithTitles(coinsBase, leadId, wild, wildLevel);
            }

            // Log with the actual awarded amount (post-titles), keep shiny flag
            AddToLogMerged(s.log, wild.id, awarded, shiny);

            // Broadcast a compact battle result for observers (UI/analytics/achievements)
            GameEvents.BattleFinished?.Invoke(new BattleResult
            {
                victory      = hb.victory,
                coinsGained  = awarded, // already titles-scaled + actually banked
                wildDef      = wild,
                wildLevel    = wildLevel
                // (add any other fields your BattleResult supports; these are the core ones)
            });
        }

        TrimLog(s.log, config.encounterLogMaxEntries);
        IdleBattleStore.Save(s);

        // Final UI refresh at the end of the batch (covers partial loops / last update)
        encounterManager?.RequestStateRefresh();

        ResourceBank.EndBatch();
    }



    private static bool SpendEnergyIfPossible(int cost)
    {
        cost = Mathf.Max(1, cost);
        if (SaveManager.Data.encounterPoints < cost) return false;
        SaveManager.Data.encounterPoints -= cost;
        if (SaveManager.Data.encounterPoints < 0) SaveManager.Data.encounterPoints = 0;
        SaveManager.Save();
        return true;
    }

    private static int RollWildLevel()
    {
        var team = SaveManager.Data.team;
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

    private static void AddToLogMerged(List<IdleEncounterLogEntry> log, string monsterId, int coins, bool shiny)
    {
        if (log == null) return;
        var e = log.Find(x => x.monsterId == monsterId);
        if (e == null)
        {
            e = new IdleEncounterLogEntry { monsterId = monsterId, count = 0, coins = 0, shinySeen = false };
            log.Add(e);
        }
        e.count += 1;
        e.coins += Mathf.Max(0, coins);
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
        if (sum.totalEncounters <= 0 && sum.totalCoins <= 0) return;
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
                res.totalCoins += e.coins;
                res.mergedLog.Add(new IdleEncounterLogEntry
                {
                    monsterId = e.monsterId,
                    count = e.count,
                    coins = e.coins,
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
        for (int i = 0; i < s.log.Count; i++) encounters += s.log[i].count;
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
        if (count <= 0) return;
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
}
