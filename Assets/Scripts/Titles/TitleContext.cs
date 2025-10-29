using UnityEngine;

/// <summary>
/// Context snapshot passed to TitleRuntime to evaluate ConditionalBoosters
/// such as HealthBelow%, HealthAbove%, AllyCountBelow, and WinStreakAbove.
/// </summary>
[System.Serializable]
public struct TitleContext
{
    
    public string ownedId;

    // Battle-facing context
    public float selfHp01;   // Current HP as 0–1 percent of max
    public int alliesAlive;  // Count of living allies (excluding self)
    public int winStreak;    // Current win streak from EncounterManager

    // Optional expansion: useful for future conditions
    public int turnIndex;    // Which turn we’re on (optional, default 0)
    public bool isBossFight; // True if current encounter is a boss

    // ────────────────────────────────────────────────
    // Constructors
    // ────────────────────────────────────────────────

    public TitleContext(string ownedId, float hpPct, int alliesAlive, int winStreak, int turnIndex = 0, bool isBoss = false)
    {
        this.ownedId = ownedId;
        this.selfHp01 = Mathf.Clamp01(hpPct);
        this.alliesAlive = Mathf.Max(0, alliesAlive);
        this.winStreak = Mathf.Max(0, winStreak);
        this.turnIndex = Mathf.Max(0, turnIndex);
        this.isBossFight = isBoss;
    }

    // Default empty context
    public static TitleContext Empty => new TitleContext
    {
        ownedId = "",
        selfHp01 = 1f,
        alliesAlive = 0,
        winStreak = 0,
        turnIndex = 0,
        isBossFight = false
    };

    // ────────────────────────────────────────────────
    // Helper methods
    // ────────────────────────────────────────────────

    public bool IsHealthBelow(float threshold01) => selfHp01 < threshold01;
    public bool IsHealthAbove(float threshold01) => selfHp01 > threshold01;
    public bool IsAllyCountBelow(int count)      => alliesAlive < count;
    public bool IsWinStreakAbove(int count)      => winStreak > count;
}
