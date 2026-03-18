using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class ExchangeToastUI : MonoBehaviour
{
    public static ExchangeToastUI I { get; private set; }

    private const string RuntimeRootName = "(Runtime) ExchangeToastUI";

    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI label;

    [Header("Timing")]
    [SerializeField] private float showSeconds = 2.25f;

    private readonly Queue<(string message, Sprite icon)> _queue = new Queue<(string, Sprite)>();
    private bool _playing;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup) canvasGroup.alpha = 0f;
    }

    public static void EnqueueGuaranteed(string message, Sprite icon = null)
    {
        if (string.IsNullOrEmpty(message)) return;

        if (I != null)
        {
            I.Enqueue(message, icon);
            return;
        }

        var found = Object.FindFirstObjectByType<ExchangeToastUI>(FindObjectsInactive.Include);
        if (found != null)
        {
            if (!found.gameObject.activeInHierarchy)
                found.gameObject.SetActive(true);

            I = found;
            I.Enqueue(message, icon);
            return;
        }

        CreateRuntimeToast();
        if (I != null)
            I.Enqueue(message, icon);
    }

    private static void CreateRuntimeToast()
    {
        var root = new GameObject(RuntimeRootName);
        Object.DontDestroyOnLoad(root);

        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9998;

        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1080, 1920);
        scaler.matchWidthOrHeight = 0.5f;

        root.AddComponent<GraphicRaycaster>();

        // Panel
        var panelGO = new GameObject("ToastPanel");
        panelGO.transform.SetParent(root.transform, false);

        var rt = panelGO.AddComponent<RectTransform>();
        rt.anchorMin = new Vector2(0.5f, 1f);
        rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -300f);
        rt.sizeDelta = new Vector2(860f, 140f);

        var bg = panelGO.AddComponent<Image>();
        bg.raycastTarget = false;
        bg.color = new Color(0.05f, 0.15f, 0.05f, 0.85f);

        var cg = panelGO.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        // Icon
        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(panelGO.transform, false);
        var iconRT = iconGO.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0f, 0.5f);
        iconRT.anchorMax = new Vector2(0f, 0.5f);
        iconRT.pivot = new Vector2(0f, 0.5f);
        iconRT.anchoredPosition = new Vector2(18f, 0f);
        iconRT.sizeDelta = new Vector2(100f, 100f);

        var iconImg = iconGO.AddComponent<Image>();
        iconImg.raycastTarget = false;
        iconImg.preserveAspect = true;
        iconImg.color = Color.white;

        // Label
        var labelGO = new GameObject("Label");
        labelGO.transform.SetParent(panelGO.transform, false);
        var labelRT = labelGO.AddComponent<RectTransform>();
        labelRT.anchorMin = new Vector2(0f, 0f);
        labelRT.anchorMax = new Vector2(1f, 1f);
        labelRT.offsetMin = new Vector2(134f, 14f);
        labelRT.offsetMax = new Vector2(-14f, -14f);

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.raycastTarget = false;
        tmp.textWrappingMode = TextWrappingModes.Normal;
        tmp.fontSize = 38f;
        tmp.alignment = TextAlignmentOptions.Left | TextAlignmentOptions.Midline;
        tmp.color = Color.white;

        var toast = panelGO.AddComponent<ExchangeToastUI>();
        toast.canvasGroup = cg;
        toast.iconImage = iconImg;
        toast.label = tmp;

        I = toast;
    }

    public void Enqueue(string message, Sprite icon = null)
    {
        if (string.IsNullOrEmpty(message)) return;

        _queue.Enqueue((message, icon));

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        if (!_playing)
            PlayNext();
    }

    private void PlayNext()
    {
        if (_queue.Count == 0)
        {
            _playing = false;
            if (canvasGroup) canvasGroup.alpha = 0f;
            return;
        }

        _playing = true;

        var (message, icon) = _queue.Dequeue();

        if (iconImage)
        {
            iconImage.sprite = icon;
            iconImage.gameObject.SetActive(icon != null);
        }
        if (label) label.text = message;

        if (canvasGroup)
        {
            LeanTween.cancel(canvasGroup.gameObject);
            canvasGroup.alpha = 0f;

            LeanTween.alphaCanvas(canvasGroup, 1f, 0.18f).setEaseOutCubic().setIgnoreTimeScale(true);

            LeanTween.delayedCall(gameObject, showSeconds, () =>
            {
                if (canvasGroup == null)
                {
                    PlayNext();
                    return;
                }

                LeanTween.alphaCanvas(canvasGroup, 0f, 0.18f).setEaseInCubic()
                    .setIgnoreTimeScale(true)
                    .setOnComplete(() => PlayNext());
            }).setIgnoreTimeScale(true);
        }
        else
        {
            LeanTween.delayedCall(gameObject, showSeconds, () => PlayNext()).setIgnoreTimeScale(true);
        }
    }
}
