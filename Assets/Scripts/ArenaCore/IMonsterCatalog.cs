// Assets/Scripts/ArenaCore/IMonsterCatalog.cs
// Shared interface for monster catalog access.

using System.Collections.Generic;

/// <summary>
/// Provides read-only access to the monster catalog.
/// Client: wraps MonsterCatalog / MonsterLibraryLocator.
/// Server: wraps a JSON-loaded catalog.
/// </summary>
public interface IMonsterCatalog
{
    IReadOnlyList<IMonsterDef> All { get; }
    IMonsterDef GetById(string id);
    bool Contains(string id);
}
