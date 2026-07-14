// Assets/Scripts/Arena/ArenaBotTemplateLibrary.cs
// BRN Arena v1 — Curated bot template repository.
// Stores 80 bot team templates (20 blueprints × 4 score bands) built from
// the monster catalog at first access.  Designers can tune blueprints,
// name pools, and rarity-to-band mappings without touching generator code.

using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static repository of 80 curated <see cref="ArenaBotTeamTemplate"/>s.
/// Templates are built lazily from 20 archetype blueprints × 4 score bands.
/// Monster pools are sourced from <see cref="MonsterCatalog"/> by type and rarity tier.
/// Title pools are harvested from each monster's title references.
/// </summary>
public static class ArenaBotTemplateLibrary
{
    // ═════════════════════════════════════════════════════════════
    //  Public API
    // ═════════════════════════════════════════════════════════════

    /// <summary>All 80 templates.  Built lazily on first access.</summary>
    public static IReadOnlyList<ArenaBotTeamTemplate> All
    {
        get { EnsureBuilt(); return _allTemplates; }
    }

    /// <summary>Returns copies of all templates targeting the given score band (up to 20).</summary>
    public static List<ArenaBotTeamTemplate> GetTemplatesForBand(ArenaScoreBand band)
    {
        EnsureBuilt();
        if (_byBand.TryGetValue(band, out var list))
            return new List<ArenaBotTeamTemplate>(list);
        return new List<ArenaBotTeamTemplate>();
    }

    /// <summary>Returns templates matching both band and archetype.</summary>
    public static List<ArenaBotTeamTemplate> GetTemplatesForBandAndArchetype(
        ArenaScoreBand band, ArenaBotArchetype archetype)
    {
        var templates = GetTemplatesForBand(band);
        templates.RemoveAll(t => t.archetype != archetype);
        return templates;
    }

    /// <summary>Forces a full rebuild on next access (e.g. after catalog changes).</summary>
    public static void Invalidate()
    {
        _allTemplates = null;
        _byBand = null;
        _built = false;
    }

    // ═════════════════════════════════════════════════════════════
    //  Internal state
    // ═════════════════════════════════════════════════════════════

    private static List<ArenaBotTeamTemplate> _allTemplates;
    private static Dictionary<ArenaScoreBand, List<ArenaBotTeamTemplate>> _byBand;
    private static bool _built;

    // ═════════════════════════════════════════════════════════════
    //  Blueprint definition
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Compact description of a bot team composition.
    /// Each blueprint is expanded into 4 templates (one per score band).
    /// </summary>
    private struct TeamBlueprint
    {
        public ArenaBotArchetype archetype;
        public MonsterType type1;
        public MonsterType type2;
        public MonsterType type3;
        public int synergyHint;          // designer estimate (0-3)
        public bool allowDuplicateSpecies;
    }

