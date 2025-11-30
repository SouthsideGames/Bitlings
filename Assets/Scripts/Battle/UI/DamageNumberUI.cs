using UnityEngine;
using TMPro;

public class DamageNumberUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TextMeshProUGUI label;

    [Header("Tuning")]
    [SerializeField] private float riseDistance = 40f;
    [SerializeField] private float lifetime     = 0.6f;

    private RectTransform _rt;
    private CanvasGroup   _cg;

    void Awake()
    {
        _rt = transform as RectTransform;
        if (!_rt) _rt = gameObject.AddComponent<RectTransform>();

        if (!label) label = GetComponentInChildren<TextMeshProUGUI>();

        _cg = GetComponent<CanvasGroup>();
        if (!_cg) _cg = gameObject.AddComponent<CanvasGroup>();
    }

    public void Init(int amount, Color color)
    {
        if (!label) return;

        label.text = amount.ToString();
        label.color = color;

        _cg.alpha = 1f;

        float startY = _rt.anchoredPosition.y;
        float endY   = startY + riseDistance;

        LeanTween.moveY(_rt, endY, lifetime).setEaseOutQuad();
        LeanTween.value(gameObject, 1f, 0f, lifetime)
            .setOnUpdate(a => _cg.alpha = a)
            .setOnComplete(() => Destroy(gameObject));
    }
}
