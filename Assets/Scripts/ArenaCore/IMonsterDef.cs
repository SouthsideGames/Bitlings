// Assets/Scripts/ArenaCore/IMonsterDef.cs
// Shared interface for monster data — used by both client (wrapping MonsterDataSO)
// and server (wrapping JSON catalog entries).

using System.Collections.Generic;

/// <summary>
/// Read-only monster definition consumed by arena logic.
/// Client: implemented by a wrapper around MonsterDataSO.
/// Server: implemented by a JSON-deserialized catalog entry.
/// </summary>
public interface IMonsterDef
{
    string Id { get; }
    string DisplayName { get; }
    MonsterType Type { get; }
    Rarity Rarity { get; }
    int BaseHP { get; }
    int BaseAttack { get; }
    int BaseDefense { get; }
    int BaseSpeed { get; }
    int ArenaScore { get; }
    string BasicAttackName { get; }
    bool IsBoss { get; }
    bool Uncatchable { get; }

    /// <summary>Titles always active on this species (for bot generation).</summary>
    IReadOnlyList<string> DefaultAlwaysOnTitleIds { get; }

    /// <summary>Iron-tier titles available to this species (for bot generation).</summary>
    IReadOnlyList<string> IronTitleIds { get; }
}