    /// <summary>
    /// 20 curated team composition blueprints (4 per archetype).
    /// Each blueprint × 4 score bands = 4 concrete templates → 80 total.
    /// Designers: edit these to control bot team compositions.
    /// </summary>
    private static readonly TeamBlueprint[] Blueprints =
    {
        // ── Balanced (4) ────────────────────────────────────────
        new TeamBlueprint { archetype = ArenaBotArchetype.Balanced,     type1 = MonsterType.Fire,     type2 = MonsterType.Water,    type3 = MonsterType.Grass,    synergyHint = 2 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Balanced,     type1 = MonsterType.Electric, type2 = MonsterType.Ground,   type3 = MonsterType.Sky,      synergyHint = 2 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Balanced,     type1 = MonsterType.Rock,     type2 = MonsterType.Ice,      type3 = MonsterType.Clash,    synergyHint = 1 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Balanced,     type1 = MonsterType.Oracle,   type2 = MonsterType.Alloy,    type3 = MonsterType.Bug,      synergyHint = 1 },

        // ── Aggressive (4) ──────────────────────────────────────
        new TeamBlueprint { archetype = ArenaBotArchetype.Aggressive,   type1 = MonsterType.Fire,     type2 = MonsterType.Electric, type3 = MonsterType.Clash,    synergyHint = 1 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Aggressive,   type1 = MonsterType.Wyrm,     type2 = MonsterType.Corrupt,  type3 = MonsterType.Fire,     synergyHint = 1 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Aggressive,   type1 = MonsterType.Electric, type2 = MonsterType.Sky,      type3 = MonsterType.Corrupt,  synergyHint = 1 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Aggressive,   type1 = MonsterType.Clash,    type2 = MonsterType.Wyrm,     type3 = MonsterType.Specter,  synergyHint = 1 },

        // ── Defensive (4) ───────────────────────────────────────
        new TeamBlueprint { archetype = ArenaBotArchetype.Defensive,    type1 = MonsterType.Rock,     type2 = MonsterType.Ground,   type3 = MonsterType.Alloy,    synergyHint = 2 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Defensive,    type1 = MonsterType.Ice,      type2 = MonsterType.Rock,     type3 = MonsterType.Ground,   synergyHint = 2 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Defensive,    type1 = MonsterType.Alloy,    type2 = MonsterType.Water,    type3 = MonsterType.Ice,      synergyHint = 2 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Defensive,    type1 = MonsterType.Ground,   type2 = MonsterType.Grass,    type3 = MonsterType.Rock,     synergyHint = 1 },

        // ── TypeSynergy (4) ─────────────────────────────────────
        new TeamBlueprint { archetype = ArenaBotArchetype.TypeSynergy,  type1 = MonsterType.Fire,     type2 = MonsterType.Water,    type3 = MonsterType.Grass,    synergyHint = 3 },
        new TeamBlueprint { archetype = ArenaBotArchetype.TypeSynergy,  type1 = MonsterType.Electric, type2 = MonsterType.Ground,   type3 = MonsterType.Sky,      synergyHint = 3 },
        new TeamBlueprint { archetype = ArenaBotArchetype.TypeSynergy,  type1 = MonsterType.Wyrm,     type2 = MonsterType.Oracle,   type3 = MonsterType.Umbral,   synergyHint = 2 },
        new TeamBlueprint { archetype = ArenaBotArchetype.TypeSynergy,  type1 = MonsterType.Specter,  type2 = MonsterType.Clash,    type3 = MonsterType.Alloy,    synergyHint = 2 },

        // ── Wildcard (4) ────────────────────────────────────────
        new TeamBlueprint { archetype = ArenaBotArchetype.Wildcard,     type1 = MonsterType.Specter,  type2 = MonsterType.Umbral,   type3 = MonsterType.Corrupt,  synergyHint = 0 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Wildcard,     type1 = MonsterType.Bug,      type2 = MonsterType.Oracle,   type3 = MonsterType.Wyrm,     synergyHint = 1 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Wildcard,     type1 = MonsterType.Alloy,    type2 = MonsterType.Sky,      type3 = MonsterType.Specter,  synergyHint = 0 },
        new TeamBlueprint { archetype = ArenaBotArchetype.Wildcard,     type1 = MonsterType.Ice,      type2 = MonsterType.Corrupt,  type3 = MonsterType.Oracle,   synergyHint = 0 },
    };

    // ═════════════════════════════════════════════════════════════
    //  Rarity → Band mapping
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Accepted monster rarities per score band, ordered by preference
    /// (first = most preferred).  Boss rarity is never included.
    /// Designers: adjust to control bot difficulty scaling across bands.
    /// </summary>
    private static readonly Rarity[][] BandRarityTiers =
    {
        /* Low      */ new[] { Rarity.Common, Rarity.Uncommon },
        /* Standard */ new[] { Rarity.Uncommon, Rarity.Common, Rarity.Rare },
        /* High     */ new[] { Rarity.Rare, Rarity.Uncommon, Rarity.Epic },
        /* Elite    */ new[] { Rarity.Epic, Rarity.Legendary, Rarity.Mythic, Rarity.Rare },
    };

    // ═════════════════════════════════════════════════════════════
    //  Display name pools (per archetype)
    // ═════════════════════════════════════════════════════════════

    private static readonly string[] BalancedNames =
    {
        "TrainerAlex",   "BitFan99",      "CalmStorm",     "SteadyEdge",
        "TeamPlayer7",   "MidLaner",      "Equalizer22",   "CoreBalance",
        "ZenFighter",    "AllRounder",    "TacticsGuy",    "TeamBuild",
        "WellRounded",   "FairPlay42",    "Classico11",    "StrategyK",
        "SolidCore",     "MasterPlan",    "PrimeTeam",     "NeutralGnd"
    };

    private static readonly string[] AggressiveNames =
    {
        "BlazeRunner",   "StormChaser",   "AttackMode",    "FullSend77",
        "NoMercy88",     "RushDown",      "AlphaStrike",   "RedZone",
        "MaxDamage",     "OffenseOnly",   "HardHitter",    "SpeedKill",
        "BurstFire",     "Relentless",    "GlassCanon",    "PureAggro",
        "ChargeFwd",     "NukeSquad",     "BurnItAll",     "FirstBlood"
    };

