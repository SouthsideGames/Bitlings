using UnityEngine;

/// <summary>
/// Auto-creates the <see cref="UGSInitializer"/> singleton if it isn't already
/// present in the scene. Runs after scene load, alongside the existing
/// <see cref="RuntimeBootChecklist"/>.
///
/// Also wires up the <see cref="CloudSaveSync.SyncOnLoginAsync"/> call
/// that runs once after authentication succeeds.
/// </summary>
public static class UGSBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AfterSceneLoad()
    {
        if (UGSInitializer.I == null)
        {
            var go = new GameObject("[UGSInitializer]");
            go.AddComponent<UGSInitializer>();
            // UGSInitializer.Awake calls DontDestroyOnLoad.
        }

        // Once auth completes, run the initial cloud save sync.
        UGSInitializer.OnReady -= OnUGSReady;
        UGSInitializer.OnReady += OnUGSReady;
    }

    private static async void OnUGSReady()
    {
        UGSInitializer.OnReady -= OnUGSReady;

        // Map the UGS PlayerId into the arena save data so all future
        // cloud operations use a stable, server-issued identifier.
        var arena = SaveManager.GetArenaSaveData();
        if (arena != null && UGSInitializer.I != null)
        {
            string ugsId = UGSInitializer.I.PlayerId;
            if (!string.IsNullOrEmpty(ugsId) && arena.arenaPlayerId != ugsId)
            {
                arena.arenaPlayerId = ugsId;
                SaveManager.Save();
            }
        }

        // Pull cloud data, merge, push.
        await CloudSaveSync.SyncOnLoginAsync();

        GameEvents.ArenaDataChanged?.Invoke();
        Debug.Log("[UGSBootstrap] Post-auth sync complete.");
    }
}
