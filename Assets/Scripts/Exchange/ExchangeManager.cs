using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
// ExchangeManager — runtime market value calculator for the
// Bitling Labor Exchange. Recalculates species values based on
// demand, world events, rarity, supply, and daily fluctuation.
// ─────────────────────────────────────────────────────────────

public sealed class ExchangeManager : MonoBehaviour
{
    public static ExchangeManager I { get; private set; }

    private const float BROKER_CUT_DEFAULT = 0.85f;  // base: broker keeps 15%
    private const float BROKER_CUT_T1 = 0.90f;        // tier 1: broker keeps 10%
    private const float BROKER_CUT_T2 = 0.95f;        // tier 2: broker keeps 5%
    private const float PREMIUM_DIVISOR_DEFAULT = 0.75f;
    private const float PREMIUM_DIVISOR_APPRAISED = 0.50f;
    private const float MONOPOLY_BONUS = 1.25f;
    private const float DIVIDEND_RATE = 0.01f; // 1% collected weekly on Monday
    private const int SENTIMENT_CAP = 12;
    private const int SENTIMENT_STEP_WIN = 1;
    private const int SENTIMENT_STEP_LOSS = 3;
    private const float SENTIMENT_MIN_MUL = 0.80f;
    private const float SENTIMENT_MAX_MUL = 1.20f;
    private const float LABOR_SAMPLE_INTERVAL = 30f;
    private const float LABOR_HOURS_CAP = 40f;
    private const float LABOR_MIN_MUL = 0.95f;
    private const float LABOR_MAX_MUL = 1.05f;
    private const float RECALC_INTERVAL = 600f;       // 10 minutes
    private const int   SECONDS_PER_DAY = 86400;

    // ── New market multiplier constants ──
    private const float LEVEL_BOOST_PER_LEVEL = 0.02f; // +2% per level above 1
    private const float LEVEL_BOOST_MAX = 0.50f;       // cap at +50%
    private const float CATCH_HYPE_BONUS = 1.20f;      // +20% freshly caught
    private const long  CATCH_HYPE_DURATION = 86400;    // 24 hours in seconds
    private const float HOT_TYPE_BONUS = 1.15f;         // +15% for trending type
    private const float SCARCITY_PER_BROKER = 0.05f;    // +5% per broker this week
    private const float SCARCITY_MAX = 0.30f;            // cap at +30%
    private const int   HOT_TYPE_COUNT = 16;             // MonsterType values 1..16
    private const float MAX_VALUE_MULTIPLIER = 12f;      // hard ceiling: final value ≤ base × 12

    private ExchangeSaveData _save;
    private Dictionary<string, MarketSpeciesState> _stateMap;
    private Dictionary<string, SpeciesBattleSentimentData> _sentimentMap;
    private readonly Dictionary<string, float> _workerHoursSampled = new Dictionary<string, float>(StringComparer.Ordinal);
    private readonly List<string> _staleKeys = new List<string>();
    private readonly HashSet<string> _activeKeysReuse = new HashSet<string>(StringComparer.Ordinal);
    private Dictionary<string, DemandOverride> _overrideMap;
    private Dictionary<string, int> _bullTokenUseMap;
    private Dictionary<string, int> _bearTokenUseMap;
    private Dictionary<string, long> _catchHypeMap;
    private Dictionary<string, int> _brokerScarcityMap;
    private HashSet<string> _surgeAlertWatchlist;
    private float _recalcTimer;
    private float _laborSampleTimer;

    // ─────────── Demand multipliers ───────────
    private static float DemandMul(DemandLevel d)
    {
        switch (d)
        {
            case DemandLevel.Low:    return 0.70f;
            case DemandLevel.Medium: return 1.00f;
            case DemandLevel.High:   return 1.40f;
            case DemandLevel.Surge:  return 1.80f;
            default:                 return 1.00f;
        }
    }

    private static float RarityMul(Rarity r)
    {
        switch (r)
        {
            case Rarity.Common:    return 1.00f;
            case Rarity.Uncommon:  return 1.10f;
            case Rarity.Rare:      return 1.25f;
            case Rarity.Epic:      return 1.40f;
            case Rarity.Legendary: return 1.60f;
            case Rarity.Mythic:    return 1.80f;
            default:               return 1.00f;
        }
    }

    // ─────────── Lifecycle ───────────

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void Start()
    {
        LoadFromSave();
        CatchUpOffline();
        RecalculateAll();
    }

    void OnEnable()
    {
        GameEvents.WorldEventsChanged += OnWorldEventsChanged;
        GameEvents.OnOwnedMonstersChanged += OnOwnedChanged;
        GameEvents.MonsterCaptured += OnMonsterCaptured;
        GameEvents.MonsterBrokered += OnMonsterBrokered;
        GameEvents.RequestFulfilled += OnRequestFulfilled;
    }

    void OnDisable()
    {
        GameEvents.WorldEventsChanged -= OnWorldEventsChanged;
        GameEvents.OnOwnedMonstersChanged -= OnOwnedChanged;
        GameEvents.MonsterCaptured -= OnMonsterCaptured;
        GameEvents.MonsterBrokered -= OnMonsterBrokered;
        GameEvents.RequestFulfilled -= OnRequestFulfilled;
    }

    void OnApplicationPause(bool paused)
    {
        if (!paused)
        {
            CatchUpOffline();
            RecalculateAll();
        }
    }

    void Update()
    {
        _laborSampleTimer += Time.unscaledDeltaTime;
        if (_laborSampleTimer >= LABOR_SAMPLE_INTERVAL)
        {
            _laborSampleTimer = 0f;
            AccumulateLaborHoursDeltas();
        }

        _recalcTimer += Time.unscaledDeltaTime;
        if (_recalcTimer >= RECALC_INTERVAL)
        {
            _recalcTimer = 0f;
            RecalculateAll();
        }
    }

