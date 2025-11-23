// Assets/Scripts/UI/UpgradesPanelUI.cs
using UnityEngine;
using System.Collections.Generic;

public class UpgradesPanelUI : MonoBehaviour
{
    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private GameObject rowPrefab;

    [Header("Catalog")]
    [SerializeField] private List<UpgradeCatalogEntry> catalog = new();

    private readonly List<UpgradeRowUI> _rows = new();

    void OnEnable()
    {
        BuildRows();
    }

    void OnDisable()
    {
        _rows.Clear();
    }

    void BuildRows()
    {
        if (listRoot == null || rowPrefab == null)
            return;

        // Clear old
        for (int i = listRoot.childCount - 1; i >= 0; i--)
        {
            Destroy(listRoot.GetChild(i).gameObject);
        }
        _rows.Clear();

        // Build new
        foreach (var entry in catalog)
        {
            if (entry == null || entry.featureId == FeatureId.None)
                continue;

            var go = Instantiate(rowPrefab, listRoot);
            var row = go.GetComponent<UpgradeRowUI>();
            if (row != null)
            {
                row.Init(entry);
                _rows.Add(row);
            }
        }
    }
}
