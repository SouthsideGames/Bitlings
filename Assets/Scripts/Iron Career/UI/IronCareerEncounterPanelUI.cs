using System.Collections;
using UnityEngine;


public sealed class IronCareerEncounterPanelUI : MonoBehaviour
{
    public static IronCareerEncounterPanelUI I { get; private set; }

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

    [Header("Fade")]
    [SerializeField, Range(0f, 1f)] private float fadeTime = 0.20f;

    private Coroutine _fadeCo;

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

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
    }

    public void ShowHire(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(hire, immediate);
    }

    public void ShowReplace(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(replace, immediate);
    }

    public void ShowPost(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(post, immediate);
    }

    public void ShowForcedEvolve(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(forcedEvolve, immediate);
    }

    public void ShowRest(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(rest, immediate);
    }

    public void ShowGameOver(bool immediate = false)
    {
        HideAll(immediate);
        ShowOnly(gameOver, immediate);
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

    /// <summary>Call when you want the player to be in battle view (no overlays).</summary>
    public void ShowBattleOnly(bool immediate = false)
    {
        HideAll(immediate);
        if (battleRoot) battleRoot.SetActive(true);
    }

    public void HideAll(bool immediate = false)
    {
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
            Debug.LogWarning($"[IronCareerEncounterPanelUI] Missing CanvasGroup on '{tr.name}'. Add one in prefab/scene for fade/control.", tr);
        return cg;
    }

    // ─────────────────────────────────────────────────────────────
    // Internal helpers
    // ─────────────────────────────────────────────────────────────

    private void HideAllImmediate()
    {
        SetCG(starter, false, 0f);
        SetCG(hire, false, 0f);
        SetCG(replace, false, 0f);
        SetCG(post, false, 0f);
        SetCG(forcedEvolve, false, 0f);
        SetCG(rest, false, 0f);
        SetCG(gameOver, false, 0f);
        SetCG(rules, false, 0f);
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