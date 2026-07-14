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
    private readonly List<ActiveRequest> _matchResult = new List<ActiveRequest>();

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
        _matchResult.Clear();
        if (_save?.activeRequests == null || string.IsNullOrEmpty(speciesId)) return _matchResult;

        var def = MonsterCatalog.GetById(speciesId);
        if (def == null) return _matchResult;

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        for (int i = 0; i < _save.activeRequests.Count; i++)
        {
            var r = _save.activeRequests[i];
            if (r.fulfilled) continue;
            if (r.expiresUnix > 0 && r.expiresUnix <= now) continue;

            if (MatchesRequest(r, def))
                _matchResult.Add(r);
        }
        return _matchResult;
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

        // This overload trades a JUST-CAPTURED duplicate (held only by the
        // duplicate-resolution panel, never added to the roster), so the specimen
        // itself cannot be consumed here. The invariant we CAN check: a duplicate
        // capture implies the player already owns at least one copy of the species.
        // Without this, any future caller could mint credits for an arbitrary
        // speciesId. Inventory-based fulfillment must use
        // TryFulfillRequestByConsumingOwned instead.
        var owned = SaveManager.Data?.ownedIds;
        if (owned == null || !owned.Contains(speciesId)) return 0;

        int credits = Mathf.Max(0, match.creditReward);
        int bonusAmount = Mathf.Max(0, match.bonusResourceAmount);

        ResourceBank.BeginBatch();
        try
        {
            match.fulfilled = true;

            // Grant rewards
            if (credits > 0) ResourceBank.Add(ResourceType.Credits, credits);
            if (bonusAmount > 0)
                ResourceBank.Add(match.bonusResourceType, bonusAmount);

            // Track stats
            _save.totalRequestsFulfilled++;

            SaveManager.SetExchangeBlob(_save);
        }
        finally
        {
            ResourceBank.EndBatch();
        }

        GameEvents.RequestFulfilled?.Invoke(requestId, speciesId);

        return credits;
    }

    /// <summary>
    /// Atomic-ish fulfillment path for inventory requests:
    /// - validates request and owned candidate
    /// - consumes the owned monster from roster/team/jobs
    /// - grants rewards and marks request fulfilled
    /// - persists exchange blob + player save in the same flow
    ///
    /// Returns awarded credits (0 = no fulfillment).
    /// </summary>
    public int TryFulfillRequestByConsumingOwned(string requestId, OwnedMonsterData ownedCandidate)
    {
        if (_save?.activeRequests == null) return 0;
        if (ownedCandidate == null || string.IsNullOrEmpty(ownedCandidate.monsterId)) return 0;

        ActiveRequest match = null;
        for (int i = 0; i < _save.activeRequests.Count; i++)
        {
            var r = _save.activeRequests[i];
            if (r != null && r.requestId == requestId && !r.fulfilled)
            {
                match = r;
                break;
            }
        }
        if (match == null) return 0;

        var def = MonsterCatalog.GetById(ownedCandidate.monsterId);
        if (def == null || !MatchesRequest(match, def)) return 0;

        if (!ConsumeOwnedMonsterFromSave(ownedCandidate))
            return 0;

        int credits = Mathf.Max(0, match.creditReward);
        int bonusAmount = Mathf.Max(0, match.bonusResourceAmount);

        ResourceBank.BeginBatch();
        try
        {
            match.fulfilled = true;
            _save.totalRequestsFulfilled = Mathf.Max(0, _save.totalRequestsFulfilled + 1);

            if (credits > 0)
                ResourceBank.Add(ResourceType.Credits, credits);

            if (bonusAmount > 0)
                ResourceBank.Add(match.bonusResourceType, bonusAmount);

            SaveManager.SetExchangeBlob(_save);
        }
        finally
        {
            ResourceBank.EndBatch();
        }

        GameEvents.OnOwnedMonstersChanged?.Invoke();
        GameEvents.OnTeamChanged?.Invoke();
        GameEvents.RequestFulfilled?.Invoke(requestId, ownedCandidate.monsterId);

        return credits;
    }

    private static bool ConsumeOwnedMonsterFromSave(OwnedMonsterData owned)
    {
        var data = SaveManager.Data;
        if (data == null || owned == null) return false;

        data.owned ??= new List<OwnedMonsterData>();
        data.team ??= new List<OwnedMonsterData>();

        string ownedUid = owned.ownedUID;
        string speciesId = owned.monsterId;
        bool removed = false;

        for (int i = data.owned.Count - 1; i >= 0; i--)
        {
            var entry = data.owned[i];
            if (entry == null) continue;

            bool same = false;
            if (!string.IsNullOrEmpty(ownedUid) && !string.IsNullOrEmpty(entry.ownedUID))
                same = string.Equals(entry.ownedUID, ownedUid, StringComparison.Ordinal);
            else
                same = ReferenceEquals(entry, owned);

            if (!same) continue;
            data.owned.RemoveAt(i);
            removed = true;
            break;
        }

        if (!removed && !string.IsNullOrEmpty(speciesId))
        {
            for (int i = data.owned.Count - 1; i >= 0; i--)
            {
                var entry = data.owned[i];
                if (entry == null || string.IsNullOrEmpty(entry.monsterId)) continue;
                if (!string.Equals(entry.monsterId, speciesId, StringComparison.Ordinal)) continue;
                data.owned.RemoveAt(i);
                removed = true;
                break;
            }
        }

        if (!removed) return false;

        for (int i = 0; i < data.team.Count; i++)
        {
            var t = data.team[i];
            if (t == null) continue;

            bool same = false;
            if (!string.IsNullOrEmpty(ownedUid) && !string.IsNullOrEmpty(t.ownedUID))
                same = string.Equals(t.ownedUID, ownedUid, StringComparison.Ordinal);
            else if (ReferenceEquals(t, owned))
                same = true;

            if (same)
                data.team[i] = new OwnedMonsterData();
        }

        if (JobManager.I != null)
            JobManager.I.RemoveFromAnyJob(!string.IsNullOrEmpty(ownedUid) ? ownedUid : speciesId);

        return true;
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

            var active = BuildActiveRequest(template, template.requestId + "_" + today + "_" + i, today * 100 + i, template.creditReward, DateTimeOffset.UtcNow.ToUnixTimeSeconds());

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

            var ar = BuildActiveRequest(
                template,
                template.requestId + "_evt_" + now + "_" + i,
                (int)(now + i * 37),
                Mathf.RoundToInt(template.creditReward * 1.25f),
                now);

            _save.activeRequests.Add(ar);
        }

        if (bonusCount > 0)
            SaveManager.SetExchangeBlob(_save);
    }

    // ─────────── Helpers ───────────

    private ActiveRequest BuildActiveRequest(ExchangeRequestSO template, string runtimeRequestId, int speciesSeed, int creditReward, long createdUnix)
    {
        string requiredSpeciesId = ResolveRequiredSpeciesId(template, speciesSeed);
        bool hasSpecificSpecies = !string.IsNullOrEmpty(requiredSpeciesId);

        return new ActiveRequest
        {
            requestId = runtimeRequestId,
            requiredSpeciesId = requiredSpeciesId,
            requiredType = hasSpecificSpecies ? MonsterType.None : template.requiredType,
            requiredMinRarity = hasSpecificSpecies ? Rarity.Common : template.requiredMinRarity,
            creditReward = creditReward,
            bonusResourceType = template.bonusResourceType,
            bonusResourceAmount = template.bonusResourceAmount,
            flavorText = template.flavorText,
            expiresUnix = createdUnix + template.durationHours * 3600,
            fulfilled = false
        };
    }

    private string ResolveRequiredSpeciesId(ExchangeRequestSO template, int speciesSeed)
    {
        if (template == null) return null;

        if (template.requiredSpecies != null && !string.IsNullOrEmpty(template.requiredSpecies.id))
            return template.requiredSpecies.id;

        if (template.requiredRandomSpeciesType == MonsterType.None)
            return null;

        var allMonsters = MonsterCatalog.All;
        if (allMonsters == null || allMonsters.Count == 0)
            return null;

        var candidates = new List<MonsterDataSO>();
        for (int i = 0; i < allMonsters.Count; i++)
        {
            var def = allMonsters[i];
            if (!IsValidRandomRequestSpecies(def)) continue;
            if (def.type != template.requiredRandomSpeciesType) continue;
            if ((int)def.rarity < (int)template.requiredMinRarity) continue;

            candidates.Add(def);
        }

        if (candidates.Count == 0)
            return null;

        int index = StableHash(speciesSeed) % candidates.Count;
        return candidates[index].id;
    }

    private static bool IsValidRandomRequestSpecies(MonsterDataSO def)
    {
        if (def == null || string.IsNullOrEmpty(def.id)) return false;
        if (def.uncatchable) return false;
        if (def.rarity == Rarity.Boss) return false;
        if (def.baseMarketValue <= 0) return false;
        return true;
    }

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
