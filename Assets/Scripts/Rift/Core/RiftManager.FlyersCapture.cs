using UnityEngine;
using System.Collections.Generic;
using System;
using Random = UnityEngine.Random;

// ─────────────────────────────────────────────────────────────
// RiftManager.FlyersCapture
// Flyer/luck/premium modifiers, weighted wild selection, and capture flow.
// ─────────────────────────────────────────────────────────────

public partial class RiftManager
{
    // ================= LURES / LUCK / PREMIUM / CAPTURE BAND =====================
    private const float MAX_PREMIUM_BOOST_MULT = 8f;

    private const int DUPLICATE_LEVELUP_STAT_POINTS = 3;

    public IReadOnlyList<FlyerBiasData> ActiveLures => SaveManager.Data?.activeFlyers;

    public void AddFlyer(MonsterType type, float bonus = 0.30f, int durationHours = 2)
    {
        if (SaveManager.Data == null) return;

        bonus = Mathf.Clamp(bonus, 0f, 2f);
        durationHours = Mathf.Max(1, durationHours);

        long now = SaveManager.NowUnix();
        long expiry = now + durationHours * 3600L;

        if (SaveManager.Data.activeFlyers == null)
            SaveManager.Data.activeFlyers = new List<FlyerBiasData>();

        SaveManager.Data.activeFlyers.Clear();
        SaveManager.Data.activeFlyers.Add(new FlyerBiasData
        {
            type = type,
            bonus = bonus,
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
    }

    private Dictionary<MonsterType, float> BuildFlyerTypeMultipliers()
    {
        var map = new Dictionary<MonsterType, float>();
        var cur = CurrentFlyer;
        if (cur == null) return map;

        float mult = Mathf.Clamp(1f + Mathf.Max(0f, cur.bonus), 1f, 3f);
        map[cur.type] = mult;
        return map;
    }

    public MonsterDataSO PickWildConsideringFlyers()
    {
        var catalog = MonsterCatalog.All;
        if (catalog == null || catalog.Count == 0)
            return null;

        var pool = new List<MonsterDataSO>(catalog.Count);
        var added = new HashSet<string>();

        // 1) Base: anything in the catalog with spawnWeight > 0 is eligible
        for (int i = 0; i < catalog.Count; i++)
        {
            var m = catalog[i];
            if (m == null || string.IsNullOrEmpty(m.id)) continue;
            if (m.spawnWeight <= 0f) continue;

            if (added.Add(m.id))
                pool.Add(m);
        }

        if (pool.Count == 0)
        {
            for (int i = 0; i < catalog.Count; i++)
            {
                var m = catalog[i];
                if (m == null || string.IsNullOrEmpty(m.id)) continue;
                if (added.Add(m.id))
                    pool.Add(m);
            }
            if (pool.Count == 0) return null;
            return pool[Random.Range(0, pool.Count)];
        }

        var typeMult = BuildFlyerTypeMultipliers();
        float flyerBonus01 = GetActiveFlyerBonus01();

        // min/max base weight for "scarcity" calc (luck favors scarce = lower base weight)
        float minBase = float.MaxValue;
        float maxBase = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            float b = Mathf.Max(0f, pool[i].spawnWeight);
            if (b < minBase) minBase = b;
            if (b > maxBase) maxBase = b;
        }

        float GetFinalWeight(MonsterDataSO m)
        {
            float baseW = Mathf.Max(0f, m.spawnWeight);
            if (baseW <= 0f) return 0f;

            float mult = 1f;

            // Lure type bias
            if (typeMult != null && typeMult.TryGetValue(m.type, out float tMult))
                mult *= Mathf.Max(0f, tMult);

            // WyrmDen rarity weighting
            mult *= JobBalance.GetWyrmDenRarityWeightMult(m.rarity);

            // Luck: favors scarce monsters
            if (flyerBonus01 > 0f && maxBase > minBase)
            {
                float scarcity01 = Mathf.Clamp01((maxBase - baseW) / (maxBase - minBase));
                float luckMult = 1f + flyerBonus01 * scarcity01;
                mult *= luckMult;
            }

            // Premium Orb: boost any premium variants
            float premiumMult = GetActivePremiumBoostMult();
            if (premiumMult > 1f && IsPremiumMonster(m))
                mult *= premiumMult;

            float finalW = baseW * mult;
            if (float.IsNaN(finalW) || float.IsInfinity(finalW)) return 0f;
            return Mathf.Max(0f, finalW);
        }

        return PickByWeight(pool, GetFinalWeight);
    }

    public FlyerBiasData CurrentFlyer
    {
        get
        {
            var list = SaveManager.Data?.activeFlyers;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private LuckBoostData CurrentLuck
    {
        get
        {
            var list = SaveManager.Data?.activeFavorBoosts;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private PremiumBoostData CurrentPremiumBoost
    {
        get
        {
            var list = SaveManager.Data?.activePremiumBoosts;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private WorkOrderData CurrentCaptureBand
    {
        get
        {
            var list = SaveManager.Data?.activeWorkOrders;
            if (list == null || list.Count == 0) return null;
            var cur = list[0];
            if (cur != null && cur.expireUnix <= SaveManager.NowUnix())
            {
                list.Clear();
                SaveManager.Save();
                GameEvents.OnResourcesChanged?.Invoke();
                return null;
            }
            return cur;
        }
    }

    private float GetActiveFlyerBonus01()
    {
        var cur = CurrentLuck;
        if (cur == null) return 0f;
        return Mathf.Clamp01(cur.bonus);
    }

    public bool HasActivePremiumBoost()
    {
        return CurrentPremiumBoost != null;
    }

    private float GetActivePremiumBoostMult()
    {
        var cur = CurrentPremiumBoost;
        if (cur == null) return 1f;
        return Mathf.Clamp(cur.bonus, 1f, MAX_PREMIUM_BOOST_MULT);
    }

    private float GetActiveCaptureBonus01()
    {
        var cur = CurrentCaptureBand;
        if (cur == null) return 0f;
        return Mathf.Clamp01(cur.bonus);
    }

    public long GetFlyerSecondsRemaining()
    {
        var cur = CurrentFlyer;
        if (cur == null) return -1;
        long now = SaveManager.NowUnix();
        long rem = cur.expireUnix - now;
        return Math.Max(0L, rem);
    }

    private static T PickByWeight<T>(IList<T> items, System.Func<T, float> getWeight)
    {
        float total = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float w = Mathf.Max(0f, getWeight(items[i]));
            if (!float.IsNaN(w) && !float.IsInfinity(w)) total += w;
        }

        if (total <= 0f)
            return items.Count > 0 ? items[Random.Range(0, items.Count)] : default;

        float roll = Random.Range(0f, total);
        float acc = 0f;
        for (int i = 0; i < items.Count; i++)
        {
            float w = Mathf.Max(0f, getWeight(items[i]));
            if (w <= 0f) continue;
            acc += w;
            if (roll <= acc) return items[i];
        }
        return items[items.Count - 1];
    }

    // ====== Capture logic ======

    /// <summary>
    /// Returns the estimated catch chance [0..1] for the given monster without performing a roll.
    /// Mirrors TryCatchWithResult's formula. Used for pre-battle catch rate preview display.
    /// </summary>
    public float GetEstimatedCatchChance(MonsterDataSO def)
    {
        if (!def || def.uncatchable) return 0f;
        var lib = MonsterLibraryLocator.Lib;
        if (!lib) return 0f;

        float minW = float.MaxValue, maxW = 0f;
        for (int i = 0; i < lib.monsters.Length; i++)
        {
            var m = lib.monsters[i];
            if (!m) continue;
            float w = Mathf.Max(0f, m.spawnWeight);
            if (w < minW) minW = w;
            if (w > maxW) maxW = w;
        }
        if (minW == float.MaxValue || maxW <= 0f || minW >= maxW) { minW = 0f; maxW = 1f; }

        float t = Mathf.Clamp01(
            (Mathf.Max(0f, def.spawnWeight) - minW) /
            Mathf.Max(0.0001f, (maxW - minW))
        );
        float baseChance = Mathf.Lerp(0.15f, 0.65f, t);
        float bandBonus  = GetActiveCaptureBonus01() * 0.25f;
        float scarcity01 = 1f - t;
        float luckBonus  = GetActiveFlyerBonus01() * 0.20f * Mathf.Clamp01(scarcity01 * 1.25f);
        float lureBonus  = 0f;
        var   lure       = CurrentFlyer;
        if (lure != null && lure.type == def.type)
            lureBonus = Mathf.Clamp01(lure.bonus) * 0.10f;
        float streakBonus = Mathf.Clamp01(CurrentWinStreak / 20f) * 0.05f;

        return Mathf.Clamp01(baseChance + bandBonus + luckBonus + lureBonus + streakBonus);
    }

    void TryCatch(MonsterDataSO def, int level) => TryCatchWithResult(def, level, out _);

    /// <summary>
    /// Attempts to capture and returns success/failure.
    /// Also outputs the finalChance used for logging/UI if desired.
    /// </summary>
    bool TryCatchWithResult(MonsterDataSO def, int level, out float finalChance)
    {
        finalChance = 0f;

        if (!def) return false;
        var data = SaveManager.Data;
        var lib = MonsterLibraryLocator.Lib;
        if (data == null || !lib) return false;

        if (def.uncatchable)
        {
            EmitStatus("(Capture skipped — uncatchable.)", LogScope.Rift);
            return false;
        }

        // Base chance from spawn weight → [15%, 65%]
        float minW = float.MaxValue, maxW = 0f;
        for (int i = 0; i < lib.monsters.Length; i++)
        {
            var m = lib.monsters[i];
            if (!m) continue;
            float w = Mathf.Max(0f, m.spawnWeight);
            if (w < minW) minW = w;
            if (w > maxW) maxW = w;
        }
        if (minW == float.MaxValue || maxW <= 0f || minW >= maxW)
        {
            minW = 0f; maxW = 1f;
        }

        float t = Mathf.Clamp01(
            (Mathf.Max(0f, def.spawnWeight) - minW) /
            Mathf.Max(0.0001f, (maxW - minW))
        );
        float baseChance = Mathf.Lerp(0.15f, 0.65f, t);

        float bandBonus = GetActiveCaptureBonus01() * 0.25f;
        float scarcity01 = 1f - t;
        float luckBonus = GetActiveFlyerBonus01() * 0.20f * Mathf.Clamp01(scarcity01 * 1.25f);
        float lureBonus = 0f;
        var lure = CurrentFlyer;
        if (lure != null && lure.type == def.type)
            lureBonus = Mathf.Clamp01(lure.bonus) * 0.10f;
        float streakBonus = Mathf.Clamp01(CurrentWinStreak / 20f) * 0.05f;

        finalChance = Mathf.Clamp01(baseChance + bandBonus + luckBonus + lureBonus + streakBonus);

        float roll = Random.value;
        bool success = roll <= finalChance;

        // ---------------------------------------------------------------------
        // PREMIUM DETERMINATION (FIXED):
        // If the rift was presented as premium, the OwnedMonsterData must store isPremium=true,
        // even if the species is not "premium-flagged" by definition.
        // ---------------------------------------------------------------------

        // Rift-scoped premium presentation (either still active, or captured from battle end)
        bool riftWasPremium = _currentWildIsPremium || _lastWildWasPremium;

        // Legacy/species premium flag OR rift presented premium
        bool isPremium = IsPremiumMonster(def) || riftWasPremium;

        // If no premium art exists, do not mark premium (prevents "premium" with normal visuals).
        if (isPremium && def.premiumIcon == null)
            isPremium = false;

        // Premium cheat
        if (SaveManager.Data != null && SaveManager.Data.forcePremiumCapturesRemaining > 0)
        {
            isPremium = true;

            // still respect art availability
            if (def.premiumIcon == null)
                isPremium = false;
        }

        string captureName = MonsterNameFormatter.Format(def, isPremium);

        FieldOpsTracker.RecordCaptureAttempt(def, success, isPremium);

        if (success)
        {
            AudioManager.I?.PlaySfx(SfxType.CaptureSuccess);

            data.owned ??= new List<OwnedMonsterData>();

            // Policy C: per-variant (premium vs non-premium) we keep ONE owned instance.
            // If the variant already exists, level it up (unless max -> convert into Growth Cores).
            OwnedMonsterData existing = FindBestOwnedVariant(data.owned, def.id, isPremium);
            int maxLevel = GetMaxLevelFor(def);

            if (existing != null)
            {
                // Consume premium cheat only on successful capture (existing behavior)
                if (isPremium && SaveManager.Data != null && SaveManager.Data.forcePremiumCapturesRemaining > 0)
                {
                    SaveManager.Data.forcePremiumCapturesRemaining =
                        Mathf.Max(0, SaveManager.Data.forcePremiumCapturesRemaining - 1);
                }

                // Consume sticky flag after use so it cannot leak.
                _lastWildWasPremium = false;

                bool isMax = existing.level >= maxLevel;

                // In auto-mode, preserve original behavior (auto-train) to avoid
                // interrupting idle play. Manual mode opens the resolution panel.
                if (autoMode)
                {
                    // Auto-mode: apply level-up or convert to cores immediately
                    if (!isMax)
                    {
                        int before = existing.level;
                        ApplyDuplicateCaptureLevelUp(existing, def, DUPLICATE_LEVELUP_STAT_POINTS);
                        SyncOwnedToTeam(existing);

                        SaveManager.Save();
                        GameEvents.OnResourcesChanged?.Invoke();
                        GameEvents.MonsterCaptured?.Invoke(def.id, def.type);

                        string key = !string.IsNullOrEmpty(existing.ownedUID) ? existing.ownedUID : existing.monsterId;
                        GameEvents.MonsterLeveled?.Invoke(key, existing.level);

                        BattleLogger.Log(
                            $"🎉 Duplicate captured! {captureName} leveled up {before} → {existing.level}. [p={Mathf.RoundToInt(finalChance * 100f)}%]",
                            LogScope.Rift
                        );
                        EmitStatus($"Duplicate captured! {captureName} leveled up to Lv {existing.level}.", LogScope.Rift);
                    }
                    else
                    {
                        int cores = CalcDuplicateConversionCores(def, level);
                        if (cores > 0)
                            ResourceManager.I?.Add(ResourceType.GrowthCore, cores);

                        SaveManager.Save();
                        GameEvents.OnResourcesChanged?.Invoke();
                        GameEvents.MonsterCaptured?.Invoke(def.id, def.type);

                        BattleLogger.Log(
                            $"🎉 Duplicate captured, but {captureName} is already max level (Lv {maxLevel}). Converted to +{cores} Growth Cores. [p={Mathf.RoundToInt(finalChance * 100f)}%]",
                            LogScope.Rift
                        );
                        EmitStatus($"Duplicate converted: +{cores} Growth Cores (already Lv {maxLevel}).", LogScope.Rift);
                    }
                }
                else
                {
                    // Manual mode: open the Duplicate Resolution panel
                    PendingDuplicateCapture.Set(existing, def, level, isPremium, isMax);
                    PersistPendingDuplicateDecision(existing, def, level, isPremium, isMax);

                    if (UIManager.I != null)
                        UIManager.I.Show(PanelId.DuplicateResolution);

                    BattleLogger.Log(
                        $"🎉 Duplicate {captureName} captured! Awaiting placement decision. [p={Mathf.RoundToInt(finalChance * 100f)}%]",
                        LogScope.Rift
                    );
                    EmitStatus($"Duplicate captured! Choose how to place {captureName}.", LogScope.Rift);
                }

                // Ensure collection tracking is up to date
                data.ownedIds ??= new HashSet<string>(); data.ownedIds.Add(def.id);
                data.seenTypes ??= new HashSet<MonsterType>(); data.seenTypes.Add(def.type);

                return true;
            }

            // First time for this variant → add a new owned instance.
            int startHP = 1;
            if (def != null)
                startHP = HealingService.CalcMaxHP(def, Mathf.Max(1, level), includeTraining: true, includeTitles: false);
            var om = new OwnedMonsterData
            {
                monsterId = def.id,
                level = Mathf.Max(1, level),
                currentHP = Mathf.Max(0, startHP),
                currentXP = 0,
                ownedUID = Guid.NewGuid().ToString("N"),

                isPremium = isPremium,
                premiumTier = isPremium ? 1 : 0
            };

            // Consume premium cheat only on successful capture (your existing behavior)
            if (isPremium && SaveManager.Data != null && SaveManager.Data.forcePremiumCapturesRemaining > 0)
            {
                SaveManager.Data.forcePremiumCapturesRemaining =
                    Mathf.Max(0, SaveManager.Data.forcePremiumCapturesRemaining - 1);
            }

            // IMPORTANT: consume the "last wild was premium" sticky flag after it is used for capture,
            // so it cannot leak into later capture attempts.
            _lastWildWasPremium = false;

            data.owned.Add(om);

            data.ownedIds ??= new HashSet<string>(); data.ownedIds.Add(def.id);
            data.seenTypes ??= new HashSet<MonsterType>(); data.seenTypes.Add(def.type);

            SaveManager.Save();
            GameEvents.OnResourcesChanged?.Invoke();

            GameEvents.MonsterCaptured?.Invoke(def.id, def.type);

            BattleLogger.Log(
                $"🎉 Capture success! {captureName} (Lv {level}) joined your roster. [p={Mathf.RoundToInt(finalChance * 100f)}%]",
                LogScope.Rift
            );
            EmitStatus($"Captured {captureName}! (Lv {level})", LogScope.Rift);
        }
        else
        {
            BattleLogger.Log(
                $"Capture failed on {captureName} (Lv {level}). [p={Mathf.RoundToInt(finalChance * 100f)}%, roll={Mathf.RoundToInt(roll * 100f)}%]",
                LogScope.Rift
            );
            EmitStatus($"Capture failed. {captureName} escaped.", LogScope.Rift);
        }

        return success;
    }

    private static void PersistPendingDuplicateDecision(OwnedMonsterData existing, MonsterDataSO def, int level, bool isPremium, bool isMax)
    {
        try
        {
            var save = SaveManager.GetExchangeBlob() ?? new ExchangeSaveData();
            save.pendingDuplicate = new PendingDuplicateCaptureSave
            {
                ownedUID = existing != null ? existing.ownedUID : null,
                speciesId = def != null ? def.id : null,
                riftLevel = Mathf.Max(1, level),
                isPremium = isPremium,
                isMaxLevel = isMax
            };
            SaveManager.SetExchangeBlob(save);
        }
        catch { }
    }

    // ── Duplicate capture helpers (Policy C) ───────────────────────────────────

    private static OwnedMonsterData FindBestOwnedVariant(List<OwnedMonsterData> owned, string monsterId, bool wantPremium)
    {
        if (owned == null || owned.Count == 0 || string.IsNullOrEmpty(monsterId)) return null;

        OwnedMonsterData best = null;
        for (int i = 0; i < owned.Count; i++)
        {
            var o = owned[i];
            if (o == null) continue;
            if (!string.Equals(o.monsterId, monsterId, StringComparison.Ordinal)) continue;
            if (o.isPremium != wantPremium) continue;

            if (best == null)
            {
                best = o;
                continue;
            }

            // Prefer higher level; tie-break by premium tier then by stable UID.
            if (o.level > best.level) best = o;
            else if (o.level == best.level)
            {
                if (o.premiumTier > best.premiumTier) best = o;
                else if (o.premiumTier == best.premiumTier)
                {
                    // Deterministic ordering (prevents flicker across sessions)
                    string a = o.ownedUID ?? "";
                    string b = best.ownedUID ?? "";
                    if (string.CompareOrdinal(a, b) < 0) best = o;
                }
            }
        }
        return best;
    }

    private static int GetMaxLevelFor(MonsterDataSO def)
    {
        int byDef = def != null ? Mathf.Max(1, def.maxLevel) : LevelRules.MaxLevel;
        return Mathf.Clamp(byDef, 1, LevelRules.MaxLevel);
    }

    private static void ApplyDuplicateCaptureLevelUp(OwnedMonsterData target, MonsterDataSO def, int pointsPerLevel)
    {
        if (target == null) return;

        // Level up
        target.level = Mathf.Max(1, target.level + 1);
        target.unspentStatPoints += Mathf.Max(0, pointsPerLevel);

        // Defensive: keep premium identity consistent
        if (target.isPremium)
            target.premiumTier = Mathf.Max(1, target.premiumTier);
        else
            target.premiumTier = 0;

        // Clamp HP to new max (baseline HP grows with level).
        if (def != null)
        {
            int totalMaxHP = HealingService.CalcMaxHP(def, target.level, includeTraining: true, includeTitles: false);
            if (target.currentHP > totalMaxHP)
                // Centralized HP contract: clamp without stamping timers.
                SaveManager.SetMonsterHP(target, target.currentHP, stampLastHpUnix: false, save: false, fireEvents: false);
        }
    }

    private static void SyncOwnedToTeam(OwnedMonsterData owned)
    {
        var data = SaveManager.Data;
        if (data == null || owned == null) return;
        if (data.team == null || data.team.Count == 0) return;

        // Prefer ownedUID (canonical); fall back to monsterId when needed.
        for (int i = 0; i < data.team.Count; i++)
        {
            var t = data.team[i];
            if (t == null) continue;

            bool match = false;
            if (!string.IsNullOrEmpty(owned.ownedUID) && !string.IsNullOrEmpty(t.ownedUID))
                match = string.Equals(t.ownedUID, owned.ownedUID, StringComparison.Ordinal);
            else if (!string.IsNullOrEmpty(owned.monsterId))
                match = string.Equals(t.monsterId, owned.monsterId, StringComparison.Ordinal) && t.isPremium == owned.isPremium;

            if (!match) continue;

            // Mirror key gameplay fields (keep this conservative)
            t.level = owned.level;
            t.currentXP = owned.currentXP;
            // Centralized HP contract: mirror HP + timestamp (remainder-accurate)
            SaveManager.SetTeamSlotHPExact(i, owned.currentHP, owned.lastHPUnix, save: false, fireEvents: false);
            t.flatAtkBonus = owned.flatAtkBonus;
            t.isTraining = owned.isTraining;
            t.trainingLastUnix = owned.trainingLastUnix;
            t.pendingLevels = owned.pendingLevels;
            t.lastLevelClaimDay = owned.lastLevelClaimDay;
            t.isPremium = owned.isPremium;
            t.premiumTier = owned.premiumTier;
            t.trainingBonus = owned.trainingBonus;
            t.autoApply = owned.autoApply;
            t.autoApplyTargetLevel = owned.autoApplyTargetLevel;
            t.lastBucketId = owned.lastBucketId;
            t.unspentStatPoints = owned.unspentStatPoints;
        }
    }

    private static int CalcDuplicateConversionCores(MonsterDataSO def, int riftLevel)
    {
        if (def == null) return 0;

        // Conversion is a consolation reward (not a full "level cost" refund).
        int baseCores = Mathf.Max(1, 2 + Mathf.Max(1, riftLevel));

        float rarityMul = 1f;
        switch (def.rarity)
        {
            case Rarity.Common:    rarityMul = 1.00f; break;
            case Rarity.Uncommon:  rarityMul = 1.10f; break;
            case Rarity.Rare:      rarityMul = 1.25f; break;
            case Rarity.Epic:      rarityMul = 1.40f; break;
            case Rarity.Legendary: rarityMul = 1.60f; break;
            case Rarity.Mythic:    rarityMul = 1.80f; break;
            default:               rarityMul = 1.00f; break;
        }

        int cores = Mathf.RoundToInt(baseCores * rarityMul);
        return Mathf.Clamp(cores, 1, 250);
    }

    // ── Premium / Unique helpers ─────────────────────────────────────────────────

    private bool IsPremiumMonster(MonsterDataSO m)
    {
        if (!m) return false;

        try
        {
            var t = m.GetType();

            var f = t.GetField("isPremium",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (f != null)
            {
                var val = f.GetValue(m);
                if (val is bool b) return b;
            }

            var p = t.GetProperty("isPremium",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (p != null && p.CanRead)
            {
                var val = p.GetValue(m, null);
                if (val is bool b) return b;
            }
        }
        catch { }

        return false;
    }

    /// <summary>
    /// Unique rifts = Legendary or Mythic monsters.
    /// We detect this by reading a "rarity" field/property and comparing its name,
    /// plus legacy boolean flags (isUniqueRift / isUnique) as fallback.
    /// </summary>
    private bool IsUniqueMonster(MonsterDataSO m)
    {
        if (!m) return false;

        try
        {
            var t = m.GetType();

            // 1) Check rarity enum/string
            object rarityObj = null;

            var fR = t.GetField("rarity",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (fR != null) rarityObj = fR.GetValue(m);

            var pR = t.GetProperty("rarity",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (rarityObj == null && pR != null && pR.CanRead)
                rarityObj = pR.GetValue(m, null);

            if (rarityObj != null)
            {
                string name = rarityObj.ToString();
                if (!string.IsNullOrEmpty(name))
                {
                    name = name.ToLowerInvariant();
                    if (name.Contains("legend") || name.Contains("myth"))
                        return true;
                }
            }

            var fU = t.GetField("isUniqueRift",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (fU != null)
            {
                var val = fU.GetValue(m);
                if (val is bool b && b) return true;
            }

            var fU2 = t.GetField("isUnique",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (fU2 != null)
            {
                var val = fU2.GetValue(m);
                if (val is bool b && b) return true;
            }

            var pU = t.GetProperty("isUniqueRift",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (pU != null && pU.CanRead)
            {
                var val = pU.GetValue(m, null);
                if (val is bool b && b) return true;
            }

            var pU2 = t.GetProperty("isUnique",
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);
            if (pU2 != null && pU2.CanRead)
            {
                var val = pU2.GetValue(m, null);
                if (val is bool b && b) return true;
            }
        }
        catch { }

        return false;
    }
}
