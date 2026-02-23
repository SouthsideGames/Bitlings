using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: IronCareerReplacePanelUI
/// Choose one party slot to dismiss in order to add the offered hire.
/// </summary>
public sealed class IronCareerReplacePanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("Slot UI (3)")]
    [SerializeField] private List<Button> slotButtons = new List<Button>(3);
    [SerializeField] private List<Image> slotIcons = new List<Image>(3);
    [SerializeField] private List<TextMeshProUGUI> slotNames = new List<TextMeshProUGUI>(3);
    [SerializeField] private List<TextMeshProUGUI> slotHp = new List<TextMeshProUGUI>(3);

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();

        for (int i = 0; i < slotButtons.Count; i++)
        {
            int idx = i;
            if (slotButtons[i])
                slotButtons[i].onClick.AddListener(() => manager?.OnReplaceChosen(idx));
        }
    }

    private void OnDestroy()
    {
        for (int i = 0; i < slotButtons.Count; i++)
            if (slotButtons[i]) slotButtons[i].onClick.RemoveAllListeners();
    }

    public void Bind(IReadOnlyList<IronMonster> party)
    {
        for (int i = 0; i < 3; i++)
        {
            var m = (party != null && i < party.Count) ? party[i] : null;
            if (slotIcons != null && i < slotIcons.Count && slotIcons[i])
                slotIcons[i].sprite = m != null && m.def ? m.def.icon : null;

            if (slotNames != null && i < slotNames.Count && slotNames[i])
                slotNames[i].text = m != null && m.def ? m.def.displayName : "-";

            if (slotHp != null && i < slotHp.Count && slotHp[i])
            {
                if (m != null)
                    slotHp[i].text = $"{Mathf.CeilToInt(m.hp)}/{Mathf.CeilToInt(m.maxHp)}";
                else
                    slotHp[i].text = "";
            }

            if (slotButtons != null && i < slotButtons.Count && slotButtons[i])
                slotButtons[i].interactable = (m != null && m.def != null);
        }
    }
}
