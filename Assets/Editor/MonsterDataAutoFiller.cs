using UnityEngine;

public static class MonsterDataAutoFiller
{
    // Cached personalities so we don't hit Resources.LoadAll repeatedly
    private static MonsterPersonalitySO[] _cachedPersonalities;

    /// <summary>
    /// Auto-populates derived fields on the MonsterDataSO based on its
    /// type, rarity, and flags. This overwrites stat/regen/fatigue/
    /// training/spawn/job fields and may assign a random Personality.
    /// </summary>
    public static void Fill(MonsterDataSO data)
    {
        if (data == null)
            return;

        // ─────────────────────────────────────────────────────────────
        // 0. Common monsters must be starters
        // ─────────────────────────────────────────────────────────────
        if (data.rarity == Rarity.Common)
        {
            data.canBeStarter = true;
        }

        // ─────────────────────────────────────────────────────────────
        // 1. Stat budget by rarity + type % distribution
        // ─────────────────────────────────────────────────────────────
        int totalBudget = GetStatBudget(data.rarity);

        GetTypeStatPercents(
            data.type,
            out float hpPct,
            out float atkPct,
            out float defPct,
            out float spdPct
        );

        float rawHP   = totalBudget * hpPct;
        float rawAtk  = totalBudget * atkPct;
        float rawDef  = totalBudget * defPct;
        float rawSpd  = totalBudget * spdPct;

        int hp   = Mathf.RoundToInt(rawHP);
        int atk  = Mathf.RoundToInt(rawAtk);
        int def  = Mathf.RoundToInt(rawDef);
        int spd  = Mathf.RoundToInt(rawSpd);

        // Fix rounding so HP+Atk+Def+Spd = totalBudget exactly
        int sum = hp + atk + def + spd;
        int diff = totalBudget - sum;
        if (diff != 0)
        {
            // Add/subtract the difference to the stat with the largest share
            float maxRaw = rawHP;
            int maxIndex = 0; // 0=HP,1=Atk,2=Def,3=Spd

            if (rawAtk > maxRaw) { maxRaw = rawAtk; maxIndex = 1; }
            if (rawDef > maxRaw) { maxRaw = rawDef; maxIndex = 2; }
            if (rawSpd > maxRaw) { maxRaw = rawSpd; maxIndex = 3; }

            switch (maxIndex)
            {
                case 0: hp  += diff; break;
                case 1: atk += diff; break;
                case 2: def += diff; break;
                case 3: spd += diff; break;
            }
        }

        data.baseHP      = Mathf.Max(1, hp);
        data.baseAttack  = Mathf.Max(1, atk);
        data.baseDefense = Mathf.Max(1, def);
        data.baseSpeed   = Mathf.Max(1, spd);

        // ─────────────────────────────────────────────────────────────
        // 2. Regen / Fatigue / Training XP
        // ─────────────────────────────────────────────────────────────
        data.hpRegenPerHour        = GetHPRegenPerHour(data.rarity);
        data.fatigueRatePerHour    = GetFatigueRatePerHour(data.rarity);
        data.fatigueCooldownHours  = GetFatigueCooldownHours(data.rarity);
        data.baseTrainingXPPerHour = GetTrainingXPPerHour(data.rarity);

        // ─────────────────────────────────────────────────────────────
        // 3. Encounter / Jobs / Starter Weight
        // ─────────────────────────────────────────────────────────────
        data.spawnWeight = GetSpawnWeight(data.rarity, data.uncatchable, data.isBoss);
        data.jobSkill    = GetJobSkill(data.rarity);

        // If it's a starter (especially Common), make sure it has a starterWeight
        if (data.canBeStarter && data.starterWeight <= 0)
        {
            data.starterWeight = GetStarterWeight(data.rarity);
        }

        // ─────────────────────────────────────────────────────────────
        // 4. Battle basics (attack name / prefab lifetime)
        // ─────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(data.basicAttackName) || data.basicAttackName == "Attack")
        {
            data.basicAttackName = GetDefaultAttackName(data.type);
        }

        if (data.basicAttackPrefabLifetime <= 0f)
        {
            data.basicAttackPrefabLifetime = 1.25f;
        }

        // ─────────────────────────────────────────────────────────────
        // 5. Personality (random from Resources/MonsterPersonalities)
        // ─────────────────────────────────────────────────────────────
        if (data.Personality == null)
        {
            data.Personality = GetRandomPersonality();
        }

        // ─────────────────────────────────────────────────────────────
        // 6. Description auto-blurb if empty
        // ─────────────────────────────────────────────────────────────
        if (string.IsNullOrWhiteSpace(data.description))
        {
            string rarityLabel = data.rarity.ToString();
            string typeLabel   = data.type.ToString();
            string role        = DetermineRole(data.baseHP, data.baseAttack, data.baseDefense, data.baseSpeed);
            string keyStat     = GetKeyStatWord(role);
            string jobSite     = GetJobSiteNameForType(data.type);

            string desc =
                $"{data.displayName} is a {rarityLabel} {typeLabel}-type monster. " +
                $"It excels as a {role} with solid {keyStat}.";

            if (!string.IsNullOrEmpty(jobSite))
            {
                desc += $" It thrives at the {jobSite} job site.";
            }

            data.description = desc;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Rarity-based tuning (stat budget)
    // ─────────────────────────────────────────────────────────────

    private static int GetStatBudget(Rarity rarity)
    {
        // From your table (HP + Atk + Def + Spd)
        switch (rarity)
        {
            case Rarity.Common:    return 200;
            case Rarity.Uncommon:  return 220;
            case Rarity.Rare:      return 240;
            case Rarity.Epic:      return 260;
            case Rarity.Legendary: return 280;
            case Rarity.Mythic:    return 300;
            default:               return 200;
        }
    }

    private static float GetHPRegenPerHour(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common:    return 4f;
            case Rarity.Uncommon:  return 5f;
            case Rarity.Rare:      return 6f;
            case Rarity.Epic:      return 7f;
            case Rarity.Legendary: return 8f;
            case Rarity.Mythic:    return 9f;
            default:               return 6f;
        }
    }

    private static float GetFatigueRatePerHour(Rarity rarity)
    {
        // Higher rarity = slightly less fatigue per hour
        switch (rarity)
        {
            case Rarity.Common:    return 0.06f;
            case Rarity.Uncommon:  return 0.055f;
            case Rarity.Rare:      return 0.05f;
            case Rarity.Epic:      return 0.045f;
            case Rarity.Legendary: return 0.040f;
            case Rarity.Mythic:    return 0.035f;
        }
        return 0.05f;
    }

    private static float GetFatigueCooldownHours(Rarity rarity)
    {
        // Stronger monsters rest faster
        switch (rarity)
        {
            case Rarity.Common:    return 10f;
            case Rarity.Uncommon:  return 9f;
            case Rarity.Rare:      return 8f;
            case Rarity.Epic:      return 7f;
            case Rarity.Legendary: return 6f;
            case Rarity.Mythic:    return 5f;
        }
        return 8f;
    }

    private static int GetTrainingXPPerHour(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common:    return 25;
            case Rarity.Uncommon:  return 30;
            case Rarity.Rare:      return 38;
            case Rarity.Epic:      return 48;
            case Rarity.Legendary: return 60;
            case Rarity.Mythic:    return 72;
        }
        return 30;
    }

