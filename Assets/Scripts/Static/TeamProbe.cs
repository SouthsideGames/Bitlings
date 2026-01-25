// Assets/Scripts/Util/TeamProbe.cs
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

public static class TeamProbe
{
    // If you have a definitive source object for team (e.g., CharacterManager, SaveManager.Data, a UI presenter),
    // assign it once via TeamProbe.RegisterSource(obj). Otherwise, providers can pass an object when calling Count.
    private static UnityEngine.Object _registeredSource;

    /// <summary>Optionally register a primary source that holds the team/slots (e.g., CharacterManager, a TeamManager, or a UI presenter).</summary>
    public static void RegisterSource(UnityEngine.Object source)
    {
        _registeredSource = source;
    }

    /// <summary>Returns how many assigned monsters exist, trying multiple well-known fields/properties via reflection.</summary>
    public static int ActiveTeamCount()
    {
        if (_registeredSource == null) return TryFindGlobally();
        return CountFromSource(_registeredSource);
    }

    /// <summary>Returns how many assigned monsters exist from a specific source object.</summary>
    public static int ActiveTeamCountFrom(UnityEngine.Object specificSource)
    {
        if (specificSource == null) return ActiveTeamCount();
        return CountFromSource(specificSource);
    }

    // —————————————————— internals ——————————————————

    // Common candidate field/property names that might hold the "team" or "slots"
    private static readonly string[] TeamNames =
    {
        "teamDefs", "team", "Team", "ActiveTeam", "activeTeam", "currentTeam", "CurrentTeam",
        "playerTeam", "PlayerTeam", "teamMonsterIds", "TeamMonsterIds", "slots", "Slots"
    };

    // Things that might hold team on known singletons
    private static readonly string[] GlobalSingletonTypes =
    {
        "CharacterManager", "TeamManager", "PlayerManager", "SaveManager", "RosterManager"
    };

    private static int TryFindGlobally()
    {
        // 1) Try common singletons (type.I or type.Instance)
        foreach (var typeName in GlobalSingletonTypes)
        {
            var t = Type.GetType(typeName) ?? FindTypeInLoadedAssemblies(typeName);
            if (t == null) continue;

            var inst = GetSingletonInstance(t);
            if (inst != null)
            {
                int c = CountFromRawObject(inst);
                if (c >= 0) return c;
            }

            // Some projects hang data directly off SaveManager.Data
            if (typeName == "SaveManager")
            {
                var data = t.GetProperty("Data", BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance)?.GetValue(inst, null);
                if (data != null)
                {
                    int c = CountFromRawObject(data);
                    if (c >= 0) return c;
                }
            }
        }

        // 2) As a last resort, scan all active behaviours for a recognizable team field
        var behaviours = UnityEngine.Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None);
        foreach (var b in behaviours)
        {
            int c = CountFromRawObject(b);
            if (c >= 0) return c;
        }

        return 0;
    }

    private static int CountFromSource(UnityEngine.Object src)
    {
        if (src == null) return 0;

        // 1) Check object directly
        int c = CountFromRawObject(src);
        if (c >= 0) return c;

        // 2) If it's something like SaveManager, try its "Data" child
        var t = src.GetType();
        var dataProp = t.GetProperty("Data", BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static);
        if (dataProp != null)
        {
            var data = dataProp.GetValue(src);
            c = CountFromRawObject(data);
            if (c >= 0) return c;
        }

        return 0;
    }

    private static int CountFromRawObject(object obj)
    {
        if (obj == null) return -1;

        var t = obj.GetType();

        // Try properties first
        foreach (var name in TeamNames)
        {
            var p = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (p == null) continue;
            var val = p.GetValue(obj, null);
            int c = CountFromUnknownCollection(val);
            if (c >= 0) return c;
        }

        // Then fields
        foreach (var name in TeamNames)
        {
            var f = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            if (f == null) continue;
            var val = f.GetValue(obj);
            int c = CountFromUnknownCollection(val);
            if (c >= 0) return c;
        }

        return -1;
    }

    private static int CountFromUnknownCollection(object maybeCollection)
    {
        if (maybeCollection == null) return -1;

        // Arrays
        if (maybeCollection is Array arr)
        {
            int count = 0;
            foreach (var it in arr) if (IsFilledSlot(it)) count++;
            return count;
        }

        // List<T>
        var asList = maybeCollection as System.Collections.IEnumerable;
        if (asList != null && !(maybeCollection is string))
        {
            // Try to detect Count if available
            int count = 0;
            foreach (var it in asList) if (IsFilledSlot(it)) count++;
            return count;
        }

        // Single ID string (unlikely, but handle)
        if (maybeCollection is string s) return string.IsNullOrEmpty(s) ? 0 : 1;

        return -1;
    }

    private static bool IsFilledSlot(object entry)
    {
        if (entry == null) return false;

        // Empty strings
        if (entry is string s) return !string.IsNullOrEmpty(s);

        // Unity objects: non-null ref is enough
        if (entry is UnityEngine.Object uo) return uo != null;

        // For custom structs/classes, try common "id"/"monsterId"/"definition" patterns
        var t = entry.GetType();

        // If it has a MonsterData/Definition field or property that is set, treat as filled
        var defProp = t.GetProperty("def", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? t.GetProperty("definition", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? t.GetProperty("data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    ?? t.GetProperty("monster", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (defProp != null)
        {
            var v = defProp.GetValue(entry, null);
            if (v is UnityEngine.Object uo2) return uo2 != null;
            if (v != null) return true;
        }

        var idProp = t.GetProperty("id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                  ?? t.GetProperty("monsterId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (idProp != null)
        {
            var v = idProp.GetValue(entry, null);
            if (v is string s2) return !string.IsNullOrEmpty(s2);
            if (v != null) return true;
        }

        var idField = t.GetField("id", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                   ?? t.GetField("monsterId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

        if (idField != null)
        {
            var v = idField.GetValue(entry);
            if (v is string s2) return !string.IsNullOrEmpty(s2);
            if (v != null) return true;
        }

        // As a fallback, assume any non-null, non-empty object in a team list is "assigned"
        return true;
    }

    private static object GetSingletonInstance(Type t)
    {
        var instProp = t.GetProperty("I", BindingFlags.Public | BindingFlags.Static)
                    ?? t.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
        if (instProp != null)
        {
            var val = instProp.GetValue(null, null);
            if (val != null) return val;
        }

        var instField = t.GetField("I", BindingFlags.Public | BindingFlags.Static)
                     ?? t.GetField("Instance", BindingFlags.Public | BindingFlags.Static);
        if (instField != null)
        {
            var val = instField.GetValue(null);
            if (val != null) return val;
        }

        // Try Unity lookup
        var objs = UnityEngine.Object.FindObjectsByType(t, FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        if (objs != null && objs.Length > 0) return objs[0];

        return null;
    }

    private static Type FindTypeInLoadedAssemblies(string typeName)
    {
        var asms = AppDomain.CurrentDomain.GetAssemblies();
        foreach (var a in asms)
        {
            var t = a.GetType(typeName);
            if (t != null) return t;
        }
        return null;
    }
}
