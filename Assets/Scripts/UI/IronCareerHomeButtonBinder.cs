using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the Iron Career button visibility to the unlock gate.
///
/// Phase 3+ approach:
/// - UIManager only shows the IronCareerEncounter container.
/// - IronCareerEncounterPanelUI owns the overlay flow (Starter/Hire/Replace/etc).
/// - We do NOT enter sealed runtime here by default; sealed runtime starts when a run starts.
/// </summary>
public sealed class IronCareerHomeButtonBinder : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject ironCareerButtonRoot;
    [SerializeField] private Button ironCareerButton;

    [Header("Optional")]
    [Tooltip("If assigned, this binder will use IronCareerManager.IsUnlocked() (supports DEV override). If null, it will try to find one.")]
    [SerializeField] private IronCareerManager ironCareerManager;

    [Header("Config")]
    [Tooltip("If true, clicking the button will call IronCareerRuntime.Enter(). Generally keep FALSE; sealed runtime should begin when a run starts.")]
    [SerializeField] private bool enterIronOnClick = false;

    // Used when this binder lives on the same GO it is trying to hide.
    // Disabling the GO would disable this script and it could never re-enable itself.
    private CanvasGroup _selfCanvasGroup;

    void OnEnable()
    {
        if (!ironCareerButtonRoot) ironCareerButtonRoot = gameObject;
        if (!ironCareerButton) ironCareerButton = GetComponent<Button>();

        if (!ironCareerManager)
            ironCareerManager = FindFirstObjectByType<IronCareerManager>(FindObjectsInactive.Include);

        // If the binder is placed on the same object as the button root,
        // never SetActive(false) on that object. Use CanvasGroup to hide/show.
        if (ironCareerButtonRoot == gameObject)
        {
            _selfCanvasGroup = GetComponent<CanvasGroup>();
            if (!_selfCanvasGroup) _selfCanvasGroup = gameObject.AddComponent<CanvasGroup>();
        }

        RefreshVisibility();
        GameEvents.PromotionRankChanged += OnPromotionRankChanged;

        if (ironCareerButton != null)
            ironCareerButton.onClick.AddListener(OnClicked);
    }

    void OnDisable()
    {
        GameEvents.PromotionRankChanged -= OnPromotionRankChanged;

        if (ironCareerButton != null)
            ironCareerButton.onClick.RemoveListener(OnClicked);
    }

    void OnPromotionRankChanged(int oldRank, int newRank) => RefreshVisibility();

    public void RefreshVisibility()
    {
        bool unlocked = IsUnlockedGate();

        // IMPORTANT: If ironCareerButtonRoot is this same GO, do NOT disable it.
        // Use CanvasGroup to hide/show while keeping the binder alive.
        if (ironCareerButtonRoot != null && ironCareerButtonRoot != gameObject)
        {
            ironCareerButtonRoot.SetActive(unlocked);
        }
        else if (_selfCanvasGroup != null)
        {
            _selfCanvasGroup.alpha = unlocked ? 1f : 0f;
            _selfCanvasGroup.interactable = unlocked;
            _selfCanvasGroup.blocksRaycasts = unlocked;
        }

        if (ironCareerButton != null)
            ironCareerButton.interactable = unlocked;
    }

    void OnClicked()
    {
        if (!IsUnlockedGate()) return;

        if (enterIronOnClick)
            IronCareerRuntime.Enter();

        if (UIManager.I)
        {
            UIManager.I.Hide(PanelId.Encounter);
            UIManager.I.Hide(PanelId.PostBattleSummary);

            UIManager.I.Show(PanelId.IronCareerEncounter);
            UIManager.I.Hide(PanelId.Home);
        }

        // IMPORTANT: do not call ShowStarter in the same frame we enabled the container.
        StartCoroutine(Co_ShowStarterNextFrame());
    }

    private bool IsUnlockedGate()
    {
        // Preferred: use the manager gate (supports DEV override)
        if (ironCareerManager != null)
            return ironCareerManager.IsUnlocked();

        // Fallback: use save gate directly
        return (SaveManager.Data != null) && SaveManager.Data.HasIronCareerUnlocked;
    }

    private IEnumerator Co_ShowStarterNextFrame()
    {
        yield return null; // wait 1 frame so the panel controller can Awake/OnEnable

        var ironUI = IronCareerEncounterPanelUI.I;
        if (!ironUI)
            ironUI = FindFirstObjectByType<IronCareerEncounterPanelUI>(FindObjectsInactive.Include);

        if (ironUI)
            ironUI.ShowStarter(immediate: true);
        else
            Debug.LogWarning("[IronCareerHomeButtonBinder] Missing IronCareerEncounterPanelUI in scene.");
    }
}