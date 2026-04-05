using UnityEngine;
using UnityEngine.UI; // ScrollRect
using TMPro;
using System;
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
    PremiumMonsters,
    BitlingPack
}

public enum DirectoryViewMode
{
    All = 0,
    Discovered = 1,
    Captured = 2
}

public class DirectoryPanelUI : MonoBehaviour
{
    [Header("Team")]
    [SerializeField] private RectTransform teamContent;
    [SerializeField] private GameObject teamCardPrefab;

    [Header("Idle Loadout")]
    [SerializeField] private RectTransform idleTeamContent;
    [SerializeField] private Button idleLoadoutToggleButton;
    [SerializeField] private TextMeshProUGUI idleLoadoutToggleText;
    [SerializeField] private CanvasGroup activeTeamRowGroup;
    [SerializeField] private CanvasGroup idleTeamRowGroup;

    [Header("Owned (Box)")]
    [SerializeField] private RectTransform ownedContent;
    [SerializeField] private GameObject ownedListItemPrefab;

    [Header("Dropdowns")]
    [SerializeField] private TMP_Dropdown sortDropdown;

    [Header("Filters")]
    [SerializeField] private Button capturedOnlyButton;
    [SerializeField] private Button favoritesOnlyButton;

    [Header("Detail")]
    [SerializeField] private MonsterDetailPanelUI detailPanel;

    private int selectedTeamIndex = 0;
    private int selectedIdleTeamIndex = 0;
    private OwnedSortMode _lastSortMode = OwnedSortMode.ByIdAsc;
    private readonly List<RectTransform> _teamCardRoots = new List<RectTransform>();
    // Maps visible team card index -> actual SaveManager.Data.team slot index.
    private readonly List<int> _teamSlotIndexByVisible = new List<int>();
    private readonly List<RectTransform> _idleTeamCardRoots = new List<RectTransform>();
    private readonly List<int> _idleTeamSlotIndexByVisible = new List<int>();

    private bool _showingIdleLoadout;

    private bool _capturedOnlyFilter = false;
    private bool _favoritesOnlyFilter = false;

    private DirectoryViewMode _viewMode = DirectoryViewMode.All;

    // cache last visible directory defs (post-filter) for swipe browsing
    private List<MonsterDataSO> _lastVisibleDirectoryDefs = new List<MonsterDataSO>();

    void OnEnable()
    {
        GameEvents.OnTeamChanged += RefreshAll;
        GameEvents.OnResourcesChanged += RefreshAll;
        GameEvents.MonsterCaptured += HandleMonsterCaptured;
        GameEvents.FavoritesChanged += HandleFavoritesChanged;

        GameEvents.DirectoryOpened?.Invoke();

        // ---------------------
        // SORT DROPDOWN
        // ---------------------
        if (sortDropdown)
        {
            BuildSortDropdownOptions();

            int saved = LoadSortIndexFromJson();
            saved = Mathf.Clamp(saved, 0, (int)OwnedSortMode.BitlingPack);

            sortDropdown.onValueChanged.RemoveAllListeners();
            sortDropdown.SetValueWithoutNotify(saved);
            _lastSortMode = (OwnedSortMode)saved;
            sortDropdown.onValueChanged.AddListener(OnSortChanged);
            sortDropdown.RefreshShownValue();
        }

        // ---------------------
        // CAPTURED-ONLY BUTTON (gated)
        // ---------------------
        bool captureFilterUnlocked =
            FeatureUnlockManager.I != null &&
            FeatureUnlockManager.I.IsUnlocked(FeatureId.Directory_CaptureOnlyFilter);

        if (capturedOnlyButton)
        {
            capturedOnlyButton.onClick.RemoveAllListeners();
            capturedOnlyButton.gameObject.SetActive(captureFilterUnlocked);

            if (captureFilterUnlocked)
                capturedOnlyButton.onClick.AddListener(OnToggleCapturedOnly);
        }

        // ---------------------
        // FAVORITES-ONLY BUTTON (gated)
        // ---------------------
        bool favoritesUnlocked =
            FeatureUnlockManager.I != null &&
            FeatureUnlockManager.I.IsUnlocked(FeatureId.Directory_Favorites);

        if (favoritesOnlyButton)
        {
            favoritesOnlyButton.onClick.RemoveAllListeners();
            favoritesOnlyButton.gameObject.SetActive(favoritesUnlocked);

            if (favoritesUnlocked)
                favoritesOnlyButton.onClick.AddListener(OnToggleFavoritesOnly);
        }

        // Reset filters if locked
        _capturedOnlyFilter = false;
        if (!favoritesUnlocked)
            _favoritesOnlyFilter = false;

        SetupIdleLoadoutToggle();
        SetLoadoutEditingMode(_showingIdleLoadout, animate: false);

        RefreshAll();
    }

