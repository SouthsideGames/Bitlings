using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

public class ResourceFlyAnimationUI : MonoBehaviour
{
    public const string HomeResourcesTargetKey = "home_resources_button";

    public static ResourceFlyAnimationUI I
    {
        get
        {
            if (_instance) return _instance;

            _instance = FindFirstObjectByType<ResourceFlyAnimationUI>(FindObjectsInactive.Include);
            if (_instance) return _instance;

            var go = new GameObject("[ResourceFlyAnimationUI]");
            _instance = go.AddComponent<ResourceFlyAnimationUI>();
            DontDestroyOnLoad(go);
            return _instance;
        }
    }

    private static ResourceFlyAnimationUI _instance;

    [Header("Canvas")]
    [SerializeField] private Canvas overlayCanvas;
    [SerializeField] private RectTransform tokenRoot;

    [Header("Visual")]
    [SerializeField] private Vector2 tokenSize = new Vector2(34f, 34f);
    [SerializeField] private Color tokenColor = Color.white;

    [Header("Timing")]
    [SerializeField] private float travelTime = 0.45f;
    [SerializeField] private float staggerSeconds = 0.05f;
    [SerializeField] private float arcHeight = 52f;

    [Header("Burst")]
    [SerializeField] private int minBurstCount = 1;
    [SerializeField] private int maxBurstCount = 8;

    [Header("Destination Impact")]
    [SerializeField] private bool pulseDestinationOnImpact = true;
    [SerializeField] private float pulseScale = 1.08f;
    [SerializeField] private float pulseUpTime = 0.07f;
    [SerializeField] private float pulseDownTime = 0.09f;
    [SerializeField] private float pulseCooldown = 0.08f;

    [Header("Impact Spark")]
    [SerializeField] private bool playImpactSpark = true;
    [SerializeField] private float sparkStartScale = 0.65f;
    [SerializeField] private float sparkEndScale = 1.35f;
    [SerializeField] private float sparkDuration = 0.16f;
    [SerializeField] private float sparkAlpha = 0.85f;
    [SerializeField] private float sparkCooldown = 0.06f;

    [Header("Impact SFX")]
    [SerializeField] private bool playImpactSfx = true;
    [SerializeField] private SfxType impactSfxType = SfxType.Filling;
    [SerializeField] private float impactSfxCooldown = 0.08f;

    [Header("Pooling")]
    [SerializeField] private bool useTokenPooling = true;
    [SerializeField] private int prewarmCount = 14;
    [SerializeField] private int maxPooledTokens = 80;

    private readonly Stack<TokenView> _pool = new Stack<TokenView>(32);
    private readonly List<TokenView> _allPooled = new List<TokenView>(64);
    private readonly Dictionary<Transform, float> _lastPulseByTarget = new Dictionary<Transform, float>();
    private readonly Dictionary<Transform, Vector3> _pulseBaseScale = new Dictionary<Transform, Vector3>();
    private readonly Dictionary<Transform, float> _lastSparkByTarget = new Dictionary<Transform, float>();
    private float _lastImpactSfxTime = -999f;

    private sealed class TokenView
    {
        public GameObject go;
        public RectTransform rt;
        public CanvasGroup cg;
        public Image img;
        public bool isPooled;
    }

    private void Awake()
    {
        if (_instance != null && _instance != this)
        {
            Destroy(gameObject);
            return;
        }

        _instance = this;
        EnsureOverlayCanvas();
        PrewarmPoolIfNeeded();
    }

    public static void PlayToHome(ResourceType type, int amount, Transform from)
    {
        var to = UIFlyTargetAnchor.Resolve(HomeResourcesTargetKey);
        if (!to)
            to = TryResolveHomeResourcesFallback();

        if (!to) return;

        Play(type, amount, from, to);
    }

    public static void PlayFromHomeTo(ResourceType type, int amount, Transform to)
    {
        var from = UIFlyTargetAnchor.Resolve(HomeResourcesTargetKey);
        if (!from)
            from = TryResolveHomeResourcesFallback();

        if (!from || !to) return;

        Play(type, amount, from, to);
    }

    private static Transform TryResolveHomeResourcesFallback()
    {
        var panelButtons = FindObjectsByType<PanelButtonUI>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (int i = 0; i < panelButtons.Length; i++)
        {
            var b = panelButtons[i];
            if (!b || b.target != PanelId.Resources) continue;
            return b.transform;
        }

        return null;
    }

    public static void Play(ResourceType type, int amount, Transform from, Transform to)
    {
        I.PlayInternal(type, amount, from, to);
    }

    public void PlayInternal(ResourceType type, int amount, Transform from, Transform to)
    {
        if (amount <= 0 || !from || !to) return;

        EnsureOverlayCanvas();
        if (!overlayCanvas || !tokenRoot) return;

        if (!TryGetScreenPoint(from, out var fromScreen)) return;
        if (!TryGetScreenPoint(to, out var toScreen)) return;
        if (!TryScreenToCanvasLocal(fromScreen, out var startLocal)) return;
        if (!TryScreenToCanvasLocal(toScreen, out var endLocal)) return;

        if (!ResourcePanelUI.TryGetCatalogIconGlobal(type, out var icon) || !icon)
            return;

        int count = ComputeBurstCount(amount);
        for (int i = 0; i < count; i++)
            SpawnAndAnimateToken(icon, startLocal, endLocal, i, to);
    }

