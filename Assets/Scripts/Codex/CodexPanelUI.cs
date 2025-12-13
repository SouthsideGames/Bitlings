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
    ByType,
    ByLevelLowToHigh,
    ByLevelHighToLow,
    ShinyMonsters
}

public class CodexPanelUI : MonoBehaviour
{
    [Header("Team")]
    [SerializeField] private RectTransform teamContent;
    [SerializeField] private GameObject teamCardPrefab;

    [Header("Owned (Box)")]
    [SerializeField] private RectTransform ownedContent;
    [SerializeField] private GameObject ownedListItemPrefab;
    [SerializeField] private TMP_Dropdown sortDropdown;

    [Header("Filters")]
    [SerializeField] private Button capturedOnlyButton;
    [SerializeField] private Button favoritesOnlyButton;

    [Header("Detail")]
    [SerializeField] private MonsterDetailPanelUI detailPanel;

    private int selectedTeamIndex = 0;
    private OwnedSortMode _lastSortMode = OwnedSortMode.ByIdAsc;
    private readonly List<RectTransform> _teamCardRoots = new List<RectTransform>();

    private bool _capturedOnlyFilter = false;
    private bool _favoritesOnlyFilter = false;

    void OnEnable()
    {
        GameEvents.OnTeamChanged      += RefreshAll;
        GameEvents.OnResourcesChanged += RefreshAll;
        GameEvents.MonsterCaptured    += HandleMonsterCaptured;
        GameEvents.FavoritesChanged   += HandleFavoritesChanged;

        // ---------------------
        // SORT DROPDOWN
        // ---------------------
        if (sortDropdown)
        {
            BuildSortDropdownOptions();

            int saved = LoadSortIndexFromJson();
            saved = Mathf.Clamp(saved, 0, (int)OwnedSortMode.ShinyMonsters);

            sortDropdown.onValueChanged.RemoveAllListeners();
            sortDropdown.SetValueWithoutNotify(saved);
            _lastSortMode = (OwnedSortMode)saved;
            sortDropdown.onValueChanged.AddListener(OnSortChanged);
            sortDropdown.RefreshShownValue();
        }

        // ---------------------
        // CAPTURED-ONLY BUTTON (newly gated)
        // ---------------------
        bool captureFilterUnlocked =
            FeatureUnlockManager.I != null &&
            FeatureUnlockManager.I.IsUnlocked(FeatureId.Codex_CaptureOnlyFilter);

        if (capturedOnlyButton)
        {
            capturedOnlyButton.onClick.RemoveAllListeners();
            capturedOnlyButton.gameObject.SetActive(captureFilterUnlocked);

            if (captureFilterUnlocked)
                capturedOnlyButton.onClick.AddListener(OnToggleCapturedOnly);
        }

        // ---------------------
        // FAVORITES-ONLY BUTTON
        // ---------------------
        bool favoritesUnlocked =
            FeatureUnlockManager.I != null &&
            FeatureUnlockManager.I.IsUnlocked(FeatureId.Codex_Favorites);

        if (favoritesOnlyButton)
        {
            favoritesOnlyButton.onClick.RemoveAllListeners();
            favoritesOnlyButton.gameObject.SetActive(favoritesUnlocked);

            if (favoritesUnlocked)
                favoritesOnlyButton.onClick.AddListener(OnToggleFavoritesOnly);
        }

        // Reset filters if locked
        _capturedOnlyFilter  = false;

        if (!favoritesUnlocked)
            _favoritesOnlyFilter = false;

        RefreshAll();
    }

    void OnDisable()
    {
        GameEvents.OnTeamChanged      -= RefreshAll;
        GameEvents.OnResourcesChanged -= RefreshAll;
        GameEvents.MonsterCaptured    -= HandleMonsterCaptured;
        GameEvents.FavoritesChanged   -= HandleFavoritesChanged;

        if (sortDropdown)
            sortDropdown.onValueChanged.RemoveListener(OnSortChanged);

        if (capturedOnlyButton)
            capturedOnlyButton.onClick.RemoveListener(OnToggleCapturedOnly);

        if (favoritesOnlyButton)
            favoritesOnlyButton.onClick.RemoveListener(OnToggleFavoritesOnly);
    }

