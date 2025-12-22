using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Button))]
public class PackShopItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;

    [Header("NEW - Row Details")]
    [SerializeField] private TextMeshProUGUI rarityText;
    [SerializeField] private TextMeshProUGUI costText;

    [Header("Badges")]
    [SerializeField] private GameObject unlockedBadge;
    [SerializeField] private GameObject returningBadge;

    private MonsterPackSO _pack;
    private PackDetailPanelUI _detailPanel;
    private Button _button;

    void Awake()
    {
        _button = GetComponent<Button>();
        _button.onClick.AddListener(OnClicked);
    }

    void OnEnable()
    {
        MonsterPackManager.OnPackUnlocked += OnPackUnlocked;
        RefreshState();
    }

    void OnDisable()
    {
        MonsterPackManager.OnPackUnlocked -= OnPackUnlocked;
    }

    void OnDestroy()
    {
        if (_button != null)
            _button.onClick.RemoveListener(OnClicked);
    }

    public void Bind(MonsterPackSO pack, PackDetailPanelUI detailPanel)
    {
        _pack = pack;
        _detailPanel = detailPanel;

        if (_pack != null)
        {
            if (icon) icon.sprite = _pack.icon;
            if (nameText) nameText.text = _pack.displayName;

            if (rarityText)
                rarityText.text = string.IsNullOrEmpty(_pack.rarityLabel) ? "" : _pack.rarityLabel;
        }

        RefreshState();
    }

    private void OnPackUnlocked(string _)
    {
        RefreshState();
    }

    private void RefreshState()
    {
        if (_pack == null || MonsterPackManager.I == null) return;

        var mgr = MonsterPackManager.I;

        bool unlocked = mgr.IsUnlocked(_pack.id);

        if (unlockedBadge)
            unlockedBadge.SetActive(unlocked);

        // Returning badge: show only if it’s a returning pack AND not already unlocked
        bool returning = !unlocked && mgr.IsReturningPackThisSeason(_pack.id);
        if (returningBadge)
            returningBadge.SetActive(returning);

        // Cost (effective)
        if (costText != null)
        {
            if (unlocked)
            {
                costText.text = "Unlocked";
            }
            else if (mgr.TryGetEffectiveCost(_pack, out int cost, out _))
            {
                costText.text = $"{cost}";
            }
            else
            {
                costText.text = "";
            }
        }
    }

    private void OnClicked()
    {
        if (_pack == null || _detailPanel == null) return;

        _detailPanel.Open(_pack);
        UIManager.I?.Show(PanelId.PackDetails);

        AudioManager.I?.PlayClick();
    }
}