    private static readonly string[] DefensiveNames =
    {
        "WallMaster",    "FortressK",     "IronShield",    "TankMode",
        "SafePlay",      "Stonewall55",   "EndureAll",     "ShieldBash",
        "BrickHouse",    "LastStand",     "HardShell",     "Unmovable",
        "TurtleUp",      "MaxDefense",    "BunkerDown",    "ArmorPlate",
        "HoldTheLine",   "NeverYield",    "CastleKing",    "Bulwark99"
    };

    private static readonly string[] TypeSynergyNames =
    {
        "TypeAce",       "SynergyPro",    "CoverageMax",   "TypeMaster",
        "PerfectTri",    "ElementPro",    "TriForce",      "TypeGenius",
        "FullCover",     "ChainLink",     "ComboKing",     "MatchType",
        "TypeLord",      "SynergyGod",    "TripleThreat",  "FullSpread",
        "TypeChart",     "OptimalPick",   "PerfectMix",    "CoverAll"
    };

    private static readonly string[] WildcardNames =
    {
        "ChaosLord",     "RNGKing",       "Wildcard42",    "DarkHorse",
        "Curveball",     "Unpredicted",   "GlitchMob",     "RandomPick",
        "MadGenius",     "Wildfire",      "JokerCard",     "NoRules",
        "FreeStyle",     "UnexpectedX",   "CuriousMix",    "RogueAgent",
        "OddOne",        "Maverick55",    "OffMeta",       "Surprise99"
    };