    private void HandleMonsterCaptured(string monsterId, MonsterType type) => RebuildOwnedOnly();

    private void HandleFavoritesChanged() => RebuildOwnedOnly();

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

    void BuildSortDropdownOptions()
    {
        if (!sortDropdown) return;

        var options = new List<TMP_Dropdown.OptionData>();
        for (int i = 0; i <= (int)OwnedSortMode.ShinyMonsters; i++)
            options.Add(new TMP_Dropdown.OptionData(GetSortLabel((OwnedSortMode)i)));

        sortDropdown.options = options;
        sortDropdown.RefreshShownValue();
    }

    string GetSortLabel(OwnedSortMode mode)
    {
        switch (mode)
        {
            case OwnedSortMode.ByIdAsc:          return "ID ↑";
            case OwnedSortMode.ByNameAZ:         return "Name A → Z";
            case OwnedSortMode.ByNameZA:         return "Name Z → A";
            case OwnedSortMode.ByType:           return "Type";
            case OwnedSortMode.ByLevelLowToHigh: return "Level ↑";
            case OwnedSortMode.ByLevelHighToLow: return "Level ↓";
            case OwnedSortMode.ShinyMonsters:    return "Shiny Only";
            default:                             return mode.ToString();
        }
    }

    void OnSortChanged(int value)
    {
        var mode = (OwnedSortMode)Mathf.Clamp(value, 0, (int)OwnedSortMode.ShinyMonsters);
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

        var scroll = ownedContent ? ownedContent.GetComponentInParent<ScrollRect>() : null;
        float pos  = scroll ? scroll.verticalNormalizedPosition : 1f;

        BuildOwned(owned, team, GetSortMode());

        if (scroll) scroll.verticalNormalizedPosition = pos;
    }

    void RebuildOwnedOnly()
    {
        var data = SaveManager.Data;
        if (data == null)
        {
            Clear(ownedContent);
            return;
        }

        var team  = data.team  ?? new List<OwnedMonsterData>();
        var owned = data.owned ?? new List<OwnedMonsterData>();

        var scroll = ownedContent ? ownedContent.GetComponentInParent<ScrollRect>() : null;
        float pos  = scroll ? scroll.verticalNormalizedPosition : 1f;

        BuildOwned(owned, team, GetSortMode());

        if (scroll) scroll.verticalNormalizedPosition = pos;
    }

    // ─────────────────────────────────────────────
    // Team row
    // ─────────────────────────────────────────────

    void BuildTeam(List<OwnedMonsterData> team)
    {
        Clear(teamContent);
        _teamCardRoots.Clear();

        if (team == null) team = new List<OwnedMonsterData>();

        // Only show filled slots (monsterId present)
        var filled = team
            .Where(m => m != null && !string.IsNullOrEmpty(m.monsterId))
            .ToList();

        for (int i = 0; i < filled.Count; i++)
        {
            var member = filled[i];

            var def = MonsterLibraryLocator.GetById(member.monsterId);

            var go = Instantiate(teamCardPrefab, teamContent);
            var card = go.GetComponent<TeamMonsterCardUI>();
            var rt = go.transform as RectTransform;
            if (rt) _teamCardRoots.Add(rt);

            // If we only generate filled slots, HP bar can always be on.
            // (You can keep SetTeamHpBarActive if you still want it for safety.)
            SetTeamHpBarActive(go, active: true);

            if (card)
            {
                int uiIndex = i; // index in the *visible list*
                card.Setup(
                    data: member,
                    def: def,
                    onClick: _ =>
                    {
                        SelectTeamSlot(uiIndex);
                        OpenTeamDetail(uiIndex, member);
                    },
                    onAnyChanged: RefreshAll
                );
            }
        }

        // If there are no cards, ensure selection doesn't break.
        if (_teamCardRoots.Count == 0)
        {
            selectedTeamIndex = 0;
            return;
        }

        SelectTeamSlot(Mathf.Clamp(selectedTeamIndex, 0, _teamCardRoots.Count - 1));
    }

