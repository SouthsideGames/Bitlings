using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds the Arena main-menu button visibility to the Arena_Basic unlock gate.
/// Attach to the arena navigation button on the home/main screen.
/// </summary>
public sealed class ArenaHomeButtonBinder : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private GameObject arenaButtonRoot;
    [SerializeField] private Button arenaButton;

    void OnEnable()
    {
        if (!arenaButtonRoot) arenaButtonRoot = gameObject;
        if (!arenaButton) arenaButton = GetComponent<Button>();

        RefreshVisibility();

        if (arenaButton) arenaButton.onClick.AddListener(HandleArenaButtonClicked);
        GameEvents.FeatureUnlocked += OnFeatureUnlocked;
    }

    void OnDisable()
    {
        if (arenaButton) arenaButton.onClick.RemoveListener(HandleArenaButtonClicked);
        GameEvents.FeatureUnlocked -= OnFeatureUnlocked;
    }

    private void OnFeatureUnlocked(FeatureId id)
    {
        if (id == FeatureId.Arena_Basic)
            RefreshVisibility();
    }

    public void RefreshVisibility()
    {
        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.Arena_Basic);

        if (arenaButtonRoot != null)
            arenaButtonRoot.SetActive(unlocked);
    }

    private void HandleArenaButtonClicked()
    {
        if (ArenaSaveHelper.MarkArenaRoundResultsViewed(save: true))
            GameEvents.ArenaDataChanged?.Invoke();
    }
}
