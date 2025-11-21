using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TitleOptionItem : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI unlockText;

    [Header("Action Button")]
    [SerializeField] private Button assignBtn;
    [SerializeField] private Sprite assignedActionSprite;
    [SerializeField] private Sprite unassignedActionSprite;

    [Header("Interaction")]
    [SerializeField] private Button infoButton;
    [SerializeField] private string infoId;

    private string _ownedId;
    private MonsterDataSO _def;
    private int _level;
    private int _tierIndex;
    private int _levelRequired;
    private TitleSO _option;
    private TitleSO _equippedInTier;
    private Action _onChanged;

    // cached UI bits
    private TextMeshProUGUI _assignBtnLabel;
    private Image _assignBtnImage;

    void Awake()
    {
        if (assignBtn)
        {
            _assignBtnLabel = assignBtn.GetComponentInChildren<TextMeshProUGUI>(true);
            _assignBtnImage = assignBtn.GetComponent<Image>();
        }

        if (infoButton)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OpenInfo);
        }
    }

    public void SetIcon(Sprite s)
    {
        if (!icon) return;
        icon.sprite = s;
        icon.enabled = s != null;
    }

    public void Setup(
        string ownedId,
        MonsterDataSO def,
        int level,
        int tierIndex,
        int levelRequired,
        TitleSO option,
        TitleSO equippedInTier,
        Action onChanged)
    {
        _ownedId        = ownedId;
        _def            = def;
        _level          = Mathf.Max(1, level);
        _tierIndex      = tierIndex;
        _levelRequired  = Mathf.Max(1, levelRequired);
        _option         = option;
        _equippedInTier = equippedInTier;
        _onChanged      = onChanged;

        // We no longer auto-generate infoId from the TitleSO.
        // If you want a custom Info entry, set infoId in the inspector.

        if (nameText)   nameText.text   = option ? option.displayName : "(null)";
        if (unlockText) unlockText.text = (_level >= _levelRequired) ? "Unlocked" : $"Lvl ≥ {_levelRequired}";

        if (assignBtn)
        {
            assignBtn.onClick.RemoveAllListeners();
            assignBtn.interactable = (_level >= _levelRequired) && _option != null;
            assignBtn.onClick.AddListener(OnActionClicked);
        }

        RefreshButtonVisuals();
    }

    void RefreshButtonVisuals()
    {
        bool isThisEquipped = _equippedInTier != null && _equippedInTier == _option;

        if (_assignBtnLabel) _assignBtnLabel.text = isThisEquipped ? "Remove" : "Assign";
        if (_assignBtnImage)
            _assignBtnImage.sprite = isThisEquipped ? assignedActionSprite : unassignedActionSprite;
    }

    void OnActionClicked()
    {
        if (_option == null || _def == null) return;

        bool isThisEquipped = _equippedInTier != null && _equippedInTier == _option;

        if (isThisEquipped)
        {
            TitleManager.I?.Unequip(_ownedId, _def, _tierIndex);
            _equippedInTier = null;
        }
        else
        {
            TitleManager.I?.Equip(_ownedId, _def, _tierIndex, _option);
            _equippedInTier = _option;
        }

        RefreshButtonVisuals();

        AudioManager.I.PlayClick();

        TitleAssignPanelUI.OnTitlesChanged?.Invoke(_ownedId);
        _onChanged?.Invoke();
    }

    void OpenInfo()
    {
        if (_option == null)
            return;

        // Allow override via inspector; otherwise use generic Title info entry.
        var id = string.IsNullOrWhiteSpace(infoId) ? "title.generic" : infoId;

        string fallbackTitle      = _option.displayName;
        const string fallbackSubtitle = "Title";
        string fallbackBody       = _option.description;

        InfoRouter.Open(id, fallbackTitle, fallbackSubtitle, fallbackBody);

        AudioManager.I.PlayClick();
    }
}
