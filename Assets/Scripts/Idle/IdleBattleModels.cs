using System;
using System.Collections.Generic;

[Serializable]
public class IdleRiftLogEntry
{
    public string monsterId;
    public int count;
    public int credits;
    public bool premiumSeen;
}

[Serializable]
public class IdleBattleSession
{
    public bool autoBattling;
    public bool rewardPanelShownThisSession; // CHANGED: persistent flag so reward panel shows at most once per auto session across resume/reload.
    
    // If true, the app detected an interrupted auto/idle batch (crash/force-close)
    // and the player should choose whether to resume or discard.
    public bool hasPendingRecovery;

    // Used by IdleBattleManager to decide whether to open the summary panel.
    public bool hasPendingSummary;
    public string biomeId;
    public int energyAtStart;
    public int totalEnergySpent;
    public int totalLosses; // CHANGED: tracks defeats this auto session for the live counter
    public long sessionStartUnix;
    public long lastTickUnix;


    // Apply-once ledger for offline simulation (prevents double-run if ResolveOfflineIfAny is invoked multiple times before store is saved).
    public long offlineLastResolvedUnix;
    // Encountered (fought)
    public List<IdleRiftLogEntry> log = new();

    // Captured (successfully captured)
    public List<IdleRiftLogEntry> capturedLog = new();
}

[Serializable]
public class IdleBattleSummary
{
    public int totalRifts;
    public int totalEnergySpent;
    public int totalcredits;
    public int totalLosses; // CHANGED: exposed in summary so reward panel can show loss count
    public float durationSeconds;

    // Encountered (fought)
    public List<IdleRiftLogEntry> mergedLog = new();

    // Captured (successfully captured)
    public List<IdleRiftLogEntry> capturedLog = new();
}
