#if UNITY_EDITOR
using UnityEngine;

public class MonsterDetailStressTester : MonoBehaviour
{
    [SerializeField] private MonsterDetailPanelUI detail;
    [SerializeField] private int perFrame = 1;

    private MonsterDataSO[] all;
    private int idx;
    private bool running;

    [ContextMenu("Run Detail Scan")]
    void Run()
    {
        var lib = MonsterLibraryLocator.Lib;
        if (!lib || lib.monsters == null || lib.monsters.Length == 0)
        {
            Debug.LogWarning("[StressTester] No library/monsters.");
            return;
        }

        // ✅ FIX: assign directly, no ToArray() needed
        all = lib.monsters;
        idx = 0;
        running = true;
        Debug.Log("[StressTester] Starting scan...");
    }

    void Update()
    {
        if (!running || detail == null || all == null) return;

        for (int i = 0; i < perFrame && idx < all.Length; i++, idx++)
        {
            var m = all[idx];
            if (!m) continue;
            try
            {
                // open & immediately hide to exercise build
                detail.Show(m, _ => { });
                detail.Hide();
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"[StressTester] Crash on monster {m.id}: {ex}");
                running = false;
                break;
            }
        }

        if (idx >= all.Length)
        {
            Debug.Log("[StressTester] Scan complete.");
            running = false;
        }
    }
}
#endif
