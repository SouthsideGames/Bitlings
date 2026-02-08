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

    [Header("Credit Heal Settings (Credits Fallback)")]
    [SerializeField, Range(0f, 1f)] private float partialHealPct = 0.25f;
    [SerializeField] private ResourceType healCostType = ResourceType.Credits;
    [SerializeField] private int creditHealCost = 1;
    [SerializeField] private int fullCreditHealCost = 3;

    [Header("Heal Settings (Medkits First)")]
    [SerializeField] private ResourceType medkitResourceType = ResourceType.Medkit;
    [SerializeField] private int partialHealMedkitCost = 1;
    [SerializeField] private int fullHealMedkitCost = 1;

    [Header("Alerts")]
    [SerializeField] private GameObject evolveAlert;
    [SerializeField] private GameObject favoriteAlert;

    private OwnedMonsterData _data;
    private OwnedMonsterData _boundInstance;
    private MonsterDataSO _def;
    private Action<OwnedMonsterData> _onClick;
    private Action _onAnyChanged;

    private string _monsterId;
    private string _ownedUid;

    private bool _bound;

    // NEW: Team-slot binding (hard source of truth)
    [SerializeField] private int _teamSlotIndex = -1;
    private bool _isTeamSlotBound;

    // ----------------------------------------------------------
    // NEW: Bind this UI to a concrete team slot index.
    // Team row cards MUST use this to avoid collapsing to preferred variants.
    // ----------------------------------------------------------
    public void BindTeamSlot(int teamSlotIndex)
    {
        _isTeamSlotBound = true;
        _teamSlotIndex = teamSlotIndex;
    }

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
        _boundInstance = data;
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
        _boundInstance = null;

        // If someone uses this legacy path, treat it as NOT team-slot bound.
        _isTeamSlotBound = false;
        _teamSlotIndex = -1;

        Refresh();
    }

    // NEW: preferred binder (doesn’t remove anything)
    public void SetupByOwnedUid(string ownedUid)
    {
        _ownedUid = ownedUid;
        _monsterId = null;
        _boundInstance = null;

        // Not necessarily a team slot; leave slot binding as-is unless explicitly bound.
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
            bool canHealWithCredits = (creditHealCost > 0) && credits >= creditHealCost;

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
        int creditCost = partial ? creditHealCost : fullCreditHealCost;

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

        int newHP = Mathf.Clamp(curHP + restore, 0, maxHP);

        // IMPORTANT: if this is a team-slot bound card, write back to that slot explicitly.
        if (_isTeamSlotBound)
        {
            var save = SaveManager.Data;
            var team = save?.team;
            if (team != null && _teamSlotIndex >= 0 && _teamSlotIndex < team.Count && team[_teamSlotIndex] != null)
            {
                team[_teamSlotIndex].currentHP = newHP;
                _data = team[_teamSlotIndex]; // keep local ref in sync
            }
            else
            {
                // Fallback if somehow slot no longer valid
                _data.currentHP = newHP;
            }
        }
        else
        {
            _data.currentHP = newHP;
        }

        SaveManager.Save();

        GameEvents.OnTeamHealthChanged?.Invoke();
        GameEvents.OnTeamChanged?.Invoke();

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
        // HARD RULE: team-slot bound cards resolve ONLY from SaveManager.Data.team[slot].
        if (_isTeamSlotBound)
        {
            var save = SaveManager.Data;
            var team = save?.team;

            if (team != null && _teamSlotIndex >= 0 && _teamSlotIndex < team.Count)
            {
                _data = team[_teamSlotIndex];

                if (_data != null)
                {
                    if (!string.IsNullOrEmpty(_data.ownedUID)) _ownedUid = _data.ownedUID;
                    if (!string.IsNullOrEmpty(_data.monsterId)) _monsterId = _data.monsterId;
                }
            }
            else
            {
                _data = null;
            }

            string finalId = _data != null ? _data.monsterId : _monsterId;
            _def = (!string.IsNullOrEmpty(finalId)) ? MonsterLibraryLocator.GetById(finalId) : null;

            RefreshVisuals();
            RefreshFavoriteIcon();
            return;
        }

        // ---------------- Legacy / non-team views ----------------

        // 1) If we were bound to a specific owned instance (team slot), try to keep that exact instance.
        if (_boundInstance != null)
        {
            var exact = FindInTeamByReference(_boundInstance);
            if (exact != null)
            {
                _data = exact;
                if (!string.IsNullOrEmpty(_data.ownedUID)) _ownedUid = _data.ownedUID;
                if (!string.IsNullOrEmpty(_data.monsterId)) _monsterId = _data.monsterId;
            }
        }

        // 2) Prefer ownedUID binding if present
        if (_data == null && !string.IsNullOrEmpty(_ownedUid))
        {
            _data = FindInTeamByOwnedUid(_ownedUid);
            _monsterId = _data != null ? _data.monsterId : _monsterId;
        }

        // 3) If only monsterId was provided (legacy / non-team views), prefer the globally preferred variant.
        if (_data == null && _boundInstance == null && !string.IsNullOrEmpty(_monsterId))
        {
            var pref = MonsterVariantPreference.GetPreferredOwned(_monsterId);
            if (pref != null && !string.IsNullOrEmpty(pref.ownedUID))
            {
                _data = FindInTeamByOwnedUid(pref.ownedUID);
                if (_data != null) _ownedUid = _data.ownedUID;
            }
        }

        // 4) Fallback: first team entry matching monsterId
        if (_data == null && !string.IsNullOrEmpty(_monsterId))
            _data = FindInTeamByMonsterId(_monsterId);

        string finalId2 = _data != null ? _data.monsterId : _monsterId;
        _def = (!string.IsNullOrEmpty(finalId2)) ? MonsterLibraryLocator.GetById(finalId2) : null;

        RefreshVisuals();
        RefreshFavoriteIcon();
    }

    OwnedMonsterData FindInTeamByOwnedUid(string ownedUid)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null || string.IsNullOrEmpty(ownedUid)) return null;
        return data.team.Find(m => m != null && !string.IsNullOrEmpty(m.ownedUID) && m.ownedUID == ownedUid);
    }

    OwnedMonsterData FindInTeamByReference(OwnedMonsterData reference)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null || reference == null) return null;

        for (int i = 0; i < data.team.Count; i++)
        {
            var m = data.team[i];
            if (m == null) continue;
            if (ReferenceEquals(m, reference))
                return m;
        }

        return null;
    }

    OwnedMonsterData FindInTeamByMonsterId(string monsterId)
    {
        var data = SaveManager.Data;
        if (data == null || data.team == null || string.IsNullOrEmpty(monsterId)) return null;
        return data.team.Find(m => m != null && m.monsterId == monsterId);
    }
}
