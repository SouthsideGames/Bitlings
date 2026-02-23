using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: IronCareerPostScreenUI
/// Party overview after hire/replace.
/// Shows HP + Title + (single) carried status icon.
/// </summary>
public sealed class IronCareerPostScreenUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;
    [SerializeField] private StatusLibrarySO statusLibrary;

    [Header("Party UI (3)")]
    [SerializeField] private List<Image> icons = new List<Image>(3);
    [SerializeField] private List<TextMeshProUGUI> names = new List<TextMeshProUGUI>(3);
    [SerializeField] private List<TextMeshProUGUI> hp = new List<TextMeshProUGUI>(3);
    [SerializeField] private List<TextMeshProUGUI> titles = new List<TextMeshProUGUI>(3);

    [Header("Carry (single status)")]
    [SerializeField] private Image statusIcon;
    [SerializeField] private TextMeshProUGUI statusName;

    [Header("Controls")]
    [SerializeField] private Button continueButton;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();
        if (continueButton) continueButton.onClick.AddListener(() => manager?.OnPostContinue());
    }

    private void OnDestroy()
    {
        if (continueButton) continueButton.onClick.RemoveAllListeners();
    }

    public void Bind(IReadOnlyList<IronMonster> party, IronFieldStatusSnapshot carry)
    {
        for (int i = 0; i < 3; i++)
        {
            var m = (party != null && i < party.Count) ? party[i] : null;

            if (icons != null && i < icons.Count && icons[i])
                icons[i].sprite = m != null && m.def ? m.def.icon : null;

            if (names != null && i < names.Count && names[i])
                names[i].text = m != null && m.def ? m.def.displayName : "-";

            if (hp != null && i < hp.Count && hp[i])
                hp[i].text = (m != null) ? $"{Mathf.CeilToInt(m.hp)}/{Mathf.CeilToInt(m.maxHp)}" : string.Empty;

            if (titles != null && i < titles.Count && titles[i])
                titles[i].text = (m != null && m.lockedTitle) ? m.lockedTitle.displayName : "";
        }

        var type = carry.type;
        if (statusIcon) statusIcon.sprite = statusLibrary ? statusLibrary.GetIcon(type) : null;
        if (statusName)
        {
            if (type == StatusType.None) statusName.text = "";
            else statusName.text = statusLibrary ? statusLibrary.GetDisplayName(type) : type.ToString();
        }
    }
}
