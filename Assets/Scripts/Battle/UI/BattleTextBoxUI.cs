using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections;

public class BattleTextBoxUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private TextMeshProUGUI lineText;

    [Header("Inline Icons")]
    [SerializeField] private GameObject iconsRoot; 
    [SerializeField] private Image critIcon;
    [SerializeField] private Image shieldIcon;
    [SerializeField] private Image effectiveIcon;     
    [SerializeField] private Sprite superEffectiveSprite;
    [SerializeField] private Sprite notEffectiveSprite;

    [Header("Timing")]
    [SerializeField] private float typeSecondsPerChar = 0.02f;
    [SerializeField] private float lineHoldSeconds = 0.25f;

    [Header("Render Override")]
    [SerializeField] private bool forceTopCanvasSorting = true;
    [SerializeField] private int topCanvasSortingOrder = 5000;

    [Header("Debug")]
#if UNITY_EDITOR || DEVELOPMENT_BUILD
    [SerializeField] private bool debugTextTrace = true;
#else
    private const bool debugTextTrace = false;
#endif

    public bool HasRenderableTarget => lineText != null;

    private RiftManager _hookedRift;

    private void Awake()
    {
        AutoWireIfNeeded();
        RefreshRiftHook();
        ApplyRegularBattleIdleVisibility();
    }

    private void OnEnable()
    {
        AutoWireIfNeeded();
        RefreshRiftHook();
        ApplyRegularBattleIdleVisibility();
    }

    private void OnDisable()
    {
        UnhookRiftState();
    }

    private void LateUpdate()
    {
        if (_hookedRift != RiftManager.I)
        {
            RefreshRiftHook();
            ApplyRegularBattleIdleVisibility();
        }
    }

    private void AutoWireIfNeeded()
    {
        if (!lineText)
            lineText = GetComponentInChildren<TextMeshProUGUI>(true);

        if (!canvasGroup)
            canvasGroup = GetComponentInChildren<CanvasGroup>(true);

        EnsureTopCanvasSorting();
    }

    private void RefreshRiftHook()
    {
        var em = RiftManager.I;
        if (_hookedRift == em) return;

        UnhookRiftState();

        _hookedRift = em;
        if (_hookedRift != null)
            _hookedRift.OnStateChanged += HandleRiftStateChanged;
    }

    private void UnhookRiftState()
    {
        if (_hookedRift != null)
            _hookedRift.OnStateChanged -= HandleRiftStateChanged;

        _hookedRift = null;
    }

    private void HandleRiftStateChanged()
    {
        ApplyRegularBattleIdleVisibility();
    }

    private void ApplyRegularBattleIdleVisibility()
    {
        if (canvasGroup == null) return;

        // In Iron Career we always keep the battle text box visible and skip rift-based visibility.
        if (IronCareerRuntime.IsActive)
        {
            canvasGroup.alpha = 1f;
            return;
        }

        var em = RiftManager.I;
        if (em == null) return;
    }

    private IEnumerator CoWaitUnscaled(float seconds)
    {
        float s = Mathf.Max(0f, seconds);
        if (s <= 0f) yield break;

        float end = Time.unscaledTime + s;
        while (Time.unscaledTime < end)
            yield return null;
    }

    public IEnumerator ShowLine(string line, float battleSpeed)
        => ShowLine(new BattleLine(line, BattleLineTag.None), battleSpeed);

    public void ShowLineInstant(string line, BattleLineTag tags, float battleSpeed)
    {
        StartCoroutine(ShowLine(new BattleLine(line, tags), battleSpeed));
    }

    public IEnumerator ShowLine(BattleLine line, float battleSpeed)
    {
        AutoWireIfNeeded();

        EnsureVisibleChain();
        transform.SetAsLastSibling();

        if (canvasGroup)
        {
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;
        }

        if (lineText == null) yield break;

        if (!lineText.gameObject.activeSelf) lineText.gameObject.SetActive(true);
        lineText.enabled = true;
        var c = lineText.color;
        c.a = 1f;
        lineText.color = c;
        lineText.canvasRenderer.SetAlpha(1f);

#if UNITY_EDITOR
        if (debugTextTrace)
        {
            string raw = line.text ?? string.Empty;
            string preview = raw.Length > 80 ? raw.Substring(0, 80) + "..." : raw;
            DevLog.Log($"[IronTextTrace] TextBoxUI.ShowLine: obj={name} active={gameObject.activeInHierarchy} lineLen={raw.Length} preview='{preview}'");
        }
#endif

        bool showIcons = SettingsManager.I == null || SettingsManager.I.GetShowInlineBattleIcons();

        if (iconsRoot) iconsRoot.SetActive(showIcons);

        if (showIcons)
        {
            if (critIcon)   critIcon.enabled   = (line.tags & BattleLineTag.Crit) != 0;
            if (shieldIcon) shieldIcon.enabled = (line.tags & BattleLineTag.Shield) != 0;

            bool se  = (line.tags & BattleLineTag.SuperEffective) != 0;
            bool nve = (line.tags & BattleLineTag.NotEffective) != 0;

            if (effectiveIcon)
            {
                effectiveIcon.enabled = se || nve;
                if (se && superEffectiveSprite) effectiveIcon.sprite = superEffectiveSprite;
                else if (nve && notEffectiveSprite) effectiveIcon.sprite = notEffectiveSprite;
            }
        }
        else
        {
            if (critIcon) critIcon.enabled = false;
            if (shieldIcon) shieldIcon.enabled = false;
            if (effectiveIcon) effectiveIcon.enabled = false;
        }

        if (showIcons)
        {
            if (critIcon && critIcon.enabled) PunchIcon(critIcon);
            else if (shieldIcon && shieldIcon.enabled) PunchIcon(shieldIcon);
            else if (effectiveIcon && effectiveIcon.enabled) PunchIcon(effectiveIcon);
        }

        string full = line.text ?? "";
        lineText.text = full;
        lineText.maxVisibleCharacters = 0;

        bool isAuto = (RiftManager.I != null && RiftManager.I.IsAutoMode);
        bool compressAuto = isAuto && (SettingsManager.I == null || SettingsManager.I.GetCompressAutoBattleText());

        float cps = Mathf.Max(0.001f, typeSecondsPerChar);
        float scaled = cps / Mathf.Max(0.25f, battleSpeed);

        if (compressAuto)
        {
            lineText.text = full;
            lineText.maxVisibleCharacters = int.MaxValue;
            float autoHold = Mathf.Max(0.05f, 0.2f / Mathf.Max(0.25f, battleSpeed));
            yield return CoWaitUnscaled(autoHold);
            yield break;
        }

        if (isAuto) scaled *= 0.25f;

        if (full.Length * scaled > 0.75f)
        {
            lineText.text = full;
            lineText.maxVisibleCharacters = int.MaxValue;
            yield return CoWaitUnscaled(0.25f / Mathf.Max(0.25f, battleSpeed));
            yield break;
        }

        int len = full.Length;
        if (len > 0)
        {
            float perChar = Mathf.Max(0.0001f, scaled);
            float next = Time.unscaledTime + perChar;

            for (int visible = 1; visible <= len; visible++)
            {
                while (Time.unscaledTime < next)
                    yield return null;

                lineText.maxVisibleCharacters = visible;
                next += perChar;
            }
        }
        else
        {
            lineText.maxVisibleCharacters = int.MaxValue;
        }

        float hold = Mathf.Max(0f, lineHoldSeconds / Mathf.Max(0.25f, battleSpeed));
        if (hold > 0f) yield return CoWaitUnscaled(hold);
    }

    private void EnsureVisibleChain()
    {
        var t = transform;
        while (t != null)
        {
            if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
            t = t.parent;
        }
    }

    private void EnsureTopCanvasSorting()
    {
        if (!forceTopCanvasSorting) return;

        var c = GetComponent<Canvas>();
        if (!c) return;

        c.enabled = true;
        c.overrideSorting = true;
        c.sortingOrder = topCanvasSortingOrder;
    }

    private void PunchIcon(Image img)
    {
        if (!img || !img.gameObject) return;

        LeanTween.cancel(img.gameObject);

        var t = img.transform;
        t.localScale = Vector3.one;

        // Small, quick punch.
        LeanTween.scale(img.gameObject, Vector3.one * 1.15f, 0.08f)
            .setEaseOutBack()
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!img) return;
                LeanTween.scale(img.gameObject, Vector3.one, 0.10f)
                    .setEaseOutQuad()
                    .setIgnoreTimeScale(true);
            });
    }
}
