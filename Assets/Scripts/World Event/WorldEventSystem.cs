using System;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Drives:
/// - Scheduled events (explicit start/end windows)
/// - Rotating announcements (timed roll) when no scheduled events exist
///
/// Also provides a single query surface for Jobs/Encounters to consume placeholder effects.
///
/// Notes:
/// - If no library is assigned/found, falls back to a small built-in placeholder set.
/// - World events are currently optional gameplay modifiers; most projects will start with ticker-only.
/// </summary>
public sealed class WorldEventSystem : MonoBehaviour
{
    public static WorldEventSystem I { get; private set; }

    [Header("Library")]
    [Tooltip("Optional. If null, will try Resources.Load<WorldEventLibrarySO>(\"WorldEvents/WorldEventLibrary\").")]
    [SerializeField] private WorldEventLibrarySO library;

    [Header("Rotation")]
    [Tooltip("If true, when no scheduled events are active, a single rotating event will be rolled.")]
    [SerializeField] private bool enableRotation = true;

    [SerializeField, Min(1f)] private float rotationDurationHours = 6f;
    [SerializeField, Min(0.25f)] private float rotationRollCheckSeconds = 5f;

    // Active
    private readonly List<WorldEventSO> _active = new();
    private WorldEventSO _rotationActive;

    // Cached effect totals (computed each refresh)
    private readonly HashSet<JobType> _disabledJobs = new();
    private readonly Dictionary<JobType, float> _jobRateMul = new();
    private bool _encountersDisabled;
    private float _encounterEnergyCostMul = 1f;
    private float _wildShinyMul = 1f;

    private float _accum;

    public IReadOnlyList<WorldEventSO> ActiveEvents => _active;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        DontDestroyOnLoad(gameObject);

        if (!library)
            library = Resources.Load<WorldEventLibrarySO>("WorldEvents/WorldEventLibrary");

        // Make sure save is available.
        if (SaveManager.Data == null) SaveManager.LoadOrCreate();

        RefreshNow(forceRollIfNeeded: true);
    }

    private void OnDestroy()
    {
        if (I == this) I = null;
    }

    private void Update()
    {
        _accum += Time.unscaledDeltaTime;
        if (_accum >= rotationRollCheckSeconds)
        {
            _accum = 0f;
            RefreshNow(forceRollIfNeeded: false);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Public queries (Jobs / Encounters)
    // ─────────────────────────────────────────────────────────────

    public bool IsJobSiteDisabled(JobType job) => _disabledJobs.Contains(job);

    public float GetJobRateMultiplier(JobType job)
        => _jobRateMul.TryGetValue(job, out var m) ? Mathf.Max(0f, m) : 1f;

    public bool AreEncountersDisabled() => _encountersDisabled;

    public float GetEncounterEnergyCostMultiplier() => Mathf.Max(0f, _encounterEnergyCostMul);

    public float GetWildShinyChanceMultiplier() => Mathf.Max(0f, _wildShinyMul);

    // ─────────────────────────────────────────────────────────────
    // Core refresh
    // ─────────────────────────────────────────────────────────────

    public void RefreshNow(bool forceRollIfNeeded)
    {
        long now = SaveManager.NowUnix();

        _active.Clear();
        _rotationActive = null;

        // 1) Scheduled
        var all = GetAllEvents();
        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (!e) continue;
            if (!e.scheduledOnly) continue;
            if (e.IsActiveNow(now)) _active.Add(e);
        }

        // 2) Rotation
        if (enableRotation && _active.Count == 0)
        {
            var blob = SaveManager.GetWorldEventsBlob() ?? new WorldEventSaveData();

            bool activeExpired = blob.rotationUntilUnix > 0 && now >= blob.rotationUntilUnix;
            bool needsRoll = string.IsNullOrEmpty(blob.rotationActiveEventId) || activeExpired;
            if (!needsRoll && forceRollIfNeeded == false)
            {
                // keep
            }
            else
            {
                if (forceRollIfNeeded || now >= blob.nextRotationRollUnix || activeExpired)
                {
                    RollRotationEvent(blob, all, now);
                    SaveManager.SetWorldEventsBlob(blob);
                }
            }

            if (!string.IsNullOrEmpty(blob.rotationActiveEventId))
            {
                _rotationActive = FindById(all, blob.rotationActiveEventId);
                if (_rotationActive) _active.Add(_rotationActive);
            }
        }

        // 3) Apply effects
        RebuildEffectCache();

        // 4) Push ticker
        PushTicker();

        GameEvents.WorldEventsChanged?.Invoke();
    }

    private void RollRotationEvent(WorldEventSaveData blob, List<WorldEventSO> all, long now)
    {
        var candidates = new List<WorldEventSO>(32);
        int weightSum = 0;

        for (int i = 0; i < all.Count; i++)
        {
            var e = all[i];
            if (!e) continue;
            if (e.scheduledOnly) continue;
            if (!e.canRotate) continue;
            if (e.weight <= 0) continue;

            if (e.minDaysBetween > 0f && WasRolledTooRecently(blob, e, now))
                continue;

            candidates.Add(e);
            weightSum += e.weight;
        }

        if (candidates.Count == 0 || weightSum <= 0)
        {
            blob.rotationActiveEventId = null;
            blob.rotationUntilUnix = 0;
            blob.nextRotationRollUnix = now + Mathf.RoundToInt(rotationDurationHours * 3600f);
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

        blob.rotationActiveEventId = picked.id;
        blob.rotationUntilUnix = now + Mathf.RoundToInt(rotationDurationHours * 3600f);
        blob.nextRotationRollUnix = blob.rotationUntilUnix;

        StampRolled(blob, picked.id, now);
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

    private void RebuildEffectCache()
    {
        _disabledJobs.Clear();
        _jobRateMul.Clear();
        _encountersDisabled = false;
        _encounterEnergyCostMul = 1f;
        _wildShinyMul = 1f;

        for (int i = 0; i < _active.Count; i++)
        {
            var e = _active[i];
            if (!e || e.effects == null) continue;

            for (int j = 0; j < e.effects.Count; j++)
            {
                var fx = e.effects[j];
                switch (fx.kind)
                {
                    case WorldEventEffectKind.DisableJobSite:
                        if (fx.job != JobType.None) _disabledJobs.Add(fx.job);
                        break;

                    case WorldEventEffectKind.JobRateMultiplier:
                        if (fx.job != JobType.None)
                        {
                            float v = fx.value == 0f ? 1f : fx.value;
                            _jobRateMul[fx.job] = _jobRateMul.TryGetValue(fx.job, out var cur) ? (cur * v) : v;
                        }
                        break;

                    case WorldEventEffectKind.DisableEncounters:
                        _encountersDisabled = true;
                        break;

                    case WorldEventEffectKind.EncounterEnergyCostMultiplier:
                        _encounterEnergyCostMul *= (fx.value == 0f ? 1f : fx.value);
                        break;

                    case WorldEventEffectKind.WildShinyChanceMultiplier:
                        _wildShinyMul *= (fx.value == 0f ? 1f : fx.value);
                        break;
                }
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

            WorldEventManager.I.Add(msg);
        }
    }

    private List<WorldEventSO> GetAllEvents()
    {
        if (library != null && library.events != null && library.events.Count > 0)
            return library.events;

        // Fallback placeholders (keeps system useful before you author assets).
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
}
