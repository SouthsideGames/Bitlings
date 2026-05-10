using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the Executive Trial button visibility to the unlock gate.
///
/// Phase 3+ approach:
/// - UIManager only shows the ExecutiveTrialRift container.
/// - ExecutiveTrialRiftPanelUI owns the overlay flow (Starter/Hire/Replace/etc).
/// - We do NOT enter sealed runtime here by default; sealed runtime starts when a run starts.
/// </summary>
public sealed class ExecutiveTrialHomeButtonBinder : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject ironCareerButtonRoot;
    [SerializeField] private Button ironCareerButton;

    [Header("Optional")]
    [Tooltip("If assigned, this binder will use ExecutiveTrialManager.IsUnlocked() (supports DEV override). If null, it will try to find one.")]
    [SerializeField] private ExecutiveTrialManager ironCareerManager;

    void OnEnable()
    {
        if (!ironCareerButtonRoot) ironCareerButtonRoot = gameObject;
        if (!ironCareerButton) ironCareerButton = GetComponent<Button>();

        if (!ironCareerManager)
            ironCareerManager = FindFirstObjectByType<ExecutiveTrialManager>(FindObjectsInactive.Include);

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
            ui.Hide(PanelId.Rift);
            ui.Hide(PanelId.PostBattleSummary);

            ui.Show(PanelId.ExecutiveTrialRift);
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
        return (SaveManager.Data != null) && SaveManager.Data.HasExecutiveTrialUnlocked;
    }

    private IEnumerator Co_ShowStarterWhenPanelReady()
    {
        const int maxFrames = 15;

        for (int frame = 0; frame < maxFrames; frame++)
        {
            yield return null; // wait so panel/container can finish enabling

            var ironUI = ExecutiveTrialRiftPanelUI.I;
            if (!ironUI)
                ironUI = FindFirstObjectByType<ExecutiveTrialRiftPanelUI>(FindObjectsInactive.Include);

            if (!ironUI)
                continue;

            var root = UIManager.I ? UIManager.I.GetRoot(PanelId.ExecutiveTrialRift) : null;
            bool panelActive = root == null || root.activeInHierarchy;
            if (!panelActive)
                continue;

            ironUI.ShowStarter(immediate: true);
            yield break;
        }

        Debug.LogWarning("[ExecutiveTrialHomeButtonBinder] Timed out waiting to show Starter (ExecutiveTrialRiftPanelUI not ready/active).");
    }
}