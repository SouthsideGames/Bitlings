using UnityEngine;
using TMPro;
using System.Collections.Generic;


public sealed class GymPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Player data source. If left null, SaveManager.Data will be used.")]
    [SerializeField] private PlayerManager playerManager;

    [Tooltip("Panel that actually spends Growth Cores and levels a monster.")]
    [SerializeField] private StatBucketPanelUI statPanel;

    [Tooltip("Label that shows how many Growth Cores you have.")]
    [SerializeField] private TextMeshProUGUI growthCoresText;

    [Tooltip("Parent transform where GrowthListItemUI rows are instantiated.")]
    [SerializeField] private Transform listParent;

    [Tooltip("Strip / banner that appears when Auto Apply is enabled on at least one monster.")]
    [SerializeField] private GameObject autoApplyStrip;

    [Header("Prefabs")]
    [SerializeField] private GrowthListItemUI itemPrefab;

    [Header("Limits")]
    [Tooltip("Maximum monsters that can have Auto Apply enabled at once. 0 or less = no cap.")]
    [SerializeField] private int autoApplyCap = 3;

    [Header("Optional Display")]
    [Tooltip("Optional explicit reference to the monster library. If null, will use MonsterLibraryLocator.Lib.")]
    [SerializeField] private MonsterLibrarySO monsterLibrary;

    // Runtime
    private readonly List<GrowthListItemUI> _rows = new();
    private int _autoApplyEnabledCount;

    // Convenience property for current player data
    private PlayerManager Data => SaveManager.Data ?? playerManager;

    // ─────────────────────────────────────────────────────────────────────────────
    // Unity
    // ─────────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (SaveManager.Data == null)
            SaveManager.LoadOrCreate();

        if (!monsterLibrary && MonsterLibraryLocator.Lib)
            monsterLibrary = MonsterLibraryLocator.Lib;

        GameEvents.OnResourcesChanged += HandleResourcesChanged; // 🔹 add this
        RefreshAll();
    }

    private void OnDisable()
    {
        GameEvents.OnResourcesChanged -= HandleResourcesChanged; // 🔹 add this
    }

    private void HandleResourcesChanged()
    {
        RefreshCoreCount();          // update the number at the top
        foreach (var row in _rows)   // update button interactable state
            row.RefreshOpenInteractable();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Public API
    // ─────────────────────────────────────────────────────────────────────────────

    public void RefreshAll()
    {
        if (Data == null)
        {
            Debug.LogWarning("[GrowthPanelUI] No player data (SaveManager.Data is null).");
            return;
        }

        RefreshCoreCount();
        BuildList();
        RefreshAutoApplyStrip();
    }

    // Can be called from other systems when Growth Cores change.
    public void RefreshCoreCount()
    {
        if (!growthCoresText) return;

        int cores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCore) : 0;
        growthCoresText.text = cores.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // List building
    // ─────────────────────────────────────────────────────────────────────────────

    private void BuildList()
    {
        if (!listParent || !itemPrefab)
        {
            Debug.LogError("[GrowthPanelUI] listParent or itemPrefab not assigned.");
            return;
        }

        // Clear old rows
        foreach (Transform child in listParent)
        {
            Destroy(child.gameObject);
        }
        _rows.Clear();

        var monsters = GetAllMonsters();
        if (monsters == null || monsters.Count == 0)
        {
            Debug.LogWarning("[GrowthPanelUI] No monsters found to populate growth list.");
            _autoApplyEnabledCount = 0;
            return;
        }

        // Recount Auto Apply flags
        _autoApplyEnabledCount = CountAutoApplyEnabled(monsters);

        foreach (var om in monsters)
        {
            if (om == null || string.IsNullOrEmpty(om.monsterId))
                continue;

            var def = GetDefinition(om.monsterId);

            string displayName = def ? def.displayName : om.monsterId;
            Sprite icon = def ? def.icon : null;
            MonsterType type = def ? def.type : MonsterType.None;

            var row = Instantiate(itemPrefab, listParent);
            _rows.Add(row);

            row.Bind(
                om,
                displayName,
                icon,
                type,
                OnRowOpen,
                CanEnableAnotherAuto,
                OnAutoChanged
            );
        }
    }

    private MonsterDataSO GetDefinition(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return null;

        if (monsterLibrary)
            return monsterLibrary.GetById(monsterId);

        return MonsterLibraryLocator.GetById(monsterId);
    }

    /// <summary>
    /// Pulls every OwnedMonsterData from the save (team + bench).
    /// Uses PlayerManager.GetAllOwnedMonsters(includeTeam: true) so
    /// we share the same logic as AutoApplyService.
    /// </summary>
    private List<OwnedMonsterData> GetAllMonsters()
    {
        if (Data == null)
            return null;

        // NOTE: this calls the method defined in PlayerManager:
        // public List<OwnedMonsterData> GetAllOwnedMonsters(bool includeTeam = true)
        var monsters = Data.GetAllOwnedMonsters(includeTeam: true);
        return monsters;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Auto Apply cap helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private bool CanEnableAnotherAuto()
    {
        if (autoApplyCap <= 0) return true;
        return _autoApplyEnabledCount < autoApplyCap;
    }

    private void OnAutoChanged()
    {
        // Recount from current save state
        var monsters = GetAllMonsters();
        _autoApplyEnabledCount = CountAutoApplyEnabled(monsters);
        RefreshAutoApplyStrip();
    }

    private void RefreshAutoApplyStrip()
    {
        if (!autoApplyStrip) return;

        bool anyAuto = _autoApplyEnabledCount > 0;
        autoApplyStrip.SetActive(anyAuto);
    }

    private int CountAutoApplyEnabled(List<OwnedMonsterData> monsters)
    {
        if (monsters == null) return 0;

        int count = 0;
        for (int i = 0; i < monsters.Count; i++)
        {
            var m = monsters[i];
            if (m != null && m.autoApply)
                count++;
        }
        return count;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Row callbacks
    // ─────────────────────────────────────────────────────────────────────────────

    private void OnRowOpen(OwnedMonsterData model)
    {
        if (model == null)
            return;

        if (!statPanel)
        {
            Debug.LogWarning("[GrowthPanelUI] statPanel is not assigned.");
            return;
        }

        var def = GetDefinition(model.monsterId);
        statPanel.OpenFor(model);

    }
}
