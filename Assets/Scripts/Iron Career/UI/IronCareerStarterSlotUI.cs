// Assets/Scripts/Iron Career/UI/IronCareerStarterSlotUI.cs

using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class IronCareerStarterSlotUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Image icon;
    [SerializeField] private Image typeIcon;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text statsText;

    private static TypeIconLibrary _typeIconLibrary;

    private MonsterDataSO _boundDef;
    public MonsterDataSO BoundDef => _boundDef;

    private void EnsureLibraryLoaded()
    {
        if (_typeIconLibrary == null)
        {
            _typeIconLibrary = Resources.Load<TypeIconLibrary>("TypeIconLibrary");

            if (_typeIconLibrary == null)
                Debug.LogError("[IronCareerStarterSlotUI] Could not load Resources/TypeIconLibrary");
        }
    }

    public void Bind(MonsterDataSO def)
    {
        _boundDef = def;

        if (def == null)
        {
            Clear();
            return;
        }

        EnsureLibraryLoaded();

        // Main icon
        if (icon)
        {
            icon.enabled = def.icon != null;
            icon.sprite = def.icon;
        }

        // Name
        if (nameText)
            nameText.text = string.IsNullOrEmpty(def.displayName)
                ? def.id
                : def.displayName;

        // Type icon
        if (typeIcon && _typeIconLibrary != null)
        {
            var sprite = _typeIconLibrary.GetIcon(def.type);
            typeIcon.enabled = sprite != null;
            typeIcon.sprite = sprite;
        }

        // Stats block (single formatted text)
        if (statsText)
        {
            statsText.text =
                $"HP: {def.baseHP}\n" +
                $"ATK: {def.baseAttack}\n" +
                $"DEF: {def.baseDefense}\n" +
                $"SPD: {def.baseSpeed}";
        }
    }

    private void Clear()
    {
        if (icon)
        {
            icon.enabled = false;
            icon.sprite = null;
        }

        if (typeIcon)
        {
            typeIcon.enabled = false;
            typeIcon.sprite = null;
        }

        if (nameText) nameText.text = "—";
        if (statsText) statsText.text = "";
    }
}