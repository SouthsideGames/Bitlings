using System;
using System.Collections.Generic;

/// <summary>
/// Lightweight runtime helper for mapping MonsterId -> PackId for UI (Codex badges, sorting, etc.).
/// Safe to call even if no pack library exists.
/// </summary>
public static class MonsterPackTagCache
{
    // monsterId -> packId
    private static Dictionary<string, string> _monsterToPack;
    private static bool _built;

    public static void Invalidate()
    {
        _built = false;
        _monsterToPack = null;
    }

    private static void EnsureBuilt()
    {
        if (_built) return;

        _built = true;
        _monsterToPack = new Dictionary<string, string>(StringComparer.Ordinal);

        var lib = MonsterPackLibraryLocator.Lib;
        if (!lib) return;

        lib.Warmup();

        var packs = lib.PacksReadOnly;
        if (packs == null) return;

        for (int p = 0; p < packs.Count; p++)
        {
            var pack = packs[p];
            if (!pack || string.IsNullOrEmpty(pack.id) || pack.monsters == null) continue;

            for (int m = 0; m < pack.monsters.Count; m++)
            {
                var def = pack.monsters[m];
                if (!def || string.IsNullOrEmpty(def.id)) continue;

                // First pack wins (keeps badge stable even if content overlaps).
                if (!_monsterToPack.ContainsKey(def.id))
                    _monsterToPack.Add(def.id, pack.id);
            }
        }
    }

    /// <summary>Returns the pack id (e.g., "MP-001") for the monster id, or null if it is not in any pack.</summary>
    public static string GetPackId(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return null;
        EnsureBuilt();
        if (_monsterToPack == null) return null;
        _monsterToPack.TryGetValue(monsterId, out var packId);
        return packId;
    }

    public static bool HasPack(string monsterId)
    {
        return !string.IsNullOrEmpty(GetPackId(monsterId));
    }

    /// <summary>
    /// Returns a short badge string for the monster. Defaults to "CORE" when the monster isn't in any pack.
    /// </summary>
    public static string GetBadge(string monsterId)
    {
        var pack = GetPackId(monsterId);
        return string.IsNullOrEmpty(pack) ? "CORE" : pack;
    }

    public static bool IsInUnlockedPack(string monsterId, IReadOnlyList<string> unlockedPacks)
    {
        if (string.IsNullOrEmpty(monsterId)) return false;

        var packId = GetPackId(monsterId);
        if (string.IsNullOrEmpty(packId)) return false;

        if (unlockedPacks == null || unlockedPacks.Count == 0) return false;

        // small list; linear scan is fine
        for (int i = 0; i < unlockedPacks.Count; i++)
        {
            if (unlockedPacks[i] == packId)
                return true;
        }
        return false;
    }

}
