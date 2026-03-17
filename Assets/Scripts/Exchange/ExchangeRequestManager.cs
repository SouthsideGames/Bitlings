using System;
using System.Collections.Generic;
using UnityEngine;

// ─────────────────────────────────────────────────────────────
// ExchangeRequestManager — rotates NPC requests on the Bitling
// Exchange board. Daily baseline + bonus requests from world events.
// ─────────────────────────────────────────────────────────────

public sealed class ExchangeRequestManager : MonoBehaviour
{
    public static ExchangeRequestManager I { get; private set; }

    private const float ROTATION_CHECK_INTERVAL = 30f;

    [Header("Config")]
    [SerializeField] private ExchangeRequestLibrarySO requestLibrary;

    [Tooltip("Number of new baseline requests per day.")]
    [SerializeField] private int dailyRequestCount = 3;

    [Tooltip("Maximum number of active requests at any time.")]
    [SerializeField] private int maxActiveRequests = 5;

    private ExchangeSaveData _save;
    private float _rotationTimer;

    // ─────────── Lifecycle ───────────

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
    }

    void Start()
    {
        _save = ExchangeManager.I != null
            ? ExchangeManager.I.SaveData
            : (SaveManager.GetExchangeBlob() ?? new ExchangeSaveData());

        _save.activeRequests ??= new List<ActiveRequest>();

        PruneExpired();
        TryRotateDaily();
    }

    void OnEnable()
    {
        GameEvents.WorldEventsChanged += OnWorldEventsChanged;
    }

    void OnDisable()
    {
        GameEvents.WorldEventsChanged -= OnWorldEventsChanged;
    }

    void Update()
    {
        _rotationTimer += Time.unscaledDeltaTime;
        if (_rotationTimer < ROTATION_CHECK_INTERVAL) return;

        _rotationTimer = 0f;
        PruneExpired();
        TryRotateDaily();
    }

    // ─────────── Public API ───────────

    public IReadOnlyList<ActiveRequest> ActiveRequests
    {
        get
        {
            PruneExpired();
            return _save?.activeRequests;
        }
    }

    /// <summary>
    /// Returns active, unfulfilled requests that the given species can fulfill.
    /// </summary>
    public List<ActiveRequest> GetMatchingRequests(string speciesId)
    {
        var result = new List<ActiveRequest>();
        if (_save?.activeRequests == null || string.IsNullOrEmpty(speciesId)) return result;

        var def = MonsterCatalog.GetById(speciesId);
        if (def == null) return result;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (int i = 0; i < _save.activeRequests.Count; i++)
        {
            var r = _save.activeRequests[i];
            if (r.fulfilled) continue;
            if (r.expiresUnix > 0 && r.expiresUnix <= now) continue;

            if (MatchesRequest(r, def))
                result.Add(r);
        }
        return result;
    }

    /// <summary>
    /// Attempt to fulfill a request with a duplicate of the given species.
    /// Returns the credit reward, or 0 if fulfillment failed.
    /// </summary>
    public int TryFulfillRequest(string requestId, string speciesId)
    {
        if (_save?.activeRequests == null) return 0;

        ActiveRequest match = null;
        for (int i = 0; i < _save.activeRequests.Count; i++)
        {
            if (_save.activeRequests[i].requestId == requestId && !_save.activeRequests[i].fulfilled)
            {
                match = _save.activeRequests[i];
                break;
            }
        }
        if (match == null) return 0;

        var def = MonsterCatalog.GetById(speciesId);
        if (def == null || !MatchesRequest(match, def)) return 0;

        match.fulfilled = true;

        // Grant rewards
        int credits = match.creditReward;
        if (credits > 0) ResourceBank.Add(ResourceType.Credits, credits);
        if (match.bonusResourceAmount > 0)
            ResourceBank.Add(match.bonusResourceType, match.bonusResourceAmount);

        // Track stats
        _save.totalRequestsFulfilled++;

        SaveManager.SetExchangeBlob(_save);
        GameEvents.OnResourcesChanged?.Invoke();
        GameEvents.RequestFulfilled?.Invoke(requestId, speciesId);

        return credits;
    }

    // ─────────── Rotation ───────────

    private void TryRotateDaily()
    {
        if (requestLibrary == null || requestLibrary.requests == null || requestLibrary.requests.Count == 0)
            return;

        int today = (int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 86400);
        if (today <= _save.lastRequestRotationDayIndex) return; // already rotated today

        // Roll new requests
        int slotsAvailable = maxActiveRequests - CountUnfulfilled();
        int toAdd = Mathf.Min(dailyRequestCount, slotsAvailable);

        for (int i = 0; i < toAdd; i++)
        {
            var template = PickWeightedRequest(today * 100 + i);
            if (template == null) continue;

            var active = new ActiveRequest
            {
                requestId = template.requestId + "_" + today + "_" + i,
                requiredSpeciesId = template.requiredSpecies != null ? template.requiredSpecies.id : null,
                requiredType = template.requiredType,
                requiredMinRarity = template.requiredMinRarity,
                creditReward = template.creditReward,
                bonusResourceType = template.bonusResourceType,
                bonusResourceAmount = template.bonusResourceAmount,
                flavorText = template.flavorText,
                expiresUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds() + template.durationHours * 3600,
                fulfilled = false
            };

            _save.activeRequests.Add(active);
        }

        _save.lastRequestRotationDayIndex = today;
        SaveManager.SetExchangeBlob(_save);
    }

    private void AddBonusRequestsFromWorldEvents()
    {
        if (requestLibrary == null || requestLibrary.requests.Count == 0) return;
        if (WorldEventSystem.I == null) return;

        var active = WorldEventSystem.I.ActiveEvents;
        if (active == null || active.Count == 0) return;

        int bonusCount = 0;
        for (int i = 0; i < active.Count; i++)
        {
            var evt = active[i];
            if (evt?.effects == null) continue;
            for (int j = 0; j < evt.effects.Count; j++)
            {
                if (evt.effects[j].kind == WorldEventEffectKind.ExchangeDemandMultiplier)
                    bonusCount++;
            }
        }

        int slotsAvailable = maxActiveRequests - CountUnfulfilled();
        bonusCount = Mathf.Min(bonusCount, slotsAvailable);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < bonusCount; i++)
        {
            var template = PickWeightedRequest((int)(now + i * 37));
            if (template == null) continue;

            var ar = new ActiveRequest
            {
                requestId = template.requestId + "_evt_" + now + "_" + i,
                requiredSpeciesId = template.requiredSpecies != null ? template.requiredSpecies.id : null,
                requiredType = template.requiredType,
                requiredMinRarity = template.requiredMinRarity,
                creditReward = Mathf.RoundToInt(template.creditReward * 1.25f), // bonus premium
                bonusResourceType = template.bonusResourceType,
                bonusResourceAmount = template.bonusResourceAmount,
                flavorText = template.flavorText,
                expiresUnix = now + template.durationHours * 3600,
                fulfilled = false
            };

            _save.activeRequests.Add(ar);
        }

        if (bonusCount > 0)
            SaveManager.SetExchangeBlob(_save);
    }

    // ─────────── Helpers ───────────

    private bool MatchesRequest(ActiveRequest r, MonsterDataSO def)
    {
        // Specific species match
        if (!string.IsNullOrEmpty(r.requiredSpeciesId))
            return string.Equals(r.requiredSpeciesId, def.id, StringComparison.Ordinal);

        // Generic: type + rarity
        bool typeOk = r.requiredType == MonsterType.None || r.requiredType == def.type;
        bool rarityOk = (int)def.rarity >= (int)r.requiredMinRarity;
        return typeOk && rarityOk;
    }

    private int CountUnfulfilled()
    {
        int count = 0;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        for (int i = 0; i < _save.activeRequests.Count; i++)
        {
            var r = _save.activeRequests[i];
            if (!r.fulfilled && (r.expiresUnix <= 0 || r.expiresUnix > now))
                count++;
        }
        return count;
    }

    private void PruneExpired()
    {
        if (_save?.activeRequests == null) return;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        bool changed = false;
        for (int i = _save.activeRequests.Count - 1; i >= 0; i--)
        {
            var r = _save.activeRequests[i];
            if (r.fulfilled || (r.expiresUnix > 0 && r.expiresUnix <= now))
            {
                _save.activeRequests.RemoveAt(i);
                changed = true;
            }
        }
        if (changed)
            SaveManager.SetExchangeBlob(_save);
    }

    private ExchangeRequestSO PickWeightedRequest(int seed)
    {
        var pool = requestLibrary.requests;
        if (pool == null || pool.Count == 0) return null;

        int totalWeight = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] != null) totalWeight += pool[i].weight;
        }
        if (totalWeight <= 0) return null;

        // Deterministic pick using seed
        int roll = StableHash(seed) % totalWeight;
        int cumulative = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i] == null) continue;
            cumulative += pool[i].weight;
            if (roll < cumulative) return pool[i];
        }
        return pool[pool.Count - 1];
    }

    private static int StableHash(int v)
    {
        unchecked
        {
            int h = (int)2166136261 ^ v;
            h *= 16777619;
            h ^= (v >> 16);
            h *= 16777619;
            return h & 0x7FFFFFFF;
        }
    }

    // ─────────── Event Handlers ───────────

    private void OnWorldEventsChanged()
    {
        AddBonusRequestsFromWorldEvents();
    }
}
