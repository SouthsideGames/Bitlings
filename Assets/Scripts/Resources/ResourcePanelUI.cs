using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

[System.Serializable]
public class ResourceCatalogEntry
{
    public ResourceType type;
    public string displayName;
    public Sprite icon;

    [Tooltip("String id used by InfoLibrary/InfoRouter, e.g., res.credits, res.energy")]
    public string infoId;
}

public class ResourcePanelUI : MonoBehaviour
{
    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private GameObject rowPrefab;

    [Header("Catalog (order shown)")]
    [SerializeField] private List<ResourceCatalogEntry> catalog = new();

    // NEW — Recycle button + target panel launcher
    [Header("Recycle Feature")]
    [SerializeField] private Button recycleButton;
    [SerializeField] private PanelId recyclePanelId = PanelId.Recycle; // Make sure this exists in your enum

    private readonly List<ResourceRowUI> _rows = new();

    void OnEnable()
    {
        GameEvents.OnResourcesChanged += Refresh;


        // Feature unlock listener
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        BuildListIfNeeded();
        UpdateRecycleButtonVisibility();
        Refresh();
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= Refresh;

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
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

            row.BindStatic(e.displayName, e.icon, e.type, e.infoId);
            _rows.Add(row);
        }
    }

    void Refresh()
    {
        foreach (var row in _rows)
            row.RefreshAmount();
    }

    // ----------------------------------------------------------------------
    // RECYCLE FEATURE SUPPORT
    // ----------------------------------------------------------------------

    /// <summary>
    /// Show or hide the Recycle button based on feature unlock.
    /// </summary>
    private void UpdateRecycleButtonVisibility()
    {
        if (!recycleButton) return;

        bool unlocked =
            FeatureUnlockManager.I &&
            FeatureUnlockManager.I.IsUnlocked(FeatureId.Recycle_Basic);

        recycleButton.gameObject.SetActive(unlocked);

        if (unlocked)
        {
            recycleButton.onClick.RemoveAllListeners();
            recycleButton.onClick.AddListener(OnClickRecycle);
        }
        else
        {
            recycleButton.onClick.RemoveAllListeners();
        }
    }

    private void HandleFeatureUnlocked(FeatureId id)
    {
        if (id == FeatureId.Recycle_Basic)
            UpdateRecycleButtonVisibility();
    }

    /// <summary>
    /// Launch the recycle panel when the button is pressed.
    /// </summary>
    private void OnClickRecycle()
    {
        if (UIManager.I != null)
        {
            UIManager.I.Show(recyclePanelId);
        }
        else
        {
            Debug.LogWarning("UIManager not found — cannot open recycle panel.");
        }

        AudioManager.I?.PlayClick();
    }
}
