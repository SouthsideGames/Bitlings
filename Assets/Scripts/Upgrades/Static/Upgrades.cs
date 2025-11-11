using UnityEngine;
public enum UpgradeType
{
    Tap,
    Idle,
    Crit,
    AutoTap,
    CoinGain,
    Offline
}


public static class Upgrades
{
  public const int   BaseCost = 20;
    public const float Growth   = 1.6f;

    public static int CostForLevel(int level)
        => Mathf.CeilToInt(BaseCost * Mathf.Pow(Growth, Mathf.Max(0, level)));

    public static int TapCost()  => CostForLevel(SaveManager.Data.tapLevel);
    public static int IdleCost() => CostForLevel(SaveManager.Data.idleLevel);
    public static int CritCost() => CostForLevel(SaveManager.Data.critLevel);
    public static int AutoTapCost() => CostForLevel(SaveManager.Data.autoTapLevel);
    public static int CoinGainCost() => CostForLevel(SaveManager.Data.coinGainLevel);
    public static int OfflineCost() => CostForLevel(SaveManager.Data.offlineLevel);
}
