using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

// ─────────────────────────────────────────────────────────────
// BattleManager.UIBindingsOverride
// Runtime HUD binding override/restore support for alternate battle UIs.
// ─────────────────────────────────────────────────────────────

public partial class BattleManager : MonoBehaviour
{
    [Serializable]
    public sealed class BattleUIBindings
    {
        [Header("Wild UI")]
        public GameObject wildPanel;
        public Slider wildHPBar;
        public Image wildIcon;
        public TextMeshProUGUI wildNameText;
        public TextMeshProUGUI wildLevelText;
        public TextMeshProUGUI wildIdText;
        public TextMeshProUGUI wildTypeText;
        public TextMeshProUGUI wildRarityText;
        public TextMeshProUGUI wildHPText;
        public TextMeshProUGUI wildATKText;
        public TextMeshProUGUI wildDEFText;
        public TextMeshProUGUI wildSPDText;

        [Header("Player UI")]
        public GameObject playerPanel;
        public Slider playerHPBar;
        public Image playerIcon;
        public TextMeshProUGUI playerNameText;
        public TextMeshProUGUI playerLevelText;
        public TextMeshProUGUI playerIdText;
        public TextMeshProUGUI playerTypeText;
        public TextMeshProUGUI playerRarityText;
        public TextMeshProUGUI playerHPText;
        public TextMeshProUGUI playerATKText;
        public TextMeshProUGUI playerDEFText;
        public TextMeshProUGUI playerSPDText;

        [Header("Bench UI")]
        public Button benchBtn1;
        public Button benchBtn2;
        public Image benchImg1;
        public Image benchImg2;
        public TextMeshProUGUI benchHPText1;
        public TextMeshProUGUI benchHPText2;

        [Header("Owned Indicator")]
        [Tooltip("Small 'already caught' icon shown when this species is in your collection.")]
        public GameObject ownedCapturedIcon;
    }

    private bool _uiBindingsDefaultsCaptured;
    private BattleUIBindings _uiBindingsDefaults;

    private BattleUIBindings _uiBindingsCurrent;
    private bool _uiBindingsHasCurrent;

    private void ReapplyRuntimeUIBindingsOverrideIfAny()
    {
        if (!_uiBindingsHasCurrent || _uiBindingsCurrent == null) return;
        SetUIBindingsOverride(_uiBindingsCurrent);
    }

    private void CaptureUIBindingsDefaults()
    {
        if (_uiBindingsDefaultsCaptured) return;
        _uiBindingsDefaultsCaptured = true;

        _uiBindingsDefaults = new BattleUIBindings
        {
            wildPanel = wildPanel,
            wildHPBar = wildHPBar,
            wildIcon = wildIcon,
            wildNameText = wildNameText,
            wildLevelText = wildLevelText,
            wildIdText = wildIdText,
            wildTypeText = wildTypeText,
            wildRarityText = wildRarityText,
            wildHPText = wildHPText,
            wildATKText = wildATKText,
            wildDEFText = wildDEFText,
            wildSPDText = wildSPDText,

            playerPanel = playerPanel,
            playerHPBar = playerHPBar,
            playerIcon = playerIcon,
            playerNameText = playerNameText,
            playerLevelText = playerLevelText,
            playerIdText = playerIdText,
            playerTypeText = playerTypeText,
            playerRarityText = playerRarityText,
            playerHPText = playerHPText,
            playerATKText = playerATKText,
            playerDEFText = playerDEFText,
            playerSPDText = playerSPDText,

            benchBtn1 = benchBtn1,
            benchBtn2 = benchBtn2,
            benchImg1 = benchImg1,
            benchImg2 = benchImg2,
            benchHPText1 = benchHPText1,
            benchHPText2 = benchHPText2,

            ownedCapturedIcon = ownedCapturedIcon,
        };

        _uiBindingsCurrent = _uiBindingsDefaults;
        _uiBindingsHasCurrent = true;
    }

