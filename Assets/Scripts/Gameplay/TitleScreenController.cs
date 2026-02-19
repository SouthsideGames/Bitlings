using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class TitleScreenController : MonoBehaviour
{
    [Header("Panel Routing")]
    [SerializeField] private PanelId selfPanelId    = PanelId.None;       
    [SerializeField] private PanelId homePanelId    = PanelId.Encounter;  
    [SerializeField] private PanelId starterPanelId = PanelId.StarterPicker;      

    [Header("Refs")]
    [SerializeField] private RectTransform titleRoot;
    [SerializeField] private Button pressToContinueButton;
    [SerializeField] private CanvasGroup pressToContinueCanvas;

    [SerializeField] private GameObject starterHolder;
    [SerializeField] private StarterSelector starterSelector;

    [Header("Anim")]
    [SerializeField] private float flashDuration       = 0.8f;
    [SerializeField] private float titleSlideY         = 420f;
    [SerializeField] private float slideTime           = 0.6f;
    [SerializeField] private float starterRevealDelay  = 0.05f;
    [SerializeField] private float starterRevealTime   = 0.25f;

    [Header("Behavior")]
    [SerializeField] private bool hideContinueOnPress  = true;
    [SerializeField] private float continueFadeOutTime = 0.2f;
    [SerializeField] private bool hideTitleWhenStarterFlowStarts = true;

    private bool _consumed;

    void Awake()
    {
        if (pressToContinueCanvas)
        {
            pressToContinueCanvas.alpha = 0f;
            pressToContinueCanvas.gameObject.SetActive(true);
        }
    

        if (starterHolder) starterHolder.SetActive(false);
    }

    void OnEnable()
    {
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (pressToContinueButton == null) Debug.LogError("pressToContinueButton is NULL");
        #endif
        #if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (pressToContinueCanvas == null) Debug.LogWarning("pressToContinueCanvas is NULL (flashing disabled)");
        #endif
        if (pressToContinueButton) pressToContinueButton.onClick.AddListener(OnTap);
        StartFlash();
    }

    void OnDisable()
    {
        if (pressToContinueButton) pressToContinueButton.onClick.RemoveListener(OnTap);
        LeanTween.cancel(gameObject);
        if (pressToContinueCanvas) LeanTween.cancel(pressToContinueCanvas.gameObject);
        if (titleRoot) LeanTween.cancel(titleRoot);
    }


    void StartFlash()
    {
        if (!pressToContinueCanvas) return;
        pressToContinueCanvas.alpha = 0f;
        LeanTween
            .alphaCanvas(pressToContinueCanvas, 1f, flashDuration)
            .setEaseInOutSine()
            .setLoopPingPong();
    }

    void StopFlash()
    {
        if (!pressToContinueCanvas) return;
        LeanTween.cancel(pressToContinueCanvas.gameObject);
        LeanTween.alphaCanvas(pressToContinueCanvas, 0f, continueFadeOutTime);
    }

    void HideContinue()
    {
        if (!pressToContinueButton) return;

        if (pressToContinueCanvas)
        {
            LeanTween.cancel(pressToContinueCanvas.gameObject);
            LeanTween.alphaCanvas(pressToContinueCanvas, 0f, continueFadeOutTime)
                .setOnComplete(() =>
                {
                    if (pressToContinueButton) pressToContinueButton.gameObject.SetActive(false);
                });
        }
        else
        {
            pressToContinueButton.gameObject.SetActive(false);
        }
    }


    void OnTap()
    {
        if (_consumed) return;
        _consumed = true;

        StopFlash();
        if (hideContinueOnPress && pressToContinueCanvas)
        {
            pressToContinueCanvas.blocksRaycasts = false;
            pressToContinueCanvas.interactable   = false;
            LeanTween.alphaCanvas(pressToContinueCanvas, 0f, continueFadeOutTime);
        }

        if (SaveManager.Data != null && SaveManager.Data.hasChosenStarter)
        {
            if (UIManager.I && homePanelId != PanelId.None) UIManager.I.Show(homePanelId);
            if (UIManager.I && selfPanelId != PanelId.None) UIManager.I.Hide(selfPanelId);
        }
        else
        {
            ShowStarterFlow();
        }
    }

    void ShowStarterFlow()
    {
        if (!titleRoot)
        {
            ShowStarterPanel();
            starterSelector?.Show();
            return;
        }

        var startPos = titleRoot.anchoredPosition;
        var endPos   = new Vector2(startPos.x, titleSlideY);

        if (pressToContinueButton) pressToContinueButton.gameObject.SetActive(false);

        LeanTween.value(gameObject, 0f, 1f, slideTime)
            .setEaseOutCubic()
            .setOnUpdate((float t) =>
            {
                titleRoot.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            })
            .setOnComplete(() =>
            {
                if (hideTitleWhenStarterFlowStarts && UIManager.I && selfPanelId != PanelId.None)
                    UIManager.I.Hide(selfPanelId);

                var sRoot = ShowStarterPanel();

                if (sRoot)
                {
                    var cg = sRoot.GetComponent<CanvasGroup>();
                    if (!cg) cg = sRoot.AddComponent<CanvasGroup>();
                    cg.alpha = 0f;
                    LeanTween.alphaCanvas(cg, 1f, starterRevealTime).setDelay(starterRevealDelay);

                    var rt = sRoot.GetComponent<RectTransform>();
                    if (rt)
                    {
                        rt.localScale = Vector3.one * 0.96f;
                        LeanTween.scale(rt, Vector3.one, 0.22f).setEaseOutBack();
                    }
                }

                if (starterSelector) starterSelector.Show();
                #if UNITY_EDITOR || DEVELOPMENT_BUILD
                else Debug.LogWarning("StarterSelector not found on starter panel root.");
                #endif
            });
    }


    GameObject GetStarterRoot()
    {
        if (UIManager.I && starterPanelId != PanelId.None)
        {
            var r = UIManager.I.GetRoot(starterPanelId);
            if (r) return r;
        }
        return starterHolder;
    }

    GameObject ShowStarterPanel()
    {
        GameObject sRoot = GetStarterRoot();

        if (UIManager.I && starterPanelId != PanelId.None)
        {
            UIManager.I.Show(starterPanelId);
            sRoot = UIManager.I.GetRoot(starterPanelId) ?? sRoot;
        }
        else if (sRoot)
        {
            sRoot.SetActive(true); 
        }

        return sRoot;
    }
}