    private static string[] GetNamePool(ArenaBotArchetype archetype)
    {
        switch (archetype)
        {
            case ArenaBotArchetype.Balanced:     return BalancedNames;
            case ArenaBotArchetype.Aggressive:   return AggressiveNames;
            case ArenaBotArchetype.Defensive:    return DefensiveNames;
            case ArenaBotArchetype.TypeSynergy:  return TypeSynergyNames;
            case ArenaBotArchetype.Wildcard:     return WildcardNames;
            default:                             return BalancedNames;
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Visibility mode presets per band
    // ═════════════════════════════════════════════════════════════

    private static List<ArenaVisibilityMode> GetVisibilityOptions(ArenaScoreBand band)
    {
        switch (band)
        {
            case ArenaScoreBand.Low:
            case ArenaScoreBand.Standard:
                return new List<ArenaVisibilityMode> { ArenaVisibilityMode.FullReveal };

            case ArenaScoreBand.High:
                return new List<ArenaVisibilityMode>
                {
                    ArenaVisibilityMode.FullReveal,
                    ArenaVisibilityMode.FullReveal,
                    ArenaVisibilityMode.LimitedReveal
                };

            case ArenaScoreBand.Elite:
                return new List<ArenaVisibilityMode>
                {
                    ArenaVisibilityMode.FullReveal,
                    ArenaVisibilityMode.LimitedReveal,
                    ArenaVisibilityMode.LimitedReveal
                };

            default:
                return new List<ArenaVisibilityMode> { ArenaVisibilityMode.FullReveal };
        }
    }

    // ═════════════════════════════════════════════════════════════
    //  Build logic
    // ═════════════════════════════════════════════════════════════

    private static void EnsureBuilt()
    {
        if (_built) return;
        _built = true;
        BuildAllTemplates();
    }

    /// <summary>
    /// Expands 20 blueprints × 4 bands into 80 concrete <see cref="ArenaBotTeamTemplate"/>s.
    /// </summary>
    private static void BuildAllTemplates()
    {
        _allTemplates = new List<ArenaBotTeamTemplate>(BlueprintCount * BandCount);
        _byBand = new Dictionary<ArenaScoreBand, List<ArenaBotTeamTemplate>>();

        var bands = AllBands;
        for (int b = 0; b < bands.Length; b++)
            _byBand[bands[b]] = new List<ArenaBotTeamTemplate>(BlueprintCount);

        for (int bp = 0; bp < Blueprints.Length; bp++)
        {
            for (int bi = 0; bi < bands.Length; bi++)
            {
                var template = BuildTemplate(Blueprints[bp], bands[bi], bp);
                _allTemplates.Add(template);
                _byBand[bands[bi]].Add(template);
            }
        }

        DevLog.Log($"[ArenaBotTemplateLibrary] Built {_allTemplates.Count} bot templates " +
                  $"({Blueprints.Length} blueprints × {bands.Length} bands).");
    }

    private const int BlueprintCount = 20;
    private const int BandCount = 4;

    private static readonly ArenaScoreBand[] AllBands =
    {
        ArenaScoreBand.Low,
        ArenaScoreBand.Standard,
        ArenaScoreBand.High,
        ArenaScoreBand.Elite
    };

    /// <summary>
    /// Creates a single concrete template from a blueprint + score band.
    /// Monster pools are sourced from <see cref="MonsterCatalog"/> by type and rarity tier.
    /// Title pools are harvested from the referenced monsters' SO data.
    /// </summary>
    private static ArenaBotTeamTemplate BuildTemplate(
        TeamBlueprint bp, ArenaScoreBand band, int blueprintIndex)
    {
        string templateId = $"BOT_{bp.archetype}_{band}_{blueprintIndex}";

        var slot1Monsters = GetMonstersForTypeAndBand(bp.type1, band);
        var slot2Monsters = GetMonstersForTypeAndBand(bp.type2, band);
        var slot3Monsters = GetMonstersForTypeAndBand(bp.type3, band);

        return new ArenaBotTeamTemplate
        {
            templateId = templateId,
            archetype = bp.archetype,
            scoreBand = band,
            displayNamePool = new List<string>(GetNamePool(bp.archetype)),
            slot1MonsterOptions = slot1Monsters,
            slot2MonsterOptions = slot2Monsters,
            slot3MonsterOptions = slot3Monsters,
            slot1TitleOptions = CollectTitlePool(slot1Monsters, band),
            slot2TitleOptions = CollectTitlePool(slot2Monsters, band),
            slot3TitleOptions = CollectTitlePool(slot3Monsters, band),
            visibilityModeOptions = GetVisibilityOptions(band),
            expectedSynergyTier = bp.synergyHint,
            allowDuplicateSpecies = bp.allowDuplicateSpecies
        };
    }

    // ═════════════════════════════════════════════════════════════
    //  Monster pool building
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns monster ids of the given type whose rarity falls within the
    /// accepted range for the score band.  Falls back to all non-boss
    /// monsters of that type if no rarity-filtered matches exist.
    /// </summary>
    private static List<string> GetMonstersForTypeAndBand(MonsterType type, ArenaScoreBand band)
    {
        var all = MonsterCatalog.All;
        if (all == null || all.Count == 0) return new List<string>();

        int bandIndex = (int)band;
        if (bandIndex < 0 || bandIndex >= BandRarityTiers.Length)
            bandIndex = 0;

        var acceptedRarities = BandRarityTiers[bandIndex];
        var result = new List<string>();

        // First pass: rarity-filtered candidates.
        for (int r = 0; r < acceptedRarities.Length; r++)
        {
            var targetRarity = acceptedRarities[r];
            for (int i = 0; i < all.Count; i++)
            {
                var m = all[i];
                if (m == null || m.type != type) continue;
                if (m.isBoss || m.uncatchable) continue;
                if (m.rarity != targetRarity) continue;
                if (!result.Contains(m.id))
                    result.Add(m.id);
            }
        }

        // Fallback: any non-boss of this type if rarity filter yielded nothing.
        if (result.Count == 0)
        {
            for (int i = 0; i < all.Count; i++)
            {
                var m = all[i];
                if (m == null || m.type != type) continue;
                if (m.isBoss || m.uncatchable) continue;
                if (!result.Contains(m.id))
                    result.Add(m.id);
            }
        }

        return result;
    }

    // ═════════════════════════════════════════════════════════════
    //  Title pool harvesting
    // ═════════════════════════════════════════════════════════════

    /// <summary>
    /// Collects title ids from the <see cref="MonsterDataSO.defaultAlwaysOnTitles"/> and
    /// <see cref="MonsterDataSO.ironTitles"/> arrays of every monster in the pool.
    /// For Low band, returns an empty list (bots at Low should not have titles).
    /// </summary>
    private static List<string> CollectTitlePool(List<string> monsterIds, ArenaScoreBand band)
    {
        if (band == ArenaScoreBand.Low)
            return new List<string>();

        var titleSet = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < monsterIds.Count; i++)
        {
            var def = MonsterCatalog.GetById(monsterIds[i]);
            if (def == null) continue;

            if (def.defaultAlwaysOnTitles != null)
            {
                for (int t = 0; t < def.defaultAlwaysOnTitles.Length; t++)
                {
                    var title = def.defaultAlwaysOnTitles[t];
                    if (title != null && !string.IsNullOrEmpty(title.titleId))
                        titleSet.Add(title.titleId);
                }
            }

            if (def.ironTitles != null)
            {
                for (int t = 0; t < def.ironTitles.Length; t++)
                {
                    var title = def.ironTitles[t];
                    if (title != null && !string.IsNullOrEmpty(title.titleId))
                        titleSet.Add(title.titleId);
                }
            }
        }

        return new List<string>(titleSet);
    }
}
