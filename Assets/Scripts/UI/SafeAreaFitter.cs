using UnityEngine;

[ExecuteAlways]
public class SafeAreaFitter : MonoBehaviour
{
    RectTransform rt;

    void Awake() { rt = GetComponent<RectTransform>(); Apply(); }
    void OnRectTransformDimensionsChange() { Apply(); }

    void Apply()
    {
        if (rt == null) return;
        var sa = Screen.safeArea;
        Vector2 anchorMin = sa.position;
        Vector2 anchorMax = sa.position + sa.size;
        anchorMin.x /= Screen.width;
        anchorMin.y /= Screen.height;
        anchorMax.x /= Screen.width;
        anchorMax.y /= Screen.height;
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }
}
