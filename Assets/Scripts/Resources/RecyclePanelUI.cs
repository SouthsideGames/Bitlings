using System.Collections;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class ResourceIconEntry
{
    public ResourceType type;
    public Sprite icon;
    public string displayName; // optional, if you want to show text somewhere
}

public class RecyclePanelUI : MonoBehaviour
{
    [Header("Recipe List")]
    [SerializeField] private Transform recipeListRoot;
    [SerializeField] private GameObject recipeItemPrefab;
    [SerializeField] private RecycleRecipeLibrarySO recipeLibrary;

    [Header("Icons")]
    [SerializeField] private List<ResourceIconEntry> iconCatalog = new();

    [Header("Controls")]
    [SerializeField] private Button convertButton;

    [Tooltip("Shown briefly after a successful conversion.")]
    [SerializeField] private GameObject successBanner;
    [SerializeField] private float successBannerDuration = 1.2f;

    private readonly List<RecycleRecipeItemUI> _items = new();
    private RecycleRecipeSO _selectedRecipe;
    private Coroutine _bannerCo;

    void OnEnable()
    {
        BuildRecipeList();
        SelectRecipe(null);

        if (successBanner)
            successBanner.SetActive(false);

        if (convertButton)
        {
            convertButton.onClick.RemoveAllListeners();
            convertButton.onClick.AddListener(OnClickConvert);
            convertButton.interactable = false;
        }
    }

    void OnDisable()
    {
        if (_bannerCo != null)
        {
            StopCoroutine(_bannerCo);
            _bannerCo = null;
        }

        if (convertButton)
            convertButton.onClick.RemoveListener(OnClickConvert);
    }

    // ---------------------------------------------------------------------
    // Building the list
    // ---------------------------------------------------------------------

    private void BuildRecipeList()
    {
        // Clear existing
        if (recipeListRoot)
        {
            for (int i = recipeListRoot.childCount - 1; i >= 0; i--)
                Destroy(recipeListRoot.GetChild(i).gameObject);
        }
        _items.Clear();

        if (!recipeItemPrefab || recipeLibrary == null || recipeLibrary.recipes == null)
            return;

        foreach (var recipe in recipeLibrary.recipes)
        {
            if (recipe == null) continue;

            var go = Instantiate(recipeItemPrefab, recipeListRoot);
            var item = go.GetComponent<RecycleRecipeItemUI>();
            if (!item) item = go.AddComponent<RecycleRecipeItemUI>();

            var fromSprite = GetIcon(recipe.fromType);
            var toSprite   = GetIcon(recipe.toType);

            item.Bind(recipe, fromSprite, toSprite, OnRecipeClicked);
            _items.Add(item);
        }
    }

    private Sprite GetIcon(ResourceType type)
    {
        for (int i = 0; i < iconCatalog.Count; i++)
        {
            var e = iconCatalog[i];
            if (e != null && e.type == type)
                return e.icon;
        }
        return null;
    }

    // ---------------------------------------------------------------------
    // Selection
    // ---------------------------------------------------------------------

    private void OnRecipeClicked(RecycleRecipeItemUI item)
    {
        SelectRecipe(item);
    }

    private void SelectRecipe(RecycleRecipeItemUI item)
    {
        _selectedRecipe = item ? item.Recipe : null;


        RefreshConvertButton();
    }

    private void RefreshConvertButton()
    {
        if (!convertButton)
            return;

        if (_selectedRecipe == null)
        {
            convertButton.interactable = false;
            return;
        }

        int have = ResourceManager.I
            ? ResourceManager.I.Get(_selectedRecipe.fromType)
            : ResourceBank.Get(_selectedRecipe.fromType);

        convertButton.interactable = (have >= _selectedRecipe.fromAmount);
    }

    // ---------------------------------------------------------------------
    // Conversion
    // ---------------------------------------------------------------------

    private void OnClickConvert()
    {
        if (_selectedRecipe == null)
            return;

        var r = _selectedRecipe;

        int cost = r.fromAmount;
        bool paid = false;

        if (ResourceManager.I != null)
        {
            paid = ResourceManager.I.TrySpend(r.fromType, cost);
        }
        else
        {
            paid = ResourceBank.TrySpend(r.fromType, cost);
        }

        if (!paid)
        {
            // Not enough resources (maybe changed while panel was open)
            RefreshConvertButton();
            return;
        }

        if (ResourceManager.I != null)
        {
            ResourceManager.I.Add(r.toType, r.toAmount);
        }
        else
        {
            ResourceBank.Add(r.toType, r.toAmount);
        }

        // Let everyone know amounts changed
        GameEvents.OnResourcesChanged?.Invoke();

        // Show "conversion complete" banner briefly
        if (successBanner)
        {
            if (_bannerCo != null) StopCoroutine(_bannerCo);
            _bannerCo = StartCoroutine(SuccessBannerRoutine());
        }

        // Update button state (in case we can't afford another conversion)
        RefreshConvertButton();
    }

    private IEnumerator SuccessBannerRoutine()
    {
        successBanner.SetActive(true);
        yield return new WaitForSeconds(successBannerDuration);
        successBanner.SetActive(false);
    }
}
