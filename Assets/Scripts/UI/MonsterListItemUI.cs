using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MonsterListItemUI : MonoBehaviour
{
    [SerializeField] Button rowButton;
    [SerializeField] Image icon;
    [SerializeField] TextMeshProUGUI nameText;
    [SerializeField] TextMeshProUGUI levelText;

    [SerializeField] MonsterDetailPanelUI detailPanel;

    OwnedMonsterData _owned;

    public void Bind(OwnedMonsterData owned)
    {
        _owned = owned;

        var def = string.IsNullOrEmpty(owned.monsterId) ? null : MonsterLibraryLocator.GetById(owned.monsterId);
        if (icon)     icon.sprite = def ? def.icon : null;
        if (nameText) nameText.text = def ? def.displayName : "-";
        if (levelText) levelText.text = $"Lv {Mathf.Max(1, owned.level)}";

        if (rowButton)
        {
            rowButton.onClick.RemoveAllListeners();
            rowButton.onClick.AddListener(OnClick);
        }
    }

    void OnClick()
    {
        if (!detailPanel || _owned == null || string.IsNullOrEmpty(_owned.monsterId)) return;
        detailPanel.ShowAssign(_owned);
    }
}
