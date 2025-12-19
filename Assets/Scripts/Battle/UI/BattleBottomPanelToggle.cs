using UnityEngine;
using UnityEngine.UI;

public class BattleBottomPanelToggle : MonoBehaviour
{
    [Header("Panels (CanvasGroup preferred)")]
    [SerializeField] private CanvasGroup battleTextGroup;
    [SerializeField] private CanvasGroup boosterBarGroup;

    [Header("Optional: button visuals")]
    [SerializeField] private Button toggleButton;

    [Header("Rules")]
    [SerializeField] private bool startShowingText = true;

    // Hook to BattleManager so we can block toggling during narration/turn locks
    [Header("Battle State Source")]
    [SerializeField] private BattleManager battle; // drag your BattleManager here

    private bool _showingText;

    void Awake()
    {
        _showingText = startShowingText;
        ApplyState(immediate: true);

        if (toggleButton != null)
            toggleButton.onClick.AddListener(Toggle);
    }

    void OnDestroy()
    {
        if (toggleButton != null)
            toggleButton.onClick.RemoveListener(Toggle);
    }

    public void Toggle()
    {
        // If battle not assigned, just toggle (safe fallback)
        if (battle != null)
        {
            // Block toggling while narration is locking input
            // You already have _narrationLock in BattleManager; expose it as a public property.
            if (battle.NarrationLocked)
                return;

            // Optional: if you only want boosters on player turn
            // if (!battle.IsPlayerTurn) return;
        }

        _showingText = !_showingText;
        ApplyState(immediate: false);
    }

    private void ApplyState(bool immediate)
    {
        if (_showingText)
        {
            SetGroupVisible(battleTextGroup, true);
            SetGroupVisible(boosterBarGroup, false);
        }
        else
        {
            SetGroupVisible(battleTextGroup, false);
            SetGroupVisible(boosterBarGroup, true);
        }
    }

    private static void SetGroupVisible(CanvasGroup cg, bool visible)
    {
        if (cg == null) return;
        cg.alpha = visible ? 1f : 0f;
        cg.interactable = visible;
        cg.blocksRaycasts = visible;
    }

    // Optional: call from BattleManager when a new line starts/ends to force text visible
    public void ForceShowText()
    {
        _showingText = true;
        ApplyState(immediate: true);
    }
}