    // ─────────── Save / Load ───────────

    private void LoadFromSave()
    {
        _save = SaveManager.GetExchangeBlob() ?? new ExchangeSaveData();
        _stateMap = new Dictionary<string, MarketSpeciesState>(StringComparer.Ordinal);
        _sentimentMap = new Dictionary<string, SpeciesBattleSentimentData>(StringComparer.Ordinal);

        _save.speciesStates ??= new List<MarketSpeciesState>();
        _save.monthlyBattleSentiments ??= new List<SpeciesBattleSentimentData>();

        for (int i = 0; i < _save.speciesStates.Count; i++)
        {
            var s = _save.speciesStates[i];
            if (!string.IsNullOrEmpty(s.speciesId))
                _stateMap[s.speciesId] = s;
        }

        for (int i = 0; i < _save.monthlyBattleSentiments.Count; i++)
        {
            var entry = _save.monthlyBattleSentiments[i];
            if (entry == null || string.IsNullOrEmpty(entry.speciesId)) continue;
            _sentimentMap[entry.speciesId] = entry;
        }

        EnsureMonthlyBattleSentimentWindow();

        // Load demand overrides (Bear/Bull tokens)
        _save.demandOverrides ??= new List<DemandOverride>();
        _overrideMap = new Dictionary<string, DemandOverride>(StringComparer.Ordinal);
        int today = DayIndex();
        for (int i = _save.demandOverrides.Count - 1; i >= 0; i--)
        {
            var ov = _save.demandOverrides[i];
            if (ov == null || ov.expiresDay <= today)
                _save.demandOverrides.RemoveAt(i);
            else if (!string.IsNullOrEmpty(ov.speciesId))
                _overrideMap[ov.speciesId] = ov;
        }

        _save.bullTokenUsages ??= new List<SpeciesTokenUsage>();
        _save.bearTokenUsages ??= new List<SpeciesTokenUsage>();
        _bullTokenUseMap = new Dictionary<string, int>(StringComparer.Ordinal);
        _bearTokenUseMap = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = _save.bullTokenUsages.Count - 1; i >= 0; i--)
        {
            var usage = _save.bullTokenUsages[i];
            if (usage == null || string.IsNullOrEmpty(usage.speciesId) || usage.expiresDay <= today)
                _save.bullTokenUsages.RemoveAt(i);
            else
                _bullTokenUseMap[usage.speciesId] = usage.expiresDay;
        }

        for (int i = _save.bearTokenUsages.Count - 1; i >= 0; i--)
        {
            var usage = _save.bearTokenUsages[i];
            if (usage == null || string.IsNullOrEmpty(usage.speciesId) || usage.expiresDay <= today)
                _save.bearTokenUsages.RemoveAt(i);
            else
                _bearTokenUseMap[usage.speciesId] = usage.expiresDay;
        }

        // Load catch-hype timestamps (prune expired entries)
        _save.catchHype ??= new List<CatchHypeEntry>();
        _catchHypeMap = new Dictionary<string, long>(StringComparer.Ordinal);
        long nowUnix = SaveManager.NowUnix();
        for (int i = _save.catchHype.Count - 1; i >= 0; i--)
        {
            var ch = _save.catchHype[i];
            if (ch == null || string.IsNullOrEmpty(ch.speciesId) || nowUnix - ch.capturedUnix > CATCH_HYPE_DURATION)
                _save.catchHype.RemoveAt(i);
            else
                _catchHypeMap[ch.speciesId] = ch.capturedUnix;
        }

        // Load broker-scarcity counts (reset on new week)
        _save.brokerScarcity ??= new List<BrokerScarcityEntry>();
        _brokerScarcityMap = new Dictionary<string, int>(StringComparer.Ordinal);
        int thisWeek = WeekIndex();
        // Scarcity counts reset each week alongside the base-value reset
        if (_save.lastWeekIndex >= 0 && _save.lastWeekIndex != thisWeek)
        {
            _save.brokerScarcity.Clear();
        }
        for (int i = 0; i < _save.brokerScarcity.Count; i++)
        {
            var bs = _save.brokerScarcity[i];
            if (bs != null && !string.IsNullOrEmpty(bs.speciesId))
                _brokerScarcityMap[bs.speciesId] = bs.timesBrokered;
        }

        // Ensure hot type is set for this week
        EnsureHotType();

