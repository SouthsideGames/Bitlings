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

    // Context
    private string _ownedId;
    private MonsterDataSO _def;
    private int _level;
    private int _tierIndex;
    private TitleSO _option;         
    private TitleSO _equippedInTier; 
    private int _levelRequired;
    private Action _onChanged;
    private TextMeshProUGUI assignBtnLabel;
    private Image assignBtnImage;       

    private void Awake()
    {
        assignBtnLabel = assignBtn.GetComponentInChildren<TextMeshProUGUI>();
        assignBtnImage = assignBtn.GetComponent<Image>();

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
        _ownedId         = ownedId;
        _def             = def;
        _level           = Mathf.Max(1, level);
        _tierIndex       = tierIndex;
        _levelRequired   = Mathf.Max(1, levelRequired);
        _option          = option;
        _equippedInTier  = equippedInTier;
        _onChanged       = onChanged;

        if (nameText) nameText.text = option ? option.displayName : "(null)";

        bool tierUnlocked = _level >= _levelRequired;
        if (unlockText) unlockText.text = tierUnlocked ? "Unlocked" : $"Lvl ≥ {_levelRequired}";

        bool isThisEquipped = _equippedInTier != null && _equippedInTier == _option;

        if (assignBtnLabel) assignBtnLabel.text = isThisEquipped ? "Remove" : "Assign";
        if (assignBtnImage) assignBtnImage.sprite = isThisEquipped ? assignedActionSprite : unassignedActionSprite;

        if (assignBtn)
        {
            assignBtn.onClick.RemoveAllListeners();
            assignBtn.interactable = tierUnlocked && _option != null;
            assignBtn.onClick.AddListener(OnActionClicked);
        }
    }

    private void OnActionClicked()
    {
        if (_option == null || _def == null) return;

        bool isThisEquipped = _equippedInTier != null && _equippedInTier == _option;

        if (isThisEquipped)
            TitleManager.I.Unequip(_ownedId, _def, _tierIndex);
        else
            TitleManager.I.Equip(_ownedId, _def, _tierIndex, _option);

        TitleAssignPanelUI.OnTitlesChanged?.Invoke(_ownedId);
        _onChanged?.Invoke();
    }
}
