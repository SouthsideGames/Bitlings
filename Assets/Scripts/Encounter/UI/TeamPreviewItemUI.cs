using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TeamPreviewItemUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private CanvasGroup cg; // optional fade

    // ─────────────────────────────────────────────────────────────
    // Micro Animations
    // ─────────────────────────────────────────────────────────────
    [Header("Micro Animations")]
    [Tooltip("Root that will bob / pulse. Defaults to icon if not set.")]
    [SerializeField] private RectTransform animRoot;

    [SerializeField, Min(0f)] private float bobAmplitude = 6f;
    [SerializeField, Min(0.1f)] private float bobDuration = 1.2f;

    [SerializeField, Range(0f, 0.25f)] private float scaleAmplitude = 0.03f;
    [SerializeField, Min(0.1f)] private float scaleDuration = 1.1f;

    [Tooltip("Random phase offset so all items aren’t in perfect sync.")]
    [SerializeField, Range(0f, 0.5f)] private float randomPhaseDelay = 0.25f;

    [SerializeField] private bool enableMicroAnim = true;

    int _bobTweenId = -1;
    int _scaleTweenId = -1;
    Vector3 _basePos;
    Vector3 _baseScale;
    bool _initialized;

    // ─────────────────────────────────────────────────────────────
    // HP Indicator (Optional)
    // ─────────────────────────────────────────────────────────────
    [Header("HP Indicator (Optional)")]
    [Tooltip("Small HP bar. Should be a filled Image.")]
    [SerializeField] private Image hpFill;

    [Tooltip("Icon or '!' marker when HP is critically low.")]
    [SerializeField] private GameObject lowHpMarker;

    [SerializeField, Range(0.05f, 0.5f)]
    private float lowHpThreshold = 0.25f;

    void Awake()
    {
        if (!animRoot)
        {
            if (icon != null)
                animRoot = icon.rectTransform;
            else
                animRoot = transform as RectTransform;
        }

        if (animRoot)
        {
            _basePos = animRoot.anchoredPosition;
            _baseScale = animRoot.localScale;
            _initialized = true;
        }

        // Hide HP UI by default until explicitly set
        if (hpFill)
            hpFill.gameObject.SetActive(false);
        if (lowHpMarker)
            lowHpMarker.SetActive(false);
    }

    void OnEnable()
    {
        if (enableMicroAnim)
            StartMicroAnim();
    }

    void OnDisable()
    {
        StopMicroAnim();
    }

    // ─────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────
    public void Bind(OwnedMonsterData om)
    {
        if (om == null) return;

        // Lookup definition (icon + display name)
        string displayName = om.monsterId;
        Sprite sprite = null;
        
        MonsterDataSO def = null;

        try
        {
            def = MonsterLibraryLocator.GetById(om.monsterId);
            if (def != null)
            {
                if (!string.IsNullOrEmpty(def.displayName))
                    displayName = def.displayName;
                sprite = def.icon; // assumes MonsterDataSO has "public Sprite icon;"
            }
        }
        catch { }

        if (icon)
                {
                    if (def != null)
                    {
                        bool preferShiny = false;
                        if (def != null && !string.IsNullOrEmpty(def.id))
                            preferShiny = MonsterVariantPreference.IsPreferredShiny(def.id);
                        else
                            preferShiny = om != null && (om.isShiny || om.shinyTier > 0);

                        var s = MonsterNameFormatter.GetIcon(def, preferShiny, backIcon: false);
                        icon.enabled = s != null;
                        icon.sprite = s;
                        icon.color = Color.white;
                    }
                    else
                    {
                        icon.enabled = false;
                        icon.sprite = null;
                    }
                }

                // Name (shiny-aware formatting)
                if (nameText)
                {
                    if (def != null)
                        nameText.text = MonsterNameFormatter.Format(def, om.isShiny);
                    else
                        nameText.text = om.monsterId;
                }

                if (levelText)
                    levelText.text = $"Lv {Mathf.Max(1, om.level)}";

        // Make sure micro anim is running after (re)bind
        if (isActiveAndEnabled && enableMicroAnim)
            StartMicroAnim();
    }

    /// <summary>
    /// Optional: call this with a normalized HP fraction [0..1] if you have it.
    /// If you never call this, the HP UI stays hidden.
    /// </summary>
    public void SetHpFraction(float fraction)
    {
        float f = Mathf.Clamp01(fraction);

        if (hpFill)
        {
            hpFill.fillAmount = f;
            hpFill.gameObject.SetActive(true);
        }

        if (lowHpMarker)
        {
            bool showLow = (f > 0f && f <= lowHpThreshold);
            lowHpMarker.SetActive(showLow);
        }
    }

    public void SetAlpha(float a)
    {
        if (!cg) return;
        a = Mathf.Clamp01(a);
        cg.alpha = a;
        cg.blocksRaycasts = a >= 0.99f;
        cg.interactable   = a >= 0.99f;
    }

    // ─────────────────────────────────────────────────────────────
    // Micro Anim helpers
    // ─────────────────────────────────────────────────────────────
    void StartMicroAnim()
    {
        if (!_initialized || animRoot == null) return;

        StopMicroAnim(); // ensure we don't stack tweens

        // Bob up/down
        _bobTweenId = LeanTween
            .moveY(animRoot,
                   _basePos.y + bobAmplitude,
                   bobDuration)
            .setEaseInOutSine()
            .setLoopPingPong()
            .setDelay(Random.value * randomPhaseDelay)
            .id;

        // Subtle scale pulse
        Vector3 targetScale = _baseScale * (1f + scaleAmplitude);
        _scaleTweenId = LeanTween
            .scale(animRoot.gameObject, targetScale, scaleDuration)
            .setEaseInOutSine()
            .setLoopPingPong()
            .id;
    }

    void StopMicroAnim()
    {
        if (_bobTweenId != -1)
        {
            LeanTween.cancel(_bobTweenId);
            _bobTweenId = -1;
        }

        if (_scaleTweenId != -1)
        {
            LeanTween.cancel(_scaleTweenId);
            _scaleTweenId = -1;
        }

        if (_initialized && animRoot)
        {
            animRoot.anchoredPosition = _basePos;
            animRoot.localScale = _baseScale;
        }
    }
}