    // Finds the HP Slider under this team card and toggles it.
    private void SetTeamHpBarActive(GameObject teamCardGO, bool active)
    {
        if (!teamCardGO) return;

        var sliders = teamCardGO.GetComponentsInChildren<Slider>(true);
        if (sliders == null || sliders.Length == 0) return;

        Slider hpSlider = null;

        for (int i = 0; i < sliders.Length; i++)
        {
            var s = sliders[i];
            if (!s) continue;

            var n = s.gameObject.name;
            if (!string.IsNullOrEmpty(n) && n.ToLowerInvariant().Contains("hp"))
            {
                hpSlider = s;
                break;
            }
        }

        if (!hpSlider) hpSlider = sliders[0];

        if (hpSlider)
            hpSlider.gameObject.SetActive(active);
    }

    void SelectTeamSlot(int idx)
    {
        if (_teamCardRoots.Count == 0) return;

        selectedTeamIndex = Mathf.Clamp(idx, 0, _teamCardRoots.Count - 1);

        for (int i = 0; i < _teamCardRoots.Count; i++)
            if (_teamCardRoots[i] != null) _teamCardRoots[i].localScale = Vector3.one;

        if (selectedTeamIndex < _teamCardRoots.Count && _teamCardRoots[selectedTeamIndex] != null)
            LeanTween.scale(_teamCardRoots[selectedTeamIndex], Vector3.one * 1.05f, 0.08f).setLoopPingPong(1);
    }

    private void OpenTeamDetail(int slotIndex, OwnedMonsterData member)
    {
        if (!detailPanel || member == null || string.IsNullOrEmpty(member.monsterId))
            return;

        // NOTE:
        // Since we no longer show fixed "slot 0/1/2" positions, slotIndex here is the visible index.
        // If your detail panel removal depends on the real team slot, you should pass the real index instead.
        detailPanel.ShowTeamMember(slotIndex, member, onRemoved: RefreshAll);
    }

    // ─────────────────────────────────────────────
    // Codex grid: all monsters (captured + unknown)
    // ─────────────────────────────────────────────

    void BuildOwned(List<OwnedMonsterData> owned, List<OwnedMonsterData> team, OwnedSortMode sortMode)
    {
        Clear(ownedContent);
        if (!ownedContent || ownedListItemPrefab == null)
            return;

        var data = SaveManager.Data;
        if (data == null)
            return;

        var allOwned   = data.GetAllOwnedMonsters(includeTeam: true) ?? new List<OwnedMonsterData>();
        var ownedById  = new Dictionary<string, OwnedMonsterData>();
        var shinyById  = new Dictionary<string, OwnedMonsterData>();

        for (int i = 0; i < allOwned.Count; i++)
        {
            var om = allOwned[i];
            if (om == null || string.IsNullOrEmpty(om.monsterId))
                continue;

            if (!ownedById.TryGetValue(om.monsterId, out var existing) || (existing != null && om.level > existing.level))
            {
                ownedById[om.monsterId] = om;
            }

            if (om.isShiny)
            {
                if (!shinyById.TryGetValue(om.monsterId, out var shinyExisting) || (shinyExisting != null && om.level > shinyExisting.level))
                {
                    shinyById[om.monsterId] = om;
                }
            }
        }

        var lib = MonsterLibraryLocator.Lib;
        if (!lib || lib.monsters == null || lib.monsters.Count() == 0)
            return;

        var defs = lib.monsters
            .Where(d => d != null)
            .ToList();

        if (defs.Count == 0)
            return;

        var sortedDefs = SortDefs(defs, sortMode, ownedById, shinyById);

        bool shinyMode = (sortMode == OwnedSortMode.ShinyMonsters);

        foreach (var def in sortedDefs)
        {
            if (!def) continue;

            OwnedMonsterData ownedData;
            bool captured;

            if (shinyMode)
            {
                captured = shinyById.TryGetValue(def.id, out ownedData);
            }
            else
            {
                captured = ownedById.TryGetValue(def.id, out ownedData);
            }

            bool isFavorite = FavoriteService.IsFavorite(def.id);

            if (_capturedOnlyFilter && !captured)
                continue;

            if (_favoritesOnlyFilter)
            {
                bool favoritesFeatureUnlocked = FeatureUnlockManager.I &&
                                                FeatureUnlockManager.I.IsUnlocked(FeatureId.Codex_Favorites);
                if (!favoritesFeatureUnlocked) continue;
                if (!isFavorite) continue;
            }

            var go = Instantiate(ownedListItemPrefab, ownedContent);
            var item = go.GetComponent<OwnedMonsterListItemUI>();
            if (item)
            {
                item.SetupForCodex(
                    def,
                    ownedData,
                    captured,
                    isFavorite,
                    allowDetail: captured,
                    detailPanelOverride: detailPanel
                );
            }
        }
    }

