using System.Collections.Generic;
using System.Linq;

public static class BattleTypeChart
{
    private static readonly Dictionary<MonsterType, Dictionary<MonsterType, float>> TYPE = new()
    {
        [MonsterType.Fire] = new()
        {
            [MonsterType.Grass] = 2f,
            [MonsterType.Bug] = 2f,
            [MonsterType.Ice] = 2f,
            [MonsterType.Water] = 0.5f,
            [MonsterType.Rock] = 0.5f,
            [MonsterType.Ground] = 0.75f
        },
        [MonsterType.Water] = new()
        {
            [MonsterType.Fire] = 2f,
            [MonsterType.Rock] = 2f,
            [MonsterType.Ground] = 2f,
            [MonsterType.Grass] = 0.5f,
            [MonsterType.Electric] = 0.5f,
            [MonsterType.Ice] = 0.5f,
            [MonsterType.Corrupt] = 0.5f
        },
        [MonsterType.Grass] = new()
        {
            [MonsterType.Water] = 2f,
            [MonsterType.Ground] = 2f,
            [MonsterType.Rock] = 2f,
            [MonsterType.Fire] = 0.5f,
            [MonsterType.Ice] = 0.5f,
            [MonsterType.Sky] = 0.75f,
            [MonsterType.Bug] = 0.5f
        },
        [MonsterType.Electric] = new()
        {
            [MonsterType.Water] = 2f,
            [MonsterType.Sky] = 2f,
            [MonsterType.Alloy] = 2f,
            [MonsterType.Grass] = 0.75f,
            [MonsterType.Ground] = 0.5f,
            [MonsterType.Rock] = 0.5f
        },
        [MonsterType.Ice] = new()
        {
            [MonsterType.Grass] = 2f,
            [MonsterType.Water] = 2f,
            [MonsterType.Sky] = 2f,
            [MonsterType.Wyrm] = 2f,
            [MonsterType.Ground] = 1.5f,
            [MonsterType.Fire] = 0.5f,
            [MonsterType.Rock] = 0.75f,
            [MonsterType.Alloy] = 0.5f,
            [MonsterType.Clash] = 0.5f
        },
        [MonsterType.Rock] = new()
        {
            [MonsterType.Sky] = 2f,
            [MonsterType.Bug] = 2f,
            [MonsterType.Fire] = 2f,
            [MonsterType.Electric] = 2f,
            [MonsterType.Specter] = 2f,
            [MonsterType.Water] = 0.5f,
            [MonsterType.Grass] = 0.5f,
            [MonsterType.Ground] = 0.75f
        },
        [MonsterType.Ground] = new()
        {
            [MonsterType.Electric] = 2f,
            [MonsterType.Corrupt] = 2f,
            [MonsterType.Rock] = 1.5f,
            [MonsterType.Fire] = 1.25f,
            [MonsterType.Grass] = 0.5f,
            [MonsterType.Ice] = 0.75f,
            [MonsterType.Sky] = 0.5f,
            [MonsterType.Water] = 0.5f
        },
        [MonsterType.Sky] = new()
        {
            [MonsterType.Bug] = 2f,
            [MonsterType.Ground] = 2f,
            [MonsterType.Electric] = 0.5f,
            [MonsterType.Rock] = 0.5f,
            [MonsterType.Ice] = 0.5f,
            [MonsterType.Wyrm] = 0.5f,
            [MonsterType.Umbral] = 0.5f
        },
        [MonsterType.Bug] = new()
        {
            [MonsterType.Umbral] = 2f,
            [MonsterType.Grass] = 2f,
            [MonsterType.Sky] = 0.5f,
            [MonsterType.Fire] = 0.5f,
            [MonsterType.Rock] = 0.5f,
            [MonsterType.Oracle] = 0.5f,
            [MonsterType.Clash] = 0.5f
        },
        [MonsterType.Specter] = new()
        {
            [MonsterType.Umbral] = 2f,
            [MonsterType.Oracle] = 2f,
            [MonsterType.Alloy] = 0.5f,
            [MonsterType.Rock] = 0.5f
        },
        [MonsterType.Umbral] = new()
        {
            [MonsterType.Oracle] = 2f,
            [MonsterType.Sky] = 2f,
            [MonsterType.Specter] = 0.5f,
            [MonsterType.Bug] = 0.5f
        },
        [MonsterType.Oracle] = new()
        {
            [MonsterType.Corrupt] = 2f,
            [MonsterType.Bug] = 2f,
            [MonsterType.Specter] = 0.5f,
            [MonsterType.Umbral] = 0.5f
        },
        [MonsterType.Wyrm] = new()
        {
            [MonsterType.Clash] = 2f,
            [MonsterType.Sky] = 2f,
            [MonsterType.Rock] = 0.75f,
            [MonsterType.Ice] = 0.5f,
            [MonsterType.Alloy] = 0.5f
        },
        [MonsterType.Corrupt] = new()
        {
            [MonsterType.Alloy] = 2f,
            [MonsterType.Water] = 2f,
            [MonsterType.Ground] = 0.5f,
            [MonsterType.Oracle] = 0.5f
        },
        [MonsterType.Clash] = new()
        {
            [MonsterType.Bug] = 2f,
            [MonsterType.Ice] = 2f,
            [MonsterType.Clash] = 1.1f,
            [MonsterType.Wyrm] = 0.5f,
            [MonsterType.Alloy] = 0.5f
        },
        [MonsterType.Alloy] = new()
        {
            [MonsterType.Wyrm] = 2f,
            [MonsterType.Ice] = 2f,
            [MonsterType.Specter] = 2f,
            [MonsterType.Electric] = 0.5f,
            [MonsterType.Corrupt] = 0.5f
        },
    };

    public static float GetMultiplier(MonsterType atk, MonsterType def)
    {
        if (atk.Equals(def)) return 1f;
        if (TYPE.TryGetValue(atk, out var row) && row.TryGetValue(def, out var mult))
            return mult;
        return 1f;
    }

    public static List<MonsterType> GetStrongAgainst(MonsterType attacker)
        => TYPE.TryGetValue(attacker, out var row) ? row.Where(kv => kv.Value > 1f).Select(kv => kv.Key).ToList() : new();
    public static List<MonsterType> GetWeakAgainst(MonsterType attacker)
        => TYPE.TryGetValue(attacker, out var row) ? row.Where(kv => kv.Value < 1f).Select(kv => kv.Key).ToList() : new();
}
