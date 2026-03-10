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

    void OnEnable()
    {
        if (!ironCareerButtonRoot) ironCareerButtonRoot = gameObject;
        if (!ironCareerButton) ironCareerButton = GetComponent<Button>();

        if (!ironCareerManager)
            ironCareerManager = FindFirstObjectByType<IronCareerManager>(FindObjectsInactive.Include);

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

        if (ironCareerButtonRoot != null)
            ironCareerButtonRoot.SetActive(unlocked);
    }

    void OnClicked()
    {
        if (!IsUnlockedGate()) return;

        if (enterIronOnClick)
        {
            Debug.LogWarning("[IronCareerHomeButtonBinder] enterIronOnClick is enabled. This is a legacy debug path and can cause pre-run side effects. Recommended: keep it OFF.");
            IronCareerRuntime.Enter();
        }

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