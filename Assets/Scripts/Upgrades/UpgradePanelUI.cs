using UnityEngine;
using System.Collections.Generic;

public class UpgradesPanelUI : MonoBehaviour
{
    [Header("List")]
    [SerializeField] private Transform listRoot;
    [SerializeField] private GameObject rowPrefab;

    [Header("Catalog (order shown)")]
    [SerializeField] private List<UpgradeCatalogEntry> catalog = new();

    private readonly List<UpgradeRowUI> _rows = new();

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
            var row = go.GetComponent<UpgradeRowUI>();
            if (!row) row = go.AddComponent<UpgradeRowUI>();

            // supply delegates per upgrade type
            System.Func<int> getLevel = () => GetLevel(e.type);
            System.Func<int> getCost  = () => GetCost(e.type);
            System.Action    onBuy    = () => TryBuy(e.type);

            row.BindStatic(e.displayName, e.icon, e.infoId, getLevel, getCost, onBuy);
            _rows.Add(row);
        }
    }

    void Refresh()
    {
        foreach (var row in _rows) row.Refresh();
    }

    // ---------- buying / mapping ----------

    int GetLevel(UpgradeType t)
    {
        if (SaveManager.Data == null) return 0;
        return t switch
        {
            UpgradeType.Tap      => SaveManager.Data.tapLevel,
            UpgradeType.Idle     => SaveManager.Data.idleLevel,
            UpgradeType.Crit     => SaveManager.Data.critLevel,
            UpgradeType.AutoTap  => SaveManager.Data.autoTapLevel,
            UpgradeType.CoinGain => SaveManager.Data.coinGainLevel,
            UpgradeType.Offline  => SaveManager.Data.offlineLevel,
            _ => 0
        };
    }

    void IncLevel(UpgradeType t)
    {
        if (SaveManager.Data == null) return;
        switch (t)
        {
            case UpgradeType.Tap:      SaveManager.Data.tapLevel++; break;
            case UpgradeType.Idle:     SaveManager.Data.idleLevel++; break;
            case UpgradeType.Crit:     SaveManager.Data.critLevel++; break;
            case UpgradeType.AutoTap:  SaveManager.Data.autoTapLevel++; break;
            case UpgradeType.CoinGain: SaveManager.Data.coinGainLevel++; break;
            case UpgradeType.Offline:  SaveManager.Data.offlineLevel++; break;
        }
    }

    int GetCost(UpgradeType t)
    {
        return t switch
        {
            UpgradeType.Tap      => Upgrades.TapCost(),
            UpgradeType.Idle     => Upgrades.IdleCost(),
            UpgradeType.Crit     => Upgrades.CritCost(),
            UpgradeType.AutoTap  => Upgrades.AutoTapCost(),
            UpgradeType.CoinGain => Upgrades.CoinGainCost(),
            UpgradeType.Offline  => Upgrades.OfflineCost(),
            _ => 0
        };
    }

    void TryBuy(UpgradeType t)
    {
        if (SaveManager.Data == null) return;

        int cost = GetCost(t);
        if (cost <= 0) return;
        if (!ResourceBank.TrySpend(ResourceType.Coins, cost)) return;

        IncLevel(t);
        SaveManager.Save();

        // refresh UI + notify others (same pattern as your old Upgrades UI)
        GameEvents.OnResourcesChanged?.Invoke();
        Refresh();
    }
}
