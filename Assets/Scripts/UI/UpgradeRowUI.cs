using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class UpgradeRowUI : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI nameLabel;
    [SerializeField] private TextMeshProUGUI levelLabel;
    [SerializeField] private TextMeshProUGUI costLabel;

    [Header("Buttons")]
    [SerializeField] private Button buyButton;
    [SerializeField] private Button infoButton;

    private string _infoId;
    private string _fallbackTitle;
    private Sprite _icon;

    // callbacks provided by the panel
    private Func<int> _getLevel;
    private Func<int> _getCost;
    private Action _onBuy;

    public void BindStatic(string displayName, Sprite icon, string infoId,
                           Func<int> getLevel, Func<int> getCost, Action onBuy)
    {
        _fallbackTitle = displayName;
        _icon = icon;
        _infoId = infoId;

        _getLevel = getLevel;
        _getCost  = getCost;
        _onBuy    = onBuy;

        if (nameLabel) nameLabel.text = displayName;
        if (iconImage) iconImage.sprite = icon;

        if (buyButton)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(() => _onBuy?.Invoke());
        }

        if (infoButton)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OpenInfo);
        }

        Refresh();
    }

    public void Refresh()
    {
        int lvl  = _getLevel != null ? _getLevel() : 0;
        int cost = _getCost  != null ? _getCost()  : 0;

        if (levelLabel) levelLabel.text = $"Lv {lvl:N0}";
        if (costLabel)  costLabel.text  = $"{cost:N0} COINS";

        if (buyButton)
        {
            int coins = ResourceBank.Get(ResourceType.Coins);
            buyButton.interactable = coins >= cost && cost > 0;
        }
    }

    void OpenInfo()
    {
        var id = string.IsNullOrWhiteSpace(_infoId) ? "upg.unknown" : _infoId;

        const string fallbackSubtitle = "Upgrade";
        const string fallbackBody = "Increases your progression stats.\nCosts Coins.";

        InfoRouter.Open(id, _fallbackTitle, fallbackSubtitle, fallbackBody, _icon);
    }
}
