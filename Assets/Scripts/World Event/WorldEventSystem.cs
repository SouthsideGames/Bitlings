using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Weekly World Events (Monday → Sunday, local device time).
///
/// Design goals:
/// - Feature-gated (WorldEvents_Basic). If locked: no ticker, no effects.
/// - Exactly 1 rolled event per week (unless you later enable scheduled overlays).
/// - Weighted category distribution (Job/Rift/Meta/Flavor).
/// - First week after unlock is forced to Flavor.
///
/// Provides a single query surface for Jobs/Rifts/Economy to consume effects.
/// </summary>
public sealed class WorldEventSystem : MonoBehaviour
{
    public static WorldEventSystem I { get; private set; }

    // -------------------------------------------------------------------------
    // Iron Career safety
    // -------------------------------------------------------------------------
    // During Iron Career we allow world events to continue ticking internally,
    // but we suppress global UI broadcasts that would wake non-Iron systems.
    private void FireWorldEventsChanged()
    {
        if (IronCareerRuntime.IsActive) return;
        GameEvents.WorldEventsChanged?.Invoke();
    }

    [Header("Feature Unlock")]
    [SerializeField] private FeatureId requiredFeature = FeatureId.WorldEvents_Basic;

    [Header("Library")]
    [Tooltip("Optional. If null, will try Resources.Load<WorldEventLibrarySO>(\"WorldEvents/WorldEventLibrary\").")]
    [SerializeField] private WorldEventLibrarySO library;

    [Header("Weekly")]
    [Tooltip("How often to re-check for week rollover / scheduled windows.")]
    [SerializeField, Min(1f)] private float refreshCheckSeconds = 5f;

    [Header("Deterministic Rotation")]
    [Tooltip("When enabled the active event is chosen deterministically from the date, not by random roll. " +
             "The event pool is all canRotate=true events (excluding scheduledOnly), ordered by list index. " +
             "Week index = weeks elapsed since epoch date, modulo pool size.")]
    [SerializeField] private bool useDeterministicRotation = false;

    [Tooltip("ISO date (YYYY-MM-DD) of the Monday that acts as week 0 for deterministic rotation. " +
             "Defaults to 2025-01-06 if left blank or invalid.")]
    [SerializeField] private string deterministicEpochDate = "2025-01-06";

    [Header("Category Weights (must sum to 1.0 in your design, but we normalize anyway)")]
    [Range(0f, 1f)] public float weightJob = 0.20f;
    [Range(0f, 1f)] public float weightRift = 0.20f;
    [Range(0f, 1f)] public float weightMeta = 0.20f;
    [Range(0f, 1f)] public float weightFlavor = 0.40f;

    // Active
    private readonly List<WorldEventSO> _active = new();

    // Cached effects
    private readonly HashSet<JobType> _disabledJobs = new();
    private readonly Dictionary<JobType, float> _jobRateMul = new();
    private readonly Dictionary<JobType, float> _jobStorageCapMul = new();
    private readonly HashSet<JobType> _jobCollectDisabled = new();
    private readonly Dictionary<JobType, float> _jobFatigueMul = new();

    private bool _riftsDisabled;
    private float _riftEnergyCostMul = 1f;
    private float _wildPremiumMul = 1f;
    private float _bossCadenceMul = 1f;

    private float _shopPriceMul = 1f;
    private readonly Dictionary<ResourceType, float> _resourceGainMul = new();

    private float _exchangeDemandMul = 1f;
    private float _exchangeValueMul = 1f;

    private float _idleRewardMul = 1f;
    private float _battleRewardMul = 1f;
    private MonsterType _boostedMonsterType = MonsterType.None;
    private float _typeDamageMul = 1f;

    private float _accum;

    // Execution-order safety: FeatureUnlockManager may initialize after this system.
    private FeatureUnlockManager _featureMgr;
    private bool _featureMgrHooked;
    private bool _wasFeatureActive;

    public IReadOnlyList<WorldEventSO> ActiveEvents => _active;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        EnsureWorldEventManagerExists();


        if (!library)
            library = Resources.Load<WorldEventLibrarySO>("WorldEvents/WorldEventLibrary");

        if (SaveManager.Data == null) SaveManager.LoadOrCreate();

        // Try to hook the feature manager now, but also keep trying in Update.
        TryHookFeatureManager();
        _wasFeatureActive = IsFeatureActive();

