using UnityEngine;
using UnityEngine.UI;
using TMPro;


public class EvolutionPanelUI : MonoBehaviour
{
    [Header("Wires")]
    [SerializeField] private Image currentIcon;
    [SerializeField] private TextMeshProUGUI currentName;
    [SerializeField] private TextMeshProUGUI currentLevel;
    [SerializeField] private Image evolutionIcon;
    [SerializeField] private TextMeshProUGUI evolutionName;

    [Header("Stat Preview")]
    [SerializeField] private TextMeshProUGUI hpLine;
    [SerializeField] private TextMeshProUGUI atkLine;
    [SerializeField] private TextMeshProUGUI defLine;
    [SerializeField] private TextMeshProUGUI spdLine;

    [Header("Buttons")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private OwnedMonsterData _source;
    private MonsterDataSO _currentDef;
    private MonsterDataSO _nextDef;

    private const string POS_COLOR_HEX = "3CDE74";
    private const string NEG_COLOR_HEX = "FF5555";

    private void Awake()
    {
        if (confirmButton)
        {
            confirmButton.onClick.RemoveAllListeners();
            confirmButton.onClick.AddListener(OnConfirm);
        }

        if (cancelButton)
        {
            cancelButton.onClick.RemoveAllListeners();
            cancelButton.onClick.AddListener(Hide);
        }
    }

    public void Open(OwnedMonsterData source)
    {
        _source = XPManager.Resolve(source) ?? source;
        if (_source == null || string.IsNullOrEmpty(_source.monsterId))
        {
            Hide();
            return;
        }

        var lib = MonsterLibraryLocator.Lib;
        _currentDef = lib ? lib.GetById(_source.monsterId) : null;
        _nextDef = (_currentDef && _currentDef.evolutionForm) ? _currentDef.evolutionForm : null;

        if (_currentDef == null || _nextDef == null)
        {
            Hide();
            return;
        }

        if (currentName) currentName.text = string.IsNullOrEmpty(_currentDef.displayName) ? _currentDef.name : _currentDef.displayName;
        if (currentLevel) currentLevel.text = $"Lv {Mathf.Max(1, _source.level)}";
        if (evolutionName) evolutionName.text = string.IsNullOrEmpty(_nextDef.displayName) ? _nextDef.name : _nextDef.displayName;

        if (currentIcon) currentIcon.sprite = _currentDef.icon;
        if (evolutionIcon) evolutionIcon.sprite = _nextDef.icon;

        RefreshStatPreview();
        PlayStatFlashAnimation();
        gameObject.SetActive(true);
    }

    private void RefreshStatPreview()
    {
        if (_currentDef == null || _nextDef == null || _source == null)
        {
            ClearStatPreview();
            return;
        }

        int level = Mathf.Max(1, _source.level);

        // HP stays routed through EvolutionHelper (keeps your existing behavior, including any special rules).
        int curHp = EvolutionHelper.CalcMaxHP(_source, _currentDef);
        int nxtHp = EvolutionHelper.CalcMaxHP(_source, _nextDef);

        int curAtk = 0, nxtAtk = 0;
        int curDef = 0, nxtDef = 0;
        int curSpd = 0, nxtSpd = 0;

        // ATK via BattleCalc + training flat
        try { curAtk = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(_currentDef, level, 0, 0)); }
        catch { curAtk = Mathf.RoundToInt(_currentDef.baseAttack); }

        try { nxtAtk = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(_nextDef, level, 0, 0)); }
        catch { nxtAtk = Mathf.RoundToInt(_nextDef.baseAttack); }

        curAtk += Mathf.Max(0, _source.trainingBonus.atk);
        nxtAtk += Mathf.Max(0, _source.trainingBonus.atk);

        // DEF via BattleCalc + training flat (FIXED: previously baseDefense only)
        try { curDef = BattleCalc.CalcDefense(_currentDef, level); }
        catch { curDef = Mathf.RoundToInt(_currentDef.baseDefense); }

        try { nxtDef = BattleCalc.CalcDefense(_nextDef, level); }
        catch { nxtDef = Mathf.RoundToInt(_nextDef.baseDefense); }

        curDef += Mathf.Max(0, _source.trainingBonus.def);
        nxtDef += Mathf.Max(0, _source.trainingBonus.def);

