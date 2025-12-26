using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System;

public static class IdleBattleStore
{
    private static readonly string FilePath = Path.Combine(Application.persistentDataPath, "idle_battle.json");

    [Serializable]
    private class Container { public IdleBattleSession session = new IdleBattleSession(); }

    private static Container _cache;

    public static IdleBattleSession Load()
    {
        if (_cache != null) return _cache.session;
        try
        {
            if (File.Exists(FilePath))
                _cache = JsonUtility.FromJson<Container>(File.ReadAllText(FilePath));
        }
        catch { }
        _cache ??= new Container { session = new IdleBattleSession() };
        _cache.session.log ??= new List<IdleEncounterLogEntry>();
        return _cache.session;
    }

    public static void Save(IdleBattleSession s)
    {
        try
        {
            var wrap = new Container { session = s };
            File.WriteAllText(FilePath, JsonUtility.ToJson(wrap, false));
            _cache = wrap;
        }
        catch { }
    }

    public static void ClearLog()
    {
        var s = Load();
        s.log?.Clear();
        Save(s);
    }
}