    // ─────────────────────────────────────────────
    // Sorting helpers
    // ─────────────────────────────────────────────

    OwnedSortMode GetSortMode()
    {
        if (!sortDropdown) return _lastSortMode;
        return (OwnedSortMode)Mathf.Clamp(sortDropdown.value, 0, (int)OwnedSortMode.ShinyMonsters);
    }

    static List<MonsterDataSO> SortDefs(
        List<MonsterDataSO> defs,
        OwnedSortMode mode,
        Dictionary<string, OwnedMonsterData> ownedById,
        Dictionary<string, OwnedMonsterData> shinyById)
    {
        IEnumerable<MonsterDataSO> query = defs;

        switch (mode)
        {
            case OwnedSortMode.ByNameAZ:
                query = defs
                    .OrderBy(d => SafeName(d))
                    .ThenBy(d => d ? d.id : string.Empty);
                break;

            case OwnedSortMode.ByNameZA:
                query = defs
                    .OrderByDescending(d => SafeName(d))
                    .ThenBy(d => d ? d.id : string.Empty);
                break;

            case OwnedSortMode.ByType:
                query = defs
                    .OrderBy(d => d ? (int)d.type : int.MaxValue)
                    .ThenBy(d => SafeName(d));
                break;

            case OwnedSortMode.ByLevelLowToHigh:
                query = defs
                    .OrderBy(d => GetOwnedLevel(d, ownedById))
                    .ThenBy(d => SafeName(d));
                break;

            case OwnedSortMode.ByLevelHighToLow:
                query = defs
                    .OrderByDescending(d => GetOwnedLevel(d, ownedById))
                    .ThenBy(d => SafeName(d));
                break;

            case OwnedSortMode.ShinyMonsters:
                query = defs
                    .Where(d => d && shinyById != null && shinyById.ContainsKey(d.id))
                    .OrderByDescending(d => GetOwnedLevel(d, shinyById))
                    .ThenBy(d => SafeName(d));
                break;

            case OwnedSortMode.ByIdAsc:
            default:
                query = defs.OrderBy(d => d ? d.id : string.Empty);
                break;
        }

        return query.ToList();
    }

    static string SafeName(MonsterDataSO d)
    {
        if (!d) return string.Empty;
        if (!string.IsNullOrEmpty(d.displayName)) return d.displayName;
        return d.name ?? string.Empty;
    }

    static int GetOwnedLevel(MonsterDataSO def, Dictionary<string, OwnedMonsterData> ownedDict)
    {
        if (!def || ownedDict == null) return 0;
        if (!ownedDict.TryGetValue(def.id, out var om) || om == null) return 0;
        return Mathf.Max(1, om.level);
    }

    // ─────────────────────────────────────────────
    // Filter button callbacks
    // ─────────────────────────────────────────────

    private void OnToggleCapturedOnly()
    {
        _capturedOnlyFilter = !_capturedOnlyFilter;
        RebuildOwnedOnly();
    }

    private void OnToggleFavoritesOnly()
    {
        _favoritesOnlyFilter = !_favoritesOnlyFilter;
        RebuildOwnedOnly();
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    static void Clear(RectTransform parent)
    {
        if (!parent) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Object.Destroy(parent.GetChild(i).gameObject);
    }
}
