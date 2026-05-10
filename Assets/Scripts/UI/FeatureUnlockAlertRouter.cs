using UnityEngine;
using UnityEngine.UI;

public sealed class FeatureUnlockAlertRouter : MonoBehaviour
{
    [Header("Rift Alert")]
    [SerializeField] private GameObject riftAlert;
    [SerializeField] private Button riftButton;

    [Header("Settings Alert")]
    [SerializeField] private GameObject settingsAlert;
    [SerializeField] private Button settingsButton;

    [Header("Directory Alert")]
    [SerializeField] private GameObject directoryAlert;
    [SerializeField] private Button directoryButton;

    [Header("Gym Alert")]
    [SerializeField] private GameObject gymAlert;
    [SerializeField] private Button gymButton;

    [Header("Resource Alert")]
    [SerializeField] private GameObject resourceAlert;
    [SerializeField] private Button resourceButton;

    [Header("Executive Trial Alert")]
    [SerializeField] private GameObject ironCareerAlert;
    [SerializeField] private Button ironCareerButton;

    [Header("Startup")]
    [SerializeField] private bool hideAllOnEnable = false;

    private const int SynergyUnlockRank = 10;
    private const int DifficultyUnlockRank = 15;
    private const int ExecutiveTrialUnlockRank = 20;

    private void OnEnable()
    {
        if (hideAllOnEnable)
            HideAllAlerts();

        GameEvents.FeatureUnlocked += HandleFeatureUnlocked;
        GameEvents.PromotionRankChanged += HandlePromotionRankChanged;

        AddDismiss(riftButton, DismissRift);
        AddDismiss(settingsButton, DismissSettings);
        AddDismiss(directoryButton, DismissDirectory);
        AddDismiss(gymButton, DismissGym);
        AddDismiss(resourceButton, DismissResource);
        AddDismiss(ironCareerButton, DismissExecutiveTrial);
    }

    private void OnDisable()
    {
        GameEvents.FeatureUnlocked -= HandleFeatureUnlocked;
        GameEvents.PromotionRankChanged -= HandlePromotionRankChanged;

        RemoveDismiss(riftButton, DismissRift);
        RemoveDismiss(settingsButton, DismissSettings);
        RemoveDismiss(directoryButton, DismissDirectory);
        RemoveDismiss(gymButton, DismissGym);
        RemoveDismiss(resourceButton, DismissResource);
        RemoveDismiss(ironCareerButton, DismissExecutiveTrial);
    }

    private void HandleFeatureUnlocked(FeatureId feature)
    {
        switch (feature)
        {
            case FeatureId.IdleBattle_Basic:
            case FeatureId.IdleBattle_SpeedControl:
                SetAlert(riftAlert, true);
                break;

            case FeatureId.Seeds_DailyBasic:
            case FeatureId.Seeds_CustomInput:
                SetAlert(settingsAlert, true);
                break;

            case FeatureId.Directory_Favorites:
            case FeatureId.Directory_CaptureOnlyFilter:
                SetAlert(directoryAlert, true);
                break;

            case FeatureId.AutoGrowth_Basic:
            case FeatureId.AutoGrowth_UsePresets:
                SetAlert(gymAlert, true);
                break;

            case FeatureId.Recycle_Basic:
                SetAlert(resourceAlert, true);
                break;
        }
    }

    private void HandlePromotionRankChanged(int oldRank, int newRank)
    {
        if (oldRank < SynergyUnlockRank && newRank >= SynergyUnlockRank)
            SetAlert(riftAlert, true);

        if (oldRank < DifficultyUnlockRank && newRank >= DifficultyUnlockRank)
            SetAlert(settingsAlert, true);

        if (oldRank < ExecutiveTrialUnlockRank && newRank >= ExecutiveTrialUnlockRank)
            SetAlert(ironCareerAlert, true);
    }

    private void DismissRift() => SetAlert(riftAlert, false);
    private void DismissSettings() => SetAlert(settingsAlert, false);
    private void DismissDirectory() => SetAlert(directoryAlert, false);
    private void DismissGym() => SetAlert(gymAlert, false);
    private void DismissResource() => SetAlert(resourceAlert, false);
    private void DismissExecutiveTrial() => SetAlert(ironCareerAlert, false);

    private void HideAllAlerts()
    {
        SetAlert(riftAlert, false);
        SetAlert(settingsAlert, false);
        SetAlert(directoryAlert, false);
        SetAlert(gymAlert, false);
        SetAlert(resourceAlert, false);
        SetAlert(ironCareerAlert, false);
    }

    private static void SetAlert(GameObject target, bool visible)
    {
        if (!target) return;
        if (target.activeSelf != visible)
            target.SetActive(visible);
    }

    private static void AddDismiss(Button button, UnityEngine.Events.UnityAction action)
    {
        if (!button) return;
        button.onClick.RemoveListener(action);
        button.onClick.AddListener(action);
    }

    private static void RemoveDismiss(Button button, UnityEngine.Events.UnityAction action)
    {
        if (!button) return;
        button.onClick.RemoveListener(action);
    }
}