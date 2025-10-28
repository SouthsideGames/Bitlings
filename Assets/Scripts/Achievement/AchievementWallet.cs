public static class AchievementWallet
{
    public static int Get() => AchievementsSaveStore.Data.tokens;
    public static void Add(int amount)
    {
        if (amount <= 0) return;
        AchievementsSaveStore.Data.tokens += amount;
        AchievementsSaveStore.Data.tokensEarnedTotal += amount;
        AchievementsSaveStore.Save();
        GameEvents.OnResourcesChanged?.Invoke();
    }
    public static bool Spend(int amount)
    {
        if (amount <= 0) return true;
        if (AchievementsSaveStore.Data.tokens < amount) return false;
        AchievementsSaveStore.Data.tokens -= amount;
        AchievementsSaveStore.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        return true;
    }
}
