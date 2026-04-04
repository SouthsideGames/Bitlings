using System;
using UnityEngine;

public static class IronCareerRuntime
{
    public static bool IsActive { get; private set; }
    public static string RunGuid { get; private set; }

    public static void Enter()
    {
        IsActive = true;
        RunGuid = Guid.NewGuid().ToString("N");
        DevLog.Log($"[IronCareerRuntime] ENTER run={RunGuid}");
    }

    public static void Exit()
    {
        DevLog.Log($"[IronCareerRuntime] EXIT run={RunGuid}");
        IsActive = false;
        RunGuid = null;
    }
}