    private int ComputeBurstCount(int amount)
    {
        if (amount <= 0) return minBurstCount;

        float t = Mathf.InverseLerp(1f, 200f, amount);
        int c = Mathf.RoundToInt(Mathf.Lerp(minBurstCount, maxBurstCount, t));
        return Mathf.Clamp(c, minBurstCount, maxBurstCount);
    }

    private void SpawnAndAnimateToken(Sprite icon, Vector2 startLocal, Vector2 endLocal, int index, Transform destination)
    {
        var token = AcquireToken();
        if (token == null || token.go == null || token.rt == null || token.cg == null || token.img == null)
            return;

        var go = token.go;
        var rt = token.rt;
        var cg = token.cg;
        var img = token.img;

        LeanTween.cancel(go);
        go.transform.SetParent(tokenRoot, false);
        go.SetActive(true);

        rt.sizeDelta = tokenSize;
        rt.anchoredPosition = startLocal;
        rt.localScale = Vector3.one * 0.75f;

        img.sprite = icon;
        img.color = tokenColor;
        cg.alpha = 1f;

        float delay = index * Mathf.Max(0f, staggerSeconds);
        float dur = Mathf.Max(0.1f, travelTime);

        float seed = (index + 1) * 0.73f;
        float side = (seed % 2f) > 1f ? 1f : -1f;
        Vector2 mid = (startLocal + endLocal) * 0.5f;
        mid.x += side * 24f;
        mid.y += arcHeight;

        LeanTween.delayedCall(go, delay, () =>
        {
            if (!rt) return;

            LeanTween.value(go, 0f, 1f, dur)
                .setEase(LeanTweenType.easeInOutSine)
                .setOnUpdate((float t) =>
                {
                    if (!rt || !cg) return;

                    Vector2 a = Vector2.Lerp(startLocal, mid, t);
                    Vector2 b = Vector2.Lerp(mid, endLocal, t);
                    rt.anchoredPosition = Vector2.Lerp(a, b, t);

                    float s = Mathf.Lerp(0.75f, 1.05f, t);
                    rt.localScale = new Vector3(s, s, 1f);

                    if (t > 0.7f)
                        cg.alpha = Mathf.InverseLerp(1f, 0.7f, t);
                })
                .setOnComplete(() =>
                {
                    if (go)
                    {
                        TryPulseDestination(destination);
                        TryPlayImpactSpark(destination, icon, endLocal);
                        TryPlayImpactSfx();
                        ReleaseToken(token);
                    }
                });
        });
    }

    private TokenView AcquireToken()
    {
        if (useTokenPooling)
        {
            if (_pool.Count > 0)
                return _pool.Pop();

            if (_allPooled.Count < Mathf.Max(1, maxPooledTokens))
            {
                var pooled = CreateToken(true);
                if (pooled != null)
                {
                    _allPooled.Add(pooled);
                    return pooled;
                }
            }
        }

        return CreateToken(false);
    }

    private void ReleaseToken(TokenView token)
    {
        if (token == null || token.go == null) return;

        LeanTween.cancel(token.go);

        if (useTokenPooling && token.isPooled)
        {
            token.cg.alpha = 0f;
            token.go.SetActive(false);
            token.go.transform.SetParent(tokenRoot, false);
            _pool.Push(token);
            return;
        }

        Destroy(token.go);
    }

