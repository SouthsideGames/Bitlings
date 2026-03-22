using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class IronCareerMetaData
{
    public string lastRerollDate;
    public int rerollsRemaining;

    // Persist the daily offer so players can't spam open/close
    public string[] starterOfferIds;
}

public static class IronCareerMetaSave
{
    private const string FileName = "IronCareerMetaSave.json";

    private static IronCareerMetaData CreateDefault()
    {
        return new IronCareerMetaData
        {
            lastRerollDate = string.Empty,
            rerollsRemaining = 0,
            starterOfferIds = null
        };
    }

    private static string FilePath =>
        Path.Combine(Application.persistentDataPath, FileName);
    private static string BackupPath =>
        Path.Combine(Application.persistentDataPath, "IronCareerMetaSave.bak");

    public static IronCareerMetaData Load()
    {
        try
        {
            if (TryRead(FilePath, out var parsed) || TryRead(BackupPath, out parsed))
                return parsed ?? CreateDefault();

            return CreateDefault();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IronCareerMetaSave] Failed to load meta save. Resetting. Error: {ex.Message}");
            return CreateDefault();
        }
    }

    public static void Save(IronCareerMetaData data)
    {
        try
        {
            string json = JsonUtility.ToJson(data, true);
            AtomicWrite(FilePath, json);
            TryCopy(FilePath, BackupPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IronCareerMetaSave] Failed to save meta. Error: {ex.Message}");
        }
    }

    private static bool TryRead(string path, out IronCareerMetaData data)
    {
        data = null;
        try
        {
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return false;
            data = JsonUtility.FromJson<IronCareerMetaData>(json);
            return data != null;
        }
        catch
        {
            data = null;
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