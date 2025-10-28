using System;
using System.Collections.Generic;

[Serializable]
public class IdleEncounterLogEntry
{
    public string monsterId;
    public int count;
    public int coins;
    public bool shinySeen;
}

[Serializable]
public class IdleBattleSession
{
    public bool autoBattling;
    public string biomeId;
    public int energyAtStart;
    public int totalEnergySpent;
    public long sessionStartUnix;
    public long lastTickUnix;
    public List<IdleEncounterLogEntry> log = new();
}

[Serializable]
public class IdleBattleSummary
{
    public int totalEncounters;
    public int totalEnergySpent;
    public int totalCoins;
    public float durationSeconds;
    public List<IdleEncounterLogEntry> mergedLog = new();
}
