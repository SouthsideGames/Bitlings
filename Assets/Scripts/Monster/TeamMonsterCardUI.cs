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

    [Header("Heal Settings (credits Fallback)")]
    [SerializeField, Range(0f, 1f)] private float partialHealPct = 0.25f;
    [SerializeField] private ResourceType healCostType = ResourceType.Credits;
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

    // Legacy support: caller might only provide monsterId.
    private string _monsterId;

    // Preferred support: bind to an ownedUID when possible.
    private string _ownedUid;

    bool _bound;

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

        _monsterId = data != null ? data.monsterId : null;
        _ownedUid = data != null ? data.ownedUID : null;

        WireButtons();
        Refresh();
        RefreshFavoriteIcon();
    }

    // KEEP EXISTING API SURFACE (legacy)
    public void Setup(string monsterId)
    {
        _monsterId = monsterId;
        _ownedUid = null;
        Refresh();
    }

    // NEW: preferred binder (doesn’t remove anything)
    public void SetupByOwnedUid(string ownedUid)
    {
        _ownedUid = ownedUid;
        _monsterId = null;
        Refresh();
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
        if (!_bound)
        {
            GameEvents.OnTeamChanged += Refresh;
            _bound = true;
        }

        GameEvents.OnResourcesChanged += HandleResourcesChanged;
        GameEvents.OnTeamChanged += HandleResourcesChanged;
        GameEvents.FavoritesChanged += RefreshFavoriteIcon;

        Refresh();
    }

    private void OnDisable()
    {
        if (_bound)
        {
            GameEvents.OnTeamChanged -= Refresh;
            _bound = false;
        }

        GameEvents.OnResourcesChanged -= HandleResourcesChanged;
        GameEvents.OnTeamChanged -= HandleResourcesChanged;
        GameEvents.FavoritesChanged -= RefreshFavoriteIcon;

        if (rootButton) rootButton.onClick.RemoveAllListeners();
        if (healBtn) healBtn.onClick.RemoveAllListeners();
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
        bool isShiny = (_data != null) && (_data.isShiny || _data.shinyTier > 0);

        if (img)
        {
            if (_def)
            {
                var spr = MonsterNameFormatter.GetIcon(_def, isShiny, backIcon: false);
                if (spr == null) spr = _def.icon;

                img.sprite = spr;
                img.enabled = (spr != null);
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
    }

    private void UpdateHpText()
    {
        if (!hpText || _def == null || _data == null) { if (hpText) hpText.text = ""; return; }

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

        bool hasFeature = FeatureUnlockManager.I &&
                          FeatureUnlockManager.I.IsUnlocked(FeatureId.Codex_Favorites);

        bool valid = _data != null && !string.IsNullOrEmpty(_data.monsterId);

        if (!hasFeature || !valid)
        {
            favoriteAlert.SetActive(false);
            return;
        }

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
            int credits = GetResource(healCostType);

            bool canHealWithMedkits = (partialHealMedkitCost > 0) && medkits >= partialHealMedkitCost;
            bool canHealWithCredits = (partialHealCost > 0) && credits >= partialHealCost;

            enable = needsHeal && (canHealWithMedkits || canHealWithCredits);
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
        int creditCost = partial ? partialHealCost : fullHealCost;

        bool paid = false;

        if (medkitCost > 0 && GetResource(medkitResourceType) >= medkitCost)
            paid = SpendResource(medkitResourceType, medkitCost);
        else if (creditCost > 0 && GetResource(healCostType) >= creditCost)
            paid = SpendResource(healCostType, creditCost);

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

    // ----------------------------------------------------------
    // Resolve & Refresh
    // ----------------------------------------------------------
    void Refresh()
    {
        // 1) Prefer ownedUID binding if present
        if (!string.IsNullOrEmpty(_ownedUid))
        {
            _data = FindInTeamByOwnedUid(_ownedUid);
            _monsterId = _data != null ? _data.monsterId : _monsterId;
        }

        // 2) If only monsterId was provided (legacy), prefer the globally preferred variant
        if (_data == null && !string.IsNullOrEmpty(_monsterId))
        {
            var pref = MonsterVariantPreference.GetPreferredOwned(_monsterId);
            if (pref != null && !string.IsNullOrEmpty(pref.ownedUID))
            {
                _data = FindInTeamByOwnedUid(pref.ownedUID);
                if (_data != null) _ownedUid = _data.ownedUID;
            }
        }

        // 3) Fallback: first team entry matching monsterId
        if (_data == null && !string.IsNullOrEmpty(_monsterId))
            _data = FindInTeamByMonsterId(_monsterId);

        // Resolve def from the resolved team entry if possible
        string finalId = _data != null ? _data.monsterId : _monsterId;
        _def = (!string.IsNullOrEmpty(finalId)) ? MonsterLibraryLocator.GetById(finalId) : null;

        RefreshVisuals();
        RefreshFavoriteIcon();
    }

    OwnedMonsterData FindInTeamByOwnedUid(string ownedUid)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null || string.IsNullOrEmpty(ownedUid)) return null;
        return data.team.Find(m => m != null && !string.IsNullOrEmpty(m.ownedUID) && m.ownedUID == ownedUid);
    }

    OwnedMonsterData FindInTeamByMonsterId(string monsterId)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null || string.IsNullOrEmpty(monsterId)) return null;
        return data.team.Find(m => m != null && m.monsterId == monsterId);
    }
}
