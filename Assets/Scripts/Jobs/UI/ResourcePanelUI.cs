using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[System.Serializable]
public class ResourceCatalogEntry
{
    public ResourceType type;
    public string displayName;
    public Sprite icon;

    [Tooltip("String id used by InfoLibrary/InfoRouter, e.g., res.coins, res.energy")]
    public string infoId;  // ← NEW
}

public class ResourcePanelUI : MonoBehaviour
{
    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private GameObject rowPrefab;

    [Header("Catalog (order shown)")]
    [SerializeField] private List<ResourceCatalogEntry> catalog = new();

    private readonly List<ResourceRowUI> _rows = new();

    void OnEnable()
    {
        GameEvents.OnResourcesChanged += Refresh;
        BuildListIfNeeded();
        Refresh();
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= Refresh;
    }

    void BuildListIfNeeded()
    {
        if (listRoot.childCount > 0) return;
        _rows.Clear();

        foreach (var e in catalog)
        {
            var go = Instantiate(rowPrefab, listRoot);
            var row = go.GetComponent<ResourceRowUI>();
            if (!row) row = go.AddComponent<ResourceRowUI>();

            // NEW: pass infoId from catalog entry
            row.BindStatic(e.displayName, e.icon, e.type, e.infoId);
            _rows.Add(row);
        }
    }

    void Refresh()
    {
        foreach (var row in _rows) row.RefreshAmount();
    }
}
