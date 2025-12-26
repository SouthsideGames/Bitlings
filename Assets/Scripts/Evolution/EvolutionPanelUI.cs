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
    [Tooltip("Line showing HP before/after and delta, e.g. 'HP: 120 → 150 (+30)'")]
    [SerializeField] private TextMeshProUGUI hpLine;
    [Tooltip("Line showing ATK before/after and delta.")]
    [SerializeField] private TextMeshProUGUI atkLine;
    [Tooltip("Line showing DEF before/after and delta.")]
    [SerializeField] private TextMeshProUGUI defLine;
    [Tooltip("Line showing SPD before/after and delta.")]
    [SerializeField] private TextMeshProUGUI spdLine;

    [Header("Buttons")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private Button cancelButton;

    private OwnedMonsterData _source;
    private MonsterDataSO _currentDef;
    private MonsterDataSO _nextDef;

    // Colors for delta rich-text
    private const string POS_COLOR_HEX = "3CDE74";  // same green you use elsewhere
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


        // Basic labels
        if (currentName)   currentName.text   = _currentDef.displayName;
        if (currentLevel)  currentLevel.text  = $"Lv {_source.level}";
        if (evolutionName) evolutionName.text = _nextDef.displayName;

        if (currentIcon)   currentIcon.sprite   = _currentDef.icon;
        if (evolutionIcon) evolutionIcon.sprite = _nextDef.icon;

        // Stat preview + flash anim
        RefreshStatPreview();
        PlayStatFlashAnimation();
    }

    private void RefreshStatPreview()
    {
        if (_currentDef == null || _nextDef == null)
        {
            ClearStatPreview();
            return;
        }

        int level = Mathf.Max(1, _source != null && _source.level > 0 ? _source.level : 1);

        int curHp = 0, nxtHp = 0;
        int curAtk = 0, nxtAtk = 0;
        int curDef = 0, nxtDef = 0;
        float curSpd = 0f, nxtSpd = 0f;

        // HP / ATK via BattleCalc, same style as MonsterDetailPanelUI
        try { curHp  = Mathf.RoundToInt(BattleCalc.CalcHP(_currentDef, level)); } catch { }
        try { nxtHp  = Mathf.RoundToInt(BattleCalc.CalcHP(_nextDef,   level)); } catch { }
        try { curAtk = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(_currentDef, level, 0, 0)); } catch { }
        try { nxtAtk = Mathf.RoundToInt(BattleCalc.CalcBaseAttack(_nextDef,   level, 0, 0)); } catch { }

        // DEF / SPD from base stats (keeps it simple and matches your detail panel)
        curDef = Mathf.RoundToInt(_currentDef.baseDefense);
        nxtDef = Mathf.RoundToInt(_nextDef.baseDefense);
        curSpd = _currentDef.baseSpeed;
        nxtSpd = _nextDef.baseSpeed;

        if (hpLine)  hpLine.text  = BuildStatLineInt("HP",  curHp,  nxtHp);
        if (atkLine) atkLine.text = BuildStatLineInt("ATK", curAtk, nxtAtk);
        if (defLine) defLine.text = BuildStatLineInt("DEF", curDef, nxtDef);
        if (spdLine) spdLine.text = BuildStatLineFloat("SPD", curSpd, nxtSpd);
    }

    // Rich-text colored deltas for integer stats
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

    // Rich-text colored deltas for float stats (SPD)
    private string BuildStatLineFloat(string label, float before, float after)
    {
        float delta = after - before;
        string beforeStr = before.ToString("0.##");
        string afterStr  = after.ToString("0.##");

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
        if (hpLine)  hpLine.text  = "";
        if (atkLine) atkLine.text = "";
        if (defLine) defLine.text = "";
        if (spdLine) spdLine.text = "";
    }

    /// <summary>
    /// Small LeanTween "flash" on each stat line to emphasize the new values.
    /// </summary>
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

        // Quick punch up then ease back to 1
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
