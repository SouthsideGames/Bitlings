using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

public class PackShopPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private PackShopItemUI packShopPrefab;
    [SerializeField] private TextMeshProUGUI currencyHeader;
    [SerializeField] private PackDetailPanelUI packDetailPanel;  

    private MonsterPackLibrarySO library;

    void Awake()
    {
        if (!library)
            library = Resources.Load<MonsterPackLibrarySO>("MonsterPackLibrary");
    }

    void OnEnable()
    {
        RefreshCurrencyHeader();
        BuildList();
        GameEvents.OnResourcesChanged += RefreshCurrencyHeader;
        MonsterPackManager.OnPackUnlocked += OnPackUnlocked;
    }

    void OnDisable()
    {
        GameEvents.OnResourcesChanged -= RefreshCurrencyHeader;
        MonsterPackManager.OnPackUnlocked -= OnPackUnlocked;
    }

    private void OnPackUnlocked(string _)
    {
        RefreshCurrencyHeader();
        BuildList();
    }

    private void RefreshCurrencyHeader()
    {
        int have = ResourceBank.Get(ResourceType.PackShard);
        if (currencyHeader)
            currencyHeader.text = $"Pack Shards: {have}";
    }

    private void BuildList()
    {
        if (!contentRoot) return;

        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);

        if (!library || library.Packs == null) return;

        var packs = new List<MonsterPackSO>(library.Packs);

        // Optional: sort so locked packs appear first
        packs.Sort((a, b) =>
        {
            bool aUnlocked = MonsterPackManager.I != null && MonsterPackManager.I.IsUnlocked(a.id);
            bool bUnlocked = MonsterPackManager.I != null && MonsterPackManager.I.IsUnlocked(b.id);
            return aUnlocked.CompareTo(bUnlocked);
        });

        foreach (var pack in packs)
        {
            if (!pack || !packShopPrefab) continue;

            var row = Instantiate(packShopPrefab, contentRoot);
            row.Bind(pack, packDetailPanel);
        }
    }
}