        // SPD via BattleCalc + training flat (FIXED: previously baseSpeed only)
        // Note: BattleCalc.CalcSpeed returns int by design (turn priority stat).
        try { curSpd = BattleCalc.CalcSpeed(_currentDef, level); }
        catch { curSpd = Mathf.Max(1, Mathf.RoundToInt(_currentDef.baseSpeed)); }

        try { nxtSpd = BattleCalc.CalcSpeed(_nextDef, level); }
        catch { nxtSpd = Mathf.Max(1, Mathf.RoundToInt(_nextDef.baseSpeed)); }

        curSpd += Mathf.Max(0, _source.trainingBonus.spd);
        nxtSpd += Mathf.Max(0, _source.trainingBonus.spd);

        if (hpLine) hpLine.text = BuildStatLineInt("HP", curHp, nxtHp);
        if (atkLine) atkLine.text = BuildStatLineInt("ATK", curAtk, nxtAtk);
        if (defLine) defLine.text = BuildStatLineInt("DEF", curDef, nxtDef);
        if (spdLine) spdLine.text = BuildStatLineInt("SPD", curSpd, nxtSpd);
    }

    private string BuildStatLineInt(string label, int before, int after)
    {
        int delta = after - before;
        if (delta == 0)
            return $"{label}: {before} → {after}";

        string colorHex = delta > 0 ? POS_COLOR_HEX : NEG_COLOR_HEX;
        string deltaSign = delta > 0 ? "+" : "";
        string deltaText = $" <color=#{colorHex}>({deltaSign}{delta})</color>";

        return $"{label}: {before} → {after}{deltaText}";
    }

    // Kept for back-compat in case you used it elsewhere / might swap SPD back to float later.
    private string BuildStatLineFloat(string label, float before, float after)
    {
        float delta = after - before;
        string beforeStr = before.ToString("0.##");
        string afterStr = after.ToString("0.##");

        if (Mathf.Approximately(delta, 0f))
            return $"{label}: {beforeStr} → {afterStr}";

        string colorHex = delta > 0f ? POS_COLOR_HEX : NEG_COLOR_HEX;
        string deltaSign = delta > 0f ? "+" : "";
        string deltaStr = delta.ToString("0.##");
        string deltaText = $" <color=#{colorHex}>({deltaSign}{deltaStr})</color>";

        return $"{label}: {beforeStr} → {afterStr}{deltaText}";
    }

    private void ClearStatPreview()
    {
        if (hpLine) hpLine.text = "";
        if (atkLine) atkLine.text = "";
        if (defLine) defLine.text = "";
        if (spdLine) spdLine.text = "";
    }

    private void PlayStatFlashAnimation()
    {
        AnimateTextPunch(hpLine);
        AnimateTextPunch(atkLine);
        AnimateTextPunch(defLine);
        AnimateTextPunch(spdLine);
    }

    private void AnimateTextPunch(TextMeshProUGUI tmp)
    {
        if (!tmp) return;

        var t = tmp.transform;
        LeanTween.cancel(t.gameObject);
        t.localScale = Vector3.one;

        LeanTween.scale(t.gameObject, Vector3.one * 1.08f, 0.12f)
            .setEaseOutBack()
            .setOnComplete(() =>
            {
                LeanTween.scale(t.gameObject, Vector3.one, 0.10f)
                    .setEaseOutQuad();
            });
    }

    private void OnConfirm()
    {
        if (_source == null || _nextDef == null)
        {
            Hide();
            return;
        }

        bool success = EvolutionService.EvolveOwnedInstance(_source, _nextDef.id, allowDuplicateSpecies: true);
        if (success)
        {
            var evoName = !string.IsNullOrEmpty(_nextDef.displayName) ? _nextDef.displayName : _nextDef.name;
            GameEvents.RaiseToast($"{evoName.ToUpperInvariant()} EVOLVED!");

            GameEvents.OnTeamChanged?.Invoke();
            GameEvents.MonsterEvolved?.Invoke(_nextDef.id);
            TitlesAdapter.OnMonsterEvolved(_nextDef.id);
        }

        Hide();
    }

    private void Hide()
    {
        ClearStatPreview();
        _source = null;
        _currentDef = null;
        _nextDef = null;
        gameObject.SetActive(false);
    }
}
