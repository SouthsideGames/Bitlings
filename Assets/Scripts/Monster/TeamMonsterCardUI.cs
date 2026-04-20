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
    private int _breatheTweenId = -1;
    private int _favoritePulseTweenId = -1;
    private int _evolvePulseTweenId = -1;
    private int _hpPunchTweenId = -1;
    private int _rootPunchTweenId = -1;
    private int _healPunchTweenId = -1;
    private int _lastShownHp = -1;

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
        PunchButton(rootButton, ref _rootPunchTweenId, 1.05f);
        _onClick?.Invoke(_data);
        AudioManager.I?.PlayClick();
    }

    private void OnClickHealPartial()
    {
        PunchButton(healBtn, ref _healPunchTweenId, 1.1f);
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
            // Defensive removal ensures no duplicate handlers on repeated OnEnable calls
            GameEvents.OnTeamChanged -= Refresh;
            GameEvents.OnTeamChanged += Refresh;
            
            GameEvents.OnResourcesChanged -= HandleResourcesChanged;
            GameEvents.OnResourcesChanged += HandleResourcesChanged;
            
            GameEvents.OnTeamChanged -= HandleResourcesChanged;
            GameEvents.OnTeamChanged += HandleResourcesChanged;
            
            GameEvents.FavoritesChanged -= RefreshFavoriteIcon;
            GameEvents.FavoritesChanged += RefreshFavoriteIcon;
            
            _bound = true;
        }

        Refresh();
        StartBreatheAnimation();
    }

    private void StartBreatheAnimation()
    {
        if (!img) return;

        StopBreatheAnimation();

        img.rectTransform.localScale = Vector3.one;
        _breatheTweenId = LeanTween.value(gameObject, 1f, 1.09f, 2.2f)
            .setEase(LeanTweenType.easeInOutSine)
            .setLoopPingPong()
            .setOnUpdate((float v) =>
            {
                if (img) img.rectTransform.localScale = new Vector3(v, v, 1f);
            })
            .id;
    }

    private void StopBreatheAnimation()
    {
        if (_breatheTweenId != -1)
        {
            LeanTween.cancel(_breatheTweenId);
            _breatheTweenId = -1;
        }

        if (img) img.rectTransform.localScale = Vector3.one;
    }

    private void OnDisable()
    {
        StopBreatheAnimation();
        StopFavoritePulse();
        StopEvolvePulse();
        StopHpTextPunch();
        StopButtonPunch(rootButton, ref _rootPunchTweenId);
        StopButtonPunch(healBtn, ref _healPunchTweenId);
        _lastShownHp = -1;

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
        bool isPremium = false;

        // Cosmetic-only: prefer the saved variant preference for this monsterId when available.
        if (_def != null && !string.IsNullOrEmpty(_def.id))
        {
            var pref = MonsterVariantPreference.GetPreferredOwned(_def.id);
            if (pref != null)
                isPremium = pref.isPremium || pref.premiumTier > 0;
            else if (_data != null)
                isPremium = _data.isPremium || _data.premiumTier > 0;
        }
        else if (_data != null)
        {
            isPremium = _data.isPremium || _data.premiumTier > 0;
        }

        if (img)
        {
            if (_def)
            {
                var spr = MonsterNameFormatter.GetIcon(_def, isPremium, backIcon: false);
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
        if (!hpText || _def == null || _data == null)
        {
            if (hpText) hpText.text = "";
            _lastShownHp = -1;
            return;
        }

        int maxHP = HealingService.CalcMaxHP(_def, _data.level);
        int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : 0; // never auto-heal from negative HP

        if (_lastShownHp >= 0 && _lastShownHp != curHP)
            PunchHpText();

        _lastShownHp = curHP;
        hpText.text = $"HP: {Mathf.Max(0, curHP)}/{maxHP}";
    }

    private void RefreshEvolutionAlert()
    {
        if (!evolveAlert) return;

        bool show = false;
        if (_data != null && _def != null)
            show = EvolutionHelper.CanEvolve(_data, _def);

        evolveAlert.SetActive(show);

        if (show) StartEvolvePulse();
        else StopEvolvePulse();
    }

    // ----------------------------------------------------------
    // FAVORITES ICON
    // ----------------------------------------------------------
    private void RefreshFavoriteIcon()
    {
        if (!favoriteAlert)
            return;

        bool hasFeature = FeatureUnlockManager.I &&
                          FeatureUnlockManager.I.IsUnlocked(FeatureId.Directory_Favorites);

        bool valid = _data != null && !string.IsNullOrEmpty(_data.monsterId);

        if (!hasFeature || !valid)
        {
            favoriteAlert.SetActive(false);
            StopFavoritePulse();
            return;
        }

        bool isFav = FavoriteService.IsFavorite(_data.monsterId);
        favoriteAlert.SetActive(isFav);
        if (isFav) StartFavoritePulse();
        else StopFavoritePulse();
    }

    private void PunchHpText()
    {
        if (!hpText) return;

        if (_hpPunchTweenId != -1)
            LeanTween.cancel(_hpPunchTweenId);

        hpText.rectTransform.localScale = Vector3.one;
        _hpPunchTweenId = LeanTween.scale(hpText.rectTransform, Vector3.one * 1.09f, 0.08f)
            .setEase(LeanTweenType.easeOutQuad)
            .setLoopPingPong(1)
            .id;
    }

    private void StopHpTextPunch()
    {
        if (_hpPunchTweenId != -1)
        {
            LeanTween.cancel(_hpPunchTweenId);
            _hpPunchTweenId = -1;
        }

        if (hpText) hpText.rectTransform.localScale = Vector3.one;
    }

    private void StartFavoritePulse()
    {
        if (!favoriteAlert) return;

        StopFavoritePulse();

        var rt = favoriteAlert.transform as RectTransform;
        if (!rt) return;

        rt.localScale = Vector3.one;
        _favoritePulseTweenId = LeanTween.scale(rt, Vector3.one * 1.08f, 0.38f)
            .setEase(LeanTweenType.easeInOutSine)
            .setLoopPingPong()
            .id;
    }

    private void StopFavoritePulse()
    {
        if (_favoritePulseTweenId != -1)
        {
            LeanTween.cancel(_favoritePulseTweenId);
            _favoritePulseTweenId = -1;
        }

        if (favoriteAlert)
        {
            var rt = favoriteAlert.transform as RectTransform;
            if (rt) rt.localScale = Vector3.one;
        }
    }

    private void StartEvolvePulse()
    {
        if (!evolveAlert) return;

        StopEvolvePulse();

        var rt = evolveAlert.transform as RectTransform;
        if (!rt) return;

        rt.localScale = Vector3.one;
        _evolvePulseTweenId = LeanTween.scale(rt, Vector3.one * 1.08f, 0.42f)
            .setEase(LeanTweenType.easeInOutSine)
            .setLoopPingPong()
            .id;
    }

    private void StopEvolvePulse()
    {
        if (_evolvePulseTweenId != -1)
        {
            LeanTween.cancel(_evolvePulseTweenId);
            _evolvePulseTweenId = -1;
        }

        if (evolveAlert)
        {
            var rt = evolveAlert.transform as RectTransform;
            if (rt) rt.localScale = Vector3.one;
        }
    }

    private void PunchButton(Button btn, ref int tweenId, float peakScale)
    {
        if (!btn) return;
        if (btn.transform == transform) return;

        if (tweenId != -1)
            LeanTween.cancel(tweenId);

        var rt = btn.transform as RectTransform;
        if (!rt) return;

        rt.localScale = Vector3.one;
        tweenId = LeanTween.scale(rt, Vector3.one * peakScale, 0.07f)
            .setEase(LeanTweenType.easeOutQuad)
            .setLoopPingPong(1)
            .id;
    }

    private void StopButtonPunch(Button btn, ref int tweenId)
    {
        if (tweenId != -1)
        {
            LeanTween.cancel(tweenId);
            tweenId = -1;
        }

        if (!btn) return;
        if (btn.transform == transform) return;

        var rt = btn.transform as RectTransform;
        if (rt) rt.localScale = Vector3.one;
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
            int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : 0; // never auto-heal from negative HP

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
        int curHP = _data.currentHP >= 0 ? Mathf.Min(_data.currentHP, maxHP) : 0; // never auto-heal from negative HP

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

        // Centralized HP contract:
        // - If team-slot bound: write via SaveManager.SetTeamSlotHP (syncs owned via ownedUID).
        // - Else if we have an ownedUID: write via SaveManager.SetOwnedMonsterHP.
        // - Else fallback to local write (legacy) and Save().
        bool hpChanged = false;

        if (_isTeamSlotBound)
        {
            hpChanged = SaveManager.SetTeamSlotHP(_teamSlotIndex, newHP, stampLastHpUnix: true, nowUnix: SaveManager.NowUnix(), save: true, fireEvents: true);

            // keep local ref in sync
            var team = SaveManager.Data?.team;
            if (team != null && _teamSlotIndex >= 0 && _teamSlotIndex < team.Count)
                _data = team[_teamSlotIndex];
        }
        else if (!string.IsNullOrEmpty(_ownedUid))
        {
            hpChanged = SaveManager.SetOwnedMonsterHP(_ownedUid, newHP, stampLastHpUnix: true, nowUnix: SaveManager.NowUnix(), save: true, fireEvents: true);

            // refresh local ref from owned if possible
            var refreshed = SaveManager.GetOwnedByUid(_ownedUid);
            if (refreshed != null) _data = refreshed;
        }
        else
        {
            // Last-resort fallback: route through the reference-based HP contract.
            // This keeps clamping + timer stamping consistent even when we don't know slot/UID.
            SaveManager.SetMonsterHP(_data, newHP, stampLastHpUnix: true, nowUnix: SaveManager.NowUnix(), save: true, fireEvents: true);
            hpChanged = true;
        }

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
