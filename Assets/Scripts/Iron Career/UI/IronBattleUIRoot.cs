using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Routes Iron Career battle UI bindings into BattleManager.
/// Iron mode does not support party switching.
/// Only handles binding overrides.
/// </summary>
public class IronBattleUIRoot : MonoBehaviour
{
    [Header("Iron UI Bindings")]
    [SerializeField] private BattleManager.BattleUIBindings ironBindings;

    [Header("Optional Iron Components (auto-find if null)")]
    [SerializeField] private BattleFeedbackManager ironFeedback;
    [SerializeField] private BattleTextBoxUI ironBattleTextBox;
    [SerializeField] private Image ironPlayerBackground;
    [SerializeField] private Image ironWildBackground;

    [SerializeField] private bool autoFindInChildren = true;

    private void Awake()
    {
        ResolveOptionalRefs();
    }

    private void ResolveOptionalRefs()
    {
        if (!autoFindInChildren) return;

        if (!ironFeedback)
            ironFeedback = GetComponentInChildren<BattleFeedbackManager>(true);

        if (!ironBattleTextBox)
        {
            var allTextBoxes = GetComponentsInChildren<BattleTextBoxUI>(true);
            DevLog.Log($"[IronBattleUIRoot] Found {allTextBoxes.Length} BattleTextBoxUI components");
            for (int i = 0; i < allTextBoxes.Length; i++)
            {
                var tb = allTextBoxes[i];
                if (!tb) continue;
                DevLog.Log($"[IronBattleUIRoot]   [{i}] {tb.name} hasRenderable={tb.HasRenderableTarget} active={tb.gameObject.activeSelf}");
                if (tb.HasRenderableTarget)
                {
                    ironBattleTextBox = tb;
                    DevLog.Log($"[IronBattleUIRoot] Assigned textbox (by HasRenderableTarget): {tb.name}");
                    break;
                }
            }

            if (!ironBattleTextBox && allTextBoxes.Length > 0)
            {
                ironBattleTextBox = allTextBoxes[0];
                DevLog.Log($"[IronBattleUIRoot] Assigned textbox (fallback first): {allTextBoxes[0].name}");
            }
        }

        if (!ironPlayerBackground)
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
                    ironPlayerBackground = img;
                    break;
                }
            }
        }

        if (!ironWildBackground)
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
                    ironWildBackground = img;
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

        DevLog.Log($"[IronBattleUIRoot] ApplyTo called: textbox={(ironBattleTextBox ? ironBattleTextBox.name : "NULL")} active={(ironBattleTextBox ? ironBattleTextBox.gameObject.activeSelf : false)}");

        battle.SetUIBindingsOverride(ironBindings);

        // No bottom toggle override — Iron does not allow switching
        battle.SetUIOverride(
            ironFeedback,
            ironBattleTextBox,
            null
        );

        // Ensure we spawn attack prefabs in Iron runs just like regular battles.
        if (ironFeedback)
            ironFeedback.SetSpawnAttackPrefabs(true);

            // Copy timing from the regular feedback so Iron Career animations feel the same speed.
            if (ironFeedback && battle.DefaultFeedback && battle.DefaultFeedback != ironFeedback)
                ironFeedback.CopyAnimationTimingsFrom(battle.DefaultFeedback);

        // Ensure text box is active before battle starts
        if (ironBattleTextBox && !ironBattleTextBox.gameObject.activeSelf)
        {
            ironBattleTextBox.gameObject.SetActive(true);
            DevLog.Log($"[IronBattleUIRoot] Activated textbox: {ironBattleTextBox.name}");
        }

#if UNITY_EDITOR
        DevLog.Log($"[IronTextTrace] ApplyTo: battle={battle.name} textbox={(ironBattleTextBox ? ironBattleTextBox.name : "NULL")} hasTarget={(ironBattleTextBox ? ironBattleTextBox.HasRenderableTarget : false)} active={(ironBattleTextBox ? ironBattleTextBox.gameObject.activeInHierarchy : false)}");
#endif

        if (!ironBattleTextBox)
            Debug.LogWarning("[IronBattleUIRoot] No BattleTextBoxUI found in Iron battle UI root. Battle text will not display.");
        else if (!ironBattleTextBox.gameObject.activeInHierarchy)
            Debug.LogWarning($"[IronBattleUIRoot] BattleTextBoxUI '{ironBattleTextBox.name}' is assigned but not active in hierarchy.");

        battle.SetBattleBackgroundOverride(ironPlayerBackground, ironWildBackground);
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