using UnityEngine;

/// <summary>
/// Ensures World Event runtime systems exist without requiring manual scene wiring.
/// Safe: creates objects only if missing.
/// </summary>
public static class WorldEventBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void EnsureWorldEventSystems()
    {
        // WorldEventManager feed
        if (WorldEventManager.I == null)
        {
            var go = new GameObject("WorldEventManager");
            go.AddComponent<WorldEventManager>();
        }

        // WorldEventSystem orchestrator
        if (WorldEventSystem.I == null)
        {
            var go = new GameObject("WorldEventSystem");
            go.AddComponent<WorldEventSystem>();
        }
    }
}
