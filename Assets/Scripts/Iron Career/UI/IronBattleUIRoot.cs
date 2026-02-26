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
        if (!autoFindInChildren) return;

        if (!ironFeedback)
            ironFeedback = GetComponentInChildren<BattleFeedbackManager>(true);

        if (!ironBattleTextBox)
            ironBattleTextBox = GetComponentInChildren<BattleTextBoxUI>(true);

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

        battle.SetUIBindingsOverride(ironBindings);

        // No bottom toggle override — Iron does not allow switching
        battle.SetUIOverride(
            ironFeedback,
            ironBattleTextBox,
            null
        );

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