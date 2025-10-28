using UnityEngine;

public struct TitleContext
{
    // Battle-facing context
    public float selfHp01;       // 0..1 current HP%
    public int allyCount;        // alive allies on field (excluding self if you prefer)
    public int winStreak;        // from your WinStreakCounter system

    // Optional: you can expand later (turn index, boss flags, etc.)
    public static TitleContext Empty => new TitleContext
    {
        selfHp01 = 1f,
        allyCount = 0,
        winStreak = 0
    };
}
