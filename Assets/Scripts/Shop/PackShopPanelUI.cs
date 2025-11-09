using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic; // ✅ add this

public class PackShopPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Transform contentRoot;
    [SerializeField] private PackShopItemUI itemPrefab;
    [SerializeField] private TextMeshProUGUI currencyHeader;
    [SerializeField] private Button closeButton;

    [Header("Data")]
    [SerializeField] private MonsterPackLibrarySO library;

    void Awake()
    {
        if (!library) library = Resources.Load<MonsterPackLibrarySO>("MonsterPackLibrary");
        if (closeButton) closeButton.onClick.AddListener(() => gameObject.SetActive(false));
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
    }

    private void RefreshCurrencyHeader()
    {
        int have = ResourceBank.Get(ResourceType.PackShards);
        if (currencyHeader) currencyHeader.text = $"Pack Shards: {have}";
    }

    private void BuildList()
    {
        for (int i = contentRoot.childCount - 1; i >= 0; i--)
            Destroy(contentRoot.GetChild(i).gameObject);

        if (!library || library.Packs == null) return;

        var packs = new List<MonsterPackSO>(library.Packs);

        packs.Sort((a,b) => (MonsterPackManager.I.IsUnlocked(a.id) ? 1 : 0).CompareTo(MonsterPackManager.I.IsUnlocked(b.id) ? 1 : 0));

        foreach (var pack in library.Packs)
        {
            if (!pack) continue;
            var row = Instantiate(itemPrefab, contentRoot);
            row.Bind(pack);
        }
    }
}
