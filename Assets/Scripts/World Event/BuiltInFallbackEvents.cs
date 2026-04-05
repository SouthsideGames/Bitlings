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
            Make("WE-001", "Cryo Lab Maintenance",
                ticker:      "🧊 Cryo Lab maintenance week — Cryo Lab is offline.",
                description: "The Cryo Lab is undergoing mandatory maintenance. Cold storage operations are suspended for the week.",
                fx: new WorldEventEffect { kind = WorldEventEffectKind.DisableJobSite, job = JobType.Cryo_Lab, value = 1f }),

            Make("WE-002", "Harbor Strike",
                ticker:      "⚓ Harbor strike reported — delays expected at Harbor.",
                description: "Dock workers have staged a strike. Harbor output rates are reduced until the dispute is resolved.",
                fx: new WorldEventEffect { kind = WorldEventEffectKind.JobRateMultiplier, job = JobType.Harbor, value = 0.75f }),

            Make("WE-003", "Power Plant Inspection",
                ticker:      "⚡ Power Plant inspection — output temporarily reduced.",
                description: "A scheduled safety inspection is underway at the Power Plant. Expect reduced output this week.",
                fx: new WorldEventEffect { kind = WorldEventEffectKind.JobRateMultiplier, job = JobType.Power_Plant, value = 0.80f }),

            Make("WE-004", "Clinic Supply Shortage",
                ticker:      "🩺 Clinic supply shortage — relief services limited.",
                description: "Supply chain disruptions have hit the Clinic. Medical relief operations are running at reduced capacity.",
                fx: new WorldEventEffect { kind = WorldEventEffectKind.JobRateMultiplier, job = JobType.Clinic, value = 0.90f }),

            Make("WE-005", "Containment Drill",
                ticker:      "🧪 Containment drill underway — stay alert.",
                description: "A city-wide containment drill is in effect. All units are on standby. No disruptions reported yet."),

            Make("WE-006", "Premium Surge",
                ticker:      "✨ Premium surge reported — odd sparkles in the wild.",
                description: "Unusual energy readings are amplifying rare traits in wild Bitlings. Premium encounter rates are doubled this week.",
                fx: new WorldEventEffect { kind = WorldEventEffectKind.WildPremiumChanceMultiplier, value = 2.0f }),

            Make("WE-007", "Citywide Curfew",
                ticker:      "🚧 Citywide curfew — encounters temporarily suspended.",
                description: "Authorities have issued a citywide curfew. Wild Bitling encounters are suspended until further notice.",
                fx: new WorldEventEffect { kind = WorldEventEffectKind.DisableEncounters, value = 1f }),

            Make("WE-008", "Battle Frenzy",
                ticker:      "⚔️ Battle Frenzy week — enhanced battle rewards.",
                description: "Something in the air is driving Bitlings to fight harder. All battle reward payouts are boosted this week.",
                battleRewardMultiplier: 1.5f),

            Make("WE-009", "Idle Boom",
                ticker:      "💤 Idle Boom week — idle reward boost active.",
                description: "Passive income channels are surging across the city. Idle reward multipliers are elevated for the week.",
                idleRewardMultiplier: 1.5f),

            Make("WE-010", "Market Rally",
                ticker:      "📈 Market Rally — exchange values are up.",
                description: "Investor sentiment is high and exchange valuations are climbing. Sell your Bitlings for more this week.",
                exchangeValueMultiplier: 1.4f),

            Make("WE-011", "Fire Surge",
                ticker:      "🔥 Fire Surge — Fire types deal bonus damage.",
                description: "Volcanic activity has energised Fire-type Bitlings worldwide. Fire types deal increased damage in battle this week.",
                boostedMonsterType: MonsterType.Fire,
                typeDamageMultiplier: 1.5f),

            Make("WE-012", "Aqua Tide",
                ticker:      "🌊 Aqua Tide — Water types deal bonus damage.",
                description: "Tidal surges have empowered Water-type Bitlings. Water types deal increased damage in battle this week.",
                boostedMonsterType: MonsterType.Water,
                typeDamageMultiplier: 1.5f),
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

    private static WorldEventSO Make(
        string id,
        string name,
        string ticker,
        string description                   = "",
        WorldEventEffect fx                  = default,
        float idleRewardMultiplier           = 1f,
        float battleRewardMultiplier         = 1f,
        float exchangeValueMultiplier        = 1f,
        MonsterType boostedMonsterType       = MonsterType.None,
        float typeDamageMultiplier           = 1f)
    {
        var e = ScriptableObject.CreateInstance<WorldEventSO>();
        e.id          = id;
        e.displayName = name;
        e.tickerMessage = ticker;
        e.description = description;
        e.effects     = new List<WorldEventEffect>();
        if (fx.kind != WorldEventEffectKind.None)
            e.effects.Add(fx);

        e.idleRewardMultiplier    = idleRewardMultiplier;
        e.battleRewardMultiplier  = battleRewardMultiplier;
        e.exchangeValueMultiplier = exchangeValueMultiplier;
        e.boostedMonsterType      = boostedMonsterType;
        e.typeDamageMultiplier    = typeDamageMultiplier;

        return e;
    }
}
