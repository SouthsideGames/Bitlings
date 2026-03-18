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

    private const float BROKER_CUT = 0.85f;          // broker keeps 15%
    private const float SHINY_DIVISOR = 0.75f;
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
    }

    private void Persist()
    {
        _save.speciesStates.Clear();
        foreach (var kv in _stateMap)
            _save.speciesStates.Add(kv.Value);

        _save.monthlyBattleSentiments.Clear();
        foreach (var kv in _sentimentMap)
            _save.monthlyBattleSentiments.Add(kv.Value);

        SaveManager.SetExchangeBlob(_save);
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
        float payout = isShiny
            ? value / Mathf.Max(0.01f, SHINY_DIVISOR)
            : value * BROKER_CUT;
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

            float final_ = baseVal * demandMul * rarityMul * supplyMod * flux * eventMul * sentimentMul * laborMul;
            state.currentValue = Mathf.Max(1, Mathf.RoundToInt(final_));

            // trend
            if (state.currentValue > state.previousValue)
                state.trend = TrendDirection.Rising;
            else if (state.currentValue < state.previousValue)
                state.trend = TrendDirection.Falling;
            else
                state.trend = TrendDirection.Stable;

            state.lastUpdateUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
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

    // ─────────── Event Handlers ───────────

    private void OnWorldEventsChanged() => RecalculateAll();

    private void OnOwnedChanged()
    {
        // Supply changed — recalculate
        _recalcTimer = RECALC_INTERVAL; // force recalc on next Update
    }
}
