using System;

/// <summary>
/// Deterministic per-battle RNG.
/// Provides a crit RNG stream (injected into BattleCalc) and a separate stream for enemy personality decisions.
/// </summary>
public sealed class BattleRngService
{
    private System.Random _battleRng;
    private System.Random _enemyRng;

    private int _battleSeed;
    private string _battleSeedLabel;
    private bool _battleSeedExplicitlySet;

    public int BattleSeed => _battleSeed;
    public string BattleSeedLabel => _battleSeedLabel;

    /// <summary>
    /// Optional: EncounterManager can set the battle seed before calling Begin(...).
    /// If not set, a deterministic seed will be derived from the active session/daily/custom seed.
    /// </summary>
    public void SetBattleSeed(int seed, string seedLabel = null)
    {
        _battleSeed = seed == 0 ? 1 : seed;
        _battleSeedLabel = seedLabel;
        _battleSeedExplicitlySet = true;
    }

    /// <summary>
    /// Call at battle start from BattleManager.Begin(). If a seed was explicitly set, it will be preserved.
    /// Otherwise, this clears any prior-battle RNG/seed to prevent accidental carry-over.
    /// </summary>
    public void ResetForBegin()
    {
        _battleRng = null;
        _enemyRng = null;

        if (!_battleSeedExplicitlySet)
        {
            _battleSeed = 0;
            _battleSeedLabel = null;
        }
    }

    /// <summary>
    /// Call at battle end to fully clear state so no data can leak into the next battle.
    /// </summary>
    public void ClearAll()
    {
        _battleRng = null;
        _enemyRng = null;
        _battleSeed = 0;
        _battleSeedLabel = null;
        _battleSeedExplicitlySet = false;
    }

    public System.Random EnemyRng => _enemyRng;

    public float Rng01()
    {
        if (_battleRng == null)
            return UnityEngine.Random.value; // fallback (should not happen once initialized)
        return (float)_battleRng.NextDouble(); // [0,1)
    }

    /// <summary>
    /// Ensures RNGs are initialized. If no seed was set, derives one from the global seed + per-battle serial + wild identity.
    /// </summary>
    public void EnsureInitialized(ref int battleSerial, MonsterDataSO wildDef, int wildLevel)
    {
        if (_battleSeed == 0)
        {
            SeedService.ApplyGlobalSeedForSession();

            battleSerial++;
            int baseSeed = SeedService.ActiveSeed != 0 ? SeedService.ActiveSeed : 1;

            string wildId = (wildDef != null && !string.IsNullOrEmpty(wildDef.id)) ? wildDef.id : "UNKNOWN";
            string raw = $"{baseSeed}|{battleSerial}|{wildId}|{wildLevel}";
            _battleSeed = StableHash(raw);
            if (_battleSeed == 0) _battleSeed = 1;

            if (string.IsNullOrEmpty(_battleSeedLabel))
                _battleSeedLabel = $"{SeedService.GetDisplaySeedPrefix()}{SeedService.GetDisplaySeedToken()}";
        }

        if (_battleRng == null)
            _battleRng = new System.Random(_battleSeed);

        if (_enemyRng == null)
            _enemyRng = new System.Random(_battleSeed ^ unchecked((int)0x9E3779B9));

        BattleCalc.SetRng(Rng01);
    }

    private static int StableHash(string s)
    {
        unchecked
        {
            int hash = 17;
            if (!string.IsNullOrEmpty(s))
            {
                for (int i = 0; i < s.Length; i++)
                    hash = hash * 31 + s[i];
            }
            return hash;
        }
    }
}
