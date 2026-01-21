using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class UpgradeRowUI : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI stateLabel;
    [SerializeField] private TextMeshProUGUI costLabel;

    [Header("Buttons")]
    [SerializeField] private Button buyButton;
    [SerializeField] private Button infoButton;

    // internal data
    FeatureId _featureId = FeatureId.None;
    int _creditCost;
    string _infoId;
    string _fallbackTitle;
    Sprite _icon;

    bool _hasValidEntry = false;

    void Awake()
    {
        if (buyButton != null)
            buyButton.onClick.AddListener(OnBuyClicked);

        if (infoButton != null)
            infoButton.onClick.AddListener(OpenInfo);
    }

    void OnEnable()
    {
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        GameEvents.OnResourcesChanged += HandleResourcesChanged;

        Refresh();
    }

    void OnDisable()
    {
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;

        GameEvents.OnResourcesChanged -= HandleResourcesChanged;
    }

    void HandleFeatureUnlocked(FeatureId f)
    {
        if (f == _featureId)
            Refresh();
    }

    void HandleResourcesChanged()
    {
        Refresh();
    }

    // ─────────────────────────────────────────────────────────────
    // Init
    // ─────────────────────────────────────────────────────────────

    public void Init(UpgradeCatalogEntry entry)
    {
        // Launch-safe: do NOT throw here. A single bad/missing entry should not crash the UI.
        if (entry == null)
        {
            _hasValidEntry = false;

            Debug.LogError(
                $"[UpgradeRowUI] Init called with null UpgradeCatalogEntry on '{gameObject.name}'. " +
                "This row will be disabled to prevent UI crashes. Check your Upgrade Catalog / references.",
                this
            );

            _featureId = FeatureId.None;
            _creditCost = 0;
            _infoId = "upg.unknown";
            _fallbackTitle = "Unavailable";

            if (nameLabel != null) nameLabel.text = _fallbackTitle;
            if (stateLabel != null) stateLabel.text = "Unavailable";
            if (costLabel != null) costLabel.text = "-";

            if (buyButton != null) buyButton.interactable = false;

            if (infoButton != null) infoButton.interactable = true;

            return;
        }

        _hasValidEntry = true;

        _featureId = entry.featureId;
        _creditCost = entry.creditCost;
        _infoId = entry.infoId;
        _fallbackTitle = string.IsNullOrWhiteSpace(entry.displayName)
            ? _featureId.ToString()
            : entry.displayName;

        if (nameLabel != null) nameLabel.text = _fallbackTitle;

        Refresh();
    }

    // ─────────────────────────────────────────────────────────────
    // UI refresh
    // ─────────────────────────────────────────────────────────────

    public void Refresh()
    {
        if (!_hasValidEntry)
        {
            if (buyButton != null) buyButton.interactable = false;
            return;
        }

        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(_featureId);

        if (stateLabel != null)
            stateLabel.text = unlocked ? "Unlocked" : "Locked";

        if (costLabel != null)
            costLabel.text = unlocked ? "-" : $"{_creditCost} credits";

        if (buyButton != null)
        {
            int credits = ResourceBank.Get(ResourceType.Credits);
            buyButton.interactable = !unlocked && _creditCost > 0 && credits >= _creditCost;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Button actions
    // ─────────────────────────────────────────────────────────────

    void OnBuyClicked()
    {
        if (!_hasValidEntry)
            return;

        if (FeatureUnlockManager.I == null)
            return;

        if (FeatureUnlockManager.I.IsUnlocked(_featureId))
            return;

        if (_creditCost > 0 && !ResourceBank.TrySpend(ResourceType.Credits, _creditCost))
            return;

        // Unlock + persist
        FeatureUnlockManager.I.Unlock(_featureId);
        SaveManager.Save();

        GameEvents.OnResourcesChanged?.Invoke();
        Refresh();
    }

    void OpenInfo()
    {
        var id = string.IsNullOrWhiteSpace(_infoId) ? "upg.unknown" : _infoId;

        const string fallbackSubtitle = "Feature Unlock";
        const string fallbackBody =
            "Unlocks a new feature or system for your account.\nCosts credits.";

        InfoRouter.Open(id, _fallbackTitle, fallbackSubtitle, fallbackBody, _icon);
    }
}
