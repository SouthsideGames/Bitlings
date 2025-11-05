using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class StatBucketPanelUI : MonoBehaviour
{
    [Header("UI - Header")]
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI pointsText;   // "Points: X"
    [SerializeField] private TextMeshProUGUI costText;     // "Cost: X / Y GC"

    [Header("UI - Buckets")]
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
    [SerializeField] private Button confirmBtn;
    [SerializeField] private Button cancelBtn;

    private TokenEconomySO   tokenEconomy;
    private BucketLibrarySO  bucketLibrary;
    private LevelCostCurveSO levelCostCurve;

    private OwnedMonsterData _m;
    private LevelUpBucketSO _bucket;

    // current base stats at this level (computed from data)
    private int _baseHP, _baseATK, _baseDEF, _baseSPD;

    // manual allocations in UI
    private int _allocHp, _allocAtk, _allocDef, _allocSpd;

    private int _points;           // total allocated picks this session
    private int _gcCost;           // Growth Cores to spend (== points)
    private int _nextCostToLevel;  // curve cost preview

    const string GREEN = "#3CDE74";

    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        AutoLoadSOsIfMissing();
        Wire();
        ClearAlloc();
        gameObject.SetActive(false);
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

        if (offenseBtn) offenseBtn.onClick.AddListener(() => SetBucket("Offense"));
        if (defenseBtn) defenseBtn.onClick.AddListener(() => SetBucket("Defense"));
        if (utilityBtn) utilityBtn.onClick.AddListener(() => SetBucket("Utility"));
        if (balanceBtn) balanceBtn.onClick.AddListener(() => SetBucket("Balance"));
        if (speedBtn)   speedBtn.onClick.AddListener(() => SetBucket("Speed"));

        if (confirmBtn) confirmBtn.onClick.AddListener(ConfirmSpend);
        if (cancelBtn)  cancelBtn.onClick.AddListener(() => gameObject.SetActive(false));
    }

    private void ClearAlloc()
    {
        _allocHp = _allocAtk = _allocDef = _allocSpd = 0;
        _points = 0;
        _gcCost = 0;
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
        _m = m;
        // compute base/current stats for display
        ComputeCurrentStats();

        ClearAlloc();

        if (bucketLibrary)
        {
            _bucket = bucketLibrary.GetById(_m.lastBucketId, bucketLibrary.DefaultBucket());
            if (!_bucket) _bucket = bucketLibrary.DefaultBucket();
        }

        gameObject.SetActive(true);
        RefreshUI();
    }

    private void ComputeCurrentStats()
    {
        _baseHP  = 0; _baseATK = 0; _baseDEF = 0; _baseSPD = 0;

        if (_m == null || string.IsNullOrEmpty(_m.monsterId)) return;

        var def = MonsterLibraryLocator.GetById(_m.monsterId);
        int lvl = Mathf.Max(1, _m.level);

        if (def)
        {
            // Base from calc
            _baseHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def,   lvl));
            _baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
            _baseDEF = BattleCalc.CalcDefense(def, lvl);
            _baseSPD = BattleCalc.CalcSpeed(def,   lvl);
        }

        // If your OwnedMonsterData stores persistent training bonuses, add them if present.
        // We do this reflectively so it’s safe even if fields don’t exist.
        TryAddOwnedBonus(ref _baseHP,  _m, "bonusHP");
        TryAddOwnedBonus(ref _baseATK, _m, "bonusATK");
        TryAddOwnedBonus(ref _baseDEF, _m, "bonusDEF");
        TryAddOwnedBonus(ref _baseSPD, _m, "bonusSPD");
    }

    private void TryAddOwnedBonus(ref int stat, OwnedMonsterData owned, string fieldName)
    {
        try
        {
            var fi = owned.GetType().GetField(fieldName);
            if (fi != null && fi.FieldType == typeof(int))
            {
                stat += Mathf.Max(0, (int)fi.GetValue(owned));
            }
        }
        catch { /* ignore */ }
    }

    private void RefreshUI()
    {
        if (_m != null && nameText) nameText.text = $"{_m.monsterId}  •  Lv {_m.level}";
        if (pointsText) pointsText.text = $"Points: {_points}";
        if (costText)   costText.text   = $"Cost: {_gcCost} / {_nextCostToLevel} GC";

        // Show current stat plus green delta if allocated
        SetStatLabel(hpVal,  _baseHP,  _allocHp);
        SetStatLabel(atkVal, _baseATK, _allocAtk);
        SetStatLabel(defVal, _baseDEF, _allocDef);
        SetStatLabel(spdVal, _baseSPD, _allocSpd);

        // Button interactivity
        int haveCores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCores) : 0;
        bool hasAnyCores = haveCores > 0;

        // If player has 0 cores: ALL +/- disabled
        SetPlusMinusInteractable(hasAnyCores);

        // Minus buttons additionally depend on allocation being > 0
        if (hpMinus)  hpMinus.interactable  = hasAnyCores && _allocHp  > 0;
        if (atkMinus) atkMinus.interactable = hasAnyCores && _allocAtk > 0;
        if (defMinus) defMinus.interactable = hasAnyCores && _allocDef > 0;
        if (spdMinus) spdMinus.interactable = hasAnyCores && _allocSpd > 0;

        // Confirm needs: at least 1 point AND enough cores to cover cost
        if (confirmBtn)
        {
            bool haveCoresForCost = haveCores >= _gcCost;
            confirmBtn.interactable = (_points > 0) && hasAnyCores && haveCoresForCost;
        }
    }

    private void SetPlusMinusInteractable(bool on)
    {
        if (hpPlus)   hpPlus.interactable   = on;
        if (atkPlus)  atkPlus.interactable  = on;
        if (defPlus)  defPlus.interactable  = on;
        if (spdPlus)  spdPlus.interactable  = on;

        // (Minus buttons are further constrained by current allocation in RefreshUI)
        if (hpMinus)  hpMinus.interactable  = on && _allocHp  > 0;
        if (atkMinus) atkMinus.interactable = on && _allocAtk > 0;
        if (defMinus) defMinus.interactable = on && _allocDef > 0;
        if (spdMinus) spdMinus.interactable = on && _allocSpd > 0;
    }

    private void SetStatLabel(TextMeshProUGUI label, int baseVal, int alloc)
    {
        if (!label) return;
        int total = baseVal + alloc;
        if (alloc > 0)
            label.text = $"{total} <color={GREEN}>(+{alloc})</color>";
        else
            label.text = $"{total}";
    }

    private void RecalcCost()
    {
        _gcCost = _points;
        _nextCostToLevel = CalcNextCostForCurrentLevel();
        RefreshUI();
    }

    private void AddAlloc(ref int field, int delta)
    {
        int haveCores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCores) : 0;
        if (haveCores <= 0 && delta > 0) return; // no cores → cannot increase

        int next = field + delta;
        if (next < 0) return;

        field = next;
        _points = _allocHp + _allocAtk + _allocDef + _allocSpd;
        RecalcCost();
    }

    private void SetBucket(string bucketId)
    {
        if (!bucketLibrary) return;

        var fallback = bucketLibrary.DefaultBucket();
        _bucket = bucketLibrary.GetById(bucketId, fallback);

        if (_m != null)
            _m.lastBucketId = _bucket ? _bucket.bucketId : null;

        // (Optional) visually highlight the selected tab here.
    }

    private void ConfirmSpend()
    {
        if (_m == null || tokenEconomy == null) { gameObject.SetActive(false); return; }
        if (_points <= 0) { gameObject.SetActive(false); return; }

        int have = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCores) : 0;
        if (have < _gcCost) return; // not enough cores

        // Spend cores
        ResourceManager.I.Add(ResourceType.GrowthCores, -_gcCost);

        // Apply stat increases
        var delta = new TrainingBonus
        {
            hp  = _allocHp  * tokenEconomy.hpPerCore,
            atk = _allocAtk * tokenEconomy.atkPerCore,
            def = _allocDef * tokenEconomy.defPerCore,
            spd = _allocSpd * tokenEconomy.spdPerCore
        };
        MonsterStatApplier.Apply(_m, delta);

        // Recompute base stats to include newly applied training
        ComputeCurrentStats();

        // Level-up progression based on total points spent this session
        int pointsLeft = _points;
        while (pointsLeft > 0)
        {
            int need = CalcNextCostForCurrentLevel();
            if (pointsLeft >= need)
            {
                pointsLeft -= need;
                _m.level = Mathf.Max(1, _m.level + 1);

                // if you gate HP refill on level up, clamp currentHP to new max
                var def = MonsterLibraryLocator.GetById(_m.monsterId);
                if (def)
                {
                    int newMaxHP = Mathf.RoundToInt(BattleCalc.CalcHP(def, _m.level));
                    _m.currentHP = Mathf.Clamp(_m.currentHP, 0, newMaxHP);
                }
                continue;
            }
            break;
        }

        SaveManager.Save();
        SaveDebugTools.ExportAuditJson(true);
        GameEvents.OnTeamChanged?.Invoke();

        gameObject.SetActive(false);
    }
}
