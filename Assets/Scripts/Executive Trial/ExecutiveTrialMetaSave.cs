using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class ExecutiveTrialMetaData
{
    public string lastRerollDate;
    public int rerollsRemaining;
    public bool runActive; // FIXED: persisted run-state flag used by crash recovery guard in AppLifecycle

    // Persist the daily offer so players can't spam open/close
    public string[] starterOfferIds;
    public bool battleInProgress; // FIXED: crash recovery checkpoint
}

public static class ExecutiveTrialMetaSave
{
    private const string FileName = "ExecutiveTrialMetaSave.json";

    private static ExecutiveTrialMetaData CreateDefault()
    {
        return new ExecutiveTrialMetaData
        {
            lastRerollDate = string.Empty,
            rerollsRemaining = 0,
            starterOfferIds = null,
            battleInProgress = false
        };
    }

    private static string FilePath =>
        Path.Combine(Application.persistentDataPath, FileName);
    private static string BackupPath =>
        Path.Combine(Application.persistentDataPath, "ExecutiveTrialMetaSave.bak");

    public static ExecutiveTrialMetaData Load()
    {
        try
        {
            if (TryRead(FilePath, out var parsed) || TryRead(BackupPath, out parsed))
                return parsed ?? CreateDefault();

            return CreateDefault();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ExecutiveTrialMetaSave] Failed to load meta save. Resetting. Error: {ex.Message}");
            return CreateDefault();
        }
    }

    public static void Save(ExecutiveTrialMetaData data)
    {
        try
        {
            string json = JsonUtility.ToJson(data, true);
            AtomicWrite(FilePath, json);
            TryCopy(FilePath, BackupPath);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[ExecutiveTrialMetaSave] Failed to save meta. Error: {ex.Message}");
        }
    }

    private static bool TryRead(string path, out ExecutiveTrialMetaData data)
    {
        data = null;
        try
        {
            if (!File.Exists(path)) return false;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return false;
            data = JsonUtility.FromJson<ExecutiveTrialMetaData>(json);
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

        // Prefer File.Replace: no window where the destination is missing.
        if (File.Exists(path))
        {
            try { File.Replace(tmp, path, null); return; }
            catch { /* unsupported on some platforms; fall through */ }
        }

        try
        {
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ExecutiveTrialMetaSave] AtomicWrite failed for '{path}': {e.GetType().Name} – {e.Message}");
            try { if (!File.Exists(path)) File.Copy(tmp, path); }
            catch (Exception e2) { Debug.LogError($"[ExecutiveTrialMetaSave] AtomicWrite fallback copy failed: {e2.Message}"); }
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