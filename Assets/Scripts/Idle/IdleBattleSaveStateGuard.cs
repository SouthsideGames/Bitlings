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
            if (!File.Exists(PathOnDisk)) return null;
            var json = File.ReadAllText(PathOnDisk);
            if (string.IsNullOrEmpty(json)) return null;
            return JsonUtility.FromJson<GuardState>(json);
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
            File.WriteAllText(PathOnDisk, JsonUtility.ToJson(s, false));
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
    }

    private static long NowUnix()
    {
        var now = DateTimeOffset.UtcNow;
        return now.ToUnixTimeSeconds();
    }
}
