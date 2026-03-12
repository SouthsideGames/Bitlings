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

    private int _fadeTweenId = -1;

    void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (!group) group = GetComponent<CanvasGroup>();

        // Keep the object ACTIVE always; hide via CanvasGroup.
        ApplyHiddenStateImmediate();

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
        if (string.IsNullOrEmpty(text)) return;

        label.text = text;
        CancelFadeTween();

        // Enable interaction while visible
        group.blocksRaycasts = true;
        group.interactable = true;

        // Fade in from current alpha (or force from 0 if you want)
        group.alpha = 0f;

        _fadeTweenId = LeanTween.alphaCanvas(group, 1f, fadeInTime)
            .setEaseOutQuad()
            .setIgnoreTimeScale(true)
            .id;
    }

    // ─────────────────────────────────────────────────────────────
    // Close Button → Hide (fade alpha only; DO NOT deactivate GO)
    // ─────────────────────────────────────────────────────────────
    public void Hide()
    {
        if (!group) return;

        CancelFadeTween();

        _fadeTweenId = LeanTween.alphaCanvas(group, 0f, fadeOutTime)
            .setEaseInQuad()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                // Disable interaction while hidden so it doesn't block UI
                group.blocksRaycasts = false;
                group.interactable = false;
            })
            .id;
    }

    // Instantly hide with no animation (if needed)
    public void HideImmediate()
    {
        if (!group) return;

        CancelFadeTween();
        ApplyHiddenStateImmediate();
    }

    private void ApplyHiddenStateImmediate()
    {
        if (!group) return;
        group.alpha = 0f;
        group.blocksRaycasts = false;
        group.interactable = false;
    }

    private void CancelFadeTween()
    {
        if (_fadeTweenId != -1)
        {
            if (LeanTween.isTweening(_fadeTweenId))
                LeanTween.cancel(_fadeTweenId);

            _fadeTweenId = -1;
        }
    }
}