    void OnDisable()
    {
        GameEvents.OnTeamChanged -= RefreshAll;
        GameEvents.OnResourcesChanged -= RefreshAll;
        GameEvents.MonsterCaptured -= HandleMonsterCaptured;
        GameEvents.FavoritesChanged -= HandleFavoritesChanged;

        if (sortDropdown)
            sortDropdown.onValueChanged.RemoveListener(OnSortChanged);

        if (capturedOnlyButton)
            capturedOnlyButton.onClick.RemoveListener(OnToggleCapturedOnly);

        if (favoritesOnlyButton)
            favoritesOnlyButton.onClick.RemoveListener(OnToggleFavoritesOnly);

        if (idleLoadoutToggleButton)
            idleLoadoutToggleButton.onClick.RemoveListener(OnToggleIdleLoadoutEditMode);

        IdleLoadoutManager.SetEditingIdleTeam(false);
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
        for (int i = 0; i <= (int)OwnedSortMode.BitlingPack; i++)
            options.Add(new TMP_Dropdown.OptionData(GetSortLabel((OwnedSortMode)i)));

        sortDropdown.options = options;
        sortDropdown.RefreshShownValue();
    }

    string GetSortLabel(OwnedSortMode mode)
    {
        switch (mode)
        {
            case OwnedSortMode.ByIdAsc: return "ID ↑";
            case OwnedSortMode.ByNameAZ: return "Name A → Z";
            case OwnedSortMode.ByNameZA: return "Name Z → A";
            case OwnedSortMode.ByType: return "Type";
            case OwnedSortMode.ByLevelLowToHigh: return "Level ↑";
            case OwnedSortMode.ByLevelHighToLow: return "Level ↓";
            case OwnedSortMode.PremiumMonsters: return "Premium First";
            case OwnedSortMode.BitlingPack: return "Bitling Pack";
            default: return mode.ToString();
        }
    }

    void OnSortChanged(int value)
    {
        var mode = (OwnedSortMode)Mathf.Clamp(value, 0, (int)OwnedSortMode.BitlingPack);
        if (mode == _lastSortMode) return;

        _lastSortMode = mode;
        SaveSortIndexToJson(value);
        RebuildOwnedOnly();
    }

    void OnViewChanged(int value)
    {
        _viewMode = (DirectoryViewMode)Mathf.Clamp(value, 0, (int)DirectoryViewMode.Captured);
        RebuildOwnedOnly();
    }

    public void RefreshAll()
    {
        var data = SaveManager.Data;
        if (data == null)
        {
            ClearAllChildren(teamContent);
            ClearAllChildren(idleTeamContent);
            ClearOwnedListItemsOnly(ownedContent);
            _lastVisibleDirectoryDefs = new List<MonsterDataSO>();
            return;
        }

        var team = data.team ?? new List<OwnedMonsterData>();
        var owned = data.owned ?? new List<OwnedMonsterData>();

        BuildTeam(team);
        BuildIdleTeam(data);

        var scroll = ownedContent ? ownedContent.GetComponentInParent<ScrollRect>() : null;
        float pos = scroll ? scroll.verticalNormalizedPosition : 1f;

        BuildOwned(owned, team, GetSortMode());

        if (scroll) scroll.verticalNormalizedPosition = pos;
    }

