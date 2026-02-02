using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class IntroManager : MonoBehaviour
{
    [Header("Panels")]
    [SerializeField] private PanelId titlePanelId   = PanelId.Intro;
    [SerializeField] private PanelId starterPanelId = PanelId.StarterPicker;
    [SerializeField] private PanelId homePanelId    = PanelId.Home;

    [Header("Story Panel")]
    [SerializeField] private PanelId storyPanelId = PanelId.Story;

    [Header("Title Refs")]
    [SerializeField] private RectTransform titleRoot;
    [SerializeField] private Button pressToContinueButton;
    [SerializeField] private CanvasGroup pressToContinueCanvas;

    [Header("Anim")]
    [SerializeField] private float flashDuration       = 0.8f;
    [SerializeField] private float titleSlideY         = 420f;
    [SerializeField] private float slideTime           = 0.6f;
    [SerializeField] private float starterRevealDelay  = 0.05f;
    [SerializeField] private float starterRevealTime   = 0.25f;
    [SerializeField] private float continueFadeOutTime = 0.2f;

    [SerializeField] private StarterSelector _starterSelector;

    private bool _consumed;

    void Awake()
    {
        if (pressToContinueCanvas)
        {
            pressToContinueCanvas.alpha = 0f;
            pressToContinueCanvas.gameObject.SetActive(true);
        }
    }

    void OnEnable()
    {
        if (pressToContinueButton)
            pressToContinueButton.onClick.AddListener(OnPressToContinue);

        _consumed = false;
        StartFlash();
    }

    void OnDisable()
    {
        if (pressToContinueButton)
            pressToContinueButton.onClick.RemoveListener(OnPressToContinue);

        LeanTween.cancel(gameObject);
        if (pressToContinueCanvas) LeanTween.cancel(pressToContinueCanvas.gameObject);
        if (titleRoot) LeanTween.cancel(titleRoot);
    }

    void OnApplicationQuit() => SeedService.ClearSessionSeed();

    // ─────────────────────────────────────────────────────────────
    // Flash / hint
    // ─────────────────────────────────────────────────────────────
    void StartFlash()
    {
        if (!pressToContinueCanvas) return;

        pressToContinueCanvas.alpha = 0f;
        LeanTween.alphaCanvas(pressToContinueCanvas, 1f, flashDuration)
            .setEaseInOutSine()
            .setLoopPingPong();
    }

    void StopFlash()
    {
        if (!pressToContinueCanvas) return;

        LeanTween.cancel(pressToContinueCanvas.gameObject);
        LeanTween.alphaCanvas(pressToContinueCanvas, 0f, continueFadeOutTime);
    }

    // ─────────────────────────────────────────────────────────────
    // Continue button
    // ─────────────────────────────────────────────────────────────
    void OnPressToContinue()
    {
        if (_consumed) return;

        bool hasSeenStory = SaveManager.Data != null && SaveManager.Data.hasSeenStory;
        bool hasStarter   = SaveManager.Data != null && SaveManager.Data.hasChosenStarter;

        if (!hasSeenStory)
        {
            UIManager.I?.Show(storyPanelId);
            return;
        }

        _consumed = true;

        StopFlash();
        if (pressToContinueCanvas)
        {
            pressToContinueCanvas.blocksRaycasts = false;
            pressToContinueCanvas.interactable = false;
            LeanTween.alphaCanvas(pressToContinueCanvas, 0f, continueFadeOutTime);
        }

        if (hasStarter)
        {
            UIManager.I?.Show(homePanelId);
            UIManager.I?.Hide(titlePanelId);

            StartCoroutine(OpenIdleRewardsAfterContinue());
            return;
        }

        ShowStarterFlow();
    }

    private IEnumerator OpenIdleRewardsAfterContinue()
    {
        yield return null;

        IdleBattleManager.I?.TryOpenSummaryIfNeeded();
    }

    // ─────────────────────────────────────────────────────────────
    // Starter flow (same as before)
    // ─────────────────────────────────────────────────────────────
    void ShowStarterFlow()
    {
        if (!titleRoot)
        {
            OpenStarterPanelAndShowSelector();
            return;
        }

        var startPos = titleRoot.anchoredPosition;
        var endPos = new Vector2(startPos.x, titleSlideY);

        if (pressToContinueButton)
            pressToContinueButton.gameObject.SetActive(false);

        LeanTween.value(gameObject, 0f, 1f, slideTime)
            .setEaseOutCubic()
            .setOnUpdate((float t) =>
            {
                titleRoot.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            })
            .setOnComplete(OpenStarterPanelAndShowSelector);
    }

    void OpenStarterPanelAndShowSelector()
    {
        UIManager.I?.Show(starterPanelId);

        var starterRoot = UIManager.I ? UIManager.I.GetRoot(starterPanelId) : null;
        if (!_starterSelector && starterRoot)
            _starterSelector = starterRoot.GetComponentInChildren<StarterSelector>(true);

        if (starterRoot)
        {
            var cg = starterRoot.GetComponent<CanvasGroup>();
            if (!cg) cg = starterRoot.AddComponent<CanvasGroup>();
            cg.alpha = 0f;
            LeanTween.alphaCanvas(cg, 1f, starterRevealTime).setDelay(starterRevealDelay);

            var rt = starterRoot.GetComponent<RectTransform>();
            if (rt)
            {
                rt.localScale = Vector3.one * 0.96f;
                LeanTween.scale(rt, Vector3.one, 0.22f).setEaseOutBack();
            }
        }

        if (UIManager.I)
        {
            var introRoot = UIManager.I.GetRoot(titlePanelId);
            bool starterIsUnderIntro = starterRoot && introRoot && starterRoot.transform.IsChildOf(introRoot.transform);

            if (!starterIsUnderIntro)
            {
                UIManager.I.Hide(titlePanelId);
            }
            else
            {
                if (pressToContinueButton) pressToContinueButton.gameObject.SetActive(false);
                if (pressToContinueCanvas) pressToContinueCanvas.gameObject.SetActive(false);
            }
        }

        if (_starterSelector)
        {
            _starterSelector.Show();
        }
        else
        {
            Debug.LogError("[IntroManager] StarterSelector not found under StarterPicker panel root.");
        }
    }
}
