using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the Iron Career button visibility to the unlock gate.
/// Lightweight: no new global events required; refreshes on enable and on promotion rank change.
/// </summary>
public sealed class IronCareerHomeButtonBinder : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject ironCareerButtonRoot;
    [SerializeField] private Button ironCareerButton;

    [Header("Config")]
    [Tooltip("If true, clicking the button will call IronCareerRuntime.Enter() (for Phase 0 testing).")]
    [SerializeField] private bool enterIronOnClick = true;

    void OnEnable()
    {
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
        bool unlocked = (SaveManager.Data != null) && SaveManager.Data.HasIronCareerUnlocked;

        if (ironCareerButtonRoot != null)
            ironCareerButtonRoot.SetActive(unlocked);

        if (ironCareerButton != null)
            ironCareerButton.interactable = unlocked;
    }

    void OnClicked()
    {
        if (SaveManager.Data == null || !SaveManager.Data.HasIronCareerUnlocked) return;

        if (enterIronOnClick)
            IronCareerRuntime.Enter();

        // Phase 1+ will open the Iron starter panel / route to Iron scene.
        Debug.Log("[IronCareer] Button clicked (Phase 0 stub).");
    }
}