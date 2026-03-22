using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System;

public static class IdleBattleStore
{
    private static readonly string FilePath = Path.Combine(Application.persistentDataPath, "idle_battle.json");
    private static readonly string BackupPath = Path.Combine(Application.persistentDataPath, "idle_battle.bak");

    [Serializable]
    private class Container { public IdleBattleSession session = new IdleBattleSession(); }

    private static Container _cache;

    public static IdleBattleSession Load()
    {
        if (_cache != null) return _cache.session;
        try
        {
            if (TryReadContainer(FilePath, out var loaded) || TryReadContainer(BackupPath, out loaded))
                _cache = loaded;
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
            string json = JsonUtility.ToJson(wrap, false);
            AtomicWrite(FilePath, json);
            TryCopy(FilePath, BackupPath);
            _cache = wrap;
        }
        catch { }
    }

    public static void ClearLog()
    {
        var s = Load();
        s.log?.Clear();
        s.capturedLog?.Clear();
        Save(s);
    }

    public static void ClearCache()
    {
        _cache = null;
    }

    private static bool TryReadContainer(string path, out Container wrap)
    {
        wrap = null;
        try
        {
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return false;
            wrap = JsonUtility.FromJson<Container>(json);
            return wrap != null;
        }
        catch
        {
            wrap = null;
            return false;
        }
    }

    private static void AtomicWrite(string path, string contents)
    {
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, contents ?? string.Empty);
        try
        {
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch
        {
            try { if (!File.Exists(path)) File.Copy(tmp, path); } catch { }
            try { File.Delete(tmp); } catch { }
        }
    }

    private static void TryCopy(string src, string dst)
    {
        try
        {
            if (File.Exists(src)) File.Copy(src, dst, overwrite: true);
        }
        catch { }
    }
}
