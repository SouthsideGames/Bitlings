using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Routes Executive Trial battle UI bindings into BattleManager.
/// Executive Trial mode does not support party switching.
/// Only handles binding overrides.
/// </summary>
public class ExecutiveTrialBattleUIRoot : MonoBehaviour
{
    [Header("Executive Trial UI Bindings")]
    [SerializeField] private BattleManager.BattleUIBindings executiveTrialBindings;

    [Header("Optional Executive Trial Components (auto-find if null)")]
    [SerializeField] private BattleFeedbackManager executiveTrialFeedback;
    [SerializeField] private BattleTextBoxUI executiveTrialBattleTextBox;
    [SerializeField] private Image executiveTrialPlayerBackground;
    [SerializeField] private Image executiveTrialWildBackground;

    [SerializeField] private bool autoFindInChildren = true;

    private void Awake()
    {
        ResolveOptionalRefs();
    }

    private void ResolveOptionalRefs()
    {
        if (!autoFindInChildren) return;

        if (!executiveTrialFeedback)
            executiveTrialFeedback = GetComponentInChildren<BattleFeedbackManager>(true);

        if (!executiveTrialBattleTextBox)
        {
            var allTextBoxes = GetComponentsInChildren<BattleTextBoxUI>(true);
            DevLog.Log($"[ExecutiveTrialBattleUIRoot] Found {allTextBoxes.Length} BattleTextBoxUI components");
            for (int i = 0; i < allTextBoxes.Length; i++)
            {
                var tb = allTextBoxes[i];
                if (!tb) continue;
                DevLog.Log($"[ExecutiveTrialBattleUIRoot]   [{i}] {tb.name} hasRenderable={tb.HasRenderableTarget} active={tb.gameObject.activeSelf}");
                if (tb.HasRenderableTarget)
                {
                    executiveTrialBattleTextBox = tb;
                    DevLog.Log($"[ExecutiveTrialBattleUIRoot] Assigned textbox (by HasRenderableTarget): {tb.name}");
                    break;
                }
            }

            if (!executiveTrialBattleTextBox && allTextBoxes.Length > 0)
            {
                executiveTrialBattleTextBox = allTextBoxes[0];
                DevLog.Log($"[ExecutiveTrialBattleUIRoot] Assigned textbox (fallback first): {allTextBoxes[0].name}");
            }
        }

        if (!executiveTrialPlayerBackground)
        {
            var allImages = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < allImages.Length; i++)
            {
                var img = allImages[i];
                if (!img) continue;
                string n = img.name ?? string.Empty;
                if (n.IndexOf("player", System.StringComparison.OrdinalIgnoreCase) >= 0
                    && n.IndexOf("background", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    executiveTrialPlayerBackground = img;
                    break;
                }
            }
        }

        if (!executiveTrialWildBackground)
        {
            var allImages = GetComponentsInChildren<Image>(true);
            for (int i = 0; i < allImages.Length; i++)
            {
                var img = allImages[i];
                if (!img) continue;
                string n = img.name ?? string.Empty;
                if (n.IndexOf("wild", System.StringComparison.OrdinalIgnoreCase) >= 0
                    && n.IndexOf("background", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    executiveTrialWildBackground = img;
                    break;
                }
            }
        }
    }

    public void ApplyTo(BattleManager battle)
    {
        if (!battle) return;

        // Ensure this UI root is active so child components can display
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        ResolveOptionalRefs();

        DevLog.Log($"[ExecutiveTrialBattleUIRoot] ApplyTo called: textbox={(executiveTrialBattleTextBox ? executiveTrialBattleTextBox.name : "NULL")} active={(executiveTrialBattleTextBox ? executiveTrialBattleTextBox.gameObject.activeSelf : false)}");

        battle.SetUIBindingsOverride(executiveTrialBindings);

        // No bottom toggle override — Executive Trial does not allow switching
        battle.SetUIOverride(
            executiveTrialFeedback,
            executiveTrialBattleTextBox,
            null
        );

        // Ensure we spawn attack prefabs in Executive Trial runs just like regular battles.
        if (executiveTrialFeedback)
            executiveTrialFeedback.SetSpawnAttackPrefabs(true);

        // Copy timing from the regular feedback so Executive Trial animations feel the same speed.
        if (executiveTrialFeedback && battle.DefaultFeedback && battle.DefaultFeedback != executiveTrialFeedback)
            executiveTrialFeedback.CopyAnimationTimingsFrom(battle.DefaultFeedback);

        var actionBars = GetComponentsInChildren<ActionBarBinder>(true);
        for (int i = 0; i < actionBars.Length; i++)
        {
            var binder = actionBars[i];
            if (!binder) continue;
            binder.BindTo(battle, executiveTrialFeedback);
        }

        // Ensure text box is active before battle starts
        if (executiveTrialBattleTextBox && !executiveTrialBattleTextBox.gameObject.activeSelf)
        {
            executiveTrialBattleTextBox.gameObject.SetActive(true);
            DevLog.Log($"[ExecutiveTrialBattleUIRoot] Activated textbox: {executiveTrialBattleTextBox.name}");
        }

#if UNITY_EDITOR
        DevLog.Log($"[ExecutiveTrialTextTrace] ApplyTo: battle={battle.name} textbox={(executiveTrialBattleTextBox ? executiveTrialBattleTextBox.name : "NULL")} hasTarget={(executiveTrialBattleTextBox ? executiveTrialBattleTextBox.HasRenderableTarget : false)} active={(executiveTrialBattleTextBox ? executiveTrialBattleTextBox.gameObject.activeInHierarchy : false)}");
#endif

        if (!executiveTrialBattleTextBox)
            Debug.LogWarning("[ExecutiveTrialBattleUIRoot] No BattleTextBoxUI found in Executive Trial battle UI root. Battle text will not display.");
        else if (!executiveTrialBattleTextBox.gameObject.activeInHierarchy)
            Debug.LogWarning($"[ExecutiveTrialBattleUIRoot] BattleTextBoxUI '{executiveTrialBattleTextBox.name}' is assigned but not active in hierarchy.");

        battle.SetBattleBackgroundOverride(executiveTrialPlayerBackground, executiveTrialWildBackground);
    }

    public void RestoreBattleManagerDefaults()
    {
        var battle = FindFirstObjectByType<BattleManager>();
        if (!battle) return;

        battle.ClearUIBindingsOverride();
        battle.ClearUIOverride();
        battle.ClearBattleBackgroundOverride();
    }
}