    /// <summary>
    /// Overrides all HUD bindings (wild/player/bench panels) at runtime.
    /// Additive only: safe for normal mode, used by Executive Trial.
    /// </summary>
    public void SetUIBindingsOverride(BattleUIBindings o)
    {
        if (o == null) return;
        CaptureUIBindingsDefaults();

        if (_uiBindingsHasCurrent)
            DeactivatePanelRoots(_uiBindingsCurrent, o);

        if (o.wildPanel) wildPanel = o.wildPanel;
        if (o.wildHPBar) wildHPBar = o.wildHPBar;
        if (o.wildIcon) wildIcon = o.wildIcon;
        if (o.wildNameText) wildNameText = o.wildNameText;
        if (o.wildLevelText) wildLevelText = o.wildLevelText;
        if (o.wildIdText) wildIdText = o.wildIdText;
        if (o.wildTypeText) wildTypeText = o.wildTypeText;
        if (o.wildRarityText) wildRarityText = o.wildRarityText;
        if (o.wildHPText) wildHPText = o.wildHPText;
        if (o.wildATKText) wildATKText = o.wildATKText;
        if (o.wildDEFText) wildDEFText = o.wildDEFText;
        if (o.wildSPDText) wildSPDText = o.wildSPDText;

        if (o.playerPanel) playerPanel = o.playerPanel;
        if (o.playerHPBar) playerHPBar = o.playerHPBar;
        if (o.playerIcon) playerIcon = o.playerIcon;
        if (o.playerNameText) playerNameText = o.playerNameText;
        if (o.playerLevelText) playerLevelText = o.playerLevelText;
        if (o.playerIdText) playerIdText = o.playerIdText;
        if (o.playerTypeText) playerTypeText = o.playerTypeText;
        if (o.playerRarityText) playerRarityText = o.playerRarityText;
        if (o.playerHPText) playerHPText = o.playerHPText;
        if (o.playerATKText) playerATKText = o.playerATKText;
        if (o.playerDEFText) playerDEFText = o.playerDEFText;
        if (o.playerSPDText) playerSPDText = o.playerSPDText;

        if (o.benchBtn1) benchBtn1 = o.benchBtn1;
        if (o.benchBtn2) benchBtn2 = o.benchBtn2;
        if (o.benchImg1) benchImg1 = o.benchImg1;
        if (o.benchImg2) benchImg2 = o.benchImg2;
        if (o.benchHPText1) benchHPText1 = o.benchHPText1;
        if (o.benchHPText2) benchHPText2 = o.benchHPText2;

        if (o.ownedCapturedIcon) ownedCapturedIcon = o.ownedCapturedIcon;

        _uiBindingsCurrent = o;
        _uiBindingsHasCurrent = true;
        ActivatePanelRoots(_uiBindingsCurrent, inBattle);
        RebindBenchButtons();
    }

    /// <summary>Restores all HUD bindings back to inspector defaults.</summary>
    public void ClearUIBindingsOverride()
    {
        if (!_uiBindingsDefaultsCaptured || _uiBindingsDefaults == null) return;

        if (_uiBindingsHasCurrent)
            DeactivatePanelRoots(_uiBindingsCurrent, _uiBindingsDefaults);

        wildPanel = _uiBindingsDefaults.wildPanel;
        wildHPBar = _uiBindingsDefaults.wildHPBar;
        wildIcon = _uiBindingsDefaults.wildIcon;
        wildNameText = _uiBindingsDefaults.wildNameText;
        wildLevelText = _uiBindingsDefaults.wildLevelText;
        wildIdText = _uiBindingsDefaults.wildIdText;
        wildTypeText = _uiBindingsDefaults.wildTypeText;
        wildRarityText = _uiBindingsDefaults.wildRarityText;
        wildHPText = _uiBindingsDefaults.wildHPText;
        wildATKText = _uiBindingsDefaults.wildATKText;
        wildDEFText = _uiBindingsDefaults.wildDEFText;
        wildSPDText = _uiBindingsDefaults.wildSPDText;

        playerPanel = _uiBindingsDefaults.playerPanel;
        playerHPBar = _uiBindingsDefaults.playerHPBar;
        playerIcon = _uiBindingsDefaults.playerIcon;
        playerNameText = _uiBindingsDefaults.playerNameText;
        playerLevelText = _uiBindingsDefaults.playerLevelText;
        playerIdText = _uiBindingsDefaults.playerIdText;
        playerTypeText = _uiBindingsDefaults.playerTypeText;
        playerRarityText = _uiBindingsDefaults.playerRarityText;
        playerHPText = _uiBindingsDefaults.playerHPText;
        playerATKText = _uiBindingsDefaults.playerATKText;
        playerDEFText = _uiBindingsDefaults.playerDEFText;
        playerSPDText = _uiBindingsDefaults.playerSPDText;

        benchBtn1 = _uiBindingsDefaults.benchBtn1;
        benchBtn2 = _uiBindingsDefaults.benchBtn2;
        benchImg1 = _uiBindingsDefaults.benchImg1;
        benchImg2 = _uiBindingsDefaults.benchImg2;
        benchHPText1 = _uiBindingsDefaults.benchHPText1;
        benchHPText2 = _uiBindingsDefaults.benchHPText2;

        ownedCapturedIcon = _uiBindingsDefaults.ownedCapturedIcon;

        _uiBindingsCurrent = _uiBindingsDefaults;
        _uiBindingsHasCurrent = true;
        ActivatePanelRoots(_uiBindingsCurrent, inBattle);
        RebindBenchButtons();
    }

    private static void ActivatePanelRoots(BattleUIBindings b, bool active)
    {
        if (b == null) return;
        if (b.wildPanel) b.wildPanel.SetActive(active);
        if (b.playerPanel) b.playerPanel.SetActive(active);
    }

    private static void DeactivatePanelRoots(BattleUIBindings prev, BattleUIBindings next)
    {
        if (prev == null) return;

        if (prev.wildPanel && (next == null || prev.wildPanel != next.wildPanel))
            prev.wildPanel.SetActive(false);

        if (prev.playerPanel && (next == null || prev.playerPanel != next.playerPanel))
            prev.playerPanel.SetActive(false);
    }
}