using UnityEngine;
using UnityEngine.UI; // ScrollRect
using TMPro;
using System.Linq;
using System.Collections.Generic;

public enum OwnedSortMode
{
    ByIdAsc,
    ByNameAZ,
    ByNameZA,
    ByType
}

public class MonstersPanelUI : MonoBehaviour
{
    [Header("Team")]
    [SerializeField] private RectTransform teamContent;
    [SerializeField] private GameObject teamCardPrefab;

    [Header("Owned (Box)")]
    [SerializeField] private RectTransform ownedContent;
    [SerializeField] private GameObject ownedListItemPrefab;
    [SerializeField] private TMP_Dropdown sortDropdown;

    [Header("Detail")]
    [SerializeField] private MonsterDetailPanelUI detailPanel;

    private int selectedTeamIndex = 0;
    private OwnedSortMode _lastSortMode = OwnedSortMode.ByIdAsc;
    private readonly List<RectTransform> _teamCardRoots = new List<RectTransform>();

    void OnEnable()
    {
        GameEvents.OnTeamChanged += RefreshAll;
        GameEvents.OnResourcesChanged += RefreshAll;

        if (sortDropdown)
        {
            BuildSortDropdownOptions();

            int saved = LoadSortIndexFromJson();
            saved = Mathf.Clamp(saved, 0, (int)OwnedSortMode.ByType);

            sortDropdown.onValueChanged.RemoveAllListeners();
            sortDropdown.SetValueWithoutNotify(saved);
            _lastSortMode = (OwnedSortMode)saved;
            sortDropdown.onValueChanged.AddListener(OnSortChanged);
            sortDropdown.RefreshShownValue();
        }

        RefreshAll();
    }

    void OnDisable()
    {
        GameEvents.OnTeamChanged -= RefreshAll;
        GameEvents.OnResourcesChanged -= RefreshAll;

        if (sortDropdown)
            sortDropdown.onValueChanged.RemoveListener(OnSortChanged);
    }

    // ---------- JSON persistence helpers ----------
    int LoadSortIndexFromJson()
    {
        var data = SaveManager.Data;
        if (data == null) return 0;
        if (data.settings == null) data.settings = new SettingsState();
        return data.settings.monstersSortMode;
    }

    void SaveSortIndexToJson(int index)
    {
        var data = SaveManager.Data;
        if (data == null) return;
        if (data.settings == null) data.settings = new SettingsState();
        data.settings.monstersSortMode = index;
        SaveManager.Save();
    }
    // ----------------------------------------------

    // Build dropdown options to match enum order
    void BuildSortDropdownOptions()
    {
        if (!sortDropdown) return;

        var options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i <= (int)OwnedSortMode.ByType; i++)
            options.Add(new TMP_Dropdown.OptionData(GetSortLabel((OwnedSortMode)i)));