    void RebuildOwnedOnly()
    {
        var data = SaveManager.Data;
        if (data == null)
        {
            ClearOwnedListItemsOnly(ownedContent);
            _lastVisibleDirectoryDefs = new List<MonsterDataSO>();
            return;
        }

        var team = data.team ?? new List<OwnedMonsterData>();
        var owned = data.owned ?? new List<OwnedMonsterData>();

        var scroll = ownedContent ? ownedContent.GetComponentInParent<ScrollRect>() : null;
        float pos = scroll ? scroll.verticalNormalizedPosition : 1f;

        BuildOwned(owned, team, GetSortMode());

        if (scroll) scroll.verticalNormalizedPosition = pos;
    }

    // ─────────────────────────────────────────────
    // Team row
    // ─────────────────────────────────────────────

    private void SetupIdleLoadoutToggle()
    {
        if (!idleLoadoutToggleButton) return;

        bool idleUnlocked = FeatureUnlockManager.I != null &&
                            FeatureUnlockManager.I.IsUnlocked(FeatureId.IdleBattle_Basic);

        idleLoadoutToggleButton.gameObject.SetActive(idleUnlocked);
        idleLoadoutToggleButton.onClick.RemoveAllListeners();

        if (idleUnlocked)
            idleLoadoutToggleButton.onClick.AddListener(OnToggleIdleLoadoutEditMode);
        else
            _showingIdleLoadout = false;

        UpdateIdleToggleLabel();
    }

    private void OnToggleIdleLoadoutEditMode()
    {
        SetLoadoutEditingMode(!_showingIdleLoadout, animate: true);
    }

    private void SetLoadoutEditingMode(bool showIdle, bool animate)
    {
        _showingIdleLoadout = showIdle;
        IdleLoadoutManager.SetEditingIdleTeam(_showingIdleLoadout);
        UpdateIdleToggleLabel();

        SetCanvasGroupVisible(activeTeamRowGroup, !_showingIdleLoadout, animate);
        SetCanvasGroupVisible(idleTeamRowGroup, _showingIdleLoadout, animate);
    }

    private void SetCanvasGroupVisible(CanvasGroup group, bool visible, bool animate)
    {
        if (!group) return;

        group.interactable = visible;
        group.blocksRaycasts = visible;

        if (animate)
            LeanTween.alphaCanvas(group, visible ? 1f : 0f, 0.18f);
        else
            group.alpha = visible ? 1f : 0f;
    }

    private void UpdateIdleToggleLabel()
    {
        if (!idleLoadoutToggleText) return;
        idleLoadoutToggleText.text = _showingIdleLoadout ? "ACTIVE" : "IDLE";
    }

    void BuildTeam(List<OwnedMonsterData> team)
    {
        ClearAllChildren(teamContent);
        _teamCardRoots.Clear();
        _teamSlotIndexByVisible.Clear();

        if (team == null) team = new List<OwnedMonsterData>();

        // Only show filled slots (monsterId present) BUT preserve the actual team slot index.
        for (int teamSlot = 0; teamSlot < team.Count; teamSlot++)
        {
            var member = team[teamSlot];
            if (member == null || string.IsNullOrEmpty(member.monsterId))
                continue;

            var memberLocal = member;
            int teamSlotLocal = teamSlot;
            var def = MonsterLibraryLocator.GetById(memberLocal.monsterId);

            var go = Instantiate(teamCardPrefab, teamContent);
            var card = go.GetComponent<TeamMonsterCardUI>();
            var rt = go.transform as RectTransform;
            if (rt) _teamCardRoots.Add(rt);
            _teamSlotIndexByVisible.Add(teamSlotLocal);

            // If we only generate filled slots, HP bar can always be on.
            SetTeamHpBarActive(go, active: true);

            if (card)
            {
                int visibleIndex = _teamCardRoots.Count - 1;

                var healCtrl = go.GetComponent<HealButtonController>();
                if (!healCtrl) healCtrl = go.GetComponentInChildren<HealButtonController>(true);
                if (healCtrl)
                {
                    healCtrl.BindTeamIndex(teamSlotLocal);
                    healCtrl.OnBeforeHeal = () => SelectTeamSlot(visibleIndex);
                }

                card.Setup(
                    data: memberLocal,
                    def: def,
                    onClick: _ =>
                    {
                        IdleLoadoutManager.SetEditingIdleTeam(false);
                        SelectTeamSlot(visibleIndex);
                        OpenTeamDetail(teamSlotLocal, memberLocal);
                    },
                    onAnyChanged: RefreshAll
                );
            }
        }

        if (_teamCardRoots.Count == 0)
        {
            selectedTeamIndex = 0;
            return;
        }

        SelectTeamSlot(Mathf.Clamp(selectedTeamIndex, 0, _teamCardRoots.Count - 1));
    }

