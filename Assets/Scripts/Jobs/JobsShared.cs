using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public enum JobType {
    Gym, Quarry, Mine, PowerPlant, Grove,
    Forge, Workshop, Harbor, CryoLab,
    Observatory, Containment, WyrmDen, ShadowMarket, Sanctum, Clinic, None
}

public enum SiteEffectKind {
    None,
    EncounterTypeWeight,
    CaptureBonus,
    RarityBonus,
    ShinyOrbs,
    StatBoostAttack,
    StatBoostHP,
    StatBoostSpeed,
    Sigils
}

public struct SiteEffect {
    public SiteEffectKind kind;
    public float value;
    public int uses;
    public MonsterType? target;
}

public static class JobStrings {
    public static string SiteName(JobType site) => site switch {
        JobType.Gym          => "Gym",
        JobType.Quarry       => "Quarry",
        JobType.Mine         => "Mine",
        JobType.PowerPlant   => "Power Plant",
        JobType.Grove        => "Grove",
        JobType.Forge        => "Forge",
        JobType.Workshop     => "Workshop",
        JobType.Harbor       => "Harbor",
        JobType.CryoLab      => "Cryo Lab",
        JobType.Observatory  => "Observatory",
        JobType.Containment  => "Containment",
        JobType.WyrmDen      => "Wyrm Den",
        JobType.ShadowMarket => "Shadow Market",
        JobType.Sanctum      => "Sanctum",
        JobType.Clinic       => "Clinic",
        _ => site.ToString()
    };

    public static string ResourceName(ResourceType t) => t switch {
        ResourceType.TrainingXP      => "Training XP",
        ResourceType.Coins           => "Coins",
        ResourceType.Energy          => "Energy",
        ResourceType.Medkits         => "Medkits",
        ResourceType.Materials       => "Materials",
        ResourceType.Sigils          => "Sigils",
        ResourceType.Lures           => "Lures",
        ResourceType.CaptureBands    => "Capture Bands",
        ResourceType.Luck            => "Luck",
        ResourceType.AttackBoosters  => "Attack Boosters",
        ResourceType.HPBoosters      => "HP Boosters",
        ResourceType.SpeedBoosters   => "Speed Boosters",
        ResourceType.ShinyOrbs       => "Shiny Orbs",
        ResourceType.BlessingTokens  => "Blessing Tokens",
        ResourceType.RestCharge      => "Rest Charge",
        _ => t.ToString()
    };
}

public static class JobOutput
{
    public static ResourceType Output(JobType site) => site switch {
        JobType.Gym         => ResourceType.TrainingXP,
        JobType.Quarry      => ResourceType.Coins,
        JobType.Mine        => ResourceType.AttackBoosters,
        JobType.PowerPlant  => ResourceType.Energy,
        JobType.Grove       => ResourceType.Medkits,
        JobType.Forge       => ResourceType.Materials,
        JobType.Workshop    => ResourceType.Sigils,
        JobType.Harbor      => ResourceType.Lures,
        JobType.CryoLab     => ResourceType.CaptureBands,
        JobType.Observatory => ResourceType.HPBoosters,
        JobType.Containment => ResourceType.SpeedBoosters,
        JobType.WyrmDen     => ResourceType.Luck,
        JobType.ShadowMarket=> ResourceType.ShinyOrbs,
        JobType.Sanctum     => ResourceType.BlessingTokens,
        JobType.Clinic      => ResourceType.RestCharge,
        _ => ResourceType.Coins
    };

    public static SiteEffect? Effect(JobType site) => site switch {
        JobType.Harbor       => new SiteEffect { kind = SiteEffectKind.EncounterTypeWeight, value = 0.30f, uses = 3, target = null },
        JobType.CryoLab      => new SiteEffect { kind = SiteEffectKind.CaptureBonus,       value = 0.10f, uses = 3, target = null },
        JobType.WyrmDen      => new SiteEffect { kind = SiteEffectKind.RarityBonus,        value = 0.05f, uses = 3, target = null },
        JobType.Containment  => new SiteEffect { kind = SiteEffectKind.StatBoostSpeed,     value = 0.15f, uses = 1,  target = null },
        JobType.ShadowMarket => new SiteEffect { kind = SiteEffectKind.ShinyOrbs,       value = 0.10f, uses = 1,  target = null },
        JobType.Mine         => new SiteEffect { kind = SiteEffectKind.StatBoostAttack,    value = 5f,   uses = 1,  target = null },
        JobType.Observatory  => new SiteEffect { kind = SiteEffectKind.StatBoostHP,        value = 10f,  uses = 1,  target = null },
        JobType.Workshop     => new SiteEffect { kind = SiteEffectKind.Sigils,              value = 0.25f, uses = 3,  target = null },
        _ => null
    };
}

public static class JobBalance
{
    private const float BEST = 1.5f;
    private const float NEUTRAL = 1.0f;
    private const float OFF = 0.9f;

