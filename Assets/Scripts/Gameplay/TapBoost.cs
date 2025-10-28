using UnityEngine;
using UnityEngine.EventSystems;

public class TapBoost : MonoBehaviour, IPointerDownHandler
{
    public static TapBoost I;
    [Header("Boost")]
    public float baseMultiplier = 2f;
    public float duration = 2f;

    float remaining;
    int tapsThisEncounter;

    void Awake() { I = this; }

    public float CurrentMultiplier => remaining > 0 ? (baseMultiplier + SaveManager.Data.tapLevel * 0.25f) : 1f;
    public int TapsThisEncounter => tapsThisEncounter;

    void Update()
    {
        if (remaining > 0) remaining -= Time.deltaTime;
    }

    public void ResetEncounter()
    {
        remaining = 0;
        tapsThisEncounter = 0;
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        remaining = duration;
        tapsThisEncounter++;
        // tiny punch effect if object has a rectTransform
        var rt = transform as RectTransform;
        if (rt) LeanTween.scale(rt, Vector3.one * 1.05f, 0.06f).setLoopPingPong(1);
    }
}
