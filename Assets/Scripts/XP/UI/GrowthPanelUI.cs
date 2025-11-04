using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class GrowthPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private PlayerManager playerManager;         // assign in Inspector
    [SerializeField] private StatBucketPanelUI statPanel;         // assign the StatBucketPanelUI in Inspector
    [SerializeField] private TextMeshProUGUI growthCoresText;     // header text: "Growth Cores: N"

    [Header("List Parents")]
    [SerializeField] private Transform autoApplyStrip;            // optional: a parent for a top strip (up to 3)
    [SerializeField] private Transform listParent;                // parent for all monster entries

    [Header("Prefabs")]
    [Tooltip("A simple prefab with a Button on the root and optional child Texts named 'NameText' and 'LevelText', 'AutoToggle' Toggle, and 'Icon' Image.")]
    [SerializeField] private GameObject listItemPrefab;           // no custom script required

    [Header("Limits")]
    [SerializeField] private int autoApplyCap = 3;

    void OnEnable() => RefreshAll();

    public void RefreshAll()
    {
        RefreshCores();
        BuildList();
        BuildAutoApplyStrip();
    }

    void RefreshCores()
    {
        if (!growthCoresText) return;
        int cores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCores) : 0;
        growthCoresText.text = $"Growth Cores: {cores}";
    }

    void BuildList()
    {
        // clear
        if (listParent)
        {
            for (int i = listParent.childCount - 1; i >= 0; i--)
                Destroy(listParent.GetChild(i).gameObject);
        }

        List<OwnedMonsterData> monsters = playerManager != null
            ? playerManager.GetAllOwnedMonsters(true)
            : null;

        if (monsters == null || listParent == null || listItemPrefab == null) return;

        foreach (var m in monsters)
        {
            if (m == null) continue;
            var go = Instantiate(listItemPrefab, listParent);

            // Try to find common UI bits by name (optional; skip if not present)
            var btn   = go.GetComponent<Button>();
            var nameT = FindTMP(go.transform, "NameText");
            var lvlT  = FindTMP(go.transform, "LevelText");
            var icon  = FindImg(go.transform,  "Icon");
            var tog   = FindToggle(go.transform, "AutoToggle");

            if (nameT) nameT.text = m.monsterId;
            if (lvlT)  lvlT.text  = $"Lv {m.level}";
            // icon: if you have an icon system, set sprite here; else ignore.

            if (tog)
            {
                tog.isOn = m.autoApply;
                tog.onValueChanged.RemoveAllListeners();
                tog.onValueChanged.AddListener(v =>
                {
                    // enforce cap at 3: if turning on would exceed, reject
                    if (v && CountAutoApplyEnabled() >= autoApplyCap)
                    {
                        tog.isOn = false;
                        return;
                    }
                    m.autoApply = v;
                    if (v && m.autoApplyTargetLevel < m.level + 1)
                        m.autoApplyTargetLevel = m.level + 1;
                });
            }

            if (btn)
            {
                btn.onClick.RemoveAllListeners();
                btn.onClick.AddListener(() =>
                {
                    if (statPanel)
                    {
                        statPanel.OpenFor(m);
                        statPanel.gameObject.SetActive(true);
                    }
                });
            }
        }
    }

    void BuildAutoApplyStrip()
    {
        // OPTIONAL: If you want a top strip of pinned monsters, populate it here.
        // For now, we simply rely on the toggle beside each list item.
        if (!autoApplyStrip) return;

        // Example (clear any children)
        for (int i = autoApplyStrip.childCount - 1; i >= 0; i--)
            Destroy(autoApplyStrip.GetChild(i).gameObject);

        // You can instantiate a small pill UI here for each enabled auto-apply monster if desired.
    }

    int CountAutoApplyEnabled()
    {
        int count = 0;
        var monsters = playerManager != null ? playerManager.GetAllOwnedMonsters(true) : null;
        if (monsters == null) return 0;
        for (int i = 0; i < monsters.Count; i++)
            if (monsters[i] != null && monsters[i].autoApply) count++;
        return count;
    }

    // --- small helpers to find optional children by name ---

    TextMeshProUGUI FindTMP(Transform root, string childName)
    {
        var t = root.Find(childName);
        return t ? t.GetComponent<TextMeshProUGUI>() : null;
    }

    Image FindImg(Transform root, string childName)
    {
        var t = root.Find(childName);
        return t ? t.GetComponent<Image>() : null;
    }

    Toggle FindToggle(Transform root, string childName)
    {
        var t = root.Find(childName);
        return t ? t.GetComponent<Toggle>() : null;
    }
}
