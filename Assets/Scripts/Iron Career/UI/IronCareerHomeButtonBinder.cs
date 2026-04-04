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

        var ui = UIManager.I;

        if (ui)
        {
            ui.Hide(PanelId.Encounter);
            ui.Hide(PanelId.PostBattleSummary);

            ui.Show(PanelId.IronCareerEncounter);
            ui.Hide(PanelId.Home);
        }

        // IMPORTANT: do not run this coroutine on the Home button object itself,
        // because hiding Home can disable this component before next frame.
        if (ui)
            ui.StartCoroutine(Co_ShowStarterWhenPanelReady());
        else
            StartCoroutine(Co_ShowStarterWhenPanelReady());
    }

    private bool IsUnlockedGate()
    {
        // Preferred: use the manager gate (supports DEV override)
        if (ironCareerManager != null)
            return ironCareerManager.IsUnlocked();

        // Fallback: use save gate directly
        return (SaveManager.Data != null) && SaveManager.Data.HasIronCareerUnlocked;
    }

    private IEnumerator Co_ShowStarterWhenPanelReady()
    {
        const int maxFrames = 15;

        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null; // wait so panel/container can finish enabling

            var ironUI = IronCareerEncounterPanelUI.I;
            if (!ironUI)
                ironUI = FindFirstObjectByType<IronCareerEncounterPanelUI>(FindObjectsInactive.Include);

            if (!ironUI)
                continue;

            var root = UIManager.I ? UIManager.I.GetRoot(PanelId.IronCareerEncounter) : null;
            bool panelActive = root == null || root.activeInHierarchy;
            if (!panelActive)
                continue;

            ironUI.ShowStarter(immediate: true);
            yield break;
        }

        Debug.LogWarning("[IronCareerHomeButtonBinder] Timed out waiting to show Starter (IronCareerEncounterPanelUI not ready/active).");
    }
}