    void BuildIdleTeam(PlayerManager data)
    {
        ClearAllChildren(idleTeamContent);
        _idleTeamCardRoots.Clear();
        _idleTeamSlotIndexByVisible.Clear();

        if (data == null || idleTeamContent == null)
            return;

        var idleUids = IdleLoadoutManager.GetIdleTeamOwnedUids();
        if (idleUids == null || idleUids.Count == 0)
            return;

        for (int idleSlot = 0; idleSlot < Mathf.Min(3, idleUids.Count); idleSlot++)
        {
            string uid = idleUids[idleSlot];
            if (string.IsNullOrEmpty(uid))
                continue;

            var member = FindOwnedByUid(data, uid);
            if (member == null || string.IsNullOrEmpty(member.monsterId))
                continue;

            var def = MonsterLibraryLocator.GetById(member.monsterId);
            int idleSlotLocal = idleSlot;
            var memberLocal = member;

            var go = Instantiate(teamCardPrefab, idleTeamContent);
            var card = go.GetComponent<TeamMonsterCardUI>();
            var rt = go.transform as RectTransform;
            if (rt) _idleTeamCardRoots.Add(rt);
            _idleTeamSlotIndexByVisible.Add(idleSlotLocal);

            SetTeamHpBarActive(go, active: true);

            if (card)
            {
                int visibleIndex = _idleTeamCardRoots.Count - 1;

                card.Setup(
                    data: memberLocal,
                    def: def,
                    onClick: _ =>
                    {
                        IdleLoadoutManager.SetEditingIdleTeam(true);
                        SelectIdleTeamSlot(visibleIndex);
                        OpenTeamDetail(idleSlotLocal, memberLocal);
                    },
                    onAnyChanged: RefreshAll
                );
            }
        }

        if (_idleTeamCardRoots.Count == 0)
        {
            selectedIdleTeamIndex = 0;
            return;
        }

        SelectIdleTeamSlot(Mathf.Clamp(selectedIdleTeamIndex, 0, _idleTeamCardRoots.Count - 1));
    }

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

    void SelectIdleTeamSlot(int idx)
    {
        if (_idleTeamCardRoots.Count == 0) return;

        selectedIdleTeamIndex = Mathf.Clamp(idx, 0, _idleTeamCardRoots.Count - 1);

        for (int i = 0; i < _idleTeamCardRoots.Count; i++)
            if (_idleTeamCardRoots[i] != null) _idleTeamCardRoots[i].localScale = Vector3.one;

        if (selectedIdleTeamIndex < _idleTeamCardRoots.Count && _idleTeamCardRoots[selectedIdleTeamIndex] != null)
            LeanTween.scale(_idleTeamCardRoots[selectedIdleTeamIndex], Vector3.one * 1.05f, 0.08f).setLoopPingPong(1);
    }

    private void OpenTeamDetail(int slotIndex, OwnedMonsterData member)
    {
        if (!detailPanel || member == null || string.IsNullOrEmpty(member.monsterId))
            return;

        detailPanel.ShowTeamMember(slotIndex, member, onRemoved: RefreshAll);
    }

