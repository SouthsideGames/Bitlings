using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TitleButtonUI : MonoBehaviour
{
    [Header("Wire These In Inspector")]
    [SerializeField] private Button button;
    [SerializeField] private TMP_Text label;
    [SerializeField] private TitleAssignPanelUI titleAssignPanel;

    // Current monster context (provided by Monster Detail)
    private string _ownedMonsterId;     // OwnedMonsterData.monsterId (your save-owned GUID/ID)
    private MonsterDataSO _monsterDef;  // Definition
    private int _level;

    private void Reset()
    {
        if (!button) button = GetComponent<Button>();
        if (!label)  label  = GetComponentInChildren<TMP_Text>(true);
    }

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(OpenPanel);
    }

    private void OnEnable()
    {
        RefreshLabel();
    }

    /// <summary>
    /// Bind this button to the monster shown in Monster Detail.
    /// Call this whenever the detail panel changes selection.
    /// </summary>
    public void Bind(string ownedMonsterId, MonsterDataSO def, int level)
    {
        _ownedMonsterId = ownedMonsterId;
        _monsterDef     = def;
        _level          = Mathf.Max(1, level);
        RefreshLabel();
    }

    private void RefreshLabel()
    {
        if (label == null)
            return;

        if (string.IsNullOrEmpty(_ownedMonsterId) || _monsterDef == null)
        {
            label.text = "Titles";
            return;
        }

        List<TitleSO> equipped = TitleManager.I.GetEquippedList(_ownedMonsterId, _monsterDef, _level);

        if (equipped == null || equipped.Count == 0)
        {
            label.text = "Set Title";
            return;
        }

        var sb = new StringBuilder();
        int show = Mathf.Min(3, equipped.Count);
        for (int i = 0; i < show; i++)
        {
            if (i > 0) sb.Append(" • ");
            sb.Append(equipped[i] ? equipped[i].displayName : "Unknown");
        }
        if (equipped.Count > show)
        {
            sb.Append(" +").Append(equipped.Count - show);
        }

        label.text = TrimToLength(sb.ToString(), 32);
    }

    private static string TrimToLength(string s, int max)
    {
        if (string.IsNullOrEmpty(s)) return s;
        if (s.Length <= max) return s;
        return s.Substring(0, Mathf.Max(0, max - 1)) + "…";
    }

    private void OpenPanel()
    {
        if (titleAssignPanel == null || string.IsNullOrEmpty(_ownedMonsterId) || _monsterDef == null)
            return;

        // ✅ Call the 3-arg Open your panel defines
        titleAssignPanel.Open(_ownedMonsterId, _monsterDef, _level);
    }

    public void ForceRefresh() => RefreshLabel();
}
