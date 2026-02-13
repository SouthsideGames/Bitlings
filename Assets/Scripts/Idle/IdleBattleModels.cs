using System;
using System.Collections.Generic;

[Serializable]
public class IdleEncounterLogEntry
{
    public string monsterId;
    public int count;
    public int credits;
    public bool shinySeen;
}

[Serializable]
public class IdleBattleSession
{
    public bool autoBattling;
    
    // If true, the app detected an interrupted auto/idle batch (crash/force-close)
    // and the player should choose whether to resume or discard.
    public bool hasPendingRecovery;

    // Used by IdleBattleManager to decide whether to open the summary panel.
    public bool hasPendingSummary;
    public string biomeId;
    public int energyAtStart;
    public int totalEnergySpent;
    public long sessionStartUnix;
    public long lastTickUnix;

    // Encountered (fought)
    public List<IdleEncounterLogEntry> log = new();

    // Captured (successfully captured)
    public List<IdleEncounterLogEntry> capturedLog = new();
}

[Serializable]
public class IdleBattleSummary
{
    public int totalEncounters;
    public int totalEnergySpent;
    public int totalcredits;
    public float durationSeconds;

    // Encountered (fought)
    public List<IdleEncounterLogEntry> mergedLog = new();

    // Captured (successfully captured)
    public List<IdleEncounterLogEntry> capturedLog = new();
}
