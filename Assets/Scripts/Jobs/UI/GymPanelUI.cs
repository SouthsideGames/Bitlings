using UnityEngine;
using TMPro;
using System.Collections.Generic;
using UnityEngine.UI;

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

    [Tooltip("Optional label inside the Auto Apply strip. If assigned, it will be updated so the strip isn't blank.")]
    [SerializeField] private TextMeshProUGUI autoApplyStripText;

    [Tooltip("Optional label for listing which monsters are Auto Apply (names). Leave null if you don't want this.")]
    [SerializeField] private TextMeshProUGUI autoApplyStripListText;

    [Header("Prefabs")]
    [SerializeField] private GrowthListItemUI itemPrefab;

    [Header("Limits")]
    [Tooltip("Maximum monsters that can have Auto Apply enabled at once. 0 or less = no cap.")]
    [SerializeField] private int autoApplyCap = 3;

    [Header("Optional Display")]
    [Tooltip("Optional explicit reference to the monster library. If null, will use MonsterLibraryLocator.Lib.")]
    [SerializeField] private MonsterLibrarySO monsterLibrary;

    [Header("Auto Growth")]
    [SerializeField] private Button applyAutoGrowthButton;
    [SerializeField] private FeatureId autoGrowthFeatureId = FeatureId.AutoGrowth_Basic;



    // Runtime
    private readonly List<GrowthListItemUI> _rows = new();
    private int _autoApplyEnabledCount;

    // Convenience property for current player data
    private PlayerManager Data => SaveManager.Data ?? playerManager;

    private void OnEnable()
    {
        if (SaveManager.Data == null)
            SaveManager.LoadOrCreate();

        if (!monsterLibrary && MonsterLibraryLocator.Lib)
            monsterLibrary = MonsterLibraryLocator.Lib;

        if (applyAutoGrowthButton != null)
        {
            applyAutoGrowthButton.onClick.RemoveListener(OnApplyAutoGrowthClicked);
            applyAutoGrowthButton.onClick.AddListener(OnApplyAutoGrowthClicked);
        }

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        GameEvents.OnResourcesChanged += HandleResourcesChanged;
        RefreshAll();
    }

    private void OnDisable()
    {
        GameEvents.OnResourcesChanged -= HandleResourcesChanged;
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
    }

    private void HandleResourcesChanged()
    {
        RefreshCoreCount();
        foreach (var row in _rows)
            row.RefreshOpenInteractable();
    }

    public void RefreshAll()
    {
        if (Data == null)
        {
            Debug.LogWarning("[GymPanelUI] No player data (SaveManager.Data is null).");
            return;
        }

        RefreshCoreCount();
        BuildList();
        RefreshAutoApplyStrip();
        RefreshApplyButtonGate();
    }

    public void RefreshCoreCount()
    {
        if (!growthCoresText) return;

        int cores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCore) : 0;
        growthCoresText.text = cores.ToString();
    }

    private void BuildList()
    {
        if (!listParent || !itemPrefab)
        {
            // Prefab/scene wiring issue. Disable the panel to avoid repeated errors.
            Debug.LogWarning("[GymPanelUI] listParent or itemPrefab not assigned. Disabling panel until wired.");
            gameObject.SetActive(false);
            return;
        }

        foreach (Transform child in listParent)
            Destroy(child.gameObject);

        _rows.Clear();

        var monsters = GetAllMonsters();
        if (monsters == null || monsters.Count == 0)
        {
            Debug.LogWarning("[GymPanelUI] No monsters found to populate growth list.");
            _autoApplyEnabledCount = 0;
            RefreshAutoApplyStrip(); // keep strip correct
            return;
        }

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

    private List<OwnedMonsterData> GetAllMonsters()
    {
        if (Data == null)
            return null;

        return Data.GetAllOwnedMonsters(includeTeam: true);
    }

    private bool CanEnableAnotherAuto()
    {
        if (autoApplyCap <= 0) return true;
        return _autoApplyEnabledCount < autoApplyCap;
    }

    private void OnAutoChanged()
    {
        var monsters = GetAllMonsters();
        _autoApplyEnabledCount = CountAutoApplyEnabled(monsters);
        RefreshAutoApplyStrip();
    }

    private void RefreshAutoApplyStrip()
    {
        if (!autoApplyStrip) return;

        bool anyAuto = _autoApplyEnabledCount > 0;
        autoApplyStrip.SetActive(anyAuto);

        // If strip is visible, keep it informative (prevents “blank strip”)
        if (anyAuto)
        {
            if (autoApplyStripText)
            {
                string cap = (autoApplyCap <= 0) ? "∞" : autoApplyCap.ToString();
                autoApplyStripText.text = $"Auto Apply: {_autoApplyEnabledCount}/{cap}";
            }

            if (autoApplyStripListText)
            {
                autoApplyStripListText.text = BuildAutoApplyNameList();
            }
        }
    }

    private string BuildAutoApplyNameList()
    {
        var monsters = GetAllMonsters();
        if (monsters == null) return "";

        List<string> names = new List<string>();

        for (int i = 0; i < monsters.Count; i++)
        {
            var m = monsters[i];
            if (m == null || !m.autoApply) continue;

            var def = GetDefinition(m.monsterId);
            names.Add(def ? def.displayName : m.monsterId);
        }

        // Keep it short in a banner
        if (names.Count == 0) return "";
        return string.Join(", ", names);
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

    private void OnRowOpen(OwnedMonsterData model)
    {
        if (model == null)
            return;

        if (!statPanel)
        {
            Debug.LogWarning("[GymPanelUI] statPanel is not assigned.");
            return;
        }

        statPanel.OpenFor(model);
    }

    private void OnApplyAutoGrowthClicked()
    {
        if (!IsAutoGrowthUnlocked())
        {
            GameEvents.RaiseToast("Auto Growth is not unlocked yet.");
            return;
        }

        GameEvents.RaiseAutoApplyRequested();
    }

    private bool IsAutoGrowthUnlocked()
    {
        return FeatureUnlockManager.I != null &&
            FeatureUnlockManager.I.IsUnlocked(autoGrowthFeatureId);
    }

    private void RefreshApplyButtonGate()
    {
        if (!applyAutoGrowthButton) return;

        bool unlocked = IsAutoGrowthUnlocked();
        applyAutoGrowthButton.gameObject.SetActive(unlocked);

    }

    private void HandleFeatureUnlocked(FeatureId feature)
    {
        if (feature == autoGrowthFeatureId)
            RefreshApplyButtonGate();
    }



}
