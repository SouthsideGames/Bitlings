using UnityEngine;

public static class JobLeveling
{
    public const int MaxLevel = 3;

    public static int MaxXpForLevel(JobType job, int level)
    {
        level = Mathf.Clamp(level, 1, MaxLevel);
        return level switch
        {
            1 => 20,
            2 => 40,
            _ => 80
        };
    }

    public static float StorageMultForLevel(int level)
    {
        return level switch
        {
            1 => 1.0f,
            2 => 1.5f,
            _ => 2.0f
        };
    }
}