    private static OwnedMonsterData FindOwnedByUid(PlayerManager data, string ownedUid)
    {
        if (data == null || string.IsNullOrEmpty(ownedUid))
            return null;

        if (data.owned != null)
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var o = data.owned[i];
                if (o == null || string.IsNullOrEmpty(o.ownedUID)) continue;
                if (o.ownedUID == ownedUid) return o;
            }
        }

        if (data.team != null)
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var t = data.team[i];
                if (t == null || string.IsNullOrEmpty(t.ownedUID)) continue;
                if (t.ownedUID == ownedUid) return t;
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────
    // Directory grid
    // ─────────────────────────────────────────────

    void BuildOwned(List<OwnedMonsterData> owned, List<OwnedMonsterData> team, OwnedSortMode sortMode)
    {
        ClearOwnedListItemsOnly(ownedContent);

        _lastVisibleDirectoryDefs = new List<MonsterDataSO>();

        if (!ownedContent || ownedListItemPrefab == null)
            return;

        var data = SaveManager.Data;
        if (data == null)
            return;

        // Build "best owned per monsterId" dictionaries (normal + premium).
        var ownedById = new Dictionary<string, OwnedMonsterData>(StringComparer.Ordinal);
        var normalById = new Dictionary<string, OwnedMonsterData>(StringComparer.Ordinal);
        var premiumById = new Dictionary<string, OwnedMonsterData>(StringComparer.Ordinal);


        // Team-aware variant preference: if a monster is on the active team, we want the Directory row
        // to reflect that exact instance (especially Premium) so the UI is consistent.
        var teamNormalById = new Dictionary<string, OwnedMonsterData>(StringComparer.Ordinal);
        var teamPremiumById  = new Dictionary<string, OwnedMonsterData>(StringComparer.Ordinal);

        void Consider(OwnedMonsterData om)
        {
            if (om == null || string.IsNullOrEmpty(om.monsterId)) return;
            bool premium = om.isPremium || om.premiumTier > 0;

            if (!ownedById.TryGetValue(om.monsterId, out var existingAny) || (existingAny != null && om.level > existingAny.level))
                ownedById[om.monsterId] = om;

            if (premium)
            {
                if (!premiumById.TryGetValue(om.monsterId, out var existingPremium) || (existingPremium != null && om.level > existingPremium.level))
                    premiumById[om.monsterId] = om;
            }
            else
            {
                if (!normalById.TryGetValue(om.monsterId, out var existingNormal) || (existingNormal != null && om.level > existingNormal.level))
                    normalById[om.monsterId] = om;
            }
        }

        var ownedOnly = data.GetAllOwnedMonsters(includeTeam: false) ?? new List<OwnedMonsterData>();
        for (int i = 0; i < ownedOnly.Count; i++) Consider(ownedOnly[i]);

        if (data.team != null)
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var t = data.team[i];
                if (t == null) continue;
                if (string.IsNullOrEmpty(t.ownedUID)) continue; // ignore placeholders
	                if (string.IsNullOrEmpty(t.monsterId)) continue; // defensive: avoid null key in dictionaries

                Consider(t);

                bool premium = t.isPremium || t.premiumTier > 0;
                if (premium)
                {
                    if (!teamPremiumById.TryGetValue(t.monsterId, out var existing) || (existing != null && t.level > existing.level))
                        teamPremiumById[t.monsterId] = t;
                }
                else
                {
                    if (!teamNormalById.TryGetValue(t.monsterId, out var existing) || (existing != null && t.level > existing.level))
                        teamNormalById[t.monsterId] = t;
                }
            }
        }

        var discoveredByPack = BuildDiscoveredMonsterIdSetFromUnlockedPacks(data);

        var defs = BuildAllDirectoryDefsFromLibraryAndUnlockedPacks(data);
        if (defs == null || defs.Count == 0)
            return;

        if (sortMode == OwnedSortMode.BitlingPack)
        {
            defs = defs
                .Where(d => d != null &&
                            !string.IsNullOrEmpty(d.id) &&
                            MonsterPackTagCache.IsInUnlockedPack(d.id, data.unlockedPacks))
                .ToList();

            if (defs.Count == 0)
                return;
        }

        var sortedDefs = SortDefs(defs, sortMode, ownedById, premiumById);

        var spawnedItems = new List<OwnedMonsterListItemUI>();

        foreach (var def in sortedDefs)
        {
            if (!def) continue;

            OwnedMonsterData ownedData = null;
            OwnedMonsterData normalData = null;
            OwnedMonsterData premiumData = null;

            bool capturedReal = ownedById.TryGetValue(def.id, out ownedData);
            normalById.TryGetValue(def.id, out normalData);
            premiumById.TryGetValue(def.id, out premiumData);

            OwnedMonsterData displayOwned = ownedData;

            // If this monster is currently on the team, force the Directory row to reflect that team instance.
            // This keeps icon + name consistent between the Team strip and the Owned list (especially for Premium).
            if (teamPremiumById.TryGetValue(def.id, out var teamPremium) && teamPremium != null)
            {
                displayOwned = teamPremium;
            }
            else if (teamNormalById.TryGetValue(def.id, out var teamNormal) && teamNormal != null)
            {
                displayOwned = teamNormal;
            }
            else
            {
                if (premiumData != null && normalData == null)
                {
                    displayOwned = premiumData;
                }
                else if (premiumData != null && normalData != null)
                {
                    bool preferPremium = (data.settings != null &&
                                       data.settings.directoryPreferPremiumIds != null &&
                                       data.settings.directoryPreferPremiumIds != null &&
                                       data.settings.directoryPreferPremiumIds.Contains(def.id));
                    displayOwned = preferPremium ? premiumData : normalData;
                }
                else if (normalData != null)
                {
                    displayOwned = normalData;
                }
            }

            // discovered = reveal in directory even if not owned yet
            bool discovered =
                capturedReal ||
                (discoveredByPack != null && discoveredByPack.Contains(def.id)) ||
                SaveManager.IsDiscovered(def.id);

            if (_viewMode == DirectoryViewMode.Captured && !capturedReal)
                continue;

            if (_viewMode == DirectoryViewMode.Discovered && !discovered)
                continue;

            bool isFavorite = FavoriteService.IsFavorite(def.id);

            if (_capturedOnlyFilter && !capturedReal)
                continue;

            if (_favoritesOnlyFilter)
            {
                bool favoritesFeatureUnlocked = FeatureUnlockManager.I &&
                                                FeatureUnlockManager.I.IsUnlocked(FeatureId.Directory_Favorites);
                if (!favoritesFeatureUnlocked) continue;
                if (!isFavorite) continue;
            }

            _lastVisibleDirectoryDefs.Add(def);

            var go = Instantiate(ownedListItemPrefab, ownedContent);
            var item = go.GetComponent<OwnedMonsterListItemUI>();
            if (item)
            {
                spawnedItems.Add(item);

                item.SetupForDirectory(
                    def,
                    displayOwned,
                    captured: discovered,
                    isFavorite: isFavorite,
                    allowDetail: discovered,
                    detailPanelOverride: detailPanel
                );
            }
        }

        for (int i = 0; i < spawnedItems.Count; i++)
        {
            if (spawnedItems[i] != null)
                spawnedItems[i].SetDirectoryBrowseContext(_lastVisibleDirectoryDefs);
        }
    }

    private List<MonsterDataSO> BuildAllDirectoryDefsFromLibraryAndUnlockedPacks(PlayerManager data)
    {
        var result = new List<MonsterDataSO>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        var lib = MonsterLibraryLocator.Lib;
        if (lib && lib.monsters != null)
        {
            foreach (var d in lib.monsters)
            {
                if (!d || string.IsNullOrEmpty(d.id)) continue;
                if (seen.Add(d.id)) result.Add(d);
            }
        }

        if (data == null || data.unlockedPacks == null || data.unlockedPacks.Count == 0)
            return result;

        var packLib = MonsterPackLibraryLocator.Lib;
        if (!packLib) return result;

        packLib.Warmup();

        for (int i = 0; i < data.unlockedPacks.Count; i++)
        {
            var packId = data.unlockedPacks[i];
            if (string.IsNullOrEmpty(packId)) continue;

            var pack = packLib.Get(packId);
            if (!pack || pack.monsters == null) continue;

            for (int m = 0; m < pack.monsters.Count; m++)
            {
                var def = pack.monsters[m];
                if (!def || string.IsNullOrEmpty(def.id)) continue;
                if (seen.Add(def.id)) result.Add(def);
            }
        }

        return result;
    }

    private HashSet<string> BuildDiscoveredMonsterIdSetFromUnlockedPacks(PlayerManager data)
    {
        if (data == null) return null;
        if (data.unlockedPacks == null || data.unlockedPacks.Count == 0) return null;

        var packLib = MonsterPackLibraryLocator.Lib;
        if (!packLib) return null;

        packLib.Warmup();

        var set = new HashSet<string>(StringComparer.Ordinal);

        for (int i = 0; i < data.unlockedPacks.Count; i++)
        {
            var packId = data.unlockedPacks[i];
            if (string.IsNullOrEmpty(packId)) continue;

            var pack = packLib.Get(packId);
            if (!pack || pack.monsters == null) continue;

            for (int m = 0; m < pack.monsters.Count; m++)
            {
                var def = pack.monsters[m];
                if (!def || string.IsNullOrEmpty(def.id)) continue;
                set.Add(def.id);
            }
        }

        return set;
    }

    // ─────────────────────────────────────────────
    // Sorting helpers
    // ─────────────────────────────────────────────

    OwnedSortMode GetSortMode()
    {
        if (!sortDropdown) return _lastSortMode;
        return (OwnedSortMode)Mathf.Clamp(sortDropdown.value, 0, (int)OwnedSortMode.BitlingPack);
    }

    static List<MonsterDataSO> SortDefs(
        List<MonsterDataSO> defs,
        OwnedSortMode mode,
        Dictionary<string, OwnedMonsterData> ownedById,
        Dictionary<string, OwnedMonsterData> premiumById)
    {
        IEnumerable<MonsterDataSO> query = defs;

        switch (mode)
        {
            case OwnedSortMode.BitlingPack:
                // In this mode we already FILTERED to pack monsters only.
                // Now sort by PackId then MonsterId.
                query = defs
                    .OrderBy(d => MonsterPackTagCache.GetPackId(d ? d.id : null) ?? string.Empty)
                    .ThenBy(d => d ? d.id : string.Empty);
                break;

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

            case OwnedSortMode.PremiumMonsters:
                query = defs
                    .OrderByDescending(d => d && premiumById != null && premiumById.ContainsKey(d.id))
                    .ThenByDescending(d => GetOwnedLevel(d, ownedById))
                    .ThenBy(d => SafeName(d))
                    .ThenBy(d => d ? d.id : string.Empty);
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

        if (_capturedOnlyFilter) _favoritesOnlyFilter = false;

        RebuildOwnedOnly();
    }

    private void OnToggleFavoritesOnly()
    {
        _favoritesOnlyFilter = !_favoritesOnlyFilter;

        if (_favoritesOnlyFilter) _capturedOnlyFilter = false;

        RebuildOwnedOnly();
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private static void ClearAllChildren(RectTransform parent)
    {
        if (!parent) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
            Destroy(parent.GetChild(i).gameObject);
    }

    private static void ClearOwnedListItemsOnly(RectTransform parent)
    {
        if (!parent) return;

        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            var child = parent.GetChild(i);
            if (!child) continue;

            if (child.GetComponent<OwnedMonsterListItemUI>() != null)
                Destroy(child.gameObject);
        }
    }
}
