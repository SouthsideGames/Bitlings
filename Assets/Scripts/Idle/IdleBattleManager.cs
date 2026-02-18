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

    private bool _summaryOpenedThisSession = false;

    private bool _headlessBatchRunning = false;

    [Header("Offline Capture")]
    [Tooltip("When unlocked, idle/auto battles can also attempt captures and list them in the rewards panel.")]
    [SerializeField] private FeatureId offlineCaptureFeatureId = FeatureId.IdleBattle_OfflineCapture;

    void OnEnable()
    {
        GameEvents.BattleFinished += HandleBattleFinished;
        GameEvents.MonsterCaptured += HandleMonsterCaptured;

        // Keep idle battling in sync with the player's AUTO toggle.
        // AUTO may remain on while panels are closed or the app is backgrounded.
        // Idle battling should stop only when the player turns AUTO off,
        // the team is defeated, or energy is exhausted.
        GameEvents.AutoBattleModeChanged += HandleAutoBattleModeChanged;
    }

    void OnDisable()
    {
        GameEvents.BattleFinished -= HandleBattleFinished;
        GameEvents.MonsterCaptured -= HandleMonsterCaptured;

        GameEvents.AutoBattleModeChanged -= HandleAutoBattleModeChanged;
    }

    private void HandleAutoBattleModeChanged(bool isAuto)
    {
        if (!IsIdleBattleUnlocked()) return;

        // Encounter auto toggle is the user's intent.
        if (isAuto) EnableAuto();
        else DisableAuto();
    }

    private void HandleMonsterCaptured(string monsterId, MonsterType _)
    {
        if (!IsIdleBattleUnlocked()) return;
        if (_headlessBatchRunning) return;
        if (!IsOfflineCaptureUnlocked()) return;

        // Only record captures during foreground auto (player chose AUTO).
        if (!IsEncounterAutoModeActive()) return;
        if (string.IsNullOrEmpty(monsterId)) return;

        var s = IdleBattleStore.Load();
        if (s == null) return;

        s.capturedLog ??= new List<IdleEncounterLogEntry>();
        AddToLogMerged(s.capturedLog, monsterId, credits: 0, shiny: false);
        TrimLog(s.capturedLog, config != null ? config.encounterLogMaxEntries : 50);

        TrySetBoolFieldIfPresent(s, "hasPendingSummary", true);
        IdleBattleStore.Save(s);
    }


    private void HandleBattleFinished(BattleResult r)
    {
        if (!IsIdleBattleUnlocked()) return;
        if (_headlessBatchRunning) return; 

        var s = IdleBattleStore.Load();
        if (s == null) return;

        if (!IsEncounterAutoModeActive()) return;

        if (r.wildDef == null || string.IsNullOrEmpty(r.wildDef.id)) return;

        AddToLogMerged(s.log, r.wildDef.id, r.creditsGained, shiny: false);
        TrimLog(s.log, config != null ? config.encounterLogMaxEntries : 50);

        try
        {
            int curEnergy = ResourceBank.Get(ResourceType.Energy);
            int spent = Mathf.Max(0, s.energyAtStart - curEnergy);
            if (spent > s.totalEnergySpent) s.totalEnergySpent = spent;
        }
        catch { }

        TrySetBoolFieldIfPresent(s, "hasPendingSummary", true);
        IdleBattleStore.Save(s);
    }

    private bool IsEncounterAutoModeActive()
    {
        try
        {
            var em = encounterManager != null ? encounterManager : EncounterManager.I;
            return em != null && em.IsAutoMode;
        }
        catch { }

        return false;
    }

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (config == null)
            config = Resources.Load<IdleBattleConfigSO>("IdleBattleConfig");

        if (config == null)
        {
            Debug.LogError("[IdleBattleManager] Missing Resources/IdleBattleConfig. Idle battles will be disabled.", this);
            enabled = false;
        }
    }

    void Start()
    {
        if (IsIdleBattleUnlocked())
        {

            // Save-State Guard: if a batch was interrupted (crash/force-close),
            // do NOT run offline simulation automatically. Instead, flag recovery
            // so UI can prompt Resume vs Discard.
            if (IdleBattleSaveStateGuard.HasPending())
            {
                var s0 = IdleBattleStore.Load();
                if (s0 != null)
                {
                    s0.hasPendingRecovery = true;
                    // Pause auto until a recovery decision is made.
                    s0.autoBattling = false;
                    IdleBattleStore.Save(s0);
                }
                return;
            }

            // Offline simulation should only run if the player left AUTO/idle battling ON.
            // If AUTO was turned off before closing the app, we must not consume energy
            // or run encounters on next boot.
            var s = IdleBattleStore.Load();
            if (s != null && s.autoBattling)
                ResolveOfflineIfAny();
        }
        else
        {
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
        if (FeatureUnlockManager.I == null)
            return true;

        return FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_Basic);
    }

    private bool IsOfflineCaptureUnlocked()
    {
        if (FeatureUnlockManager.I == null)
            return true;
        if (offlineCaptureFeatureId == FeatureId.None)
            return true;
        return FeatureUnlockManager.I.IsUnlocked(offlineCaptureFeatureId);
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
            s.hasPendingRecovery = false;
            s.sessionStartUnix = NowUnix();
            s.lastTickUnix = s.sessionStartUnix;

            s.energyAtStart = ResourceBank.Get(ResourceType.Energy);

            s.biomeId = biomeId;

            _summaryOpenedThisSession = false;

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

    // ─────────────────────────────────────────────────────────────
    // Save-State Guard recovery controls
    // ─────────────────────────────────────────────────────────────
    public bool HasPendingRecovery()
    {
        try
        {
            var s = IdleBattleStore.Load();
            return (s != null && s.hasPendingRecovery) || IdleBattleSaveStateGuard.HasPending();
        }
        catch { return IdleBattleSaveStateGuard.HasPending(); }
    }

    /// <summary>
    /// Resume auto battling after an interrupted batch.
    /// This clears the guard and re-enables auto; the next tick will safely run batches again.
    /// </summary>
    public void ResumePendingRecovery(string biomeId = null)
    {
        var s = IdleBattleStore.Load();
        if (s != null)
        {
            s.hasPendingRecovery = false;
            s.autoBattling = true;
            if (!string.IsNullOrEmpty(biomeId)) s.biomeId = biomeId;
            s.lastTickUnix = NowUnix();
            IdleBattleStore.Save(s);
        }

        // Clear the guard so Start() won't re-pause.
        IdleBattleSaveStateGuard.Discard();
    }

    /// <summary>
    /// Discard an interrupted batch cleanly.
    /// This clears the guard and leaves auto OFF.
    /// </summary>
    public void DiscardPendingRecovery(bool clearLogs = false)
    {
        var s = IdleBattleStore.Load();
        if (s != null)
        {
            s.hasPendingRecovery = false;
            s.autoBattling = false;
            s.lastTickUnix = NowUnix();
            if (clearLogs)
            {
                s.log?.Clear();
                s.capturedLog?.Clear();
                s.hasPendingSummary = false;
            }
            IdleBattleStore.Save(s);
        }

        IdleBattleSaveStateGuard.Discard();
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

        MarkSummaryPendingIfLogExists();
    }

    private void TickForegroundAuto()
    {
        if (config == null) return;

        var s = IdleBattleStore.Load();
        if (!s.autoBattling) return;

        // Stop conditions (must match design):
        // - Player turned AUTO off (handled via AutoBattleModeChanged)
        // - No team / all team members down
        // - No energy (handled later)
        if (!HasAnyAliveTeamMember())
        {
            DisableAuto();
            MarkSummaryPendingIfLogExists();
            return;
        }

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

        if (GetEnergySafe() < baseCost)
        {
            DisableAuto();

            MarkSummaryPendingIfLogExists();
        }
    }

    private void RunBatchEncounters(int count)
    {
        if (!IsIdleBattleUnlocked() || count <= 0) return;
        if (config == null) return;

        _headlessBatchRunning = true;

        // Save-State Guard: mark batch as in-progress BEFORE doing any work.
        // If the app crashes mid-batch, we can resume safely or discard cleanly.
        string guardId;
        var s = IdleBattleStore.Load();
        int sessionSeed = SeedForSession(s);
        IdleBattleSaveStateGuard.Begin(count, sessionSeed, out guardId);

        // IMPORTANT: We stage changes (energy spend / rewards / captures) and only apply
        // them during a commit phase at the end of the batch. This is what makes the
        // save-state guard effective.

        ResourceBank.BeginBatch();

        try
        {
            var rng = new System.Random(sessionSeed);
            var teamP = JobIdlePassives.ComputeForActiveTeam();

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

        float creditMulNeutral = 1f;

        if (FeatureUnlockManager.I != null &&
            FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_RewardBoost))
        {
            float boost = 1.5f; 
            boost = Mathf.Max(1f, config.rewardBoostMultiplier);

            creditMulNeutral *= boost;
        }

        int baseCost = GetEncounterCostSafe();
        int effectiveCost = Mathf.Max(1, Mathf.RoundToInt(baseCost * Mathf.Clamp(teamP.energyCostMul, 0.5f, 1f)));

        int energyRemaining = GetEnergySafe();
        int totalSpentLocal = 0;

        var pending = new List<PendingIdleEncounter>(Mathf.Clamp(count, 1, 256));

        for (int i = 0; i < count; i++)
        {
            if (energyRemaining < effectiveCost) break;
            energyRemaining -= effectiveCost;
            totalSpentLocal += effectiveCost;

            var wild = encounterManager != null
                ? encounterManager.PickWildConsideringFlyers()
                : null;
            if (wild == null) continue;

            int wildLevel = RollWildLevel();
            bool shiny = RollShiny(wild, rng);
            int avgLv = GetAverageTeamLevel();

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
                        titleDefMul *= 1f + Mathf.Clamp01(df.percentReduce);
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

            bool captured = false;
            if (hb.victory && IsOfflineCaptureUnlocked() && wild != null && !wild.uncatchable)
            {
                float chance = CalcIdleCaptureChance01(wild);
                if (rng.NextDouble() <= chance)
                    captured = true;
            }

            pending.Add(new PendingIdleEncounter
            {
                wildDef = wild,
                wildLevel = wildLevel,
                shiny = shiny,
                victory = hb.victory,
                creditsBase = creditsBase,
                capture = captured
            });
        }

        // ─────────────────────────────────────────────
        // Commit Phase (atomic-ish): apply staged changes
        // ─────────────────────────────────────────────

        // Spend energy in one shot.
        if (totalSpentLocal > 0)
        {
            int available = Mathf.Max(0, ResourceBank.Get(ResourceType.Energy));
            int toSpend = Mathf.Min(available, Mathf.Max(0, totalSpentLocal));
            if (toSpend > 0)
            {
                ResourceBank.TrySpend(ResourceType.Energy, toSpend);
                GameEvents.EnergyChanged?.Invoke();
            }
            s.totalEnergySpent += Mathf.Max(0, toSpend);
        }

        // Apply rewards/captures & write logs.
        string leadIdForGrant = (teamIds.Count > 0) ? teamIds[0] : null;
        for (int i = 0; i < pending.Count; i++)
        {
            var p = pending[i];
            if (p == null || p.wildDef == null) continue;

            int awarded = 0;
            if (p.victory && p.creditsBase > 0)
            {
                try
                {
                    awarded = ResourceManager.I != null
                        ? ResourceManager.I.AddCreditsWithTitles(p.creditsBase, leadIdForGrant, p.wildDef, p.wildLevel)
                        : 0;
                }
                catch { awarded = 0; }
            }

            AddToLogMerged(s.log, p.wildDef.id, awarded, p.shiny);

            if (p.victory && p.capture && IsOfflineCaptureUnlocked() && p.wildDef != null && !p.wildDef.uncatchable)
            {
                bool applied = false;
                try { applied = ApplyIdleCaptureToSave(p.wildDef, p.wildLevel, isShiny: p.shiny); }
                catch { applied = false; }

                if (applied)
                {
                    s.capturedLog ??= new List<IdleEncounterLogEntry>();
                    AddToLogMerged(s.capturedLog, p.wildDef.id, credits: 0, shiny: p.shiny);
                    TrimLog(s.capturedLog, config != null ? config.encounterLogMaxEntries : 50);
                }
            }

            // Notify (for UI/battle loggers). Guarded by _headlessBatchRunning in HandleBattleFinished.
            try
            {
                GameEvents.BattleFinished?.Invoke(new BattleResult
                {
                    victory = p.victory,
                    creditsGained = awarded,
                    creditsBase = 0,
                    creditsTitleBonus = 0,
                    activeMonsterOwnedId = leadIdForGrant,
                    wildDef = p.wildDef,
                    wildLevel = p.wildLevel
                });
            }
            catch { }
        }

        TrimLog(s.log, config.encounterLogMaxEntries);
        IdleBattleStore.Save(s);

        MarkSummaryPendingIfLogExists();

        encounterManager?.RequestStateRefresh();

            // Save-State Guard: batch committed successfully.
            IdleBattleSaveStateGuard.Complete(guardId);
        }
        finally
        {
            try { ResourceBank.EndBatch(); } catch { }
            _headlessBatchRunning = false;
        }
    }

    [Serializable]
    private class PendingIdleEncounter
    {
        public MonsterDataSO wildDef;
        public int wildLevel;
        public bool shiny;
        public bool victory;
        public int creditsBase;
        public bool capture;
    }

    private void MarkSummaryPendingIfLogExists()
    {
        var s = IdleBattleStore.Load();
        if (s == null) return;

        bool hasLog = (s.log != null && s.log.Count > 0) || (s.capturedLog != null && s.capturedLog.Count > 0);
        if (!hasLog) return;

        TrySetBoolFieldIfPresent(s, "hasPendingSummary", true);
        IdleBattleStore.Save(s);
    }

    private bool HasPendingSummary(IdleBattleSession s)
    {
        if (s == null) return false;

        bool pending;
        if (TryGetBoolFieldIfPresent(s, "hasPendingSummary", out pending))
            return pending;

        return (s.log != null && s.log.Count > 0) || (s.capturedLog != null && s.capturedLog.Count > 0);
    }

    private void ClearPendingSummaryFlag(IdleBattleSession s)
    {
        if (s == null) return;
        TrySetBoolFieldIfPresent(s, "hasPendingSummary", false);
    }

    private static bool TryGetBoolFieldIfPresent(object obj, string fieldName, out bool value)
    {
        value = false;
        if (obj == null) return false;

        var t = obj.GetType();
        var f = t.GetField(fieldName);
        if (f == null || f.FieldType != typeof(bool)) return false;

        value = (bool)f.GetValue(obj);
        return true;
    }

    private static void TrySetBoolFieldIfPresent(object obj, string fieldName, bool value)
    {
        if (obj == null) return;

        var t = obj.GetType();
        var f = t.GetField(fieldName);
        if (f == null || f.FieldType != typeof(bool)) return;

        f.SetValue(obj, value);
    }

    // ─────────────────────────────────────────────────────────────
    // Bank-only energy spend (prefer EncounterManager for timer correctness)
    // ─────────────────────────────────────────────────────────────
    private static bool SpendEnergy(int cost)
    {
        cost = Mathf.Max(1, cost);

        if (EncounterManager.I != null)
        {
            if (ResourceBank.Get(ResourceType.Energy) < cost) return false;
            if (!ResourceBank.TrySpend(ResourceType.Energy, cost)) return false;

            GameEvents.EnergyChanged?.Invoke();
            EncounterManager.I.RequestStateRefresh();
            return true;
        }

        if (ResourceBank.Get(ResourceType.Energy) < cost) return false;
        if (!ResourceBank.TrySpend(ResourceType.Energy, cost)) return false;

        GameEvents.EnergyChanged?.Invoke();
        return true;
    }

    private int GetEnergySafe()
    {
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

    private static bool HasAnyAliveTeamMember()
    {
        var team = SaveManager.Data?.team;
        if (team == null || team.Count == 0) return false;

        int n = Mathf.Min(3, team.Count);
        for (int i = 0; i < n; i++)
        {
            var m = team[i];
            if (m == null) continue;
            if (string.IsNullOrEmpty(m.monsterId)) continue;

            // Convention across the project: HP == 0 means down; -1 means "uninitialized" and is treated as alive.
            if (m.currentHP != 0) return true;
        }

        return false;
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
        const int baseOdds = 512;   
        const float maxMult = 8f;  
        float mult = 1f;

        var list = SaveManager.Data?.activeShinyBoosts;
        if (list != null && list.Count > 0)
        {
            var cur = list[0];
            long now = SaveManager.NowUnix();
            if (cur != null && cur.expireUnix > now)
                mult = Mathf.Clamp(cur.bonus, 1f, maxMult);
        }

        int threshold = Mathf.Max(1, Mathf.FloorToInt(baseOdds / mult));
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

    // ------------------------------------------------------------------------------------
    // Idle captures (headless batches)
    // ------------------------------------------------------------------------------------

    private static float CalcIdleCaptureChance01(MonsterDataSO def)
    {
        if (def == null) return 0f;

        // Mirror the general intent of EncounterManager capture logic:
        // common spawns are easier, rare spawns are harder, clamped to a sane range.
        float weight = Mathf.Max(0.0001f, def.spawnWeight);

        float minW = weight;
        float maxW = weight;
        try
        {
            var lib = MonsterLibraryLocator.Lib;
            if (lib != null)
            {
                foreach (var m in lib.All)
                {
                    if (m == null) continue;
                    if (m.spawnWeight <= 0f) continue;
                    minW = Mathf.Min(minW, m.spawnWeight);
                    maxW = Mathf.Max(maxW, m.spawnWeight);
                }
            }
        }
        catch { }

        float t = 0f;
        if (maxW > minW)
            t = Mathf.InverseLerp(maxW, minW, weight); // higher weight => closer to 1 (easier)

        // 15% .. 65%
        return Mathf.Clamp01(Mathf.Lerp(0.15f, 0.65f, t));
    }

    private static bool ApplyIdleCaptureToSave(MonsterDataSO def, int level, bool isShiny)
    {
        if (def == null) return false;
        if (SaveManager.Data == null) return false;

        // Do not capture bosses/uncatchables (already guarded by caller) but keep it defensive.
        if (def.uncatchable) return false;

        var data = SaveManager.Data;
        data.owned ??= new List<OwnedMonsterData>();

        // Match variants: same monsterId + shiny flag.
        OwnedMonsterData existing = null;
        for (int i = 0; i < data.owned.Count; i++)
        {
            var om = data.owned[i];
            if (om == null) continue;
            if (!string.Equals(om.monsterId, def.id, StringComparison.OrdinalIgnoreCase)) continue;
            if (om.isShiny != isShiny) continue;
            existing = om;
            break;
        }

        int maxLv = Mathf.Max(1, def.maxLevel);

        if (existing == null)
        {
            int startHP = 1;
            if (def != null)
                startHP = HealingService.CalcMaxHP(def, Mathf.Clamp(level <= 0 ? 1 : level, 1, maxLv), includeTraining: true, includeTitles: false);
            var om = new OwnedMonsterData
            {
                monsterId = def.id,
                level = Mathf.Clamp(level <= 0 ? 1 : level, 1, maxLv),
                currentXP = 0,
                currentHP = Mathf.Max(0, startHP),
                lastHPUnix = 0,
                ownedUID = System.Guid.NewGuid().ToString("N"),
                isShiny = isShiny,
                shinyTier = isShiny ? 1 : 0
            };

            data.owned.Add(om);
            try { GameEvents.OnOwnedMonstersChanged?.Invoke(); } catch { }
            try { GameEvents.MonsterCaptured?.Invoke(def.id, def.type); } catch { }
            SaveManager.Save();
            return true;
        }

        // Duplicate: level up by +1 if not max.
        int before = existing.level;
        existing.level = Mathf.Clamp(existing.level + 1, 1, maxLv);

        // List reference is the same object; still fire events.
        try { GameEvents.OnOwnedMonstersChanged?.Invoke(); } catch { }
        if (existing.level > before)
            try { GameEvents.MonsterLeveled?.Invoke(existing.monsterId, existing.level); } catch { }

        try { GameEvents.MonsterCaptured?.Invoke(def.id, def.type); } catch { }
        SaveManager.Save();
        return true;
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

        // runtime guard
        _summaryOpenedThisSession = true;

        UIManager.I?.Show(PanelId.IdleBattleRewards);

        rewardPanel.Open(sum, onCollected: () =>
        {
            IdleBattleStore.ClearLog();

            var ss = IdleBattleStore.Load();
            ClearPendingSummaryFlag(ss);
            IdleBattleStore.Save(ss);

            UIManager.I?.Hide(PanelId.IdleBattleRewards);
        });
    }

    public void TryOpenSummaryIfNeeded()
    {
        if (_summaryOpenedThisSession) return;

        var s = IdleBattleStore.Load();
        if (!HasPendingSummary(s)) return;

        ForceOpenSummary();

        var ss = IdleBattleStore.Load();
        ClearPendingSummaryFlag(ss);
        IdleBattleStore.Save(ss);
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

        if (s?.capturedLog != null)
        {
            foreach (var e in s.capturedLog)
            {
                res.capturedLog.Add(new IdleEncounterLogEntry
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
