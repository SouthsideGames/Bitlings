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

    [Header("Status / Messaging")]
    [SerializeField] private TextMeshProUGUI statusText;            // NEW: reason like "Not available this season"
    [SerializeField] private TextMeshProUGUI purchaseButtonLabel;   

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

    private void OnEnable()
    {
        GameEvents.OnResourcesChanged += OnResourcesChanged;
        MonsterPackManager.OnPackUnlocked += OnPackUnlocked;
        RefreshUI();
    }

    private void OnDisable()
    {
        GameEvents.OnResourcesChanged -= OnResourcesChanged;
        MonsterPackManager.OnPackUnlocked -= OnPackUnlocked;
    }

    private void OnResourcesChanged()
    {
        RefreshUI();
    }

    private void OnPackUnlocked(string _)
    {
        RefreshUI();
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
            return;

        var mgr = MonsterPackManager.I;
        if (mgr == null)
            return;

        if (!mgr.CanPurchase(_currentPack.id, out string reason))
        {
            // Surface the reason to UI
            SetStatus(reason);
            RefreshUI();
            return;
        }

        bool success = mgr.Purchase(_currentPack.id);

        if (success)
        {
            var name = string.IsNullOrEmpty(_currentPack.displayName)
            ? "UNKNOWN PACK"
            : _currentPack.displayName.ToUpperInvariant();

            GameEvents.RaiseToast($"PACK PURCHASED: {name}!");

            RefreshUI();
        }
        else
        {
            Debug.LogError("[PackDetailPanelUI] Pack purchase failed inside MonsterPackManager.");
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

        var mgr = MonsterPackManager.I;

        // Cost display
        if (mgr != null && mgr.TryGetEffectiveCost(_currentPack, out int cost, out ResourceType currency))
        {
            int have = (ResourceManager.I != null) ? ResourceManager.I.Get(currency) : ResourceBank.Get(currency);

            if (costText)
                costText.text = $"{have} / {cost} {CurrencyLabel(currency)}";
        }
        else
        {
            if (costText) costText.text = string.Empty;
        }

        // Purchase state & messaging
        if (purchaseButton != null)
        {
            if (mgr == null)
            {
                purchaseButton.interactable = false;
                SetButtonLabel("Unavailable");
                SetStatus("Shop unavailable");
            }
            else
            {
                // Use reason string from manager
                bool canPurchase = mgr.CanPurchase(_currentPack.id, out string reason);

                bool unlocked = mgr.IsUnlocked(_currentPack.id);

                if (unlocked)
                {
                    purchaseButton.interactable = false;
                    SetButtonLabel("Unlocked");
                    SetStatus("Already unlocked");
                }
                else if (canPurchase)
                {
                    purchaseButton.interactable = true;
                    SetButtonLabel("Purchase");
                    ClearStatus();
                }
                else
                {
                    purchaseButton.interactable = false;
                    SetButtonLabel("Unavailable");
                    SetStatus(reason);
                }
            }
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

    private void SetStatus(string msg)
    {
        if (!statusText) return;
        statusText.gameObject.SetActive(!string.IsNullOrEmpty(msg));
        statusText.text = msg ?? string.Empty;
    }

    private void ClearStatus()
    {
        if (!statusText) return;
        statusText.gameObject.SetActive(false);
        statusText.text = string.Empty;
    }

    private void SetButtonLabel(string text)
    {
        if (purchaseButtonLabel)
            purchaseButtonLabel.text = text;
    }

    private string CurrencyLabel(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.PackVoucher:
                return "Pack Vouchers";
            default:
                return type.ToString();
        }
    }
}
