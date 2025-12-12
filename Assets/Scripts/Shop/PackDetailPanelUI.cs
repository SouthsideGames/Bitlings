using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PackDetailPanelUI : MonoBehaviour
{
    [Header("Pack Info")]
    [SerializeField] private Image packIcon;
    [SerializeField] private TextMeshProUGUI packNameText;
    [SerializeField] private TextMeshProUGUI descriptionText;
    [SerializeField] private TextMeshProUGUI costText;

    [Header("Monster Icons")]
    [SerializeField] private Transform monsterIconRoot;
    [SerializeField] private Image monsterIconPrefab;

    [Header("Buttons")]
    [SerializeField] private HoldToPurchaseButton purchaseButton;

    private MonsterPackSO _currentPack;

    void Awake()
    {
        if (purchaseButton)
            purchaseButton.onHoldComplete.AddListener(PurchaseCurrentPack);
    }

    public void Open(MonsterPackSO pack)
    {
        _currentPack = pack;
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        RefreshUI();
    }

    public void PurchaseCurrentPack()
    {
        if (_currentPack == null || MonsterPackManager.I == null)
            return;

        if (!MonsterPackManager.I.CanPurchase(_currentPack.id, out _))
            return;

        bool success = MonsterPackManager.I.Purchase(_currentPack.id);
        if (success && purchaseButton)
            purchaseButton.gameObject.SetActive(false);
    }

    private void RefreshUI()
    {
        if (_currentPack == null) return;

        if (packIcon) packIcon.sprite = _currentPack.icon;
        if (packNameText) packNameText.text = _currentPack.displayName;
        if (descriptionText) descriptionText.text = _currentPack.description;

        if (MonsterPackManager.I != null &&
            MonsterPackManager.I.TryGetEffectiveCost(_currentPack, out int cost, out ResourceType currency))
        {
            if (costText)
                costText.text = $"{cost} {CurrencyLabel(currency)}";
        }
        else
        {
            if (costText)
                costText.text = string.Empty;
        }

        BuildMonsterIcons();
    }

    private void BuildMonsterIcons()
    {
        if (!monsterIconRoot || !monsterIconPrefab || _currentPack == null)
            return;

        for (int i = monsterIconRoot.childCount - 1; i >= 0; i--)
            Destroy(monsterIconRoot.GetChild(i).gameObject);

        if (_currentPack.monsters == null) return;

        foreach (var monster in _currentPack.monsters)
        {
            if (!monster) continue;

            var iconInstance = Instantiate(monsterIconPrefab, monsterIconRoot);
            iconInstance.sprite = monster.icon;
        }
    }

    private string CurrencyLabel(ResourceType t)
    {
        switch (t)
        {
            case ResourceType.PackVoucher: return "Shards";
            default: return t.ToString();
        }
    }
}
