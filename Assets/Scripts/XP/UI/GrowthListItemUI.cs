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
    [SerializeField] private TextMeshProUGUI typeText;
    [SerializeField] private Image iconImage;
    [SerializeField] private Toggle autoToggle;
    [SerializeField] private TextMeshProUGUI autoStateText;

    private OwnedMonsterData _model;
    private Action<OwnedMonsterData> _onOpen;
    private Func<bool> _canEnableAnotherAuto;
    private Action _onAutoChanged;
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

        if (nameText) nameText.text = displayName ?? model.monsterId;
        if (levelText) levelText.text = $"Lv {Mathf.Max(1, model.level)}";
        if (typeText) typeText.text = type.ToString().ToUpperInvariant();

        if (iconImage)
        {
            iconImage.enabled = icon != null;
            iconImage.sprite = icon;
        }

        if (autoToggle)
        {
            _suppressToggle = true;
            autoToggle.SetIsOnWithoutNotify(_model != null && _model.autoApply);
            _suppressToggle = false;

            autoToggle.onValueChanged.RemoveAllListeners();
            autoToggle.onValueChanged.AddListener(OnAutoToggleChanged);
        }

        RefreshAutoToggleFeatureGate();
        RefreshOpenInteractable();
        RefreshAutoStateText();

        if (openButton)
        {
            openButton.onClick.RemoveAllListeners();
            openButton.onClick.AddListener(() => _onOpen?.Invoke(_model));
        }
    }

    private void OnEnable()
    {
        GameEvents.OnTeamChanged += HandleTeamChanged;

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked += HandleFeatureUnlocked;

        if (_model != null)
        {
            RefreshAutoToggleFeatureGate();
            RefreshOpenInteractable();
            RefreshAutoStateText();
        }
    }

    private void OnDisable()
    {
        GameEvents.OnTeamChanged -= HandleTeamChanged;

        if (FeatureUnlockManager.I != null)
            FeatureUnlockManager.I.OnFeatureUnlocked -= HandleFeatureUnlocked;
    }

    private void RefreshAutoStateText()
    {
        if (!autoStateText) return;
        bool on = _model != null && _model.autoApply;
        autoStateText.text = on ? "ON" : "OFF";
    }

    private void HandleFeatureUnlocked(FeatureId feature)
    {
        if (feature == FeatureId.AutoGrowth_Basic)
        {
            RefreshAutoToggleFeatureGate();
            RefreshAutoStateText();
        }
    }

    private void HandleTeamChanged()
    {
        if (_model == null) return;

        var data = SaveManager.Data;
        if (data == null) return;

        OwnedMonsterData latest = null;

        if (data.owned != null)
        {
            if (!string.IsNullOrEmpty(_model.ownedUID))
                latest = data.owned.Find(o => o != null && o.ownedUID == _model.ownedUID);

            if (latest == null && !string.IsNullOrEmpty(_model.monsterId))
            {
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

        if (latest == null && data.team != null)
        {
            if (!string.IsNullOrEmpty(_model.ownedUID))
                latest = data.team.Find(t => t != null && t.ownedUID == _model.ownedUID);

            if (latest == null && !string.IsNullOrEmpty(_model.monsterId))
            {
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

            _suppressToggle = true;
            if (autoToggle) autoToggle.SetIsOnWithoutNotify(_model.autoApply);
            _suppressToggle = false;

            RefreshAutoStateText();
            RefreshOpenInteractable();
        }
        else
        {
            RefreshOpenInteractable();
            RefreshAutoStateText();
        }
    }

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

    public void RefreshOpenInteractable()
    {
        if (!openButton) return;

        int cores =
            ResourceManager.I ? ResourceManager.I.Get(ResourceType.GrowthCore)
            : ResourceBank.Get(ResourceType.GrowthCore);

        int unspent = _model != null ? Mathf.Max(0, _model.unspentStatPoints) : 0;

        openButton.interactable = (cores > 0) || (unspent > 0);
    }

    private void OnAutoToggleChanged(bool isOn)
    {
        if (_suppressToggle) return;
        if (_model == null) return;

        if (!IsAutoGrowthUnlocked())
        {
            _suppressToggle = true;
            if (autoToggle) autoToggle.SetIsOnWithoutNotify(false);
            _suppressToggle = false;

            _model.autoApply = false;
            SaveManager.Save();

            RefreshAutoStateText();
            return;
        }

        if (isOn)
        {
            if (_canEnableAnotherAuto != null && !_canEnableAnotherAuto())
            {
                _suppressToggle = true;
                if (autoToggle) autoToggle.SetIsOnWithoutNotify(false);
                _suppressToggle = false;

                RefreshAutoStateText();
                return;
            }

            _model.autoApply = true;

            // IMPORTANT: 0 means "no cap" so Apply will level as far as budget allows
            _model.autoApplyTargetLevel = 0;
        }
        else
        {
            _model.autoApply = false;
        }

        SaveManager.Save();

        RefreshAutoStateText();
        RefreshOpenInteractable();

        GameEvents.OnTeamChanged?.Invoke();

        // IMPORTANT: Do NOT auto-level on toggle. The Gym panel Apply button triggers leveling.
        _onAutoChanged?.Invoke();
    }

    public void RefreshLevel(int newLevel)
    {
        if (levelText) levelText.text = $"Lv {Mathf.Max(1, newLevel)}";
    }

    public void ButtonClick() => AudioManager.I?.PlayClick();
}
