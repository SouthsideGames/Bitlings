using System;
using System.Collections.Generic;

// ─────────────────────────────────────────────────────────────
// Exchange Data — runtime + persisted structs for the Bitling Exchange
// ─────────────────────────────────────────────────────────────

public enum DemandLevel { Low = 0, Medium = 1, High = 2, Surge = 3 }
public enum TrendDirection { Falling = 0, Stable = 1, Rising = 2 }

[Serializable]
public class MarketSpeciesState
{
    public string speciesId;
    public int currentValue;
    public int previousValue;
    public DemandLevel demandLevel = DemandLevel.Medium;
    public TrendDirection trend = TrendDirection.Stable;
    public long lastUpdateUnix;
}

[Serializable]
public class ActiveRequest
{
    public string requestId;
    public string requiredSpeciesId;     // specific species (null/empty = generic)
    public MonsterType requiredType;     // for generic-type requests
    public Rarity requiredMinRarity;     // for generic-rarity requests
    public int creditReward;
    public ResourceType bonusResourceType;
    public int bonusResourceAmount;
    public string flavorText;
    public long expiresUnix;
    public bool fulfilled;
}

[Serializable]
public class SpeciesBattleSentimentData
{
    public string speciesId;
    public int monthlyWinsAgainst;
    public int monthlyLossesAgainst;
    public int sentimentScore;
    public float monthlyHoursWorked;
}

[Serializable]
public class ExchangeSaveData
{
    public List<MarketSpeciesState> speciesStates = new List<MarketSpeciesState>();
    public List<SpeciesBattleSentimentData> monthlyBattleSentiments = new List<SpeciesBattleSentimentData>();
    public List<ActiveRequest> activeRequests = new List<ActiveRequest>();
    public int totalBrokered;
    public int totalCreditsBrokered;
    public int totalRequestsFulfilled;
    public int dailySeed;
    public int lastDayIndex = -1;
    public int lastRequestRotationDayIndex = -1;
    public int battleSentimentMonthKey = -1;
}

// ─────────────────────────────────────────────────────────────
// Pending duplicate data — passed from capture flow to resolution panel
// ─────────────────────────────────────────────────────────────

public static class PendingDuplicateCapture
{
    public static bool HasPending { get; private set; }
    public static OwnedMonsterData Existing { get; private set; }
    public static MonsterDataSO Def { get; private set; }
    public static int EncounterLevel { get; private set; }
    public static bool IsShiny { get; private set; }
    public static bool IsMaxLevel { get; private set; }

    public static void Set(OwnedMonsterData existing, MonsterDataSO def, int encounterLevel, bool isShiny, bool isMaxLevel)
    {
        HasPending = true;
        Existing = existing;
        Def = def;
        EncounterLevel = encounterLevel;
        IsShiny = isShiny;
        IsMaxLevel = isMaxLevel;
    }

    public static void Clear()
    {
        HasPending = false;
        Existing = null;
        Def = null;
        EncounterLevel = 0;
        IsShiny = false;
        IsMaxLevel = false;
    }
}
