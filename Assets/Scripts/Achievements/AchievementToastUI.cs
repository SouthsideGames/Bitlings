using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public sealed class AchievementToastUI : MonoBehaviour
{
    public static AchievementToastUI I { get; private set; }

    // If no prefab instance exists in a scene (common during battle-only scenes),
    // we will spawn a minimal runtime toast UI so achievement popups are ALWAYS shown.
    private const string RuntimeRootName = "(Runtime) AchievementToastUI";

    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI label;

    [Header("Timing")]
    [SerializeField] private float showSeconds = 2.25f;

    [Header("Text")]
    [SerializeField] private string prefixText = "Achievement Unlocked:";

    private readonly Queue<AchievementEntrySO> _queue = new Queue<AchievementEntrySO>();
    private bool _playing;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        if (!canvasGroup) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup) canvasGroup.alpha = 0f;
    }

    /// <summary>
    /// Guaranteed enqueue: if no toast exists yet, create a minimal runtime toast UI.
    /// Safe to call from anywhere (battle, menus, transitions).
    /// </summary>
    public static void EnqueueGuaranteed(AchievementEntrySO entry)
    {
        if (entry == null) return;

        // If instance already exists, use it.
        if (I != null)
        {
            I.QueueUnlocked(entry);
            return;
        }

        // Try to find an inactive instance first.
        var found = Object.FindFirstObjectByType<AchievementToastUI>(FindObjectsInactive.Include);
        if (found != null)
        {
            if (!found.gameObject.activeInHierarchy)
                found.gameObject.SetActive(true);

            I = found;
            I.QueueUnlocked(entry);
            return;
        }

        CreateRuntimeToast();
        if (I != null)
            I.QueueUnlocked(entry);
    }

    private static void CreateRuntimeToast()
    {
        // Root
        var root = new GameObject(RuntimeRootName);
        Object.DontDestroyOnLoad(root);

        // Canvas
        var canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 9999;

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
        rt.anchoredPosition = new Vector2(0f, -120f);
        rt.sizeDelta = new Vector2(860f, 160f);

        var bg = panelGO.AddComponent<Image>();
        bg.raycastTarget = false;
        // Default white sprite; tint to dark.
        bg.color = new Color(0f, 0f, 0f, 0.75f);

        var cg = panelGO.AddComponent<CanvasGroup>();
        cg.alpha = 0f;

        // Icon
        var iconGO = new GameObject("Icon");
        iconGO.transform.SetParent(panelGO.transform, false);
        var iconRT = iconGO.AddComponent<RectTransform>();
        iconRT.anchorMin = new Vector2(0f, 0.5f);
        iconRT.anchorMax = new Vector2(0f, 0.5f);
        iconRT.pivot = new Vector2(0f, 0.5f);
        iconRT.anchoredPosition = new Vector2(22f, 0f);
        iconRT.sizeDelta = new Vector2(110f, 110f);

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
        labelRT.offsetMin = new Vector2(150f, 18f);
        labelRT.offsetMax = new Vector2(-18f, -18f);

        var tmp = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.raycastTarget = false;

        // FIX: TMP_Text.enableWordWrapping is obsolete -> use textWrappingMode
        tmp.textWrappingMode = TextWrappingModes.Normal;

        tmp.fontSize = 42f;
        tmp.alignment = TextAlignmentOptions.Left | TextAlignmentOptions.Midline;
        tmp.color = Color.white;

        var toast = panelGO.AddComponent<AchievementToastUI>();
        toast.canvasGroup = cg;
        toast.iconImage = iconImg;
        toast.label = tmp;

        // Ensure singleton binding
        I = toast;
    }

    /// <summary>
    /// Queue a toast for an unlocked achievement. Safe to call even if the toast is inactive.
    /// </summary>
    public void QueueUnlocked(AchievementEntrySO entry)
    {
        if (entry == null) return;

        _queue.Enqueue(entry);

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

        var entry = _queue.Dequeue();

        if (iconImage) iconImage.sprite = entry.icon;
        if (label) label.text = $"{prefixText} {entry.displayName}";

        if (AudioManager.I != null)
            AudioManager.I.PlaySfx(SfxType.AchievementUnlocked);

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
