using System;
using System.Collections.Generic;
using System.Linq;
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
    private const float SHINY_DIVISOR_DEFAULT = 0.75f;
    private const float SHINY_DIVISOR_APPRAISED = 0.50f;
    private const float MONOPOLY_BONUS = 1.25f;
    private const float DIVIDEND_RATE = 0.01f; // 1% daily
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

    private ExchangeSaveData _save;
    private Dictionary<string, MarketSpeciesState> _stateMap;
    private Dictionary<string, SpeciesBattleSentimentData> _sentimentMap;
    private readonly Dictionary<string, float> _workerHoursSampled = new Dictionary<string, float>(StringComparer.Ordinal);
    private Dictionary<string, DemandOverride> _overrideMap;
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
    }

    void OnDisable()
    {
        GameEvents.WorldEventsChanged -= OnWorldEventsChanged;
        GameEvents.OnOwnedMonstersChanged -= OnOwnedChanged;
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

        _save.lastRecalcUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        SaveManager.SetExchangeBlob(_save);
    }

    // ─────────── Offline Catch-Up ───────────

    private void CatchUpOffline()
    {
        if (_save == null || _stateMap == null) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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

    public int GetBrokerPayout(string speciesId, bool isShiny = false)
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

        // Shiny Appraiser improves shiny payout
        float shinyDiv = SHINY_DIVISOR_DEFAULT;
        if (isShiny && FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_ShinyAppraiser))
            shinyDiv = SHINY_DIVISOR_APPRAISED;

        float payout = isShiny
            ? value / Mathf.Max(0.01f, shinyDiv)
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

        bool monthReset = EnsureMonthlyBattleSentimentWindow();
        AccumulateLaborHoursDeltas();

        // New calendar month → reset every species to its base market value
        if (monthReset)
        {
            ResetAllValuesToBase();
            return;
        }

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

            // Monopoly Bonus: if player owns every species of this type, boost value
            float monopolyMul = 1f;
            if (FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_MonopolyBonus))
            {
                if (HasMonopolyOnType(def.type, ownedCounts))
                    monopolyMul = MONOPOLY_BONUS;
            }

            float final_ = baseVal * demandMul * rarityMul * supplyMod * flux * eventMul * sentimentMul * laborMul * monopolyMul;
            state.currentValue = Mathf.Max(1, Mathf.RoundToInt(final_));

            // trend
            if (state.currentValue > state.previousValue)
                state.trend = TrendDirection.Rising;
            else if (state.currentValue < state.previousValue)
                state.trend = TrendDirection.Falling;
            else
                state.trend = TrendDirection.Stable;

            state.lastUpdateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            // Surge alert (requires unlock)
            if (state.demandLevel == DemandLevel.Surge && newDay)
            {
                if (FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_SurgeAlert))
                {
                    ExchangeToastUI.EnqueueGuaranteed($"SURGE: {def.displayName} demand is surging!", def.icon);
                }
            }
        }

        // Dividend Yield: on new day, pay 1% of total portfolio value
        if (newDay && _save.lastDividendDayIndex != today)
        {
            if (FeatureUnlockManager.I != null && FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_DividendYield))
            {
                int portfolioValue = GetTotalPortfolioValue();
                int dividend = Mathf.Max(0, Mathf.RoundToInt(portfolioValue * DIVIDEND_RATE));
                if (dividend > 0)
                {
                    ResourceBank.Add(ResourceType.Credits, dividend);
                    ExchangeToastUI.EnqueueGuaranteed($"Dividend: +{dividend} credits from portfolio yield!");
                }
            }
            _save.lastDividendDayIndex = today;
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

            state.lastUpdateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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
            if (string.IsNullOrEmpty(o.monsterId)) continue;
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
        float mul = 1f;
        var active = WorldEventSystem.I.ActiveEvents;
        if (active == null) return 1f;

        for (int i = 0; i < active.Count; i++)
        {
            var evt = active[i];
            if (evt?.effects == null) continue;
            for (int j = 0; j < evt.effects.Count; j++)
            {
                var e = evt.effects[j];
                if (e.kind == WorldEventEffectKind.ExchangeValueMultiplier)
                    mul *= Mathf.Max(0.01f, e.value);
            }
        }
        return mul;
    }

    private static int DayIndex()
    {
        return (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / SECONDS_PER_DAY);
    }

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

        var activeKeys = new HashSet<string>(StringComparer.Ordinal);
        bool addedHours = false;

        for (int i = 0; i < data.jobAssignments.Count; i++)
        {
            var assignment = data.jobAssignments[i];
            if (assignment?.workerIds == null) continue;

            for (int j = 0; j < assignment.workerIds.Count; j++)
            {
                string key = assignment.workerIds[j];
                if (string.IsNullOrWhiteSpace(key)) continue;
                activeKeys.Add(key);

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
            var stale = _workerHoursSampled.Keys.Where(k => !activeKeys.Contains(k)).ToList();
            for (int i = 0; i < stale.Count; i++)
                _workerHoursSampled.Remove(stale[i]);
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
        var now = DateTimeOffset.UtcNow;
        return now.Year * 100 + now.Month;
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

    public bool UseBullToken(string speciesId)
    {
        if (string.IsNullOrEmpty(speciesId)) return false;
        if (FeatureUnlockManager.I == null || !FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_BearBullTokens))
            return false;
        if (!ResourceBank.TrySpend(ResourceType.BullToken, 1)) return false;

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
        if (!ResourceBank.TrySpend(ResourceType.BearToken, 1)) return false;

        ApplyDemandOverride(speciesId, DemandLevel.Low);
        ExchangeToastUI.EnqueueGuaranteed("Bear Token used! Demand set to LOW for today.");
        RecalculateAll();
        return true;
    }

    private void ApplyDemandOverride(string speciesId, DemandLevel level)
    {
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
}
