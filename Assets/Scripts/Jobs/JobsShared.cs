// Assets/Scripts/Jobs/JobShared.cs
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

public enum JobType {
    Gym = 0, Quarry = 1, Mine = 2, Power_Plant = 3, Grove = 4,
    Forge = 5, Workshop = 6, Harbor = 7, Cryo_Lab = 8,
    Observatory = 9, Containment = 10, Wyrm_Den = 11, Shadow_Market = 12, Sanctum = 13, Clinic = 14,
    None = 15,
    Expedition = 16 
}

public enum SiteEffectKind {
    None,
    EncounterTypeWeight,
    CaptureBonus,
    RarityBonus,
    ShinyOrb,
    StatBoostAttack,
    StatBoostHP,
    StatBoostSpeed,
    TypeResBoosters
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
        JobType.Power_Plant   => "Power Plant",
        JobType.Grove        => "Grove",
        JobType.Forge        => "Forge",
        JobType.Workshop     => "Workshop",
        JobType.Harbor       => "Harbor",
        JobType.Cryo_Lab      => "Cryo Lab",
        JobType.Observatory  => "Observatory",
        JobType.Containment  => "Containment",
        JobType.Wyrm_Den      => "Wyrm Den",
        JobType.Shadow_Market => "Shadow Market",
        JobType.Sanctum      => "Sanctum",
        JobType.Clinic       => "Clinic",
        JobType.Expedition   => "Expedition",

        _ => site.ToString()
    };

    public static string ResourceName(ResourceType t) => t switch {
        ResourceType.GrowthCore             => "Growth Core",
        ResourceType.Credits                => "Credit",
        ResourceType.Energy                 => "Energy",
        ResourceType.Medkit                 => "Medkit",
        ResourceType.Material               => "Material",
        ResourceType.PPEPermit              => "PPE Permit",
        ResourceType.Flyer                  => "Flyer",
        ResourceType.WorkOrder              => "Work Order",
        ResourceType.Favor                  => "Luck",
        ResourceType.TrainingVoucher        => "Training Voucher",
        ResourceType.WellnessVoucher        => "Wellness Voucher",
        ResourceType.EfficiencyVoucher      => "Efficiency Voucher",
        ResourceType.ShinyOrb               => "Shiny Orb",
        ResourceType.BlessingScale          => "Blessing Scale",
        ResourceType.Coffee                 => "Rest Charge",
        ResourceType.PackVoucher            => "Pack Voucher",
        _ => t.ToString()
    };
}

public static class JobOutput
{
    public static ResourceType Output(JobType site) => site switch {
        JobType.Gym         => ResourceType.GrowthCore,
        JobType.Quarry      => ResourceType.Credits,
        JobType.Mine        => ResourceType.TrainingVoucher,
        JobType.Power_Plant  => ResourceType.Energy,
        JobType.Grove       => ResourceType.Medkit,
        JobType.Forge       => ResourceType.Material,
        JobType.Workshop    => ResourceType.PPEPermit,
        JobType.Harbor      => ResourceType.Flyer,
        JobType.Cryo_Lab     => ResourceType.WorkOrder,
        JobType.Observatory => ResourceType.WellnessVoucher,
        JobType.Containment => ResourceType.EfficiencyVoucher,
        JobType.Wyrm_Den     => ResourceType.Favor,
        JobType.Shadow_Market=> ResourceType.ShinyOrb,
        JobType.Sanctum     => ResourceType.BlessingScale,
        JobType.Clinic      => ResourceType.Coffee,
        JobType.Expedition  => ResourceType.PackVoucher,

        _ => ResourceType.Credits
    };

