using UnityEngine;

[DefaultExecutionOrder(-10000)]
public sealed class GameBootstrap : MonoBehaviour
{
    private void Awake()
    {
        SaveManager.LoadOrCreate(); // ✅ must happen before JobManager.Awake
    }
}
