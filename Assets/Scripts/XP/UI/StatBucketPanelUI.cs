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

    private OwnedMonsterData _m;
    private LevelUpBucketSO _bucket;

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
        RefreshUI();
    }

    private void ComputeCurrentStats()
    {
        _baseHP  = 0;
        _baseATK = 0;
        _baseDEF = 0;
        _baseSPD = 0;

        if (_m == null || string.IsNullOrEmpty(_m.monsterId)) return;

        var def = MonsterLibraryLocator.GetById(_m.monsterId);
        int lvl = Mathf.Max(1, _m.level);

        if (def)
        {
            _baseHP  = Mathf.RoundToInt(BattleCalc.CalcHP(def, lvl));
            _baseATK = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(def, lvl, 0, 0));
            _baseDEF = BattleCalc.CalcDefense(def, lvl);
            _baseSPD = BattleCalc.CalcSpeed(def,   lvl);
        }

        _baseHP  += _m.trainingBonus.hp;
        _baseATK += _m.trainingBonus.atk;
        _baseDEF += _m.trainingBonus.def;
        _baseSPD += _m.trainingBonus.spd;
    }

    private void RefreshUI()
    {
        if (_m != null && nameText)
            nameText.text = $"{_m.monsterId}  •  Lv {_m.level}";

        int unspent = _m != null ? Mathf.Max(0, _m.unspentStatPoints) : 0;
        int remaining = Mathf.Max(0, unspent - _points);

        if (pointsText)
            pointsText.text = $"Points used: {_points}  •  Unspent: {remaining}";

        int haveCores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCores) : 0;
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

        SetStatLabel(hpVal,  _baseHP,  hpDelta);
        SetStatLabel(atkVal, _baseATK, atkDelta);
        SetStatLabel(defVal, _baseDEF, defDelta);
        SetStatLabel(spdVal, _baseSPD, spdDelta);

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

    private void SetBucket(string bucketId)
    {
        if (!bucketLibrary) return;

        var fallback = bucketLibrary.DefaultBucket();
        _bucket = bucketLibrary.GetById(bucketId, fallback);

        if (_m != null)
            _m.lastBucketId = _bucket ? _bucket.bucketId : null;
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
        if (_m == null || _points <= 0)
        {
            gameObject.SetActive(false);
            return;
        }

        _m = XPManager.Resolve(_m);
        if (_m == null)
        {
            gameObject.SetActive(false);
            return;
        }

        TrainingBonus delta = new TrainingBonus
        {
            hp  = _allocHp  * (tokenEconomy ? tokenEconomy.hpPerCore  : 1),
            atk = _allocAtk * (tokenEconomy ? tokenEconomy.atkPerCore : 1),
            def = _allocDef * (tokenEconomy ? tokenEconomy.defPerCore : 1),
            spd = _allocSpd * (tokenEconomy ? tokenEconomy.spdPerCore : 1)
        };

        XPManager.ApplyTrainingAndSave(_m, delta);

        var data = SaveManager.Data;
        if (data != null && data.owned != null)
        {
            OwnedMonsterData match = null;

            if (!string.IsNullOrEmpty(_m.ownedUID))
            {
                match = data.owned.Find(o => o != null && o.ownedUID == _m.ownedUID);
            }
            else
            {
                match = data.owned.Find(o => o != null && o.monsterId == _m.monsterId);
            }

            if (match != null)
            {
                match.unspentStatPoints -= _points;
                if (match.unspentStatPoints < 0) match.unspentStatPoints = 0;

                _m.unspentStatPoints = match.unspentStatPoints;

                SaveManager.Save();
            }
        }

        ComputeCurrentStats();
        gameObject.SetActive(false);
    }
}