    private static readonly Dictionary<JobType, float> _basePerHour = new() {
        { JobType.Gym,          60f },
        { JobType.Quarry,       80f },
        { JobType.Mine,          1f },
        { JobType.PowerPlant,   20f },
        { JobType.Grove,         6f },
        { JobType.Forge,       120f },
        { JobType.Workshop,      2f },
        { JobType.Harbor,        2f },
        { JobType.CryoLab,       2f },
        { JobType.Observatory,   1f },
        { JobType.Containment, 0.5f },
        { JobType.WyrmDen,     0.5f },
        { JobType.ShadowMarket,  1f },
        { JobType.Sanctum,     0.25f },
        { JobType.Clinic,      0.25f }
    };

    private static readonly Dictionary<JobType, HashSet<MonsterType>> _bestTypes = new() {
        { JobType.Gym,          new() { MonsterType.Clash } },
        { JobType.Quarry,       new() { MonsterType.Ground, MonsterType.Rock } },
        { JobType.Mine,         new() { MonsterType.Rock } },
        { JobType.PowerPlant,   new() { MonsterType.Electric } },
        { JobType.Grove,        new() { MonsterType.Grass, MonsterType.Bug} },
        { JobType.Forge,        new() { MonsterType.Fire, MonsterType.Alloy } },
        { JobType.Workshop,     new() { MonsterType.Alloy } },
        { JobType.Harbor,       new() { MonsterType.Water, MonsterType.Sky } },
        { JobType.CryoLab,      new() { MonsterType.Ice } },
        { JobType.Observatory,  new() { MonsterType.Oracle } },
        { JobType.Containment,  new() { MonsterType.Corrupt } },
        { JobType.WyrmDen,      new() { MonsterType.Wyrm } },
        { JobType.ShadowMarket, new() { MonsterType.Umbral, MonsterType.Specter } },
        { JobType.Sanctum,      new() { MonsterType.Specter } },
        { JobType.Clinic,       new() { MonsterType.Sky } },
    };

    private static readonly Dictionary<JobType, HashSet<MonsterType>> _offTypes = new() {
        { JobType.Quarry, new() { MonsterType.Grass } },
        { JobType.Grove,  new() { MonsterType.Fire } },
    };

    public static float GetBasePerHour(JobType job)
        => _basePerHour.TryGetValue(job, out var v) ? v : 60f;

    public static float AffinityMult(JobType job, MonsterType type)
    {
        if (_bestTypes.TryGetValue(job, out var best) && best.Contains(type)) return BEST;
        if (_offTypes.TryGetValue(job, out var off) && off.Contains(type)) return OFF;
        return NEUTRAL;
    }

    public static float RarityMult(Rarity r) => r switch
    {
        Rarity.Uncommon => 1.10f,
        Rarity.Rare => 1.25f,
        Rarity.Epic => 1.50f,
        Rarity.Legendary => 1.80f,
        Rarity.Mythic => 2.20f,
        _ => 1f
    };

    public static float EvolutionMult(int stage) => stage switch
    {
        2 => 1.15f,
        3 => 1.35f,
        _ => 1f
    };

    public static int Tick(
        JobType job,
        MonsterType workerType,
        float deltaTimeSeconds,
        ref float carryProgress,
        Rarity rarity = Rarity.Common,
        int evolutionStage = 1)
    {
        float perHour =
            GetBasePerHour(job)
            * AffinityMult(job, workerType)
            * RarityMult(rarity)
            * EvolutionMult(evolutionStage);

        float perSecond = perHour / 3600f;

        float raw = perSecond * deltaTimeSeconds + carryProgress;
        int whole = (int)raw;
        carryProgress = raw - whole;
        return whole;
    }
    

    public static int GetActiveWorkers(JobType job)
    {
        if (JobManager.I == null || JobManager.I.States == null) return 0;

        var state = JobManager.I.States.FirstOrDefault(s => s != null && s.config != null && s.config.jobType == job);
        if (state == null || state.workers == null) return 0;

        int active = 0;
        for (int i = 0; i < state.workers.Count; i++)
        {
            var w = state.workers[i];
            if (w != null && w.def != null) active++;
        }
        return active;
    }


    public static float GetWyrmDenRarityWeightMult(Rarity r)
    {
        int active = GetActiveWorkers(JobType.WyrmDen);
        if (active <= 0) return 1f;

        const float legendaryPerWorker = 0.25f;
        const float mythicPerWorker    = 0.12f;
        const float hardCap            = 5f;

        float mult = 1f;
        switch (r)
        {
            case Rarity.Legendary: mult += legendaryPerWorker * active; break;
            case Rarity.Mythic:    mult += mythicPerWorker    * active; break;
            default:               mult = 1f; break;
        }

        if (mult > hardCap) mult = hardCap;
        if (mult < 0f)      mult = 0f;
        return mult;
    }
}
