using System.Collections.Generic;
using UnityEngine;
using TMPro;

public class GrowthPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerManager playerManager;     // optional
    [SerializeField] private StatBucketPanelUI statPanel;     // required
    [SerializeField] private TextMeshProUGUI growthCoresText; // header label

    [Header("List Roots")]
    [SerializeField] private Transform listParent;            // required (ScrollRect content)
    [SerializeField] private Transform autoApplyStrip;        // optional

    [Header("Prefabs")]
    [SerializeField] private GrowthListItemUI listItemPrefab; // required (must live on root)

    [Header("Limits")]
    [SerializeField, Min(1)] private int autoApplyCap = 3;

    [Header("Optional Display")]
    [SerializeField] private MonsterLibrarySO monsterLibrary; // optional; auto-wires from Locator

    void OnEnable()
    {
        SaveManager.Data?.EnsureTransientSets();
        // Try to auto-wire MonsterLibrary if not set
        if (!monsterLibrary && MonsterLibraryLocator.Lib)
        {
            monsterLibrary = MonsterLibraryLocator.Lib;
        }
        RefreshAll();
    }

    public void RefreshAll()
    {
        if (!listParent)
        {
            Debug.LogError("[GrowthPanelUI] listParent is not assigned. Assign the ScrollRect Content transform.");
            return;
        }
        if (!listItemPrefab)
        {
            Debug.LogError("[GrowthPanelUI] listItemPrefab is not assigned. Assign a prefab with GrowthListItemUI on the root.");
            return;
        }
        if (!statPanel)
        {
            Debug.LogError("[GrowthPanelUI] statPanel is not assigned. Drag your StatBucketPanelUI.");
            return;
        }

        RefreshCores();
        BuildList();
        BuildAutoApplyStrip();
    }

    private void RefreshCores()
    {
        if (!growthCoresText) return;
        int cores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCores) : 0;
        growthCoresText.text = $"Growth Cores: {cores}";
    }

    private void BuildList()
    {
        // clear children
        for (int i = listParent.childCount - 1; i >= 0; i--)
            Destroy(listParent.GetChild(i).gameObject);

        var monsters = GetAllMonsters();

        if (monsters == null)
        {
            Debug.LogWarning("[GrowthPanelUI] GetAllMonsters() returned null. Is SaveManager.Data loaded?");
            return;
        }
        if (monsters.Count == 0)
        {
            Debug.LogWarning("[GrowthPanelUI] No monsters found (owned/team empty).");
            return;
        }

        int spawned = 0;

        for (int i = 0; i < monsters.Count; i++)
        {
            var m = monsters[i];
            if (m == null || string.IsNullOrEmpty(m.monsterId)) continue;

            // Optional name/icon/type from library
            string displayName = m.monsterId;
            Sprite icon = null;
            MonsterType type = MonsterType.None;

            if (monsterLibrary)
            {
                var def = monsterLibrary.GetById(m.monsterId);
                if (def)
                {
                    if (!string.IsNullOrEmpty(def.displayName)) displayName = def.displayName;
                    icon = def.icon;
                    type = def.type;
                }
                else
                {
                    // Still spawn; just log once per missing ID
                    Debug.LogWarning($"[GrowthPanelUI] MonsterLibrary has no def for '{m.monsterId}'. Row will use ID.");
                }
            }

            var view = Instantiate(listItemPrefab, listParent);
            if (!view)
            {
                Debug.LogError("[GrowthPanelUI] Failed to instantiate listItemPrefab.");
                continue;
            }

            view.Bind(
                model: m,
                displayName: displayName,
                icon: icon,
                type: type,
                onOpen: HandleOpenItem,
                canEnableAnotherAuto: CanEnableAnotherAuto,
                onAutoChanged: HandleAutoChanged
            );

            spawned++;
        }

        if (spawned == 0)
        {
            Debug.LogWarning("[GrowthPanelUI] Monsters existed but no rows spawned (all invalid?). Check console warnings above.");
        }
        else
        {
            Debug.Log($"[GrowthPanelUI] Spawned {spawned} monster rows.");
        }
    }

    private void BuildAutoApplyStrip()
    {
        if (!autoApplyStrip) return;
        for (int i = autoApplyStrip.childCount - 1; i >= 0; i--)
            Destroy(autoApplyStrip.GetChild(i).gameObject);
        // Optional: add chips for enabled auto-apply targets here.
    }

    // --- Callbacks ---
    private void HandleOpenItem(OwnedMonsterData m)
    {
        if (!statPanel) return;
        statPanel.OpenFor(m);
        statPanel.gameObject.SetActive(true);
    }

    private bool CanEnableAnotherAuto() => CountAutoApplyEnabled() < autoApplyCap;

    private void HandleAutoChanged()
    {
        BuildAutoApplyStrip();
    }

    // --- Data ---
    private List<OwnedMonsterData> GetAllMonsters()
    {
        // Prefer PlayerManager if it actually returns data; otherwise fall back to SaveManager.
        if (playerManager != null)
        {
            var fromPM = playerManager.GetAllOwnedMonsters(true);
            if (fromPM != null && fromPM.Count > 0) return fromPM;
            Debug.Log("[GrowthPanelUI] PlayerManager empty; falling back to SaveManager.");
        }

        RosterUtils.EnsureTeam3();
        var merged = RosterUtils.GetAllOwnedMonstersMerged(includeTeam: true);
        Debug.Log($"[GrowthPanelUI] Roster merged count = {merged.Count}");
        return merged;
    }

    private int CountAutoApplyEnabled()
    {
        var monsters = GetAllMonsters();
        if (monsters == null) return 0;
        int c = 0;
        for (int i = 0; i < monsters.Count; i++)
            if (monsters[i] != null && monsters[i].autoApply) c++;
        return c;
    }
}
