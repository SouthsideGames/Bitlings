// Assets/Scripts/ArenaCore/ArenaCoreTypes.cs
// Portable data types for JSON reference data catalogs.
// These mirror the ScriptableObject data but are plain C# for server use.

using System;
using System.Collections.Generic;

/// <summary>
/// JSON-serializable monster definition. Implements IMonsterDef.
/// Used by the server-side catalog and the data exporter.
/// </summary>
[Serializable]
public class MonsterCatalogEntry : IMonsterDef
{
    public string id;
    public string displayName;
    public int type;     // MonsterType as int
    public int rarity;   // Rarity as int
    public int baseHP;
    public int baseAttack;
    public int baseDefense;
    public int baseSpeed;
    public int arenaScore;
    public string basicAttackName;
    public bool isBoss;
    public bool uncatchable;
    public List<string> defaultAlwaysOnTitleIds = new List<string>();
    public List<string> ironTitleIds = new List<string>();

    // IMonsterDef
    string IMonsterDef.Id => id;
    string IMonsterDef.DisplayName => displayName;
    MonsterType IMonsterDef.Type => (MonsterType)type;
    Rarity IMonsterDef.Rarity => (Rarity)rarity;
    int IMonsterDef.BaseHP => baseHP;
    int IMonsterDef.BaseAttack => baseAttack;
    int IMonsterDef.BaseDefense => baseDefense;
    int IMonsterDef.BaseSpeed => baseSpeed;
    int IMonsterDef.ArenaScore => arenaScore;
    string IMonsterDef.BasicAttackName => basicAttackName;
    bool IMonsterDef.IsBoss => isBoss;
    bool IMonsterDef.Uncatchable => uncatchable;
    IReadOnlyList<string> IMonsterDef.DefaultAlwaysOnTitleIds => defaultAlwaysOnTitleIds;
    IReadOnlyList<string> IMonsterDef.IronTitleIds => ironTitleIds;
}

/// <summary>
/// JSON-serializable title definition. Implements ITitleDef.
/// </summary>
[Serializable]
public class TitleCatalogEntry : ITitleDef
{
    public string titleId;
    public string displayName;
    public int arenaScore;

    // ITitleDef
    string ITitleDef.TitleId => titleId;
    string ITitleDef.DisplayName => displayName;
    int ITitleDef.ArenaScore => arenaScore;
}

/// <summary>
/// Root container for the exported monster catalog JSON.
/// </summary>
[Serializable]
public class MonsterCatalogData
{
    public List<MonsterCatalogEntry> monsters = new List<MonsterCatalogEntry>();
}

/// <summary>
/// Root container for the exported title catalog JSON.
/// </summary>
[Serializable]
public class TitleCatalogData
{
    public List<TitleCatalogEntry> titles = new List<TitleCatalogEntry>();
}

/// <summary>
/// Type effectiveness entry for the type chart JSON export.
/// </summary>
[Serializable]
public class TypeChartEntry
{
    public int attackerType;
    public int defenderType;
    public float multiplier;
}

/// <summary>
/// Root container for the exported type chart JSON.
/// </summary>
[Serializable]
public class TypeChartData
{
    public List<TypeChartEntry> entries = new List<TypeChartEntry>();
}