        sortDropdown.options = options;
        sortDropdown.RefreshShownValue();
    }

    string GetSortLabel(OwnedSortMode mode)
    {
        switch (mode)
        {
            case OwnedSortMode.ByIdAsc:  return "ID ↑";
            case OwnedSortMode.ByNameAZ: return "Name A → Z";
            case OwnedSortMode.ByNameZA: return "Name Z → A";
            case OwnedSortMode.ByType:   return "Type";
            default:                     return mode.ToString();
        }
    }

    // Called when the dropdown value changes
    void OnSortChanged(int value)
    {
        var mode = (OwnedSortMode)Mathf.Clamp(value, 0, (int)OwnedSortMode.ByType);
        if (mode == _lastSortMode) return;

        _lastSortMode = mode;
        SaveSortIndexToJson(value);
        RebuildOwnedOnly();
    }

    public void RefreshAll()
    {
        var data = SaveManager.Data;
        if (data == null)
        {
            Clear(teamContent);
            Clear(ownedContent);
            return;
        }

        var team  = data.team  ?? new List<OwnedMonsterData>();
        var owned = data.owned ?? new List<OwnedMonsterData>();

        BuildTeam(team);

        // preserve Owned scroll across full refreshes
        var scroll = ownedContent ? ownedContent.GetComponentInParent<ScrollRect>() : null;
        float pos = scroll ? scroll.verticalNormalizedPosition : 1f;

        BuildOwned(owned, team, GetSortMode());

        if (scroll) scroll.verticalNormalizedPosition = pos;
    }

    void RebuildOwnedOnly()
    {
        var data = SaveManager.Data;
        if (data == null) { Clear(ownedContent); return; }

        var team  = data.team  ?? new List<OwnedMonsterData>();
        var owned = data.owned ?? new List<OwnedMonsterData>();

        var scroll = ownedContent ? ownedContent.GetComponentInParent<ScrollRect>() : null;
        float pos = scroll ? scroll.verticalNormalizedPosition : 1f;

        BuildOwned(owned, team, GetSortMode());

        if (scroll) scroll.verticalNormalizedPosition = pos;
    }

    void BuildTeam(List<OwnedMonsterData> team)
    {
        Clear(teamContent);
        _teamCardRoots.Clear();

        for (int i = 0; i < 3; i++)
        {
            var member = (i < team.Count) ? team[i] : null;
            var def = (member != null && !string.IsNullOrEmpty(member.monsterId))
                ? MonsterLibraryLocator.GetById(member.monsterId)
                : null;

            var go = Instantiate(teamCardPrefab, teamContent);
            var card = go.GetComponent<TeamMonsterCardUI>();
            var rt = go.transform as RectTransform;
            if (rt) _teamCardRoots.Add(rt);

            if (card)
            {
                int slotIndex = i;
               card.Setup(
                    data: member,
                    def: def,
                    onClick: _ =>
                    {
                        SelectTeamSlot(slotIndex);
                        if (member != null && !string.IsNullOrEmpty(member.monsterId))
                            OpenTeamDetail(slotIndex, member);
                    },
                    onAnyChanged: RefreshAll
                );

            }
        }

        SelectTeamSlot(Mathf.Clamp(selectedTeamIndex, 0, 2));
    }

    void BuildOwned(List<OwnedMonsterData> owned, List<OwnedMonsterData> team, OwnedSortMode sortMode)
    {
        Clear(ownedContent);
        if (owned == null) return;

        var teamKeys = new HashSet<string>((team ?? new List<OwnedMonsterData>()).Select(SafeKey));
        var list = owned.Where(o => o != null && !string.IsNullOrEmpty(o.monsterId) && !teamKeys.Contains(SafeKey(o)))
                        .ToList();

        var sorted = SortOwned(list, sortMode);

        foreach (var o in sorted)
        {
            var def = MonsterLibraryLocator.GetById(o.monsterId);
            var go = Instantiate(ownedListItemPrefab, ownedContent);
            var item = go.GetComponent<OwnedMonsterListItemUI>();
            if (item)
            {
                item.Setup(o, def);
            }
        }
    }

    void OpenDetailFromOwned(OwnedMonsterData owned)
    {
        if (!detailPanel || owned == null || string.IsNullOrEmpty(owned.monsterId)) return;

        var def = MonsterLibraryLocator.GetById(owned.monsterId);

        detailPanel.Show(
            def,
            _ =>
            {
                AddOrReplaceInTeam(owned);
                detailPanel.Hide();
            }
        );
    }

    void AddOrReplaceInTeam(OwnedMonsterData pick)
    {
        var team = SaveManager.Data.team;
        if (team == null) return;

        while (team.Count < 3) team.Add(new OwnedMonsterData());

        int empty = team.FindIndex(t => t == null || string.IsNullOrEmpty(t.monsterId));
        int slot = (empty >= 0) ? empty : Mathf.Clamp(selectedTeamIndex, 0, 2);

        team[slot] = new OwnedMonsterData
        {
            monsterId = pick.monsterId,
            level = pick.level,
            currentHP = pick.currentHP,
            currentXP = pick.currentXP
        };

        SaveManager.Save();
        RefreshAll();
    }

    void SelectTeamSlot(int idx)
    {
        selectedTeamIndex = Mathf.Clamp(idx, 0, 2);

        for (int i = 0; i < _teamCardRoots.Count; i++)
            if (_teamCardRoots[i] != null) _teamCardRoots[i].localScale = Vector3.one;

        if (selectedTeamIndex < _teamCardRoots.Count && _teamCardRoots[selectedTeamIndex] != null)
            LeanTween.scale(_teamCardRoots[selectedTeamIndex], Vector3.one * 1.05f, 0.08f).setLoopPingPong(1);
    }

    OwnedSortMode GetSortMode()
    {
        if (!sortDropdown) return OwnedSortMode.ByIdAsc;
        return (OwnedSortMode)Mathf.Clamp(sortDropdown.value, 0, (int)OwnedSortMode.ByType);
    }

    List<OwnedMonsterData> SortOwned(List<OwnedMonsterData> list, OwnedSortMode mode)
    {
        switch (mode)
        {
            case OwnedSortMode.ByNameAZ:
                return list.OrderBy(o => GetNameKey(o)).ThenBy(o => o.monsterId).ToList();
            case OwnedSortMode.ByNameZA:
                return list.OrderByDescending(o => GetNameKey(o)).ThenBy(o => o.monsterId).ToList();
            case OwnedSortMode.ByType:
                return list.OrderBy(o => GetTypeKey(o)).ThenBy(o => GetNameKey(o)).ToList();
            case OwnedSortMode.ByIdAsc:
            default:
                return list.OrderBy(o => o.monsterId).ToList();
        }
    }

    string GetNameKey(OwnedMonsterData o)
    {
        var def = MonsterLibraryLocator.GetById(o.monsterId);
        return def ? (string.IsNullOrEmpty(def.displayName) ? def.name : def.displayName) : "~";
    }

    string GetTypeKey(OwnedMonsterData o)
    {
        var def = MonsterLibraryLocator.GetById(o.monsterId);
        return def ? def.type.ToString() : "~";
    }

    static void Clear(RectTransform parent)
    {
        if (!parent) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.Destroy(parent.GetChild(i).gameObject);
    }

    static string SafeKey(OwnedMonsterData d)
    {
        if (d == null) return "";
        return (d.monsterId ?? "") + "#" + d.level;
    }

    private void OpenTeamDetail(int slotIndex, OwnedMonsterData member)
    {
        if (!detailPanel || member == null || string.IsNullOrEmpty(member.monsterId))
            return;

        detailPanel.ShowTeamMember(slotIndex, member, onRemoved: RefreshAll);
    }
}
