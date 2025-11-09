using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PackShopItemUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI descText;
    [SerializeField] private TextMeshProUGUI costText;
    [SerializeField] private Button buyButton;
    [SerializeField] private GameObject unlockedBadge;

    private MonsterPackSO _pack;

    void Awake()
    {
        if (buyButton) buyButton.onClick.AddListener(OnBuyClicked);
    }

    public void Bind(MonsterPackSO pack)
    {
        _pack = pack;
        if (_pack)
        {
            if (icon) icon.sprite = _pack.icon;
            if (nameText) nameText.text = _pack.displayName;
            if (descText) descText.text = _pack.description;
        }

        RefreshState();

        GameEvents.OnResourcesChanged += OnResourcesChanged;
        MonsterPackManager.OnPackUnlocked += OnPackUnlocked;
    }

    void OnDestroy()
    {
        GameEvents.OnResourcesChanged -= OnResourcesChanged;
        MonsterPackManager.OnPackUnlocked -= OnPackUnlocked;
    }

    private void OnResourcesChanged() => RefreshState();
    private void OnPackUnlocked(string _) => RefreshState();

    private void RefreshState()
    {
        if (_pack == null || MonsterPackManager.I == null) return;

        if (MonsterPackManager.I.TryGetEffectiveCost(_pack, out int cost, out ResourceType currency))
        {
            if (costText)
                costText.text = $"{cost} {CurrencyLabel(currency)}";
        }

        bool unlocked = MonsterPackManager.I.IsUnlocked(_pack.id);
        if (unlockedBadge) unlockedBadge.SetActive(unlocked);
        if (buyButton) buyButton.gameObject.SetActive(!unlocked);

        string reason = null; // ✅ initialize
        bool can = !unlocked && MonsterPackManager.I.CanPurchase(_pack.id, out reason);

        if (buyButton) buyButton.interactable = can;
        if (costText) costText.color = can ? Color.white : new Color(1f, 0.6f, 0.6f);

        if (!can && !unlocked && !string.IsNullOrEmpty(reason) && descText)
            descText.text = $"{_pack.description}\n<size=80%><color=#FFAAAA>{reason}</color></size>";
    }

    private string CurrencyLabel(ResourceType t)
    {
        switch (t)
        {
            case ResourceType.PackShards: return "Shards";
            default:                      return t.ToString();
        }
    }

    private void OnBuyClicked()
    {
        if (_pack == null || MonsterPackManager.I == null) return;
        if (MonsterPackManager.I.Purchase(_pack.id))
            RefreshState();
    }
}

