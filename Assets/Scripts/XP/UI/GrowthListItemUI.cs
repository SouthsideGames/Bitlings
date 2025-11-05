using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;

public class GrowthListItemUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button openButton;
    [SerializeField] private TextMeshProUGUI nameText;
    [SerializeField] private TextMeshProUGUI levelText;
    [SerializeField] private TextMeshProUGUI typeText;          // 🆕 Type label
    [SerializeField] private Image iconImage;                   // optional
    [SerializeField] private Toggle autoToggle;                 // optional

    private OwnedMonsterData _model;
    private Action<OwnedMonsterData> _onOpen;
    private Func<bool> _canEnableAnotherAuto;   // returns true if enabling is allowed
    private Action _onAutoChanged;              // callback to notify parent
    private bool _suppressToggle;

    public void Bind(
        OwnedMonsterData model,
        string displayName,
        Sprite icon,
        MonsterType type,
        Action<OwnedMonsterData> onOpen,
        Func<bool> canEnableAnotherAuto,
        Action onAutoChanged)
    {
        _model = model;
        _onOpen = onOpen;
        _canEnableAnotherAuto = canEnableAnotherAuto;
        _onAutoChanged = onAutoChanged;

        if (nameText)  nameText.text  = displayName ?? model.monsterId;
        if (levelText) levelText.text = $"Lv {Mathf.Max(1, model.level)}";
        if (typeText)  typeText.text  = type.ToString().ToUpperInvariant();

        if (iconImage)
        {
            iconImage.enabled = icon != null;
            iconImage.sprite  = icon;
        }

        if (autoToggle)
        {
            _suppressToggle = true;
            autoToggle.SetIsOnWithoutNotify(_model.autoApply);
            _suppressToggle = false;

            autoToggle.onValueChanged.RemoveAllListeners();
            autoToggle.onValueChanged.AddListener(OnAutoToggleChanged);
        }

        if (openButton)
        {
            openButton.onClick.RemoveAllListeners();
            openButton.onClick.AddListener(() => _onOpen?.Invoke(_model));
        }

        RefreshOpenInteractable();
    }

    // Call this if cores change globally and you want to refresh rows.
    public void RefreshOpenInteractable()
    {
        if (!openButton) return;
        int cores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCores) : 0;
        openButton.interactable = cores > 0;
    }

    private void OnAutoToggleChanged(bool isOn)
    {
        if (_suppressToggle) return;

        if (isOn)
        {
            // ask parent if we can enable (cap check)
            if (_canEnableAnotherAuto != null && !_canEnableAnotherAuto())
            {
                _suppressToggle = true;
                autoToggle.SetIsOnWithoutNotify(false);
                _suppressToggle = false;
                return;
            }

            _model.autoApply = true;
            if (_model.autoApplyTargetLevel < _model.level + 1)
                _model.autoApplyTargetLevel = _model.level + 1;
        }
        else
        {
            _model.autoApply = false;
        }

        SaveManager.Save();
        _onAutoChanged?.Invoke();
    }

    // Optional helper if you need to refresh only the level label externally
    public void RefreshLevel(int newLevel)
    {
        if (levelText) levelText.text = $"Lv {Mathf.Max(1, newLevel)}";
    }
}
