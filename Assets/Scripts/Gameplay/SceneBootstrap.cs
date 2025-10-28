using UnityEngine;

[DefaultExecutionOrder(-500)]
public class SceneBootstrap : MonoBehaviour
{
    void Awake()
    {
        if (SaveManager.Data == null)
            SaveManager.LoadOrCreate();
    }
}