    private static float GetJobSkill(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common:    return 1.0f;
            case Rarity.Uncommon:  return 1.05f;
            case Rarity.Rare:      return 1.12f;
            case Rarity.Epic:      return 1.20f;
            case Rarity.Legendary: return 1.30f;
            case Rarity.Mythic:    return 1.40f;
        }
        return 1.0f;
    }

    private static float GetSpawnWeight(Rarity rarity, bool uncatchable, bool isBoss)
    {
        if (isBoss)
            return 0f; // Bosses handled via boss tables

        if (uncatchable)
            return 0f; // Story-only / event-only

        switch (rarity)
        {
            case Rarity.Common:    return 12f;
            case Rarity.Uncommon:  return 7f;
            case Rarity.Rare:      return 4f;
            case Rarity.Epic:      return 2.0f;
            case Rarity.Legendary: return 1.0f;
            case Rarity.Mythic:    return 0.4f;
        }
        return 1f;
    }

    private static int GetStarterWeight(Rarity rarity)
    {
        // Higher rarity starters should be rarer in the starter roll.
        switch (rarity)
        {
            case Rarity.Common:    return 10;
            case Rarity.Uncommon:  return 8;
            case Rarity.Rare:      return 6;
            case Rarity.Epic:      return 4;
            case Rarity.Legendary: return 2;
            case Rarity.Mythic:    return 1;
        }
        return 5;
    }

    // ─────────────────────────────────────────────────────────────
    // Type-based stat percentages from your table
    // ─────────────────────────────────────────────────────────────

    private static void GetTypeStatPercents(
        MonsterType type,
        out float hpPct,
        out float atkPct,
        out float defPct,
        out float spdPct)
    {
        // Defaults if None or missing: roughly balanced
        hpPct  = 0.25f;
        atkPct = 0.25f;
        defPct = 0.25f;
        spdPct = 0.25f;

        switch (type)
        {
            case MonsterType.Fire:
                hpPct  = 0.25f;
                atkPct = 0.35f;
                defPct = 0.15f;
                spdPct = 0.25f;
                break;

            case MonsterType.Water:
                hpPct  = 0.35f;
                atkPct = 0.20f;
                defPct = 0.30f;
                spdPct = 0.15f;
                break;

            case MonsterType.Grass:
                hpPct  = 0.30f;
                atkPct = 0.20f;
                defPct = 0.35f;
                spdPct = 0.15f;
                break;

            case MonsterType.Electric:
                hpPct  = 0.20f;
                atkPct = 0.30f;
                defPct = 0.15f;
                spdPct = 0.35f;
                break;

            case MonsterType.Ice:
                hpPct  = 0.20f;
                atkPct = 0.35f;
                defPct = 0.15f;
                spdPct = 0.30f;
                break;

            case MonsterType.Clash:
                hpPct  = 0.30f;
                atkPct = 0.35f;
                defPct = 0.15f;
                spdPct = 0.20f;
                break;

            case MonsterType.Corrupt:
                hpPct  = 0.20f;
                atkPct = 0.40f;
                defPct = 0.15f;
                spdPct = 0.25f;
                break;

            case MonsterType.Ground:
                hpPct  = 0.40f;
                atkPct = 0.20f;
                defPct = 0.30f;
                spdPct = 0.10f;
                break;

            case MonsterType.Sky:
                hpPct  = 0.20f;
                atkPct = 0.25f;
                defPct = 0.15f;
                spdPct = 0.40f;
                break;

            case MonsterType.Oracle:
                hpPct  = 0.20f;
                atkPct = 0.30f;
                defPct = 0.20f;
                spdPct = 0.30f;
                break;

            case MonsterType.Bug:
                hpPct  = 0.25f;
                atkPct = 0.25f;
                defPct = 0.20f;
                spdPct = 0.30f;
                break;

            case MonsterType.Rock:
                hpPct  = 0.40f;
                atkPct = 0.20f;
                defPct = 0.30f;
                spdPct = 0.10f;
                break;

            case MonsterType.Specter:
                hpPct  = 0.20f;
                atkPct = 0.25f;
                defPct = 0.15f;
                spdPct = 0.40f;
                break;

            case MonsterType.Wyrm:
                hpPct  = 0.35f;
                atkPct = 0.35f;
                defPct = 0.20f;
                spdPct = 0.10f;
                break;

            case MonsterType.Umbral:
                hpPct  = 0.25f;
                atkPct = 0.35f;
                defPct = 0.15f;
                spdPct = 0.25f;
                break;

            case MonsterType.Alloy:
                hpPct  = 0.35f;
                atkPct = 0.20f;
                defPct = 0.35f;
                spdPct = 0.10f;
                break;

            // MonsterType.None uses defaults above
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Default attack names per type
    // ─────────────────────────────────────────────────────────────

    private static string GetDefaultAttackName(MonsterType type)
    {
        switch (type)
        {
            case MonsterType.Fire:     return "Flame Jab";
            case MonsterType.Water:    return "Aqua Pulse";
            case MonsterType.Grass:    return "Leaf Strike";
            case MonsterType.Electric: return "Volt Jab";
            case MonsterType.Ice:      return "Frost Bite";
            case MonsterType.Clash:    return "Rival Smash";
            case MonsterType.Corrupt:  return "Hex Lash";
            case MonsterType.Ground:   return "Quake Jab";
            case MonsterType.Sky:      return "Gale Cut";
            case MonsterType.Oracle:   return "Foresight Ray";
            case MonsterType.Bug:      return "Chitter Jab";
            case MonsterType.Rock:     return "Stone Bash";
            case MonsterType.Specter:  return "Phantom Hit";
            case MonsterType.Wyrm:     return "Drake Slash";
            case MonsterType.Umbral:   return "Shadow Slash";
            case MonsterType.Alloy:    return "Metal Bash";
            case MonsterType.None:
            default:                   return "Tackle";
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Personality selection
    // ─────────────────────────────────────────────────────────────

    private static MonsterPersonalitySO GetRandomPersonality()
    {
        if (_cachedPersonalities == null || _cachedPersonalities.Length == 0)
        {
            // Looks under Resources/MonsterPersonalities
            _cachedPersonalities = Resources.LoadAll<MonsterPersonalitySO>("MonsterPersonalities");

            if (_cachedPersonalities == null || _cachedPersonalities.Length == 0)
            {
                Debug.LogWarning("MonsterDataAutoFiller: No MonsterPersonalitySO found in Resources/MonsterPersonalities.");
                return null;
            }
        }

        int idx = Random.Range(0, _cachedPersonalities.Length);
        return _cachedPersonalities[idx];
    }

    // ─────────────────────────────────────────────────────────────
    // Job site flavor text per type (from your mapping)
    // ─────────────────────────────────────────────────────────────

    private static string GetJobSiteNameForType(MonsterType type)
    {
        switch (type)
        {
            case MonsterType.Clash:   return "Gym";
            case MonsterType.Ground:  return "Quarry";
            case MonsterType.Rock:    return "Mine";
            case MonsterType.Electric:return "Power Plant";
            case MonsterType.Grass:   return "Grove";
            case MonsterType.Fire:    return "Forge";
            case MonsterType.Alloy:   return "Workshop";
            case MonsterType.Water:   return "Harbor";
            case MonsterType.Ice:     return "Cryo Lab";
            case MonsterType.Oracle:  return "Observatory";
            case MonsterType.Corrupt: return "Containment";
            case MonsterType.Wyrm:    return "Wyrm Den";
            case MonsterType.Umbral:  return "Shadow Market";
            case MonsterType.Specter: return "Sanctum";
            case MonsterType.Sky:     return "Clinic";
            case MonsterType.Bug:     return "Expedition";
            default:                  return null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Simple "role" determination for description text
    // ─────────────────────────────────────────────────────────────

    private static string DetermineRole(int hp, int atk, int def, int spd)
    {
        int max = Mathf.Max(hp, atk, def, spd);

        if (max == atk)
            return "attacker";
        if (max == def)
            return "defender";
        if (max == spd)
            return "speedster";

        // default if HP is highest or tie
        return "brawler";
    }

    private static string GetKeyStatWord(string role)
    {
        switch (role)
        {
            case "attacker":  return "offense";
            case "defender":  return "defense";
            case "speedster": return "speed";
            case "brawler":   return "durability";
        }
        return "stats";
    }
}
