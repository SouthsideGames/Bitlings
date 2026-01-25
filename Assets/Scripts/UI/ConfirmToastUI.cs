using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class ConfirmToastUI : MonoBehaviour
{
    public static ConfirmToastUI I { get; private set; }

    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private RectTransform toastRoot;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI label;

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float fadeInSeconds = 0.12f;
    [SerializeField, Min(0.05f)] private float showSeconds = 1.25f;
    [SerializeField, Min(0.05f)] private float fadeOutSeconds = 0.12f;

    [Header("Motion")]
    [SerializeField] private float riseY = 18f;

    [Header("Optional")]
    [SerializeField] private Sprite defaultIcon;
    [SerializeField] private bool autoSubscribeToGameEvents = true;

    private Vector2 _baseAnchoredPos;
    private bool _playing;

    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        if (!toastRoot) toastRoot = transform as RectTransform;

        if (toastRoot)
            _baseAnchoredPos = toastRoot.anchoredPosition;

        if (canvasGroup)
            canvasGroup.alpha = 0f;
    }

    private void OnEnable()
    {
        if (autoSubscribeToGameEvents)
            GameEvents.ToastRequested += HandleToastRequested;
    }

    private void OnDisable()
    {
        if (autoSubscribeToGameEvents)
            GameEvents.ToastRequested -= HandleToastRequested;
    }

    private void HandleToastRequested(string msg)
    {
        Show(msg);
    }

    /// <summary>
    /// Show a single confirmation toast.
    /// If already playing, the call is ignored.
    /// </summary>
    public void Show(string message, Sprite iconOverride = null)
    {
        if (_playing) return;
        if (string.IsNullOrWhiteSpace(message)) return;

        _playing = true;

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        if (label)
            label.text = message;

        if (iconImage)
        {
            var use = iconOverride ?? defaultIcon;
            iconImage.sprite = use;
            iconImage.enabled = use != null;
        }

        // Cancel any previous tweens
        if (canvasGroup) LeanTween.cancel(canvasGroup.gameObject);
        if (toastRoot) LeanTween.cancel(toastRoot);

        // Reset visuals
        if (canvasGroup) canvasGroup.alpha = 0f;
        if (toastRoot) toastRoot.anchoredPosition = _baseAnchoredPos;

        // Fade in
        if (canvasGroup)
            LeanTween.alphaCanvas(canvasGroup, 1f, fadeInSeconds)
                     .setEaseOutCubic();

        // Rise motion (optional polish)
        if (toastRoot && Mathf.Abs(riseY) > 0.01f)
            LeanTween.move(
                toastRoot,
                _baseAnchoredPos + new Vector2(0f, riseY),
                showSeconds
            ).setEaseOutCubic();

        // Hold → Fade out → Stop
        LeanTween.delayedCall(gameObject, fadeInSeconds + showSeconds, () =>
        {
            if (canvasGroup == null)
            {
                _playing = false;
                return;
            }

            LeanTween.alphaCanvas(canvasGroup, 0f, fadeOutSeconds)
                     .setEaseInCubic()
                     .setOnComplete(() =>
                     {
                         if (toastRoot)
                             toastRoot.anchoredPosition = _baseAnchoredPos;

                         _playing = false;
                     });
        });
    }
}
