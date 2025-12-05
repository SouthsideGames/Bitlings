using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class TeamMonsterCardUI : MonoBehaviour
{
    [Header("Visuals")]
    [SerializeField] private Image img;
    [SerializeField] private TextMeshProUGUI hpText;

    [Header("Buttons")]
    [SerializeField] private Button rootButton;
    [SerializeField] private Button healBtn;

    [Header("Heal Settings (Coins Fallback)")]
    [SerializeField, Range(0f, 1f)] private float partialHealPct = 0.25f;
    [SerializeField] private ResourceType healCostType = ResourceType.Coin;
    [SerializeField] private int partialHealCost = 1;
    [SerializeField] private int fullHealCost = 3;

    [Header("Heal Settings (Medkits First)")]
    [SerializeField] private ResourceType medkitResourceType = ResourceType.Medkit;
    [SerializeField] private int partialHealMedkitCost = 1;
    [SerializeField] private int fullHealMedkitCost = 1;

    [Header("Alerts")]
    [SerializeField] private GameObject evolveAlert;
    [SerializeField] private GameObject favoriteAlert;


    private OwnedMonsterData _data;
    private MonsterDataSO _def;
    private Action<OwnedMonsterData> _onClick;
    private Action _onAnyChanged;

    // ----------------------------------------------------------
    // Setup
    // ----------------------------------------------------------
    public void Setup(
        OwnedMonsterData data,
        MonsterDataSO def,
        Action<OwnedMonsterData> onClick,
        Action onAnyChanged)
    {
        _data = data;
        _def = def;
        _onClick = onClick;
        _onAnyChanged = onAnyChanged;

        WireButtons();
        RefreshVisuals();
        RefreshFavoriteIcon();
    }

    private void WireButtons()
    {
        if (rootButton)
        {
            rootButton.onClick.RemoveAllListeners();
            rootButton.onClick.AddListener(OnClickRoot);
        }

        if (healBtn)
        {
            healBtn.onClick.RemoveAllListeners();
            healBtn.onClick.AddListener(OnClickHealPartial);
        }
    }

    private void OnClickRoot()
    {
        _onClick?.Invoke(_data);
        AudioManager.I?.PlayClick();
    }

    private void OnClickHealPartial()
    {
        TryHeal(partial: true);
        AudioManager.I?.PlayClick();
    }

    private void OnClickHealFull()
    {
        TryHeal(partial: false);
        AudioManager.I?.PlayClick();
    }

    // ----------------------------------------------------------
    // Events
    // ----------------------------------------------------------
    private void OnEnable()
    {
        GameEvents.OnResourcesChanged += HandleResourcesChanged;
        GameEvents.OnTeamChanged += HandleResourcesChanged;
        GameEvents.FavoritesChanged += RefreshFavoriteIcon;
    }

    private void OnDisable()
    {
        GameEvents.OnResourcesChanged -= HandleResourcesChanged;
        GameEvents.OnTeamChanged -= HandleResourcesChanged;
        GameEvents.FavoritesChanged -= RefreshFavoriteIcon;
    }

    private void HandleResourcesChanged()
    {
        UpdateHpText();
        UpdateHealInteractable();
        RefreshEvolutionAlert();
        RefreshFavoriteIcon();
    }

    // ----------------------------------------------------------
    // Visuals
    // ----------------------------------------------------------
    public void RefreshVisuals()
    {
        if (img)
        {
            if (_def && _def.icon)
            {
                img.enabled = true;
                img.sprite = _def.icon;
            }
            else
            {
                img.enabled = false;
                img.sprite = null;
            }
        }

        UpdateHpText();
        UpdateHealInteractable();
        RefreshEvolutionAlert();
        RefreshFavoriteIcon();
    }

    private void UpdateHpText()
    {
        if (!hpText || _def == null || _data == null) return;

        int maxHP = HealingService.CalcMaxHP(_def, _data.level);
        int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : maxHP;

        hpText.text = $"HP: {Mathf.Max(0, curHP)}/{maxHP}";
    }

    private void RefreshEvolutionAlert()
    {
        if (!evolveAlert) return;

        bool show = false;

        if (_data != null && _def != null)
            show = EvolutionHelper.CanEvolve(_data, _def);

        evolveAlert.SetActive(show);
    }

    // ----------------------------------------------------------
    // FAVORITES ICON
    // ----------------------------------------------------------
    private void RefreshFavoriteIcon()
    {
        if (!favoriteAlert)
            return;

        // Must unlock feature
        bool hasFeature = FeatureUnlockManager.I &&
                          FeatureUnlockManager.I.IsUnlocked(FeatureId.Codex_Favorites);

        // Must have valid monster
        bool valid = _data != null && !string.IsNullOrEmpty(_data.monsterId);

        if (!hasFeature || !valid)
        {
            favoriteAlert.SetActive(false);
            return;
        }

        // Check if this monster (by definition ID) is favorited
        bool isFav = FavoriteService.IsFavorite(_data.monsterId);

        favoriteAlert.SetActive(isFav);
    }

    // ----------------------------------------------------------
    // Healing
    // ----------------------------------------------------------
    private void UpdateHealInteractable()
    {
        if (!healBtn) return;

        bool enable = false;

        if (_def != null && _data != null)
        {
            int maxHP = HealingService.CalcMaxHP(_def, _data.level);
            int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : maxHP;
            bool needsHeal = curHP < maxHP;

            int medkits = GetResource(medkitResourceType);
            int coins = GetResource(healCostType);

            bool canHealWithMedkits = medkits >= partialHealMedkitCost;
            bool canHealWithCoins = coins >= partialHealCost;

            enable = needsHeal && (canHealWithMedkits || canHealWithCoins);
        }

        healBtn.gameObject.SetActive(enable);
        healBtn.interactable = enable;
    }

    private void TryHeal(bool partial)
    {
        if (_def == null || _data == null) return;

        int maxHP = HealingService.CalcMaxHP(_def, _data.level);
        int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : maxHP;

        if (curHP >= maxHP)
        {
            UpdateHealInteractable();
            return;
        }

        int medkitCost = partial ? partialHealMedkitCost : fullHealMedkitCost;
        int coinCost = partial ? partialHealCost : fullHealCost;

        bool paid = false;

        if (GetResource(medkitResourceType) >= medkitCost && medkitCost > 0)
            paid = SpendResource(medkitResourceType, medkitCost);
        else if (coinCost > 0)
            paid = SpendResource(healCostType, coinCost);

        if (!paid)
        {
            UpdateHealInteractable();
            return;
        }

        int restore = partial
            ? Mathf.CeilToInt(maxHP * Mathf.Clamp01(partialHealPct))
            : (maxHP - curHP);

        _data.currentHP = Mathf.Clamp(curHP + restore, 0, maxHP);

        SaveManager.Save();

        UpdateHpText();
        UpdateHealInteractable();
        _onAnyChanged?.Invoke();
    }

    private int GetResource(ResourceType type) =>
        ResourceManager.I ? ResourceManager.I.Get(type) : 0;

    private bool SpendResource(ResourceType type, int amount) =>
        ResourceManager.I && ResourceManager.I.TrySpend(type, amount);
}
