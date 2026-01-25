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
        RefreshOpenInteractable();
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
        if (data == null) return;

        OwnedMonsterData latest = null;

        // Prefer canonical owned instance first (bench monsters must refresh too)
        if (data.owned != null)
        {
            if (!string.IsNullOrEmpty(_model.ownedUID))
                latest = data.owned.Find(o => o != null && o.ownedUID == _model.ownedUID);

            if (latest == null && !string.IsNullOrEmpty(_model.monsterId))
            {
                // Safe fallback: if multiple exist, we can't disambiguate; do nothing.
                int count = 0;
                OwnedMonsterData single = null;
                for (int i = 0; i < data.owned.Count; i++)
                {
                    var o = data.owned[i];
                    if (o != null && o.monsterId == _model.monsterId)
                    {
                        count++;
                        if (count == 1) single = o;
                        else break;
                    }
                }
                if (count == 1) latest = single;
            }
        }

        // If not found in owned, try team list
        if (latest == null && data.team != null)
        {
            if (!string.IsNullOrEmpty(_model.ownedUID))
                latest = data.team.Find(t => t != null && t.ownedUID == _model.ownedUID);

            if (latest == null && !string.IsNullOrEmpty(_model.monsterId))
            {
                // Same safe fallback rule (unique only)
                int count = 0;
                OwnedMonsterData single = null;
                for (int i = 0; i < data.team.Count; i++)
                {
                    var t = data.team[i];
                    if (t != null && t.monsterId == _model.monsterId)
                    {
                        count++;
                        if (count == 1) single = t;
                        else break;
                    }
                }
                if (count == 1) latest = single;
            }
        }

        if (latest != null)
        {
            _model = latest;
            RefreshLevel(_model.level);
            RefreshOpenInteractable();
        }
        else
        {
            // Still refresh interactable state (cores / points may have changed)
            RefreshOpenInteractable();
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
    }

    // Call this if cores change globally and you want to refresh rows.
    public void RefreshOpenInteractable()
    {
        if (!openButton) return;

        int cores = ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCore) : 0;
        int unspent = _model != null ? Mathf.Max(0, _model.unspentStatPoints) : 0;

        // IMPORTANT: You must be able to open the stats panel even if you have 0 cores,
        // as long as you have unspent stat points to spend.
        openButton.interactable = (cores > 0) || (unspent > 0);
    }

    private void OnAutoToggleChanged(bool isOn)
    {
        if (_suppressToggle) return;
        if (_model == null) return;

        if (!IsAutoGrowthUnlocked())
        {
            _suppressToggle = true;
            autoToggle.SetIsOnWithoutNotify(false);
            _suppressToggle = false;
            return;
        }

        if (isOn)
        {
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
