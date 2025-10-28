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
    [SerializeField] private Button actionBtn;
    [SerializeField] private TextMeshProUGUI actionBtnLabel;

    // Context
    private string _ownedId;
    private MonsterDataSO _def;
    private int _level;
    private int _tierIndex;
    private TitleSO _option;         // the title represented by this row
    private TitleSO _equippedInTier; // current equipped title for this tier (can be null)
    private int _levelRequired;
    private Action _onChanged;       // notify panel to refresh & TitleButtonUI to update

    // Optional: if you want to set sprite later
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

        // Name
        if (nameText)
            nameText.text = option ? option.displayName : "(null)";

        // Unlock text + button state
        bool tierUnlocked = _level >= _levelRequired;
        if (unlockText)
            unlockText.text = tierUnlocked ? "Unlocked" : $"Lvl ≥ {_levelRequired}";

        // Button label & interactivity
        bool isThisEquipped = (_equippedInTier != null && _equippedInTier == _option);
        if (actionBtnLabel)
            actionBtnLabel.text = isThisEquipped ? "Remove" : "Assign";

        // If tier is locked, disable; if unlocked, allow Assign/Remove.
        if (actionBtn)
        {
            actionBtn.onClick.RemoveAllListeners();
            actionBtn.interactable = tierUnlocked && _option != null;
            actionBtn.onClick.AddListener(OnActionClicked);
        }
    }

    private void OnActionClicked()
    {
        if (_option == null || _def == null) return;

        bool isThisEquipped = (_equippedInTier != null && _equippedInTier == _option);

        if (isThisEquipped)
        {
            // Remove
            TitleManager.I.Unequip(_ownedId, _def, _tierIndex);
        }
        else
        {
            // Assign
            TitleManager.I.Equip(_ownedId, _def, _tierIndex, _option);
        }

        TitleAssignPanelUI.OnTitlesChanged?.Invoke(_ownedId);
        _onChanged?.Invoke();
    }
}
