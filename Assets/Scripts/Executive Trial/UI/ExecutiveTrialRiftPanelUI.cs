using System.Collections;
using UnityEngine;


public sealed class ExecutiveTrialRiftPanelUI : MonoBehaviour
{
    public static ExecutiveTrialRiftPanelUI I { get; private set; }

    [Header("Roots")]
    [SerializeField] private GameObject battleRoot; 

    [Header("Overlays (CanvasGroups)")]
    [SerializeField] private CanvasGroup starter;
    [SerializeField] private CanvasGroup hire;
    [SerializeField] private CanvasGroup replace;
    [SerializeField] private CanvasGroup post;
    [SerializeField] private CanvasGroup forcedEvolve;
    [SerializeField] private CanvasGroup rest;
    [SerializeField] private CanvasGroup gameOver;
    [SerializeField] private CanvasGroup rules;
    [SerializeField] private CanvasGroup records;

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float fadeTime = 0.20f;

    private Coroutine _fadeCo;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        // Auto-bind common children by name if references are missing (safety for prefab wiring regressions).
        if (!starter) starter = FindCg("Starter");
        if (!hire) hire = FindCg("Hire");
        if (!replace) replace = FindCg("Replace");
        if (!post) post = FindCg("Post");
        if (!forcedEvolve) forcedEvolve = FindCg("ForcedEvolve");
        if (!rest) rest = FindCg("Rest");
        if (!gameOver) gameOver = FindCg("GameOver");
        if (!rules) rules = FindCg("Rules");
        if (!records) records = FindCg("Records");

        if (!battleRoot)
        {
            var tr = transform.Find("ExecutiveTrialBattle");
            if (tr) battleRoot = tr.gameObject;
        }