        RefreshNow(forceRollIfNeeded: true);
    }

    private void EnsureWorldEventManagerExists()
    {
        if (WorldEventManager.I != null) return;

        var existing = FindFirstObjectByType<WorldEventManager>();
        if (existing != null) return;

        var go = new GameObject("WorldEventManager");
        go.AddComponent<WorldEventManager>();
    }

    private void OnDestroy()
    {
        UnhookFeatureManager();

        if (I == this) I = null;
    }

    private void Update()
    {
        // Late-bind FeatureUnlockManager if it wasn't ready during Awake.
        TryHookFeatureManager();

        // If we were previously locked due to missing FeatureUnlockManager, but it becomes
        // available (and the feature is already unlocked), we need to refresh once so UI appears.
        bool activeNow = IsFeatureActive();
        if (activeNow && !_wasFeatureActive)
        {
            _wasFeatureActive = true;
            RefreshNow(forceRollIfNeeded: true);
        }
        else
        {
            _wasFeatureActive = activeNow;
        }

        _accum += Time.unscaledDeltaTime;
        if (_accum < refreshCheckSeconds) return;
        _accum = 0f;

        RefreshNow(forceRollIfNeeded: false);
    }

    private void TryHookFeatureManager()
    {
        // If a different instance becomes active, re-hook.
        var mgr = FeatureUnlockManager.I;
        if (mgr == null) return;

        if (_featureMgr == mgr && _featureMgrHooked) return;

        UnhookFeatureManager();

        _featureMgr = mgr;
        _featureMgr.OnFeatureUnlocked += HandleFeatureUnlocked;
        _featureMgrHooked = true;
    }

    private void UnhookFeatureManager()
    {
        if (_featureMgrHooked && _featureMgr != null)
            _featureMgr.OnFeatureUnlocked -= HandleFeatureUnlocked;

        _featureMgrHooked = false;
        _featureMgr = null;
    }

    private void HandleFeatureUnlocked(FeatureId id)
    {
        if (id != requiredFeature) return;

        // Force a refresh immediately when unlocked.
        RefreshNow(forceRollIfNeeded: true);
    }

    // ─────────────────────────────────────────────────────────────
    // Feature gate
    // ─────────────────────────────────────────────────────────────

    public bool IsFeatureActive()
    {
        if (requiredFeature == FeatureId.None) return true;
        return FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(requiredFeature);
    }

    // ─────────────────────────────────────────────────────────────
    // Public queries (Jobs / Rifts / Economy)
    // ─────────────────────────────────────────────────────────────

    public bool IsJobSiteDisabled(JobType job)
        => IsFeatureActive() && _disabledJobs.Contains(job);

    public float GetJobRateMultiplier(JobType job)
    {
        if (!IsFeatureActive()) return 1f;
        return _jobRateMul.TryGetValue(job, out var m) ? Mathf.Max(0f, m) : 1f;
    }

    public float GetJobStorageCapMultiplier(JobType job)
    {
        if (!IsFeatureActive()) return 1f;
        return _jobStorageCapMul.TryGetValue(job, out var m) ? Mathf.Max(0f, m) : 1f;
    }

    public bool IsJobCollectDisabled(JobType job)
        => IsFeatureActive() && _jobCollectDisabled.Contains(job);

    public float GetJobFatigueRateMultiplier(JobType job)
    {
        if (!IsFeatureActive()) return 1f;
        return _jobFatigueMul.TryGetValue(job, out var m) ? Mathf.Max(0f, m) : 1f;
    }

    public bool AreRiftsDisabled() => IsFeatureActive() && _riftsDisabled;

    public float GetRiftEnergyCostMultiplier()
        => IsFeatureActive() ? Mathf.Max(0f, _riftEnergyCostMul) : 1f;

    public float GetWildPremiumChanceMultiplier()
        => IsFeatureActive() ? Mathf.Max(0f, _wildPremiumMul) : 1f;

    public float GetBossCadenceMultiplier()
        => IsFeatureActive() ? Mathf.Max(0.05f, _bossCadenceMul) : 1f;

    public float GetShopPriceMultiplier()
        => IsFeatureActive() ? Mathf.Max(0f, _shopPriceMul) : 1f;

    public float GetResourceGainMultiplier(ResourceType type)
    {
        if (!IsFeatureActive()) return 1f;
        return _resourceGainMul.TryGetValue(type, out var m) ? Mathf.Max(0f, m) : 1f;
    }

    public float GetExchangeDemandMultiplier()
        => IsFeatureActive() ? Mathf.Max(0f, _exchangeDemandMul) : 1f;

    public float GetExchangeValueMultiplier()
        => IsFeatureActive() ? Mathf.Max(0f, _exchangeValueMul) : 1f;

    public float GetIdleRewardMultiplier()
        => IsFeatureActive() ? Mathf.Max(0f, _idleRewardMul) : 1f;

    public float GetBattleRewardMultiplier()
        => IsFeatureActive() ? Mathf.Max(0f, _battleRewardMul) : 1f;

    /// <summary>Returns the boosted monster type this week, or MonsterType.None if none.</summary>
    public MonsterType GetBoostedMonsterType()
        => IsFeatureActive() ? _boostedMonsterType : MonsterType.None;

    /// <summary>
    /// Damage multiplier that applies to the boosted monster type this week.
    /// Returns 1f if no type boost is active.
    /// </summary>
    public float GetTypeDamageMultiplier()
        => IsFeatureActive() && _boostedMonsterType != MonsterType.None ? Mathf.Max(0f, _typeDamageMul) : 1f;

    // ─────────────────────────────────────────────────────────────
    // Core refresh
    // ─────────────────────────────────────────────────────────────

    public void RefreshNow(bool forceRollIfNeeded)
    {
        // If locked: no active events, no effects, no ticker.
        if (!IsFeatureActive())
        {
            _active.Clear();
            RebuildEffectCache();
            if (WorldEventManager.I != null) WorldEventManager.I.Clear();
            FireWorldEventsChanged();
            return;
        }

        long nowUtc = SaveManager.NowUnix();
        long weekStartUnixLocal = GetLocalWeekStartUnix(DateTimeOffset.Now);

        _active.Clear();

        var all = GetAllEvents();

        // 1) Optional scheduled overlays (keep support; not required for your current design)
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (!e) continue;
            if (!e.scheduledOnly) continue;
            if (e.IsActiveNow(nowUtc)) _active.Add(e);
        }

        // 2) Weekly event (exactly 1) — only if no scheduled events are active.
        if (_active.Count == 0)
        {
            if (useDeterministicRotation)
            {
                // Deterministic path: event is derived from the current week index.
                // No save-data mutation needed — the result is always computable from the date.
                var det = GetDeterministicWeekEvent(all);
                if (det) _active.Add(det);
            }
            else
            {
                // Random roll path (original behaviour).
                var blob = SaveManager.GetWorldEventsBlob() ?? new WorldEventSaveData();

                bool needsWeekRoll = string.IsNullOrEmpty(blob.weeklyActiveEventId)
                                     || blob.weeklyWeekStartUnix != weekStartUnixLocal;

                // IMPORTANT:
                // We only want *one* rolled event per week.
                // `forceRollIfNeeded` exists to cover startup / unlock timing (when systems init out of order),
                // but it must NOT re-roll if we already have a valid event for the current week.
                bool shouldRoll = needsWeekRoll
                                  || (forceRollIfNeeded && string.IsNullOrEmpty(blob.weeklyActiveEventId));

                if (shouldRoll)
                {
                    // First week after unlock must be Flavor.
                    bool forceFlavor = !blob.firstUnlockFlavorConsumed;

                    RollWeeklyEvent(blob, all, nowUtc, forceFlavor);

                    // Mark first-week flavor as consumed once we successfully pick one.
                    if (!string.IsNullOrEmpty(blob.weeklyActiveEventId))
                        blob.firstUnlockFlavorConsumed = true;

                    blob.weeklyWeekStartUnix = weekStartUnixLocal;
                    SaveManager.SetWorldEventsBlob(blob);
                }

                if (!string.IsNullOrEmpty(blob.weeklyActiveEventId))
                {
                    var e = FindById(all, blob.weeklyActiveEventId);
                    if (e) _active.Add(e);
                }
            }
        }

        // 3) Apply effects + ticker
        RebuildEffectCache();
        PushTicker();

        FireWorldEventsChanged();
    }

    // ─────────────────────────────────────────────────────────────
    // Deterministic rotation
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the event for the current week based purely on date math.
    /// Pool = all canRotate=true, non-scheduledOnly events, in library list order.
    /// weekIndex = floor((nowUnix - epochUnix) / 604800), clamped ≥ 0.
    /// activeEvent = pool[weekIndex % pool.Count]
    /// </summary>
    private WorldEventSO GetDeterministicWeekEvent(List<WorldEventSO> all)
    {
        var pool = new List<WorldEventSO>(all.Count);
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (!e || e.scheduledOnly || !e.canRotate) continue;
            pool.Add(e);
        }

        if (pool.Count == 0) return null;

        // Parse epoch. Fall back to 2025-01-06 (a known Monday) if blank / invalid.
        DateTimeOffset epoch;
        if (string.IsNullOrWhiteSpace(deterministicEpochDate) ||
            !DateTimeOffset.TryParse(deterministicEpochDate, out epoch))
        {
            epoch = new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero);
        }

        long epochUnix = epoch.ToUnixTimeSeconds();
        long nowUnix = SaveManager.NowUnix();

        long secondsSinceEpoch = nowUnix - epochUnix;
        int weekIndex = secondsSinceEpoch >= 0
            ? (int)(secondsSinceEpoch / 604800L)
            : 0;

        return pool[weekIndex % pool.Count];
    }

    private void RollWeeklyEvent(WorldEventSaveData blob, List<WorldEventSO> all, long nowUtc, bool forceFlavor)
    {
        // Category selection first (distribution control).
        WorldEventCategory chosenCat = forceFlavor
            ? WorldEventCategory.Flavor
            : RollCategory();

        // Candidate list for chosen category.
        var candidates = new List<WorldEventSO>(64);
        int weightSum = 0;

        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (!e) continue;
            if (e.scheduledOnly) continue;
            if (!e.canRotate) continue;
            if (e.weight <= 0) continue;
            if (e.category != chosenCat) continue;

            if (e.minDaysBetween > 0f && WasRolledTooRecently(blob, e, nowUtc))
                continue;

            candidates.Add(e);
            weightSum += e.weight;
        }

        // Fallbacks:
        // 1) If we rolled a non-Flavor category and it's empty, try Flavor.
        // 2) If we *forced* Flavor (first week after unlock) but there are no Flavor events authored yet,
        //    fall back to ANY rotatable event so the ticker still appears.
        if (candidates.Count == 0)
        {
            if (chosenCat != WorldEventCategory.Flavor)
            {
                chosenCat = WorldEventCategory.Flavor;
                for (int i = 0; i < all.Count; i++)
                {
                    var e = all[i];
                    if (!e) continue;
                    if (e.scheduledOnly) continue;
                    if (!e.canRotate) continue;
                    if (e.weight <= 0) continue;
                    if (e.category != chosenCat) continue;

                    if (e.minDaysBetween > 0f && WasRolledTooRecently(blob, e, nowUtc))
                        continue;

                    candidates.Add(e);
                    weightSum += e.weight;
                }
            }
            else
            {
                for (int i = 0; i < all.Count; i++)
                {
                    var e = all[i];
                    if (!e) continue;
                    if (e.scheduledOnly) continue;
                    if (!e.canRotate) continue;
                    if (e.weight <= 0) continue;

                    if (e.minDaysBetween > 0f && WasRolledTooRecently(blob, e, nowUtc))
                        continue;

                    candidates.Add(e);
                    weightSum += e.weight;
                }
            }
        }

        if (candidates.Count == 0 || weightSum <= 0)
        {
            // Ultimate fallback: clear.
            blob.weeklyActiveEventId = null;
            return;
        }

        int roll = Random.Range(0, weightSum);
        WorldEventSO picked = null;
        int running = 0;
        for (int i = 0; i < candidates.Count; i++)
        {
            running += candidates[i].weight;
            if (roll < running) { picked = candidates[i]; break; }
        }

        if (!picked) picked = candidates[0];

        blob.weeklyActiveEventId = picked.id;
        StampRolled(blob, picked.id, nowUtc);
    }

    private WorldEventCategory RollCategory()
    {
        float j = Mathf.Max(0f, weightJob);
        float e = Mathf.Max(0f, weightRift);
        float m = Mathf.Max(0f, weightMeta);
        float f = Mathf.Max(0f, weightFlavor);

        float sum = j + e + m + f;
        if (sum <= 0f) return WorldEventCategory.Flavor;

        float r = Random.value * sum;
        if (r < j) return WorldEventCategory.Job;
        r -= j;
        if (r < e) return WorldEventCategory.Rift;
        r -= e;
        if (r < m) return WorldEventCategory.Meta;
        return WorldEventCategory.Flavor;
    }

    private void RebuildEffectCache()
    {
        _disabledJobs.Clear();
        _jobRateMul.Clear();
        _jobStorageCapMul.Clear();
        _jobCollectDisabled.Clear();
        _jobFatigueMul.Clear();

        _riftsDisabled = false;
        _riftEnergyCostMul = 1f;
        _wildPremiumMul = 1f;
        _bossCadenceMul = 1f;

        _shopPriceMul = 1f;
        _resourceGainMul.Clear();

        _exchangeDemandMul = 1f;
        _exchangeValueMul = 1f;

        _idleRewardMul = 1f;
        _battleRewardMul = 1f;
        _boostedMonsterType = MonsterType.None;
        _typeDamageMul = 1f;

        for (int i = 0; i < _active.Count; i++)
        {
            var e = _active[i];
            if (!e || e.effects == null) continue;

            for (int j = 0; j < e.effects.Count; j++)
            {
                var fx = e.effects[j];
                float v = (Mathf.Approximately(fx.value, 0f) ? 1f : fx.value);

                switch (fx.kind)
                {
                    // Jobs
                    case WorldEventEffectKind.DisableJobSite:
                        if (fx.job != JobType.None) _disabledJobs.Add(fx.job);
                        break;

                    case WorldEventEffectKind.JobRateMultiplier:
                        if (fx.job != JobType.None)
                            _jobRateMul[fx.job] = _jobRateMul.TryGetValue(fx.job, out var cur) ? (cur * v) : v;
                        break;

                    case WorldEventEffectKind.JobStorageCapMultiplier:
                        if (fx.job != JobType.None)
                            _jobStorageCapMul[fx.job] = _jobStorageCapMul.TryGetValue(fx.job, out var curCap) ? (curCap * v) : v;
                        break;

                    case WorldEventEffectKind.JobCollectDisabled:
                        if (fx.job != JobType.None)
                        {
                            bool on = fx.flag || (Mathf.Approximately(fx.value, 0f) ? true : (fx.value > 0f));
                            if (on) _jobCollectDisabled.Add(fx.job);
                        }
                        break;

                    case WorldEventEffectKind.JobFatigueRateMultiplier:
                        if (fx.job != JobType.None)
                            _jobFatigueMul[fx.job] = _jobFatigueMul.TryGetValue(fx.job, out var curFat) ? (curFat * v) : v;
                        break;

                    // Rifts
                    case WorldEventEffectKind.DisableRifts:
                        _riftsDisabled = true;
                        break;

                    case WorldEventEffectKind.RiftEnergyCostMultiplier:
                        _riftEnergyCostMul *= v;
                        break;

                    case WorldEventEffectKind.WildPremiumChanceMultiplier:
                        _wildPremiumMul *= v;
                        break;

                    case WorldEventEffectKind.BossCadenceMultiplier:
                        _bossCadenceMul *= v;
                        break;

                    // Meta / Economy
                    case WorldEventEffectKind.ShopPriceMultiplier:
                        _shopPriceMul *= v;
                        break;

                    case WorldEventEffectKind.ResourceGainMultiplier:
                        if (fx.resource != ResourceType.None)
                            _resourceGainMul[fx.resource] = _resourceGainMul.TryGetValue(fx.resource, out var curRes) ? (curRes * v) : v;
                        break;

                    // Exchange
                    case WorldEventEffectKind.ExchangeDemandMultiplier:
                        _exchangeDemandMul *= v;
                        break;

                    case WorldEventEffectKind.ExchangeValueMultiplier:
                        _exchangeValueMul *= v;
                        break;

                    // Idle / Battle rewards
                    case WorldEventEffectKind.IdleRewardMultiplier:
                        _idleRewardMul *= v;
                        break;

                    case WorldEventEffectKind.BattleRewardMultiplier:
                        _battleRewardMul *= v;
                        break;

                    // Monster type boost
                    case WorldEventEffectKind.BoostedMonsterType:
                        if (fx.monsterType != MonsterType.None)
                            _boostedMonsterType = fx.monsterType;
                        break;

                    case WorldEventEffectKind.TypeDamageMultiplier:
                        _typeDamageMul *= v;
                        break;
                }
            }

            // Flat modifier fields — compose with any matching effects above.
            if (!Mathf.Approximately(e.idleRewardMultiplier, 1f))
                _idleRewardMul *= Mathf.Max(0f, e.idleRewardMultiplier);

            if (!Mathf.Approximately(e.battleRewardMultiplier, 1f))
                _battleRewardMul *= Mathf.Max(0f, e.battleRewardMultiplier);

            if (!Mathf.Approximately(e.exchangeValueMultiplier, 1f))
                _exchangeValueMul *= Mathf.Max(0f, e.exchangeValueMultiplier);

            if (e.boostedMonsterType != MonsterType.None)
            {
                _boostedMonsterType = e.boostedMonsterType;
                if (!Mathf.Approximately(e.typeDamageMultiplier, 1f))
                    _typeDamageMul *= Mathf.Max(0f, e.typeDamageMultiplier);
            }
        }
    }

    private void PushTicker()
    {
        if (WorldEventManager.I == null) return;

        WorldEventManager.I.Clear();

        for (int i = 0; i < _active.Count; i++)
        {
            var e = _active[i];
            if (!e) continue;

            string msg = !string.IsNullOrWhiteSpace(e.tickerMessage)
                ? e.tickerMessage
                : (!string.IsNullOrWhiteSpace(e.displayName) ? e.displayName : e.id);

            if (string.IsNullOrWhiteSpace(msg)) continue;

            bool hasEffect = e.HasAnyModifier ||
                             (e.effects != null && e.effects.Count > 0 &&
                              e.effects.Exists(fx => fx.kind != WorldEventEffectKind.None));
            WorldEventManager.I.Add(msg, hasEffect: hasEffect);
        }
    }

    private List<WorldEventSO> GetAllEvents()
    {
        if (library != null && library.events != null && library.events.Count > 0)
            return library.events;

        // If you haven't authored assets yet, keep system alive.
        return BuiltInFallbackEvents.Get();
    }

    private static WorldEventSO FindById(List<WorldEventSO> all, string id)
    {
        if (string.IsNullOrEmpty(id) || all == null) return null;
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (!e) continue;
            if (string.Equals(e.id, id, StringComparison.Ordinal)) return e;
        }
        return null;
    }

    private static bool WasRolledTooRecently(WorldEventSaveData blob, WorldEventSO e, long now)
    {
        if (blob == null || blob.cooldowns == null) return false;
        for (int i = 0; i < blob.cooldowns.Count; i++)
        {
            var c = blob.cooldowns[i];
            if (c == null) continue;
            if (!string.Equals(c.id, e.id, StringComparison.Ordinal)) continue;

            long min = (long)Mathf.RoundToInt(e.minDaysBetween * 86400f);
            return c.lastRolledUnix > 0 && (now - c.lastRolledUnix) < min;
        }
        return false;
    }

    private static void StampRolled(WorldEventSaveData blob, string id, long now)
    {
        blob.cooldowns ??= new List<WorldEventRollCooldown>();

        for (int i = 0; i < blob.cooldowns.Count; i++)
        {
            var c = blob.cooldowns[i];
            if (c == null) continue;
            if (string.Equals(c.id, id, StringComparison.Ordinal))
            {
                c.lastRolledUnix = now;
                blob.cooldowns[i] = c;
                return;
            }
        }

        blob.cooldowns.Add(new WorldEventRollCooldown { id = id, lastRolledUnix = now });
    }

    // Monday 00:00 local time → unix seconds
    public static long GetLocalWeekStartUnix(DateTimeOffset localNow)
    {
        // Convert Sunday(0) to 7 for easier math? We'll use modulo diff.
        int dow = (int)localNow.DayOfWeek; // Sunday=0
        int monday = (int)DayOfWeek.Monday; // 1
        int diff = (7 + dow - monday) % 7;

        var startDate = localNow.Date.AddDays(-diff); // DateTime
        var start = new DateTimeOffset(startDate.Year, startDate.Month, startDate.Day, 0, 0, 0, localNow.Offset);
        return start.ToUnixTimeSeconds();
    }
}
