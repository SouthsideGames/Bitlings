using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Save-State Guard for idle/auto battling.
///
/// Purpose:
/// - If the app crashes / force-closes mid-batch, we can resume safely or discard cleanly.
/// - Prevents partial/corrupted state by treating each headless batch as a small transaction.
///
/// Notes:
/// - This guard is intentionally lightweight and independent of SaveManager's meta save.
/// - The actual "transaction" behavior is enforced by IdleBattleManager staging changes
///   and only applying them during a commit phase.
/// </summary>
public static class IdleBattleSaveStateGuard
{
    private const string FileName = "idle_battle_guard.json";
    private const string BackupFileName = "idle_battle_guard.bak";

    [Serializable]
    private class GuardState
    {
        public int version = 1;
        public bool inProgress;
        public string guardId;
        public long startedUnix;
        public int plannedCount;
        public int sessionSeed;
    }

    private static string PathOnDisk => System.IO.Path.Combine(Application.persistentDataPath, FileName);
    private static string BackupPathOnDisk => System.IO.Path.Combine(Application.persistentDataPath, BackupFileName);

    public static bool HasPending()
    {
        var s = Load();
        return s != null && s.inProgress && !string.IsNullOrEmpty(s.guardId);
    }

    public static string GetPendingId()
    {
        var s = Load();
        return s != null ? s.guardId : null;
    }

    public static bool Begin(int plannedCount, int sessionSeed, out string guardId)
    {
        guardId = null;
        try
        {
            var gs = new GuardState
            {
                inProgress = true,
                guardId = Guid.NewGuid().ToString("N"),
                startedUnix = NowUnix(),
                plannedCount = Mathf.Max(0, plannedCount),
                sessionSeed = sessionSeed
            };

            Save(gs);
            guardId = gs.guardId;
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static void Complete(string expectedGuardId = null)
    {
        try
        {
            var gs = Load();
            if (gs == null) { Delete(); return; }

            if (!string.IsNullOrEmpty(expectedGuardId) && gs.guardId != expectedGuardId)
                return; // don't clobber a newer guard

            Delete();
        }
        catch
        {
            // ignore
        }
    }

    public static void Discard()
    {
        // Discard means: remove pending guard so the system won't attempt a resume.
        // Any staged changes won't exist because commits only happen at the end of a batch.
        try { Delete(); } catch { }
    }

    private static GuardState Load()
    {
        try
        {
            if (TryRead(PathOnDisk, out var state) || TryRead(BackupPathOnDisk, out state))
                return state;
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static void Save(GuardState s)
    {
        try
        {
            string json = JsonUtility.ToJson(s, false);
            AtomicWrite(PathOnDisk, json);
            TryCopy(PathOnDisk, BackupPathOnDisk);
        }
        catch
        {
            // ignore
        }
    }

    private static void Delete()
    {
        if (File.Exists(PathOnDisk))
            File.Delete(PathOnDisk);
        if (File.Exists(BackupPathOnDisk))
            File.Delete(BackupPathOnDisk);
    }

    private static long NowUnix()
    {
        var now = DateTimeOffset.UtcNow;
        return now.ToUnixTimeSeconds();
    }

    private static bool TryRead(string path, out GuardState state)
    {
        state = null;
        try
        {
            if (!File.Exists(path)) return false;
            var json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return false;
            state = JsonUtility.FromJson<GuardState>(json);
            return state != null;
        }
        catch
        {
            state = null;
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
            Debug.LogError($"[IdleBattleSaveStateGuard] AtomicWrite failed for '{path}': {e.GetType().Name} – {e.Message}");
            try { if (!File.Exists(path)) File.Copy(tmp, path); }
            catch (Exception e2) { Debug.LogError($"[IdleBattleSaveStateGuard] AtomicWrite fallback copy failed: {e2.Message}"); }
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
