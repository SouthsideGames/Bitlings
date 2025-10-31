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
    [SerializeField] private Sprite assignedActionSprite;   // shown when THIS option is equipped (button = Remove)
    [SerializeField] private Sprite unassignedActionSprite; // shown when NOT equipped (button = Assign)

    // Context (set by Setup)
    private string _ownedId;
    private MonsterDataSO _def;
    private int _level;
    private int _tierIndex;
    private int _levelRequired;
    private TitleSO _option;             // this row's option
    private TitleSO _equippedInTier;     // current equipped for this tier
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
        _equippedInTier = equippedInTier; // <<— important: seed current state
        _onChanged      = onChanged;

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

    private void RefreshButtonVisuals()
    {
        bool isThisEquipped = (_equippedInTier != null && _equippedInTier == _option);

        if (_assignBtnLabel) _assignBtnLabel.text = isThisEquipped ? "Remove" : "Assign";
        if (_assignBtnImage)
            _assignBtnImage.sprite = isThisEquipped ? assignedActionSprite : unassignedActionSprite;
    }

    private void OnActionClicked()
    {
        if (_option == null || _def == null) return;

        bool isThisEquipped = (_equippedInTier != null && _equippedInTier == _option);

        // Drive the runtime
        if (isThisEquipped)
        {
            TitleManager.I?.Unequip(_ownedId, _def, _tierIndex);
            _equippedInTier = null; // <<— flip local state immediately
        }
        else
        {
            TitleManager.I?.Equip(_ownedId, _def, _tierIndex, _option);
            _equippedInTier = _option; // <<— flip local state immediately
        }

        // Update this row’s visuals right away so the user sees the change
        RefreshButtonVisuals();

        // Notify the rest of the UI to refresh if needed (lists, headers, etc.)
        TitleAssignPanelUI.OnTitlesChanged?.Invoke(_ownedId);
        _onChanged?.Invoke();
    }

    
}
