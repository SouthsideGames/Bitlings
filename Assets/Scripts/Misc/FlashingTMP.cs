using UnityEngine;
using TMPro;

[RequireComponent(typeof(TextMeshProUGUI))]
public class FlashingTMP : MonoBehaviour
{
    [SerializeField, Range(0.2f, 5f)] float speed = 1.25f;
    [SerializeField, Range(0f, 1f)]   float minAlpha = 0.35f;
    [SerializeField, Range(0f, 1f)]   float maxAlpha = 1f;
    TextMeshProUGUI _tmp;
    Color _base;

    void Awake()
    {
        _tmp = GetComponent<TextMeshProUGUI>();
        _base = _tmp.color;
    }

    void Update()
    {
        float a = Mathf.Lerp(minAlpha, maxAlpha, 0.5f * (1f + Mathf.Sin(Time.unscaledTime * Mathf.PI * 2f * speed)));
        var c = _base; c.a = a;
        _tmp.color = c;
    }
}