    private TokenView CreateToken(bool pooled)
    {
        var go = new GameObject("FlyToken", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        go.transform.SetParent(tokenRoot, false);

        var token = new TokenView
        {
            go = go,
            rt = go.GetComponent<RectTransform>(),
            cg = go.GetComponent<CanvasGroup>(),
            img = go.GetComponent<Image>(),
            isPooled = pooled
        };

        token.cg.alpha = 0f;
        go.SetActive(!pooled);
        return token;
    }

    private void PrewarmPoolIfNeeded()
    {
        if (!useTokenPooling) return;

        int wanted = Mathf.Clamp(prewarmCount, 0, Mathf.Max(0, maxPooledTokens));
        while (_allPooled.Count < wanted)
        {
            var token = CreateToken(true);
            if (token == null) break;

            _allPooled.Add(token);
            _pool.Push(token);
        }
    }

    private void TryPulseDestination(Transform destination)
    {
        if (!pulseDestinationOnImpact || !destination) return;

        float now = Time.unscaledTime;
        if (_lastPulseByTarget.TryGetValue(destination, out float last) && (now - last) < Mathf.Max(0.01f, pulseCooldown))
            return;

        _lastPulseByTarget[destination] = now;

        // Store the original scale before the first pulse so overlapping pulses
        // always return to the true rest scale instead of a mid-animation value.
        if (!_pulseBaseScale.TryGetValue(destination, out var baseScale))
        {
            baseScale = destination.localScale;
            _pulseBaseScale[destination] = baseScale;
        }

        var peak = baseScale * Mathf.Max(1.01f, pulseScale);

        LeanTween.cancel(destination.gameObject);
        LeanTween.scale(destination.gameObject, peak, Mathf.Max(0.02f, pulseUpTime))
            .setEase(LeanTweenType.easeOutQuad)
            .setIgnoreTimeScale(true)
            .setOnComplete(() =>
            {
                if (!destination) return;
                LeanTween.scale(destination.gameObject, baseScale, Mathf.Max(0.02f, pulseDownTime))
                    .setEase(LeanTweenType.easeInOutSine)
                    .setIgnoreTimeScale(true)
                    .setOnComplete(() =>
                    {
                        _pulseBaseScale.Remove(destination);
                    });
            });
    }

    private void TryPlayImpactSpark(Transform destination, Sprite icon, Vector2 endLocal)
    {
        if (!playImpactSpark || !destination || !icon || !tokenRoot) return;

        float now = Time.unscaledTime;
        if (_lastSparkByTarget.TryGetValue(destination, out float last) && (now - last) < Mathf.Max(0.01f, sparkCooldown))
            return;

        _lastSparkByTarget[destination] = now;

        var go = new GameObject("ImpactSpark", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        go.transform.SetParent(tokenRoot, false);

        var rt = go.GetComponent<RectTransform>();
        var cg = go.GetComponent<CanvasGroup>();
        var img = go.GetComponent<Image>();

        rt.sizeDelta = tokenSize;
        rt.anchoredPosition = endLocal;
        rt.localScale = Vector3.one * Mathf.Max(0.1f, sparkStartScale);

        img.sprite = icon;
        img.color = tokenColor;

        cg.alpha = Mathf.Clamp01(sparkAlpha);

        float dur = Mathf.Max(0.04f, sparkDuration);
        LeanTween.value(go, 0f, 1f, dur)
            .setEase(LeanTweenType.easeOutQuad)
            .setIgnoreTimeScale(true)
            .setOnUpdate((float t) =>
            {
                if (!rt || !cg) return;

                float s = Mathf.Lerp(sparkStartScale, sparkEndScale, t);
                rt.localScale = new Vector3(s, s, 1f);
                cg.alpha = Mathf.Lerp(Mathf.Clamp01(sparkAlpha), 0f, t);
            })
            .setOnComplete(() =>
            {
                if (go) Destroy(go);
            });
    }

    private void TryPlayImpactSfx()
    {
        if (!playImpactSfx) return;
        if (impactSfxType == SfxType.None) return;

        float now = Time.unscaledTime;
        if ((now - _lastImpactSfxTime) < Mathf.Max(0.01f, impactSfxCooldown))
            return;

        _lastImpactSfxTime = now;
        AudioManager.I?.PlaySfx(impactSfxType);
    }

    private bool TryGetScreenPoint(Transform tr, out Vector2 screen)
    {
        screen = default;
        if (!tr) return false;

        if (tr is RectTransform rt)
        {
            var c = rt.GetComponentInParent<Canvas>();
            Camera cam = null;
            if (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay)
                cam = c.worldCamera;

            Vector3 world = rt.TransformPoint(rt.rect.center);
            screen = RectTransformUtility.WorldToScreenPoint(cam, world);
            return true;
        }

        var sceneCam = Camera.main;
        if (!sceneCam) return false;

        screen = sceneCam.WorldToScreenPoint(tr.position);
        return true;
    }

    private bool TryScreenToCanvasLocal(Vector2 screen, out Vector2 local)
    {
        local = default;
        if (!overlayCanvas || !tokenRoot) return false;

        Camera cam = overlayCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null
            : overlayCanvas.worldCamera;

        return RectTransformUtility.ScreenPointToLocalPointInRectangle(tokenRoot, screen, cam, out local);
    }

    private void EnsureOverlayCanvas()
    {
        if (overlayCanvas && tokenRoot) return;

        if (!overlayCanvas)
            overlayCanvas = GetComponentInChildren<Canvas>(true);

        if (!overlayCanvas)
        {
            var canvasGo = new GameObject("FlyFXCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.transform.SetParent(transform, false);

            overlayCanvas = canvasGo.GetComponent<Canvas>();
            overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
            overlayCanvas.sortingOrder = 2500;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            var raycaster = canvasGo.GetComponent<GraphicRaycaster>();
            raycaster.enabled = false;
        }

        if (!tokenRoot)
        {
            tokenRoot = overlayCanvas.transform as RectTransform;
            if (tokenRoot)
            {
                tokenRoot.anchorMin = Vector2.zero;
                tokenRoot.anchorMax = Vector2.one;
                tokenRoot.offsetMin = Vector2.zero;
                tokenRoot.offsetMax = Vector2.zero;
            }
        }
    }
}