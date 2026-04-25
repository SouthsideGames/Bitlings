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

    [Header("Recycle Feature")]
    [SerializeField] private Button recycleButton;
    [SerializeField] private PanelId recyclePanelId = PanelId.Recycle; 

    private readonly List<ResourceRowUI> _rows = new();

    public bool TryGetCatalogIcon(ResourceType type, out Sprite icon)
    {
        icon = null;
        if (catalog == null) return false;

        for (int i = 0; i < catalog.Count; i++)
        {
            var e = catalog[i];
            if (e == null || e.type != type) continue;
            icon = e.icon;
            return icon != null;
        }

        return false;
    }

    public static bool TryGetCatalogIconGlobal(ResourceType type, out Sprite icon)
    {
        icon = null;
        var panel = FindFirstObjectByType<ResourcePanelUI>(FindObjectsInactive.Include);
        return panel != null && panel.TryGetCatalogIcon(type, out icon);
    }

    void OnEnable()
    {
        GameEvents.OnResourcesChanged += Refresh;

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

        bool tokensUnlocked = FeatureUnlockManager.I != null &&
                              FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_BearBullTokens);
        bool arenaUnlocked = FeatureUnlockManager.I != null &&
                             FeatureUnlockManager.I.IsUnlocked(FeatureId.Arena_Basic);

        foreach (var e in catalog)
        {
            var go = Instantiate(rowPrefab, listRoot);
            var row = go.GetComponent<ResourceRowUI>();
            if (!row) row = go.AddComponent<ResourceRowUI>();

            row.BindStatic(e.displayName, e.icon, e.type, e.infoId);
            _rows.Add(row);

            if (e.type == ResourceType.BullToken || e.type == ResourceType.BearToken)
                go.SetActive(tokensUnlocked);
            else if (e.type == ResourceType.ArenaTicket)
                go.SetActive(arenaUnlocked);
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

        if (id == FeatureId.Exchange_BearBullTokens)
            UpdateTokenRowVisibility();

        if (id == FeatureId.Arena_Basic)
            UpdateArenaTicketRowVisibility();
    }

    private void UpdateTokenRowVisibility()
    {
        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.Exchange_BearBullTokens);

        for (int i = 0; i < catalog.Count && i < _rows.Count; i++)
        {
            var e = catalog[i];
            if (e.type == ResourceType.BullToken || e.type == ResourceType.BearToken)
                _rows[i].gameObject.SetActive(unlocked);
        }
    }

    private void UpdateArenaTicketRowVisibility()
    {
        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.Arena_Basic);

        for (int i = 0; i < catalog.Count && i < _rows.Count; i++)
        {
            if (catalog[i].type == ResourceType.ArenaTicket)
                _rows[i].gameObject.SetActive(unlocked);
        }
    }

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
