using UnityEngine;

/// <summary>
/// Convenience accessor for the global GameBalanceSO.
/// Expects an asset at: Resources/GameBalance.asset
/// </summary>
public static class GameBalance
{
    private const string ResourcesPath = "GameBalanceConfig";
    private static GameBalanceSO _cached;

    public static GameBalanceSO Current
    {
        get
        {
            if (_cached) return _cached;
            _cached = Resources.Load<GameBalanceSO>(ResourcesPath);
            if (!_cached)
                Debug.LogError($"[GameBalance] Missing GameBalanceSO at Resources/{ResourcesPath}.asset");
            return _cached;
        }
    }

    public static bool TryGet(out GameBalanceSO balance)
    {
        balance = Current;
        return balance != null;
    }
}