        // Default: battle HUD visible, overlays hidden.
        if (battleRoot) battleRoot.SetActive(true);
        HideAllImmediate();
    }

    // ─────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────
    public void ShowStarter(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(starter, immediate);

        if (!starter)
            Debug.LogError("[ExecutiveTrialRiftPanelUI] Starter CanvasGroup missing. Panel will appear blank.");
    }

    public void ShowHire(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(hire, immediate);

        if (!hire)
            Debug.LogError("[ExecutiveTrialRiftPanelUI] Hire CanvasGroup missing. Panel will appear blank.");
    }

    public void ShowReplace(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(replace, immediate);

        if (!replace)
            Debug.LogError("[ExecutiveTrialRiftPanelUI] Replace CanvasGroup missing. Panel will appear blank.");
    }

    public void ShowPost(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(post, immediate);

        if (!post)
            Debug.LogError("[ExecutiveTrialRiftPanelUI] Post CanvasGroup missing. Panel will appear blank.");
    }

    public void ShowForcedEvolve(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(forcedEvolve, immediate);

        if (!forcedEvolve)
            Debug.LogError("[ExecutiveTrialRiftPanelUI] ForcedEvolve CanvasGroup missing. Panel will appear blank.");
    }

    public void ShowRest(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(rest, immediate);

        if (!rest)
            Debug.LogError("[ExecutiveTrialRiftPanelUI] Rest CanvasGroup missing. Panel will appear blank.");
    }

    public void ShowGameOver(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(gameOver, immediate);

        if (!gameOver)
            Debug.LogError("[ExecutiveTrialRiftPanelUI] GameOver CanvasGroup missing. Panel will appear blank.");
    }

    public void ShowRules(bool immediate = false)
    {
        // Rules is an overlay popup: don't hide others.
        ShowOnly(rules, immediate);
    }

    public void HideRules(bool immediate = false)
    {
        HideOnly(rules, immediate);
    }

    public void ShowRecords(bool immediate = false)
    {
        // Records is an overlay popup: don't hide others.
        ShowOnly(records, immediate);
    }

    public void HideRecords(bool immediate = false)
    {
        HideOnly(records, immediate);
    }

    /// <summary>Call when you want the player to be in battle view (no overlays).</summary>
    public void ShowBattleOnly(bool immediate = false)
    {
        HideAll(immediate);
        if (battleRoot) battleRoot.SetActive(true);
    }

    public IEnumerator Co_ShowBattleOnlyThenReady()
    {
        if (_fadeCo != null)
        {
            StopCoroutine(_fadeCo);
            _fadeCo = null;
        }

        if (battleRoot) battleRoot.SetActive(true);

        CanvasGroup[] overlays = { starter, hire, replace, post, forcedEvolve, rest, gameOver, rules, records };

        if (fadeTime <= 0f)
        {
            HideAllImmediate();
            yield break;
        }

        bool hasVisible = false;
        float[] from = new float[overlays.Length];

        for (int i = 0; i < overlays.Length; i++)
        {
            var cg = overlays[i];
            if (!cg) continue;

            bool visible = cg.gameObject.activeSelf && cg.alpha > 0.01f;
            if (!visible)
            {
                SetCG(cg, false, 0f);
                continue;
            }

            hasVisible = true;
            from[i] = Mathf.Clamp01(cg.alpha);
            cg.interactable = false;
            cg.blocksRaycasts = false;
        }

        if (!hasVisible)
        {
            HideAllImmediate();
            yield break;
        }

        float t = 0f;
        float dur = Mathf.Max(0.01f, fadeTime);
        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / dur);

            for (int i = 0; i < overlays.Length; i++)
            {
                var cg = overlays[i];
                if (!cg) continue;
                if (!cg.gameObject.activeSelf) continue;

                cg.alpha = Mathf.Lerp(from[i], 0f, a);
            }

            yield return null;
        }

        HideAllImmediate();
    }

    public void HideAll(bool immediate = false)
    {
        if (battleRoot) battleRoot.SetActive(false);
        if (immediate) HideAllImmediate();
        else
        {
            HideOnly(starter, false);
            HideOnly(hire, false);
            HideOnly(replace, false);
            HideOnly(post, false);
            HideOnly(forcedEvolve, false);
            HideOnly(rest, false);
            HideOnly(gameOver, false);
            HideOnly(rules, false);
            HideOnly(records, false);
        }
    }

    private CanvasGroup FindCg(string name)
    {
        // Search entire subtree (safe for prefab re-orgs)
        var tr = transform.Find(name);
        if (!tr)
        {
            var all = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] && all[i].name == name) { tr = all[i]; break; }
            }
        }

        if (!tr) return null;

        var cg = tr.GetComponent<CanvasGroup>();
        if (!cg)
            Debug.LogWarning($"[ExecutiveTrialRiftPanelUI] Missing CanvasGroup on '{tr.name}'. Add one in prefab/scene for fade/control.", tr);
        return cg;
    }

    // ─────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────

    private void HideAllImmediate()
    {
        if (battleRoot) battleRoot.SetActive(false);
        SetCG(starter, false, 0f);
        SetCG(hire, false, 0f);
        SetCG(replace, false, 0f);
        SetCG(post, false, 0f);
        SetCG(forcedEvolve, false, 0f);
        SetCG(rest, false, 0f);
        SetCG(gameOver, false, 0f);
        SetCG(rules, false, 0f);
        SetCG(records, false, 0f);
    }

    private void ShowOnly(CanvasGroup cg, bool immediate)
    {
        if (!cg) return;
        if (immediate || fadeTime <= 0f)
        {
            SetCG(cg, true, 1f);
            return;
        }

        StartFade(cg, true);
    }

    private void HideOnly(CanvasGroup cg, bool immediate)
    {
        if (!cg) return;
        if (immediate || fadeTime <= 0f)
        {
            SetCG(cg, false, 0f);
            return;
        }

        StartFade(cg, false);
    }

    private void StartFade(CanvasGroup cg, bool show)
    {
        if (_fadeCo != null) StopCoroutine(_fadeCo);
        _fadeCo = StartCoroutine(Co_Fade(cg, show));
    }

    private IEnumerator Co_Fade(CanvasGroup cg, bool show)
    {
        if (!cg) yield break;

        float from = cg.alpha;
        float to = show ? 1f : 0f;

        if (show)
        {
            cg.gameObject.SetActive(true);
            cg.interactable = true;
            cg.blocksRaycasts = true;
        }

        float t = 0f;
        while (t < fadeTime)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / fadeTime);
            cg.alpha = Mathf.Lerp(from, to, a);
            yield return null;
        }

        cg.alpha = to;

        if (!show)
        {
            cg.interactable = false;
            cg.blocksRaycasts = false;
            cg.gameObject.SetActive(false);
        }

        _fadeCo = null;
    }

    private static void SetCG(CanvasGroup cg, bool on, float alpha)
    {
        if (!cg) return;
        cg.gameObject.SetActive(on);
        cg.alpha = alpha;
        cg.interactable = on;
        cg.blocksRaycasts = on;
    }
}