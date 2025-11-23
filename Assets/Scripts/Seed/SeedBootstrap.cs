using UnityEngine;

/// <summary>
/// Attach this to an always-present object in your main scene.
/// It applies the global RNG seed (custom or daily) once on startup.
/// </summary>
public class SeedBootstrap : MonoBehaviour
{
    void Start()
    {
        SeedService.ApplyGlobalSeedForSession();
    }
}
