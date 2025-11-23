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
    [SerializeField] private TextMeshProUGUI typeText;          // Type label
    [SerializeField] private Image iconImage;                   // optional
    [SerializeField] private Toggle autoToggle;                 // optional

    private OwnedMonsterData _model;
    private Action<OwnedMonsterData> _onOpen;
    private Func<bool> _canEnableAnotherAuto;   // returns true if enabling is allowed
    private Action _onAutoChanged;              // callback to notify parent
    private bool _suppressToggle;

    // -------------------------------------------------------------------------
    // BIND
    // -------------------------------------------------------------------------
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

        RefreshAutoToggleFeatureGate();
        RefreshOpenInteractable();

        if (openButton)
        {
            openButton.onClick.RemoveAllListeners();
            openButton.onClick.AddListener(() => _onOpen?.Invoke(_model));
        }
    }

    // -------------------------------------------------------------------------
    // LIFECYCLE – subscribe to team changes so level label updates when saved
    // -------------------------------------------------------------------------
    private void OnEnable()
    {
        GameEvents.OnTeamChanged += HandleTeamChanged;

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        RefreshAutoToggleFeatureGate();
    }

    private void OnDisable()
    {
        GameEvents.OnTeamChanged -= HandleTeamChanged;

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
    }

    private void HandleFeatureUnlocked(FeatureId feature)
    {
        if (feature == FeatureId.AutoGrowth_Basic)
            RefreshAutoToggleFeatureGate();
    }

    private void HandleTeamChanged()
    {
        if (_model == null) return;

        var data = SaveManager.Data;
        if (data == null || data.team == null) return;

        OwnedMonsterData latest = null;

        // Prefer matching by ownedUID if present (most robust if team order changes)
        if (!string.IsNullOrEmpty(_model.ownedUID))
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var m = data.team[i];
                if (m != null && m.ownedUID == _model.ownedUID)
                {
                    latest = m;
                    break;
                }
            }
        }

        // Fallback: match by monsterId if we didn’t find by ownedUID
        if (latest == null && !string.IsNullOrEmpty(_model.monsterId))
        {
            for (int i = 0; i < data.team.Count; i++)
            {
                var m = data.team[i];
                if (m != null && m.monsterId == _model.monsterId)
                {
                    latest = m;
                    break;
                }
            }
        }

        if (latest != null)
        {
            _model = latest;
            RefreshLevel(_model.level);
        }
    }

    // -------------------------------------------------------------------------
    // CORE UI HELPERS
    // -------------------------------------------------------------------------

    private bool IsAutoGrowthUnlocked()
    {
        return FeatureUnlockManager.I != null &&
               FeatureUnlockManager.I.IsUnlocked(FeatureId.AutoGrowth_Basic);
    }

    private void RefreshAutoToggleFeatureGate()
    {
        if (!autoToggle) return;

        bool unlocked = IsAutoGrowthUnlocked();
        autoToggle.gameObject.SetActive(unlocked);

        // If the feature is somehow locked but the data has autoApply = true,
        // we leave the data as-is but hide the toggle.
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
        if (_model == null) return;

        if (!IsAutoGrowthUnlocked())
        {
            // Safety guard – should not happen because toggle is hidden when locked.
            _suppressToggle = true;
            autoToggle.SetIsOnWithoutNotify(false);
            _suppressToggle = false;
            return;
        }

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

    public void RefreshLevel(int newLevel)
    {
        if (levelText) levelText.text = $"Lv {Mathf.Max(1, newLevel)}";
    }

    public void ButtonClick() => AudioManager.I.PlayClick();
}
