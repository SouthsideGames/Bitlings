using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class PackShopItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private Button rootButton;
    [SerializeField] private GameObject unlockedBadge;

    private MonsterPackSO _pack;
    private PackDetailPanelUI _detailPanel;

    void Awake()
    {
        if (rootButton)
            rootButton.onClick.AddListener(OnClicked);
    }

    public void Bind(MonsterPackSO pack, PackDetailPanelUI detailPanel)
    {
        _pack = pack;
        _detailPanel = detailPanel;

        if (_pack != null)
        {
            if (icon) icon.sprite = _pack.icon;
            if (nameText) nameText.text = _pack.displayName;
        }

        RefreshState();
    }

    private void RefreshState()
    {
        if (_pack == null || MonsterPackManager.I == null) return;

        bool unlocked = MonsterPackManager.I.IsUnlocked(_pack.id);
        if (unlockedBadge)
            unlockedBadge.SetActive(unlocked);
    }

    private void OnClicked()
    {
        if (_pack == null || _detailPanel == null) return;
        _detailPanel.Open(_pack);

        AudioManager.I?.PlayClick();
    }
}
