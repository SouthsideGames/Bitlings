using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class StatBucketPanelUI : MonoBehaviour
{
    [Header("UI - Header")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI pointsText;
    [SerializeField] private TextMeshProUGUI costText;

    [Header("UI - Buckets")]
    [SerializeField] private GameObject presetsRoot;
    [SerializeField] private Button offenseBtn;
    [SerializeField] private Button defenseBtn;
    [SerializeField] private Button utilityBtn;
    [SerializeField] private Button balanceBtn;
    [SerializeField] private Button speedBtn;

    [Header("UI - Stats Row (−/+)")]
    [SerializeField] private TextMeshProUGUI hpVal;
    [SerializeField] private Button hpMinus;
    [SerializeField] private Button hpPlus;

    [SerializeField] private TextMeshProUGUI atkVal;
    [SerializeField] private Button atkMinus;
    [SerializeField] private Button atkPlus;

    [SerializeField] private TextMeshProUGUI defVal;
    [SerializeField] private Button defMinus;
    [SerializeField] private Button defPlus;

    [SerializeField] private TextMeshProUGUI spdVal;
    [SerializeField] private Button spdMinus;
    [SerializeField] private Button spdPlus;

    [Header("UI - Footer")]
    [SerializeField] private Button levelUpBtn;
    [SerializeField] private Button confirmBtn;
    [SerializeField] private Button closeBtn;

    [Header("Config")]
    [SerializeField, Min(1)] private int pointsPerLevel = 3;

    private TokenEconomySO   tokenEconomy;
    private BucketLibrarySO  bucketLibrary;
    private LevelCostCurveSO levelCostCurve;
    private bool _isConfirming;
    private OwnedMonsterData _m;
    private LevelUpBucketSO _bucket;

    // Display breakdown
    // "Monster base" = monster's level-scaled base stats (no training)
    // "Training"      = saved training bonuses + pending allocation (green)
    private int _monHP, _monATK, _monDEF, _monSPD;
    private int _trainHP, _trainATK, _trainDEF, _trainSPD;

    private int _baseHP, _baseATK, _baseDEF, _baseSPD;

    private int _allocHp, _allocAtk, _allocDef, _allocSpd;

    private int _points;
    private int _nextCostToLevel;

    const string GREEN = "#3CDE74";

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        AutoLoadSOsIfMissing();
        Wire();
        ClearAlloc();
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        RefreshPresetVisibility();
        RefreshUI();
    }

    private void OnDisable()
    {
        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (!Application.isPlaying) AutoLoadSOsIfMissing();
    }
#endif

    private void AutoLoadSOsIfMissing()
    {
        if (!tokenEconomy)
        {
            var direct = Resources.Load<TokenEconomySO>("TokenEconomy");
            if (direct) tokenEconomy = direct;
            else
            {
                var all = Resources.LoadAll<TokenEconomySO>("");
                if (all != null && all.Length > 0) tokenEconomy = all[0];
            }
        }
        if (!bucketLibrary)
        {
            var all = Resources.LoadAll<BucketLibrarySO>("");
            if (all != null && all.Length > 0) bucketLibrary = all[0];
        }
        if (!levelCostCurve)
        {
            var all = Resources.LoadAll<LevelCostCurveSO>("");
            if (all != null && all.Length > 0) levelCostCurve = all[0];
        }
    }

    private void Wire()
    {
        if (hpMinus)  hpMinus.onClick.AddListener(() => AddAlloc(ref _allocHp,  -1));
        if (hpPlus)   hpPlus.onClick.AddListener(() => AddAlloc(ref _allocHp,  +1));
        if (atkMinus) atkMinus.onClick.AddListener(() => AddAlloc(ref _allocAtk, -1));
        if (atkPlus)  atkPlus.onClick.AddListener(() => AddAlloc(ref _allocAtk, +1));
        if (defMinus) defMinus.onClick.AddListener(() => AddAlloc(ref _allocDef, -1));
        if (defPlus)  defPlus.onClick.AddListener(() => AddAlloc(ref _allocDef, +1));
        if (spdMinus) spdMinus.onClick.AddListener(() => AddAlloc(ref _allocSpd, -1));
        if (spdPlus)  spdPlus.onClick.AddListener(() => AddAlloc(ref _allocSpd, +1));

        // Preset buttons: set bucket + apply preset allocation
        if (offenseBtn) offenseBtn.onClick.AddListener(() => OnPresetClicked("Offense"));
        if (defenseBtn) defenseBtn.onClick.AddListener(() => OnPresetClicked("Defense"));
        if (utilityBtn) utilityBtn.onClick.AddListener(() => OnPresetClicked("Utility"));
        if (balanceBtn) balanceBtn.onClick.AddListener(() => OnPresetClicked("Balance"));
        if (speedBtn)   speedBtn.onClick.AddListener(() => OnPresetClicked("Speed"));

        if (levelUpBtn) levelUpBtn.onClick.AddListener(OnClickLevelUp);
        if (confirmBtn) confirmBtn.onClick.AddListener(ConfirmSpend);
        if (closeBtn)   closeBtn.onClick.AddListener(ClosePanel);
    }

    private void ClosePanel() => gameObject.SetActive(false);

    private void ClearAlloc()
    {
        _allocHp = _allocAtk = _allocDef = _allocSpd = 0;
        _points = 0;
        _nextCostToLevel = CalcNextCostForCurrentLevel();
        RefreshUI();
    }

    private int CalcNextCostForCurrentLevel()
    {
        if (_m == null || levelCostCurve == null) return 1;
        return Mathf.Max(1, levelCostCurve.CoresToNextLevel(Mathf.Max(1, _m.level)));
    }

    public void OpenFor(OwnedMonsterData m)
    {
        if (m == null || string.IsNullOrEmpty(m.monsterId))
        {
            gameObject.SetActive(false);
            return;
        }

        _m = XPManager.Resolve(m);

        ComputeCurrentStats();
        ClearAlloc();

        if (bucketLibrary)
        {
            _bucket = bucketLibrary.GetById(_m.lastBucketId, bucketLibrary.DefaultBucket());
            if (!_bucket) _bucket = bucketLibrary.DefaultBucket();
        }

        var def = MonsterLibraryLocator.GetById(_m.monsterId);
        if (nameText)
            nameText.text = def ? def.displayName : _m.monsterId;

        gameObject.SetActive(true);
        RefreshPresetVisibility();
        RefreshUI();
    }

    private void ComputeCurrentStats()
    {
        _monHP  = 0;
        _monATK = 0;
        _monDEF = 0;
        _monSPD = 0;

        _trainHP  = 0;
        _trainATK = 0;
        _trainDEF = 0;
        _trainSPD = 0;

        _baseHP  = 0;
        _baseATK = 0;
        _baseDEF = 0;
        _baseSPD = 0;

        if (_m == null || string.IsNullOrEmpty(_m.monsterId)) return;

        // Canonical breakdown:
        // base+level derived, training from save, total is sum.
        var stats = ProgressionStatCalc.Get(_m);

        _monHP  = stats.basePlusLevelHP;
        _monATK = stats.basePlusLevelATK;
        _monDEF = stats.basePlusLevelDEF;
        _monSPD = stats.basePlusLevelSPD;

        _trainHP  = stats.trainingHP;
        _trainATK = stats.trainingATK;
        _trainDEF = stats.trainingDEF;
        _trainSPD = stats.trainingSPD;

        // legacy aggregates (if any other UI expects these)
        _baseHP  = _monHP  + _trainHP;
        _baseATK = _monATK + _trainATK;
        _baseDEF = _monDEF + _trainDEF;
        _baseSPD = _monSPD + _trainSPD;
    }


    private void RefreshUI()
    {
        if (_m != null && nameText)
            nameText.text = $"{_m.monsterId}  •  Lv {_m.level}";

        int unspent = _m != null ? Mathf.Max(0, _m.unspentStatPoints) : 0;
        int remaining = Mathf.Max(0, unspent - _points);

        if (pointsText)
            pointsText.text = $"Points used: {_points}  •  Unspent: {remaining}";

        int haveCores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCore) : 0;
        _nextCostToLevel = CalcNextCostForCurrentLevel();

        if (costText)
        {
            if (_m == null)
                costText.text = "Next Lv: -- / -- GC";
            else
                costText.text = $"Next Lv: {haveCores}/{_nextCostToLevel} GC";
        }

        int hpDelta  = _allocHp  * (tokenEconomy ? tokenEconomy.hpPerCore  : 1);
        int atkDelta = _allocAtk * (tokenEconomy ? tokenEconomy.atkPerCore : 1);
        int defDelta = _allocDef * (tokenEconomy ? tokenEconomy.defPerCore : 1);
        int spdDelta = _allocSpd * (tokenEconomy ? tokenEconomy.spdPerCore : 1);

        // Adjusted stats: Total (MonsterBase + Training[green])
        // Training includes saved training bonuses + pending allocation.
        SetStatLabelBreakdown(hpVal,  _monHP,  _trainHP,  hpDelta);
        SetStatLabelBreakdown(atkVal, _monATK, _trainATK, atkDelta);
        SetStatLabelBreakdown(defVal, _monDEF, _trainDEF, defDelta);
        SetStatLabelBreakdown(spdVal, _monSPD, _trainSPD, spdDelta);

        bool hasPointsLeft = remaining > 0;

        if (hpPlus)   hpPlus.interactable   = hasPointsLeft;
        if (atkPlus)  atkPlus.interactable  = hasPointsLeft;
        if (defPlus)  defPlus.interactable  = hasPointsLeft;
        if (spdPlus)  spdPlus.interactable  = hasPointsLeft;

        if (hpMinus)  hpMinus.interactable  = _allocHp  > 0;
        if (atkMinus) atkMinus.interactable = _allocAtk > 0;
        if (defMinus) defMinus.interactable = _allocDef > 0;
        if (spdMinus) spdMinus.interactable = _allocSpd > 0;

        if (confirmBtn)
            confirmBtn.interactable = _points > 0;

        if (levelUpBtn)
        {
            bool canLevel = _m != null
                            && _m.level < LevelRules.MaxLevel
                            && haveCores >= _nextCostToLevel;
            levelUpBtn.interactable = canLevel;

            var label = levelUpBtn.GetComponentInChildren<TextMeshProUGUI>();
            if (label)
                label.text = $"Level Up ({_nextCostToLevel} GC)";
        }
    }

    // Display format requested:
    //   100 (Base + Training[green])
    // Base = monster base stats (level-scaled), excludes training.
    // Training = saved training bonus + pending allocation.
    private void SetStatLabelBreakdown(TextMeshProUGUI label, int monsterBase, int trainingSaved, int allocDelta)
    {
        if (!label) return;

        int baseVal = Mathf.Max(0, monsterBase);
        int trainingVal = Mathf.Max(0, trainingSaved) + Mathf.Max(0, allocDelta);
        int total = baseVal + trainingVal;

        // Keep it simple and consistent even when training is 0.
        label.text = $"{total} ({baseVal} + <color={GREEN}>{trainingVal}</color>)";
    }

    private void SetStatLabel(TextMeshProUGUI label, int baseVal, int allocDelta)
    {
        if (!label) return;
        int total = baseVal + allocDelta;
        if (allocDelta > 0)
            label.text = $"{total} <color={GREEN}>(+{allocDelta})</color>";
        else
            label.text = $"{total}";
    }

    private void AddAlloc(ref int field, int delta)
    {
        if (_m == null) return;

        int unspent = Mathf.Max(0, _m.unspentStatPoints);
        int remaining = Mathf.Max(0, unspent - _points);

        if (delta > 0 && remaining <= 0)
            return;

        int next = field + delta;
        if (next < 0) return;

        field = next;
        _points = _allocHp + _allocAtk + _allocDef + _allocSpd;
        RefreshUI();
    }

    // Preset click: remember bucket + auto-allocate points
    private void OnPresetClicked(string bucketId)
    {
        SetBucket(bucketId);
        ApplyPreset(bucketId);
    }

    private void SetBucket(string bucketId)
    {
        if (!bucketLibrary) return;

        var fallback = bucketLibrary.DefaultBucket();
        _bucket = bucketLibrary.GetById(bucketId, fallback);

        if (_m != null)
            _m.lastBucketId = _bucket ? _bucket.bucketId : null;
    }

    // Apply up to 5 points according to the selected preset
    private void ApplyPreset(string bucketId)
    {
        if (_m == null) return;

        int unspent = Mathf.Max(0, _m.unspentStatPoints);
        int remaining = Mathf.Max(0, unspent - _points);
        if (remaining <= 0) return;

        int toSpend = Mathf.Min(5, remaining);

        switch (bucketId)
        {
            case "Offense":
                for (int i = 0; i < toSpend; i++)
                    AddAlloc(ref _allocAtk, +1);
                break;

            case "Defense":
                for (int i = 0; i < toSpend; i++)
                    AddAlloc(ref _allocDef, +1);
                break;

            case "Speed":
                for (int i = 0; i < toSpend; i++)
                    AddAlloc(ref _allocSpd, +1);
                break;

            case "Balance":
                for (int i = 0; i < toSpend; i++)
                {
                    int step = i % 3;
                    if (step == 0)      AddAlloc(ref _allocHp,  +1);
                    else if (step == 1) AddAlloc(ref _allocAtk, +1);
                    else                AddAlloc(ref _allocDef, +1);
                }
                break;

            case "Utility":
                for (int i = 0; i < toSpend; i++)
                {
                    if (i % 2 == 0) AddAlloc(ref _allocHp, +1);
                    else            AddAlloc(ref _allocSpd, +1);
                }
                break;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Level Up: spend Growth Cores → gain stat points
    // ─────────────────────────────────────────────────────────────

    private void OnClickLevelUp()
    {
        if (_m == null || levelCostCurve == null) return;
        if (_m.level >= LevelRules.MaxLevel) return;

        _m = XPManager.Resolve(_m);
        if (_m == null) return;

        bool success = XPManager.TryManualLevelUp(
            _m,
            pointsPerLevel,
            levelCostCurve,
            monsterLibrary: null
        );

        if (!success)
            return;

        string key = !string.IsNullOrEmpty(_m.ownedUID)
            ? _m.ownedUID
            : _m.monsterId;

        GameEvents.MonsterLeveled?.Invoke(key, _m.level);

        ComputeCurrentStats();
        _nextCostToLevel = CalcNextCostForCurrentLevel();
        RefreshUI();
        AudioManager.I.PlaySfx(SfxType.LevelUp);
    }

    // ─────────────────────────────────────────────────────────────
    // Confirm stat allocation: spend stat points, no cores
    // ─────────────────────────────────────────────────────────────

    private void ConfirmSpend()
    {
        if (_isConfirming) return;
        _isConfirming = true;

        try
        {
            // Guard
            if (_m == null || _points <= 0)
            {
                gameObject.SetActive(false);
                return;
            }

            if (confirmBtn) confirmBtn.interactable = false;

            // Resolve to canonical save instance
            _m = XPManager.Resolve(_m);
            if (_m == null)
            {
                gameObject.SetActive(false);
                return;
            }

            // Apply training bonus (EV-like)
            TrainingBonus delta = new TrainingBonus
            {
                hp  = _allocHp  * (tokenEconomy ? tokenEconomy.hpPerCore  : 1),
                atk = _allocAtk * (tokenEconomy ? tokenEconomy.atkPerCore : 1),
                def = _allocDef * (tokenEconomy ? tokenEconomy.defPerCore : 1),
                spd = _allocSpd * (tokenEconomy ? tokenEconomy.spdPerCore : 1)
            };

            XPManager.ApplyTrainingAndSave(_m, delta);

            // Spend stat points on the canonical instance as well (do not rely on match != null)
            _m.unspentStatPoints = Mathf.Max(0, _m.unspentStatPoints - _points);

            // Also try to update the owned list entry if it exists (keeps your existing behavior)
            var data = SaveManager.Data;
            if (data != null && data.owned != null)
            {
                OwnedMonsterData match = null;

                if (!string.IsNullOrEmpty(_m.ownedUID))
                    match = data.owned.Find(o => o != null && o.ownedUID == _m.ownedUID);
                else
                    match = data.owned.Find(o => o != null && o.monsterId == _m.monsterId);

                if (match != null)
                    match.unspentStatPoints = _m.unspentStatPoints;

                SaveManager.Save();
            }

            // Refresh local computed stats + close
            ComputeCurrentStats();
            gameObject.SetActive(false);
        }
        finally
        {
            // ALWAYS release the confirm lock and re-enable the button for the next open
            _isConfirming = false;
            if (confirmBtn) confirmBtn.interactable = true;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Feature gating – presets
    // ─────────────────────────────────────────────────────────────

    private void HandleFeatureUnlocked(FeatureId feature)
    {
        if (feature == FeatureId.AutoGrowth_UsePresets)
            RefreshPresetVisibility();
    }

    private void RefreshPresetVisibility()
    {
        if (!presetsRoot) return;

        bool unlocked = FeatureUnlockManager.I != null &&
                        FeatureUnlockManager.I.IsUnlocked(FeatureId.AutoGrowth_UsePresets);

        presetsRoot.SetActive(unlocked);
    }
}
