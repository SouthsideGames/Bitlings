using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class StatBucketPanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private TokenEconomy tokenEconomy;   // drag or auto-load
    [SerializeField] private BucketLibrarySO bucketLibrary; // drag
    [SerializeField] private LevelCostCurveSO levelCostCurve; // drag

    [Header("UI - Header")]
    [SerializeField] private TextMeshProUGUI titleText;
    [SerializeField] private TextMeshProUGUI pointsText; // "Points: X"
    [SerializeField] private TextMeshProUGUI costText;   // "Cost: Y GC"

    [Header("UI - Buckets")]
    [SerializeField] private Button offenseBtn;
    [SerializeField] private Button defenseBtn;
    [SerializeField] private Button utilityBtn;

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

    OwnedMonsterData _m;
    LevelUpBucketSO _bucket;
    int _allocHp, _allocAtk, _allocDef, _allocSpd;
    int _points;     // allocation "clicks"
    int _gcCost;     // GC cost for this spend

    void Awake()
    {
        if (!tokenEconomy) tokenEconomy = TokenEconomy.Load();

        Wire();
        ClearAlloc();
        gameObject.SetActive(false);
    }

    void Wire()
    {
        if (hpMinus) hpMinus.onClick.AddListener(() => AddAlloc(ref _allocHp, -1));
        if (hpPlus)  hpPlus.onClick.AddListener(() => AddAlloc(ref _allocHp, +1));
        if (atkMinus) atkMinus.onClick.AddListener(() => AddAlloc(ref _allocAtk, -1));
        if (atkPlus)  atkPlus.onClick.AddListener(() => AddAlloc(ref _allocAtk, +1));
        if (defMinus) defMinus.onClick.AddListener(() => AddAlloc(ref _allocDef, -1));
        if (defPlus)  defPlus.onClick.AddListener(() => AddAlloc(ref _allocDef, +1));
        if (spdMinus) spdMinus.onClick.AddListener(() => AddAlloc(ref _allocSpd, -1));
        if (spdPlus)  spdPlus.onClick.AddListener(() => AddAlloc(ref _allocSpd, +1));

        if (offenseBtn) offenseBtn.onClick.AddListener(() => SetBucket("Offense"));
        if (defenseBtn) defenseBtn.onClick.AddListener(() => SetBucket("Defense"));
        if (utilityBtn) utilityBtn.onClick.AddListener(() => SetBucket("Utility"));

        if (confirmBtn) confirmBtn.onClick.AddListener(ConfirmSpend);
        if (cancelBtn) cancelBtn.onClick.AddListener(() => gameObject.SetActive(false));
    }

    void ClearAlloc()
    {
        _allocHp = _allocAtk = _allocDef = _allocSpd = 0;
        _points = 0;
        _gcCost = 0;
        RefreshUI();
    }

    void RefreshUI()
    {
        if (_m != null && titleText) titleText.text = $"{_m.monsterId}  •  Lv {_m.level}";
        if (pointsText) pointsText.text = $"Points: {_points}";
        if (costText) costText.text = $"Cost: {_gcCost} GC";

        if (hpVal)  hpVal.text  = $"{_allocHp}";
        if (atkVal) atkVal.text = $"{_allocAtk}";
        if (defVal) defVal.text = $"{_allocDef}";
        if (spdVal) spdVal.text = $"{_allocSpd}";
    }

    void RecalcCost()
    {
        // Simple rule: each "point" costs 1 GC. If you prefer per-level cost, use LevelCostCurve.
        _gcCost = _points;
        RefreshUI();
    }

    void AddAlloc(ref int field, int delta)
    {
        int next = field + delta;
        if (next < 0) return;
        field = next;
        _points = _allocHp + _allocAtk + _allocDef + _allocSpd;
        RecalcCost();
    }

    void SetBucket(string bucketId)
    {
        if (bucketLibrary == null) return;
        _bucket = bucketLibrary.GetById(bucketId, bucketLibrary.DefaultBucket());
        if (_m != null) _m.lastBucketId = _bucket ? _bucket.bucketId : null;
        // (Optional) visual highlights for selected tab.
    }

    public void OpenFor(OwnedMonsterData m)
    {
        _m = m;
        ClearAlloc();
        if (bucketLibrary) _bucket = bucketLibrary.GetById(_m.lastBucketId, bucketLibrary.DefaultBucket());
        if (_bucket == null && bucketLibrary) _bucket = bucketLibrary.DefaultBucket();
        gameObject.SetActive(true);
        RefreshUI();
    }

    void ConfirmSpend()
    {
        if (_m == null || tokenEconomy == null) return;
        if (_points <= 0) { gameObject.SetActive(false); return; }

        // Check & spend GC
        int have = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCores) : 0;
        if (have < _gcCost) { /* show toast "Not enough Growth Cores" */ return; }
        ResourceManager.I.Add(ResourceType.GrowthCores, -_gcCost);

        // Build delta by converting each allocation via TokenEconomy
        var delta = new TrainingBonus
        {
            hp  = _allocHp  * tokenEconomy.hpPerCore,
            atk = _allocAtk * tokenEconomy.atkPerCore,
            def = _allocDef * tokenEconomy.defPerCore,
            spd = _allocSpd * tokenEconomy.spdPerCore
        };

        MonsterStatApplier.Apply(_m, delta);

        // Optional: also level up if you want "points == cost to next level"
        // Here we leave level unchanged; leveling can also occur via AutoApplyService using LevelCostCurve.

        // Close
        gameObject.SetActive(false);
    }
}
