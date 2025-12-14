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
    [SerializeField] private Button purchaseButton;

    private MonsterPackSO _currentPack;

    private void Awake()
    {
        if (purchaseButton != null)
        {
            purchaseButton.onClick.RemoveAllListeners();
            purchaseButton.onClick.AddListener(PurchaseCurrentPack);
        }
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
        if (_currentPack == null)
        {
            Debug.LogError("Pack purchase failed: No current pack assigned.");
            return;
        }

        if (MonsterPackManager.I == null)
        {
            Debug.LogError("Pack purchase failed: MonsterPackManager not available.");
            return;
        }

        if (!MonsterPackManager.I.CanPurchase(_currentPack.id, out _))
        {
            Debug.Log("Pack purchase blocked: Cannot afford or not allowed.");
            RefreshUI(); // keep UI honest
            return;
        }

        bool success = MonsterPackManager.I.Purchase(_currentPack.id);

        if (success)
        {
            Debug.Log($"Pack purchased: {_currentPack.displayName}");

            if (purchaseButton != null)
                purchaseButton.gameObject.SetActive(false);

            RefreshUI();
        }
        else
        {
            Debug.LogError("Pack purchase failed inside MonsterPackManager.");
            RefreshUI();
        }
    }

    private void RefreshUI()
    {
        if (_currentPack == null)
            return;

        if (packIcon) packIcon.sprite = _currentPack.icon;
        if (packNameText) packNameText.text = _currentPack.displayName;
        if (descriptionText) descriptionText.text = _currentPack.description;

        int cost = 0;
        ResourceType currency = ResourceType.None;

        if (MonsterPackManager.I != null &&
            MonsterPackManager.I.TryGetEffectiveCost(_currentPack, out cost, out currency))
        {
            int have = 0;

            // Prefer ResourceManager if it exists, otherwise fallback to ResourceBank.
            if (ResourceManager.I != null)
                have = ResourceManager.I.Get(currency);
            else
                have = ResourceBank.Get(currency);

            if (costText)
                costText.text = $"{have} / {cost} {CurrencyLabel(currency)}";
        }
        else
        {
            if (costText) costText.text = string.Empty;
        }

        // Button availability
        if (purchaseButton != null)
        {
            bool canPurchase = (MonsterPackManager.I != null) && MonsterPackManager.I.CanPurchase(_currentPack.id, out _);
            purchaseButton.interactable = canPurchase;
        }

        BuildMonsterIcons();
    }

    private void BuildMonsterIcons()
    {
        if (!monsterIconRoot || !monsterIconPrefab || _currentPack == null)
            return;

        for (int i = monsterIconRoot.childCount - 1; i >= 0; i--)
            Destroy(monsterIconRoot.GetChild(i).gameObject);

        if (_currentPack.monsters == null)
            return;

        foreach (var monster in _currentPack.monsters)
        {
            if (!monster) continue;

            var iconInstance = Instantiate(monsterIconPrefab, monsterIconRoot);
            iconInstance.sprite = monster.icon;
        }
    }

    private string CurrencyLabel(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.PackVoucher:
                return "Pack Vouchers"; // was incorrectly "Shards"
            default:
                return type.ToString();
        }
    }
}
