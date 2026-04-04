using UnityEngine;

/// <summary>
/// Lightweight runtime checklist to catch missing critical scene wiring.
///
/// Goals:
/// - Ensure SaveManager is loaded early.
/// - Catch (and log) cases where UI exists but its backing manager singletons do not.
/// - Avoid creating managers that require inspector wiring (we only auto-create truly safe systems elsewhere).
/// </summary>
public static class RuntimeBootChecklist
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AfterSceneLoad()
    {
        if (SaveManager.Data == null)
            SaveManager.LoadOrCreate();

        if (JobManager.I == null)
        {
            if (Object.FindFirstObjectByType<JobPanelUI>() != null || Object.FindFirstObjectByType<JobSiteView>() != null)
            {
                Debug.LogWarning("[BootChecklist] Jobs UI exists but JobManager.I is null. Ensure a JobManager is present in this scene or a persistent bootstrap object.");
            }
        }

        // If Idle reward panel exists, IdleBattleManager should exist.
        if (IdleBattleManager.I == null)
        {
            if (Object.FindFirstObjectByType<IdleBattleRewardPanelUI>() != null)
            {
                Debug.LogWarning("[BootChecklist] IdleBattleRewardPanelUI exists but IdleBattleManager.I is null. Ensure IdleBattleManager is present in this scene.");
            }
        }
    }
}
