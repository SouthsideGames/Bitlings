using UnityEngine;

/// <summary>
/// Marks a transform as a named fly-animation destination.
/// Example key: home_resources_button
/// </summary>
public class UIFlyTargetAnchor : MonoBehaviour
{
    [SerializeField] private string targetKey = "home_resources_button";

    public string TargetKey => targetKey;

    public static Transform Resolve(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        var anchors = FindObjectsByType<UIFlyTargetAnchor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < anchors.Length; i++)
        {
            var a = anchors[i];
            if (!a) continue;
            if (!string.Equals(a.targetKey, key, System.StringComparison.OrdinalIgnoreCase)) continue;
            return a.transform;
        }

        return null;
    }
}