        _save.surgeAlertSpeciesIds ??= new List<string>();
        _surgeAlertWatchlist = new HashSet<string>(_save.surgeAlertSpeciesIds, StringComparer.Ordinal);
    }

    private void Persist()
    {
        _save.speciesStates.Clear();
        foreach (var kv in _stateMap)
            _save.speciesStates.Add(kv.Value);

        _save.monthlyBattleSentiments.Clear();
        foreach (var kv in _sentimentMap)
            _save.monthlyBattleSentiments.Add(kv.Value);

        _save.demandOverrides.Clear();
        if (_overrideMap != null)
        {
            foreach (var kv in _overrideMap)
                _save.demandOverrides.Add(kv.Value);
        }

        _save.bullTokenUsages.Clear();
        if (_bullTokenUseMap != null)
        {
            int today = DayIndex();
            foreach (var kv in _bullTokenUseMap)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value <= today) continue;
                _save.bullTokenUsages.Add(new SpeciesTokenUsage { speciesId = kv.Key, expiresDay = kv.Value });
            }
        }

        _save.bearTokenUsages.Clear();
        if (_bearTokenUseMap != null)
        {
            int today = DayIndex();
            foreach (var kv in _bearTokenUseMap)
            {
                if (string.IsNullOrEmpty(kv.Key) || kv.Value <= today) continue;
                _save.bearTokenUsages.Add(new SpeciesTokenUsage { speciesId = kv.Key, expiresDay = kv.Value });
            }
        }

        // Persist catch-hype (prune expired)
        _save.catchHype.Clear();
        if (_catchHypeMap != null)
        {
            long now = SaveManager.NowUnix();
            foreach (var kv in _catchHypeMap)
            {
                if (now - kv.Value <= CATCH_HYPE_DURATION)
                    _save.catchHype.Add(new CatchHypeEntry { speciesId = kv.Key, capturedUnix = kv.Value });
            }
        }

        // Persist broker-scarcity
        _save.brokerScarcity.Clear();
        if (_brokerScarcityMap != null)
        {
            foreach (var kv in _brokerScarcityMap)
                _save.brokerScarcity.Add(new BrokerScarcityEntry { speciesId = kv.Key, timesBrokered = kv.Value });
        }

        _save.lastRecalcUnix = SaveManager.NowUnix();

        _save.surgeAlertSpeciesIds.Clear();
        if (_surgeAlertWatchlist != null)
        {
            foreach (var speciesId in _surgeAlertWatchlist)
            {
                if (!string.IsNullOrEmpty(speciesId))
                    _save.surgeAlertSpeciesIds.Add(speciesId);
            }
        }

        SaveManager.SetExchangeBlob(_save);
    }

    // ─────────── Offline Catch-Up ───────────

    private void CatchUpOffline()
    {
        if (_save == null || _stateMap == null) return;

        long now = SaveManager.NowUnix();
        long lastRecalc = _save.lastRecalcUnix;
        if (lastRecalc <= 0) return; // first launch, nothing to catch up

        long elapsed = now - lastRecalc;
        if (elapsed < RECALC_INTERVAL) return; // was away less than one interval

        // How many recalc intervals were missed
        int missedIntervals = Mathf.Min((int)(elapsed / (long)RECALC_INTERVAL), 144); // cap ~24h worth

        // How many new days passed while offline
        int dayThen = (int)(lastRecalc / SECONDS_PER_DAY);
        int dayNow  = DayIndex();
        int missedDays = dayNow - dayThen;

        if (missedDays <= 0 && missedIntervals <= 1) return;

        var allMonsters = MonsterCatalog.All;
        if (allMonsters == null) return;

        var ownedCounts = BuildOwnedCounts();

        // Simulate one recalc per missed day (with that day's seed),
        // then additional intra-day recalcs for the current day.
        // Cap total simulated days to 30 to prevent lag on very long absence.
        int daysToSim = Mathf.Clamp(missedDays, 0, 30);

        for (int d = 1; d <= daysToSim; d++)
        {
            int simDay = dayThen + d;
            int simSeed = HashDay(simDay);

            for (int i = 0; i < allMonsters.Count; i++)
            {
                var def = allMonsters[i];
                if (def == null || string.IsNullOrEmpty(def.id)) continue;
                if (def.rarity == Rarity.Boss || def.baseMarketValue <= 0) continue;

                if (!_stateMap.TryGetValue(def.id, out var state))
                {
                    state = new MarketSpeciesState
                    {
                        speciesId = def.id,
                        currentValue = def.baseMarketValue,
                        previousValue = def.baseMarketValue,
                        demandLevel = DemandLevel.Medium,
                        trend = TrendDirection.Stable
                    };
                    _stateMap[def.id] = state;
                }

                // Simulate demand update for this day
                _save.dailySeed = simSeed;
                UpdateDemand(state, def);

                state.previousValue = state.currentValue;

                float baseVal = def.baseMarketValue;
                float demandMul = DemandMul(state.demandLevel);
                float rarityMul = RarityMul(def.rarity);

                int owned = 0;
                ownedCounts.TryGetValue(def.id, out owned);
                float supplyMod = Mathf.Clamp(1.15f - 0.03f * owned, 0.85f, 1.15f);

                float flux = DailyFlux(def.id, simSeed);

                // Use neutral world-event & sentiment multipliers for offline days
                // (we can't know what events were active in the past)
                float final_ = baseVal * demandMul * rarityMul * supplyMod * flux;
                state.currentValue = Mathf.Max(1, Mathf.RoundToInt(final_));

                state.trend = state.currentValue > state.previousValue ? TrendDirection.Rising
                            : state.currentValue < state.previousValue ? TrendDirection.Falling
                            : TrendDirection.Stable;
            }
        }

        // Update save state to reflect we've caught up through today
        _save.dailySeed = HashDay(dayNow);
        _save.lastDayIndex = dayNow;

        Persist();
    }

    // ─────────── Public API ───────────

    public int GetCurrentValue(string speciesId)
    {
        if (_stateMap != null && _stateMap.TryGetValue(speciesId, out var state))
            return state.currentValue;
        var def = MonsterCatalog.GetById(speciesId);
        return def != null ? def.baseMarketValue : 0;
    }

    public int GetBrokerPayout(string speciesId, bool isPremium = false)
    {
        int value = GetCurrentValue(speciesId);

        // Licensed Broker tiers reduce the cut
        float brokerCut = BROKER_CUT_DEFAULT;
        if (FeatureUnlockManager.I != null)
        {
            if (FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_LicensedBroker_T2))
                brokerCut = BROKER_CUT_T2;
            else if (FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_LicensedBroker_T1))
                brokerCut = BROKER_CUT_T1;
        }

        // Premium Appraiser improves premium payout
        float premiumDiv = PREMIUM_DIVISOR_DEFAULT;
        if (isPremium && FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_PremiumAppraiser))
            premiumDiv = PREMIUM_DIVISOR_APPRAISED;

        float payout = isPremium
            ? value / Mathf.Max(0.01f, premiumDiv)
            : value * brokerCut;
        return Mathf.Max(1, Mathf.RoundToInt(payout));
    }

    public void RecordBattleOutcome(string speciesId, bool victory, bool defeat, bool escaped)
    {
        if (_save == null || _sentimentMap == null) return;
        if (string.IsNullOrEmpty(speciesId)) return;
        if (escaped) return;
        if (!victory && !defeat) return;

        EnsureMonthlyBattleSentimentWindow();

        if (!_sentimentMap.TryGetValue(speciesId, out var entry))
        {
            entry = new SpeciesBattleSentimentData
            {
                speciesId = speciesId,
                monthlyWinsAgainst = 0,
                monthlyLossesAgainst = 0,
                sentimentScore = 0
            };
            _sentimentMap[speciesId] = entry;
        }

        if (victory)
        {
            entry.monthlyWinsAgainst++;
            entry.sentimentScore = Mathf.Max(-SENTIMENT_CAP, entry.sentimentScore - SENTIMENT_STEP_WIN);
        }
        else if (defeat)
        {
            entry.monthlyLossesAgainst++;
            entry.sentimentScore = Mathf.Min(SENTIMENT_CAP, entry.sentimentScore + SENTIMENT_STEP_LOSS);
        }

        RecalculateAll();
    }

    public MarketSpeciesState GetState(string speciesId)
    {
        if (_stateMap != null && _stateMap.TryGetValue(speciesId, out var s)) return s;
        return null;
    }

    public IReadOnlyDictionary<string, MarketSpeciesState> AllStates => _stateMap;

    public ExchangeSaveData SaveData => _save;

    // ─────────── Recalculation ───────────

    public void RecalculateAll()
    {
        if (_save == null || _stateMap == null) return;

        EnsureMonthlyBattleSentimentWindow();
        AccumulateLaborHoursDeltas();

        // New week → reset every species to its base market value
        int thisWeek = WeekIndex();
        if (_save.lastWeekIndex >= 0 && thisWeek != _save.lastWeekIndex)
        {
            _save.lastWeekIndex = thisWeek;
            _brokerScarcityMap?.Clear();
            _save.brokerScarcity.Clear();
            EnsureHotType();
            ResetAllValuesToBase();
            return;
        }
        _save.lastWeekIndex = thisWeek;
        EnsureHotType();

        int today = DayIndex();
        int previousDay = _save.lastDayIndex;
        bool newDay = today != previousDay;
        if (newDay)
        {
            _save.dailySeed = HashDay(today);
            _save.lastDayIndex = today;
        }

        bool isMarketReset = newDay && previousDay >= 0;

        var allMonsters = MonsterCatalog.All;
        if (allMonsters == null) return;

        var ownedCounts = BuildOwnedCounts();

        for (int i = 0; i < allMonsters.Count; i++)
        {
            var def = allMonsters[i];
            if (def == null || string.IsNullOrEmpty(def.id)) continue;
            if (def.rarity == Rarity.Boss || def.baseMarketValue <= 0) continue;

            if (!_stateMap.TryGetValue(def.id, out var state))
            {
                state = new MarketSpeciesState
                {
                    speciesId = def.id,
                    currentValue = def.baseMarketValue,
                    previousValue = def.baseMarketValue,
                    demandLevel = DemandLevel.Medium,
                    trend = TrendDirection.Stable
                };
                _stateMap[def.id] = state;
            }

            if (newDay) UpdateDemand(state, def);

            // Only snapshot previousValue on day transitions so the
            // Trends tab can show yesterday-vs-today deltas all day long.
            // Without this guard, intra-day recalcs (every 10 min) immediately
            // set previousValue = currentValue, making every trend "Stable".
            if (newDay)
                state.previousValue = state.currentValue;

            // ── value formula ──
            float baseVal = def.baseMarketValue;
            float demandMul = DemandMul(state.demandLevel);
            float rarityMul = RarityMul(def.rarity);

            // supply modifier: fewer owned → higher value
            int owned = 0;
            ownedCounts.TryGetValue(def.id, out owned);
            float supplyMod = Mathf.Clamp(1.15f - 0.03f * owned, 0.85f, 1.15f);

            // daily fluctuation: seeded per species+day, ±8%
            float flux = DailyFlux(def.id, _save.dailySeed);

            // world event multiplier
            float eventMul = GetWorldEventMultiplier();

            // monthly battle sentiment: losses against species push value up, wins push down
            float sentimentMul = GetBattleSentimentMultiplier(def.id);
            float laborMul = GetLaborHoursMultiplier(def.id);

            // Level Boost: higher-level owned specimens increase species value
            float levelMul = GetLevelBoostMultiplier(def.id);

            // Freshly Caught: 24-hour hype window after capture
            float catchHypeMul = GetCatchHypeMultiplier(def.id);

            // Type Trends: one type is "hot" each week
            float typeTrendMul = GetTypeTrendMultiplier(def.type);

            // Scarcity from brokering: selling raises value
            float scarcityMul = GetBrokerScarcityMultiplier(def.id);

            // Monopoly Bonus: if player owns every species of this type, boost value
            float monopolyMul = 1f;
            if (FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_MonopolyBonus))
            {
                if (HasMonopolyOnType(def.type, ownedCounts))
                    monopolyMul = MONOPOLY_BONUS;
            }

            float final_ = baseVal * demandMul * rarityMul * supplyMod * flux * eventMul
                         * sentimentMul * laborMul * levelMul * catchHypeMul
                         * typeTrendMul * scarcityMul * monopolyMul;
            int hardCeiling = Mathf.Max(1, Mathf.RoundToInt(baseVal * MAX_VALUE_MULTIPLIER));
            state.currentValue = Mathf.Clamp(Mathf.RoundToInt(final_), 1, hardCeiling);

            // trend
            if (state.currentValue > state.previousValue)
                state.trend = TrendDirection.Rising;
            else if (state.currentValue < state.previousValue)
                state.trend = TrendDirection.Falling;
            else
                state.trend = TrendDirection.Stable;

            state.lastUpdateUnix = SaveManager.NowUnix();

            // Surge alert (requires unlock)
            if (state.demandLevel == DemandLevel.Surge && newDay)
            {
                if (FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_SurgeAlert))
                {
                    bool canAlertThisSpecies = IsSurgeAlertEnabledForSpecies(def.id);
                    bool inBattle = IsAnyBattleActive();
                    if (canAlertThisSpecies && !inBattle)
                        ExchangeToastUI.EnqueueGuaranteed($"SURGE: {def.displayName} demand is surging!", def.icon);
                }
            }
        }

        // Dividend Yield: process once per local day, collect on Monday.
        int localToday = LocalDayIndex();
        if (_save.lastDividendDayIndex != localToday)
        {
            _save.lastDividendDayIndex = localToday;

            if (DateTimeOffset.FromUnixTimeSeconds(SaveManager.NowUnix()).UtcDateTime.DayOfWeek == DayOfWeek.Monday &&
                FeatureUnlockManager.I != null &&
                FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_DividendYield))
            {
                int portfolioValue = GetTotalPortfolioValue();
                long rawDividend = (long)Mathf.Max(0f, (float)portfolioValue * DIVIDEND_RATE);
                int dividend = (int)Mathf.Min(rawDividend, int.MaxValue);
                if (dividend > 0)
                {
                    ResourceBank.Add(ResourceType.Credits, dividend);
                    _save.pendingDividendToastAmount = dividend;
                    _save.pendingDividendToastDayIndex = localToday;
                }
            }
        }

        Persist();
        GameEvents.ExchangeValuesChanged?.Invoke();

        if (isMarketReset)
            GameEvents.ExchangeMarketReset?.Invoke();
    }

    // ─────────── Monthly Reset ───────────

    private void ResetAllValuesToBase()
    {
        var allMonsters = MonsterCatalog.All;
        if (allMonsters == null) return;

        for (int i = 0; i < allMonsters.Count; i++)
        {
            var def = allMonsters[i];
            if (def == null || string.IsNullOrEmpty(def.id)) continue;
            if (def.rarity == Rarity.Boss || def.baseMarketValue <= 0) continue;

            if (!_stateMap.TryGetValue(def.id, out var state))
            {
                state = new MarketSpeciesState { speciesId = def.id };
                _stateMap[def.id] = state;
            }

            state.previousValue = state.currentValue;
            state.currentValue = def.baseMarketValue;
            state.demandLevel = DemandLevel.Medium;

            if (state.currentValue != state.previousValue)
                state.trend = state.currentValue > state.previousValue
                    ? TrendDirection.Rising : TrendDirection.Falling;
            else
                state.trend = TrendDirection.Stable;

            state.lastUpdateUnix = SaveManager.NowUnix();
        }

        // Reset daily seed so flux doesn't carry over
        _save.dailySeed = HashDay(DayIndex());
        _save.lastDayIndex = DayIndex();

        Persist();
        GameEvents.ExchangeValuesChanged?.Invoke();
        GameEvents.ExchangeMarketReset?.Invoke();
    }

    // ─────────── Demand ───────────

    private void UpdateDemand(MarketSpeciesState state, MonsterDataSO def)
    {
        // Check if any active requests target this species → push demand up
        bool hasRequest = ExchangeRequestManager.I != null &&
                          ExchangeRequestManager.I.GetMatchingRequests(def.id).Count > 0;

        // Random daily drift using species-seeded hash
        int hash = StableHash(state.speciesId + _save.dailySeed);
        float roll = (hash & 0xFFFF) / 65535f; // 0..1

        DemandLevel target;
        if (hasRequest)
        {
            target = roll < 0.3f ? DemandLevel.Surge : DemandLevel.High;
        }
        else
        {
            if (roll < 0.15f) target = DemandLevel.Low;
            else if (roll < 0.70f) target = DemandLevel.Medium;
            else if (roll < 0.90f) target = DemandLevel.High;
            else target = DemandLevel.Surge;
        }

        state.demandLevel = target;

        // Token override — Bull/Bear tokens force demand for a day
        if (_overrideMap != null && _overrideMap.TryGetValue(state.speciesId, out var ov))
        {
            if (ov.expiresDay > DayIndex())
                state.demandLevel = ov.forcedDemand;
            else
                _overrideMap.Remove(state.speciesId);
        }
    }

    // ─────────── Helpers ───────────

    private Dictionary<string, int> BuildOwnedCounts()
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var data = SaveManager.Data;
        if (data?.owned == null) return counts;

        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;
            counts.TryGetValue(o.monsterId, out int c);
            counts[o.monsterId] = c + 1;
        }
        return counts;
    }

    private float DailyFlux(string speciesId, int daySeed)
    {
        int h = StableHash(speciesId + daySeed.ToString());
        float norm = (h & 0xFFFF) / 65535f; // 0..1
        return 0.92f + norm * 0.16f;         // 0.92 .. 1.08
    }

    private float GetWorldEventMultiplier()
    {
        if (WorldEventSystem.I == null) return 1f;
        return WorldEventSystem.I.GetExchangeValueMultiplier();
    }

    private static int DayIndex()
    {
        return (int)(SaveManager.NowUnix() / SECONDS_PER_DAY);
    }

    private static int WeekIndex()
    {
        return DayIndex() / 7;
    }

    private static int LocalDayIndex()
    {
        var localDate = DateTime.Now.Date;
        return (localDate.Year * 10000) + (localDate.Month * 100) + localDate.Day;
    }

    // ─────────── New Market Multipliers ───────────

    /// <summary>
    /// Level Boost: highest owned level of this species → up to +50% value.
    /// </summary>
    private float GetLevelBoostMultiplier(string speciesId)
    {
        var data = SaveManager.Data;
        if (data?.owned == null) return 1f;

        int maxLevel = 0;
        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o != null && o.monsterId == speciesId && o.level > maxLevel)
                maxLevel = o.level;
        }
        if (maxLevel <= 1) return 1f;

        float bonus = Mathf.Min((maxLevel - 1) * LEVEL_BOOST_PER_LEVEL, LEVEL_BOOST_MAX);
        return 1f + bonus;
    }

    /// <summary>
    /// Freshly Caught: +20% for 24 hours after most recent capture of this species.
    /// </summary>
    private float GetCatchHypeMultiplier(string speciesId)
    {
        if (_catchHypeMap == null || !_catchHypeMap.TryGetValue(speciesId, out long capturedUnix))
            return 1f;

        long elapsed = SaveManager.NowUnix() - capturedUnix;
        if (elapsed > CATCH_HYPE_DURATION)
        {
            _catchHypeMap.Remove(speciesId);
            return 1f;
        }
        return CATCH_HYPE_BONUS;
    }

    /// <summary>
    /// Type Trends: one randomly chosen type is "hot" each week → +15%.
    /// </summary>
    private float GetTypeTrendMultiplier(MonsterType type)
    {
        if (_save == null || type == MonsterType.None) return 1f;
        return type == _save.hotType ? HOT_TYPE_BONUS : 1f;
    }

    /// <summary>
    /// Picks a new hot type at the start of each week (deterministic from seed).
    /// </summary>
    private void EnsureHotType()
    {
        int week = WeekIndex();
        if (_save.hotTypeWeekIndex == week) return;

        _save.hotTypeWeekIndex = week;
        int hash = StableHash("HotType" + week);
        // MonsterType values are 1..16, skip None(0)
        _save.hotType = (MonsterType)(1 + (hash % HOT_TYPE_COUNT));
    }

    /// <summary>
    /// Scarcity from brokering: +5% per broker this week, capped at +30%.
    /// </summary>
    private float GetBrokerScarcityMultiplier(string speciesId)
    {
        if (_brokerScarcityMap == null || !_brokerScarcityMap.TryGetValue(speciesId, out int count))
            return 1f;
        float bonus = Mathf.Min(count * SCARCITY_PER_BROKER, SCARCITY_MAX);
        return 1f + bonus;
    }

    /// <summary>
    /// Called when a monster is captured — stamps catch-hype timestamp.
    /// </summary>
    private void OnMonsterCaptured(string speciesId, MonsterType type)
    {
        if (string.IsNullOrEmpty(speciesId)) return;
        _catchHypeMap ??= new Dictionary<string, long>(StringComparer.Ordinal);
        _catchHypeMap[speciesId] = SaveManager.NowUnix();
        RecalculateAll();
    }

    /// <summary>
    /// Called when a monster is brokered/request fulfilled — increments scarcity.
    /// </summary>
    private void OnMonsterBrokered(string speciesId, int credits)
    {
        if (string.IsNullOrEmpty(speciesId)) return;
        _brokerScarcityMap ??= new Dictionary<string, int>(StringComparer.Ordinal);
        _brokerScarcityMap.TryGetValue(speciesId, out int count);
        _brokerScarcityMap[speciesId] = count + 1;
        RecalculateAll();
    }

    /// <summary>
    /// Called when a request is fulfilled — also increments scarcity.
    /// </summary>
    private void OnRequestFulfilled(string requestId, string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return;
        _brokerScarcityMap ??= new Dictionary<string, int>(StringComparer.Ordinal);
        _brokerScarcityMap.TryGetValue(speciesId, out int count);
        _brokerScarcityMap[speciesId] = count + 1;
        RecalculateAll();
    }

    public MonsterType GetHotType() => _save?.hotType ?? MonsterType.None;

    /// <summary>
    /// Returns true if a new calendar month was detected and sentiment data was reset.
    /// </summary>
    private bool EnsureMonthlyBattleSentimentWindow()
    {
        int monthKey = MonthKeyUtc();
        if (_save.battleSentimentMonthKey == monthKey) return false;

        _save.battleSentimentMonthKey = monthKey;
        _save.monthlyBattleSentiments ??= new List<SpeciesBattleSentimentData>();
        _save.monthlyBattleSentiments.Clear();
        _sentimentMap ??= new Dictionary<string, SpeciesBattleSentimentData>(StringComparer.Ordinal);
        _sentimentMap.Clear();
        _workerHoursSampled.Clear();
        return true;
    }

    private float GetBattleSentimentMultiplier(string speciesId)
    {
        if (_sentimentMap == null || string.IsNullOrEmpty(speciesId)) return 1f;
        if (!_sentimentMap.TryGetValue(speciesId, out var entry)) return 1f;

        float t = Mathf.Clamp(entry.sentimentScore / (float)Mathf.Max(1, SENTIMENT_CAP), -1f, 1f);
        float normalized = (t + 1f) * 0.5f;
        return Mathf.Lerp(SENTIMENT_MIN_MUL, SENTIMENT_MAX_MUL, normalized);
    }

    private float GetLaborHoursMultiplier(string speciesId)
    {
        if (_sentimentMap == null || string.IsNullOrEmpty(speciesId)) return 1f;
        if (!_sentimentMap.TryGetValue(speciesId, out var entry)) return LABOR_MAX_MUL;

        float h = Mathf.Clamp01(entry.monthlyHoursWorked / Mathf.Max(1f, LABOR_HOURS_CAP));
        return Mathf.Lerp(LABOR_MAX_MUL, LABOR_MIN_MUL, h);
    }

    private void AccumulateLaborHoursDeltas()
    {
        if (_save == null || _sentimentMap == null) return;
        var jm = JobManager.I;
        var data = SaveManager.Data;
        if (jm == null || data?.jobAssignments == null) return;

        EnsureMonthlyBattleSentimentWindow();

        _activeKeysReuse.Clear();
        bool addedHours = false;

        for (int i = 0; i < data.jobAssignments.Count; i++)
        {
            var assignment = data.jobAssignments[i];
            if (assignment?.workerIds == null) continue;

            for (int j = 0; j < assignment.workerIds.Count; j++)
            {
                string key = assignment.workerIds[j];
                if (string.IsNullOrWhiteSpace(key)) continue;
                _activeKeysReuse.Add(key);

                if (!jm.TryGetWorkerAssignment(key, out _, out _, out float hoursAssigned)) continue;
                hoursAssigned = Mathf.Max(0f, hoursAssigned);

                if (!_workerHoursSampled.TryGetValue(key, out float previousHours))
                {
                    _workerHoursSampled[key] = hoursAssigned;
                    continue;
                }

                float delta = Mathf.Max(0f, hoursAssigned - previousHours);
                _workerHoursSampled[key] = hoursAssigned;
                if (delta <= 0f) continue;

                string speciesId = ResolveSpeciesIdForWorkerKey(key);
                if (string.IsNullOrEmpty(speciesId)) continue;

                var entry = GetOrCreateSentimentEntry(speciesId);
                entry.monthlyHoursWorked += delta;
                addedHours = true;
            }
        }

        if (_workerHoursSampled.Count > 0)
        {
            // Remove stale keys without LINQ allocation
            _staleKeys.Clear();
            foreach (var kv in _workerHoursSampled)
                if (!_activeKeysReuse.Contains(kv.Key)) _staleKeys.Add(kv.Key);
            for (int i = 0; i < _staleKeys.Count; i++)
                _workerHoursSampled.Remove(_staleKeys[i]);
        }

        if (addedHours)
            _recalcTimer = RECALC_INTERVAL;
    }

    private SpeciesBattleSentimentData GetOrCreateSentimentEntry(string speciesId)
    {
        if (_sentimentMap.TryGetValue(speciesId, out var entry)) return entry;

        entry = new SpeciesBattleSentimentData
        {
            speciesId = speciesId,
            monthlyWinsAgainst = 0,
            monthlyLossesAgainst = 0,
            sentimentScore = 0,
            monthlyHoursWorked = 0f
        };
        _sentimentMap[speciesId] = entry;
        return entry;
    }

    private static string ResolveSpeciesIdForWorkerKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        var data = SaveManager.Data;
        if (data?.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var o = data.owned[i];
                if (o == null || string.IsNullOrEmpty(o.ownedUID)) continue;
                if (!string.Equals(o.ownedUID, key, StringComparison.Ordinal)) continue;
                return o.monsterId;
            }
        }

        var def = MonsterCatalog.GetById(key);
        return def != null ? def.id : null;
    }

    private static int MonthKeyUtc()
    {
        var dt = DateTimeOffset.FromUnixTimeSeconds(SaveManager.NowUnix()).UtcDateTime;
        return dt.Year * 100 + dt.Month;
    }

    private static int HashDay(int day)
    {
        // Simple deterministic seed from day index
        return StableHash("ExchangeDay" + day);
    }

    private static int StableHash(string s)
    {
        // Deterministic FNV-1a 32-bit hash
        unchecked
        {
            int hash = (int)2166136261;
            for (int i = 0; i < s.Length; i++)
            {
                hash ^= s[i];
                hash *= 16777619;
            }
            return hash & 0x7FFFFFFF;
        }
    }

    // ─────────── Bear / Bull Tokens ───────────

    public bool CanUseBullTokenOnSpecies(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId) || _bullTokenUseMap == null) return false;
        return !_bullTokenUseMap.TryGetValue(speciesId, out var expiresDay) || expiresDay <= DayIndex();
    }

    public bool CanUseBearTokenOnSpecies(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId) || _bearTokenUseMap == null) return false;
        return !_bearTokenUseMap.TryGetValue(speciesId, out var expiresDay) || expiresDay <= DayIndex();
    }

    public bool UseBullToken(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return false;
        if (FeatureUnlockManager.I == null || !FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_BearBullTokens))
            return false;
        if (!CanUseBullTokenOnSpecies(speciesId))
        {
            ExchangeToastUI.EnqueueGuaranteed("Bull Token already used on this species today.");
            return false;
        }
        if (!ResourceBank.TrySpend(ResourceType.BullToken, 1)) return false;

        _bullTokenUseMap ??= new Dictionary<string, int>(StringComparer.Ordinal);
        _bullTokenUseMap[speciesId] = DayIndex() + 1;

        ApplyDemandOverride(speciesId, DemandLevel.Surge);
        ExchangeToastUI.EnqueueGuaranteed("Bull Token used! Demand set to SURGE for today.");
        RecalculateAll();
        return true;
    }

    public bool UseBearToken(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return false;
        if (FeatureUnlockManager.I == null || !FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_BearBullTokens))
            return false;
        if (!CanUseBearTokenOnSpecies(speciesId))
        {
            ExchangeToastUI.EnqueueGuaranteed("Bear Token already used on this species today.");
            return false;
        }
        if (!ResourceBank.TrySpend(ResourceType.BearToken, 1)) return false;

        _bearTokenUseMap ??= new Dictionary<string, int>(StringComparer.Ordinal);
        _bearTokenUseMap[speciesId] = DayIndex() + 1;

        ApplyDemandOverride(speciesId, DemandLevel.Low);
        ExchangeToastUI.EnqueueGuaranteed("Bear Token used! Demand set to LOW for today.");
        RecalculateAll();
        return true;
    }

    private void ApplyDemandOverride(string speciesId, DemandLevel level)
    {
        if (_save == null) return;

        _overrideMap ??= new Dictionary<string, DemandOverride>(StringComparer.Ordinal);

        var ov = new DemandOverride
        {
            speciesId = speciesId,
            forcedDemand = level,
            expiresDay = DayIndex() + 1
        };
        _overrideMap[speciesId] = ov;

        if (_stateMap != null && _stateMap.TryGetValue(speciesId, out var state))
            state.demandLevel = level;

        Persist();
    }

    // ─────────── Monopoly Check ───────────

    public bool HasMonopoly(MonsterType type)
    {
        if (type == MonsterType.None) return false;
        return HasMonopolyOnType(type, BuildOwnedCounts());
    }

    private bool HasMonopolyOnType(MonsterType type, Dictionary<string, int> ownedCounts)
    {
        if (type == MonsterType.None) return false;
        var allMonsters = MonsterCatalog.All;
        if (allMonsters == null) return false;

        for (int i = 0; i < allMonsters.Count; i++)
        {
            var def = allMonsters[i];
            if (def == null || def.type != type) continue;
            if (def.rarity == Rarity.Boss || def.baseMarketValue <= 0) continue;
            if (!ownedCounts.ContainsKey(def.id)) return false;
        }
        return true;
    }

    // ─────────── Portfolio Value ───────────

    public int GetTotalPortfolioValue()
    {
        var data = SaveManager.Data;
        if (data?.owned == null || _stateMap == null) return 0;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        int total = 0;
        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;
            if (!seen.Add(o.monsterId)) continue;

            total += GetCurrentValue(o.monsterId);
        }
        return total;
    }

    public int GetCurrentDividendAmount()
    {
        int portfolioValue = GetTotalPortfolioValue();
        return Mathf.Max(0, Mathf.RoundToInt(portfolioValue * DIVIDEND_RATE));
    }

    public void TryShowPendingDividendHomeToast()
    {
        if (_save == null) return;

        int amount = _save.pendingDividendToastAmount;
        int pendingDay = _save.pendingDividendToastDayIndex;
        if (amount <= 0 || pendingDay < 0) return;

        int localToday = LocalDayIndex();
        if (pendingDay != localToday)
        {
            // Only show on the same day the dividend was collected.
            _save.pendingDividendToastAmount = 0;
            _save.pendingDividendToastDayIndex = -1;
            Persist();
            return;
        }

        _save.pendingDividendToastAmount = 0;
        _save.pendingDividendToastDayIndex = -1;
        Persist();

        GameEvents.RaiseToast($"Dividend collected: +{amount} Credits");
    }

    // ─────────── Market Forecast ───────────

    /// <summary>
    /// Returns tomorrow's predicted demand for a species.
    /// Only meaningful if Exchange_MarketForecast is unlocked.
    /// </summary>
    public DemandLevel GetForecastDemand(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return DemandLevel.Medium;

        int tomorrowSeed = HashDay(DayIndex() + 1);

        bool hasRequest = ExchangeRequestManager.I != null &&
                          ExchangeRequestManager.I.GetMatchingRequests(speciesId).Count > 0;

        int hash = StableHash(speciesId + tomorrowSeed);
        float roll = (hash & 0xFFFF) / 65535f;

        if (hasRequest)
            return roll < 0.3f ? DemandLevel.Surge : DemandLevel.High;

        if (roll < 0.15f) return DemandLevel.Low;
        if (roll < 0.70f) return DemandLevel.Medium;
        if (roll < 0.90f) return DemandLevel.High;
        return DemandLevel.Surge;
    }

    // ─────────── Event Handlers ───────────

    private void OnWorldEventsChanged() => RecalculateAll();

    private void OnOwnedChanged()
    {
        // Supply changed — recalculate
        _recalcTimer = RECALC_INTERVAL; // force recalc on next Update
    }

    public bool IsSurgeAlertEnabledForSpecies(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return false;
        _surgeAlertWatchlist ??= new HashSet<string>(StringComparer.Ordinal);
        return _surgeAlertWatchlist.Contains(speciesId);
    }

    public bool SetSurgeAlertForSpecies(string speciesId, bool enabled)
    {
        if (string.IsNullOrEmpty(speciesId)) return false;
        _surgeAlertWatchlist ??= new HashSet<string>(StringComparer.Ordinal);

        bool changed = enabled
            ? _surgeAlertWatchlist.Add(speciesId)
            : _surgeAlertWatchlist.Remove(speciesId);

        if (!changed) return false;

        Persist();
        GameEvents.ExchangeValuesChanged?.Invoke();
        return true;
    }

    private static bool IsAnyBattleActive()
    {
        if (EncounterManager.I != null && EncounterManager.I.IsInBattle)
            return true;

        var battle = UnityEngine.Object.FindFirstObjectByType<BattleManager>(FindObjectsInactive.Include);
        return battle != null && battle.InBattle;
    }
}
