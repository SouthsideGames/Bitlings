using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class JobMaterialItemUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Image icon;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI xpText;
    [SerializeField] private Button minusBtn;
    [SerializeField] private Button plusBtn;

    // Colors for feedback
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color addColor = new Color(0.25f, 1f, 0.25f);  
    [SerializeField] private Color removeColor = new Color(1f, 0.3f, 0.3f); 

    // Local model
    private Sprite _icon;
    private string _jobName;
    private int _level;
    private int _maxLevel;
    private int _baseCurXP;   
    private int _maxXP;
    private int _pending;
    private Func<bool> _canSpendOne;
    private Action<int> _onDeltaChanged;

    public void Setup(
        Sprite iconSprite,
        string jobDisplayName,
        int level,
        int maxLevel,
        int currentXP,
        int maxXPForLevel,
        Func<bool> canSpendOneMaterial,
        Action<int> onDeltaChanged,
        Action requestRefresh)
    {
        _icon = iconSprite;
        _jobName = jobDisplayName;
        _level = level;
        _maxLevel = maxLevel;
        _baseCurXP = Mathf.Max(0, currentXP);
        _maxXP = Mathf.Max(1, maxXPForLevel);
        _pending = 0;
        _canSpendOne = canSpendOneMaterial;
        _onDeltaChanged = onDeltaChanged;

        if (icon) icon.sprite = _icon;
        if (nameText) nameText.text = _jobName;

        WireButtons(requestRefresh);
        RefreshVisuals();
    }

    public int Pending => _pending;
    public int PendingCurXP => Mathf.Clamp(_baseCurXP + _pending, 0, _maxXP);

    void WireButtons(Action requestRefresh)
    {
        if (minusBtn)
        {
            minusBtn.onClick.RemoveAllListeners();
            minusBtn.onClick.AddListener(() =>
            {
                if (_level >= _maxLevel) return; 
                if (PendingCurXP <= 0) return;

                _pending -= 1;
                _onDeltaChanged?.Invoke(-1);
                RefreshVisuals();
                requestRefresh?.Invoke();

                AudioManager.I.PlayClick();
            });
        }

        if (plusBtn)
        {
            plusBtn.onClick.RemoveAllListeners();
            plusBtn.onClick.AddListener(() =>
            {
                if (_level >= _maxLevel) return;
                if (PendingCurXP >= _maxXP) return;
                if (_canSpendOne != null && !_canSpendOne()) return;

                _pending += 1;
                _onDeltaChanged?.Invoke(+1);
                RefreshVisuals();
                requestRefresh?.Invoke();

                AudioManager.I.PlayClick();
            });
        }
    }

    public void RefreshVisuals()
    {
        if (levelText)
            levelText.text = (_level >= _maxLevel) ? "MAX" : $"L{_level}";

        int cur = PendingCurXP;
        if (xpText)
        {
            xpText.text = $"{cur}/{_maxXP} XP";
            if (_pending > 0)      xpText.color = addColor;
            else if (_pending < 0) xpText.color = removeColor;
            else                   xpText.color = normalColor;
        }

        bool atMaxLevel = _level >= _maxLevel;
        if (minusBtn) minusBtn.interactable = !atMaxLevel && (cur > 0);
        if (plusBtn)  plusBtn.interactable  = !atMaxLevel && (cur < _maxXP);
    }


    public void SetLevelAndCaps(int level, int maxXPForLevel)
    {
        _level = level;
        _maxXP = Mathf.Max(1, maxXPForLevel);
        _pending = 0; 
        _baseCurXP = 0;
        RefreshVisuals();
    }

}
