using System.Collections.Generic;
using UnityEngine;

public static class BuiltInFallbackEvents
{
    private static List<WorldEventSO> _cache;

    public static List<WorldEventSO> Get()
    {
        if (_cache != null) return _cache;

        _cache = new List<WorldEventSO>(12)
        {
            Make("WE-001", "Cryo Lab Maintenance", "🧊 Cryo Lab maintenance week — Cryo Lab is offline.",
                new WorldEventEffect { kind = WorldEventEffectKind.DisableJobSite, job = JobType.Cryo_Lab, value = 1f }),

            Make("WE-002", "Harbor Strike", "⚓ Harbor strike reported — delays expected at Harbor.",
                new WorldEventEffect { kind = WorldEventEffectKind.JobRateMultiplier, job = JobType.Harbor, value = 0.75f }),

            Make("WE-003", "Power Plant Inspection", "⚡ Power Plant inspection — output temporarily reduced.",
                new WorldEventEffect { kind = WorldEventEffectKind.JobRateMultiplier, job = JobType.Power_Plant, value = 0.80f }),

            Make("WE-004", "Clinic Supply Shortage", "🩺 Clinic supply shortage — relief services limited.",
                new WorldEventEffect { kind = WorldEventEffectKind.JobRateMultiplier, job = JobType.Clinic, value = 0.90f }),

            Make("WE-005", "Containment Drill", "🧪 Containment drill underway — stay alert.", default),

            Make("WE-006", "Premium Surge", "✨ Premium surge reported — odd sparkles in the wild.",
                new WorldEventEffect { kind = WorldEventEffectKind.WildPremiumChanceMultiplier, value = 2.0f }),

            Make("WE-007", "Citywide Curfew", "🚧 Citywide curfew — encounters temporarily suspended.",
                new WorldEventEffect { kind = WorldEventEffectKind.DisableEncounters, value = 1f }),
        };

        // Rotation-friendly defaults
        for (int i = 0; i < _cache.Count; i++)
        {
            if (!_cache[i]) continue;
            _cache[i].scheduledOnly = false;
            _cache[i].canRotate = true;
            _cache[i].weight = 1;
            _cache[i].minDaysBetween = 0.5f;
        }

        return _cache;
    }

    private static WorldEventSO Make(string id, string name, string ticker, WorldEventEffect fx = default)
    {
        var e = ScriptableObject.CreateInstance<WorldEventSO>();
        e.id = id;
        e.displayName = name;
        e.tickerMessage = ticker;
        e.effects = new List<WorldEventEffect>();
        if (fx.kind != WorldEventEffectKind.None)
            e.effects.Add(fx);
        return e;
    }
}
