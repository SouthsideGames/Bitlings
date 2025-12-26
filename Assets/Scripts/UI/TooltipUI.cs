using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class TooltipUI : MonoBehaviour
{
    public static TooltipUI I { get; private set; }

    [Header("Wiring")]
    [SerializeField] private CanvasGroup group;
    [SerializeField] private TextMeshProUGUI label;
    [SerializeField] private Button closeButton;

    [Header("Animation")]
    [SerializeField] private float fadeInTime = 0.12f;
    [SerializeField] private float fadeOutTime = 0.25f;

    private bool _visible;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (!group) group = GetComponent<CanvasGroup>();
        if (group) group.alpha = 0f;

        if (closeButton)
        {
            closeButton.onClick.RemoveAllListeners();
            closeButton.onClick.AddListener(Hide);
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Show Tooltip
    // ─────────────────────────────────────────────────────────────
    public void Show(string text)
    {
        if (!label || !group) return;

        label.text = text;
        _visible = true;

        // Reset instantly then fade in
        group.alpha = 0f;
        gameObject.SetActive(true);

        LeanTween.alphaCanvas(group, 1f, fadeInTime)
            .setEaseOutQuad();
    }

    // ─────────────────────────────────────────────────────────────
    // Close Button → Hide
    // ─────────────────────────────────────────────────────────────
    public void Hide()
    {
        if (!_visible || !group) return;

        _visible = false;

        LeanTween.alphaCanvas(group, 0f, fadeOutTime)
            .setEaseInQuad()
            .setOnComplete(() =>
            {
                gameObject.SetActive(false);
            });
    }

    // Instantly hide with no animation (if needed)
    public void HideImmediate()
    {
        _visible = false;
        if (group)
        {
            group.alpha = 0f;
        }
        gameObject.SetActive(false);
    }
}
