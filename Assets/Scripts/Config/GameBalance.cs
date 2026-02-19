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
            {
                // Fail-soft: allow boot with a runtime default instance.
                // This prevents hard blocks in release builds if the asset is missing.
                Debug.LogWarning($"[GameBalance] Missing GameBalanceSO at Resources/{ResourcesPath}.asset. Using runtime defaults.");
                _cached = ScriptableObject.CreateInstance<GameBalanceSO>();
            }
            return _cached;
        }
    }

    public static bool TryGet(out GameBalanceSO balance)
    {
        balance = Current;
        return balance != null;
    }
}