    public static SiteEffect? Effect(JobType site) => site switch {
        JobType.Harbor       => new SiteEffect { kind = SiteEffectKind.EncounterTypeWeight, value = 0.30f, uses = 3, target = null },
        JobType.Cryo_Lab      => new SiteEffect { kind = SiteEffectKind.CaptureBonus,        value = 0.10f, uses = 3, target = null },
        JobType.Wyrm_Den      => new SiteEffect { kind = SiteEffectKind.RarityBonus,         value = 0.05f, uses = 3, target = null },
        JobType.Containment  => new SiteEffect { kind = SiteEffectKind.StatBoostSpeed,      value = 0.15f, uses = 1,  target = null },
        JobType.Shadow_Market => new SiteEffect { kind = SiteEffectKind.ShinyOrb,           value = 0.10f, uses = 1,  target = null },
        JobType.Mine         => new SiteEffect { kind = SiteEffectKind.StatBoostAttack,     value = 5f,    uses = 1,  target = null },
        JobType.Observatory  => new SiteEffect { kind = SiteEffectKind.StatBoostHP,         value = 10f,   uses = 1,  target = null },
        JobType.Workshop     => new SiteEffect { kind = SiteEffectKind.TypeResBoosters,     value = 0.25f, uses = 3,  target = null },
        _ => null
    };
}

public static class JobBalance
{
    private const float BEST = 1.5f;
    private const float NEUTRAL = 1.0f;
    private const float OFF = 0.9f;

    private static readonly Dictionary<JobType, float> _basePerHour = new() {
        { JobType.Gym,          6f },
        { JobType.Quarry,       80f },
        { JobType.Mine,          1f },
        { JobType.Power_Plant,   20f },
        { JobType.Grove,         6f },
        { JobType.Forge,       120f },
        { JobType.Workshop,      2f },
        { JobType.Harbor,        2f },
        { JobType.Cryo_Lab,       2f },
        { JobType.Observatory,   1f },
        { JobType.Containment,  0.5f },
        { JobType.Wyrm_Den,      0.5f },
        { JobType.Shadow_Market,  1f },
        { JobType.Sanctum,      0.25f },
        { JobType.Clinic,       0.25f },
        { JobType.Expedition,    1.0f },
    };

    private static readonly Dictionary<JobType, HashSet<MonsterType>> _bestTypes = new() {
        { JobType.Gym,          new() { MonsterType.Clash } },
        { JobType.Quarry,       new() { MonsterType.Ground } },
        { JobType.Mine,         new() { MonsterType.Rock } },
        { JobType.Power_Plant,   new() { MonsterType.Electric } },
        { JobType.Grove,        new() { MonsterType.Grass} },
        { JobType.Forge,        new() { MonsterType.Fire } },
        { JobType.Workshop,     new() { MonsterType.Alloy } },
        { JobType.Harbor,       new() { MonsterType.Water } },
        { JobType.Cryo_Lab,      new() { MonsterType.Ice } },
        { JobType.Observatory,  new() { MonsterType.Oracle } },
        { JobType.Containment,  new() { MonsterType.Corrupt } },
        { JobType.Wyrm_Den,      new() { MonsterType.Wyrm } },
        { JobType.Shadow_Market, new() { MonsterType.Umbral } },
        { JobType.Sanctum,      new() { MonsterType.Specter } },
        { JobType.Clinic,       new() { MonsterType.Sky } },
        { JobType.Expedition,   new() { MonsterType.Bug  }  },
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
        Rarity.Uncommon  => 1.10f,
        Rarity.Rare      => 1.25f,
        Rarity.Epic      => 1.50f,
        Rarity.Legendary => 1.80f,
        Rarity.Mythic    => 2.20f,
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
        int active = GetActiveWorkers(JobType.Wyrm_Den);
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

    public static IEnumerable<JobType> JobsUnlockedByType(MonsterType type)
    {
        if (_bestTypes == null || _bestTypes.Count == 0)
            yield break;

        foreach (var kv in _bestTypes)
            if (kv.Value != null && kv.Value.Contains(type))
                yield return kv.Key;
    }

    public static bool IsTypeAllowedStrict(JobType job, MonsterType type)
    {
        return _bestTypes != null
            && _bestTypes.TryGetValue(job, out var set)
            && set != null
            && set.Contains(type);
    }

    public static bool TryGetAllowedTypes(JobType job, out HashSet<MonsterType> set)
    {
        if (_bestTypes != null && _bestTypes.TryGetValue(job, out var s) && s != null)
        {
            set = s;
            return true;
        }
        set = null;
        return false;
    }

    public static MonsterType[] AllowedTypesFor(JobType job)
    {
        return TryGetAllowedTypes(job, out var set) ? new List<MonsterType>(set).ToArray() : System.Array.Empty<MonsterType>();
    }
}
