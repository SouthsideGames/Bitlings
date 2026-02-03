// Assets/Scripts/Battle/UI/BattleSpeedToggleUI.cs
using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class BattleSpeedToggleUI : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private Button speedButton;
    [SerializeField] private TextMeshProUGUI speedLabel;

    [Header("Optional")]
    [SerializeField] private GameObject lockedBadge; 

    [Header("Speed Steps (must match BattleManager clamp range)")]
    [SerializeField] private float[] speeds = new float[] { 1f, 2f, 3f };

    private BattleManager _battle;
    private bool _isInteractable = true;

    private void Awake()
    {
        if (speedButton != null)
            speedButton.onClick.AddListener(OnSpeedPressed);
    }

    private void OnEnable()
    {
        GameEvents.OnEncounterAutoModeChanged += HandleAutoModeChanged;
        GameEvents.OnSettingsApplied += HandleSettingsApplied;

        ResolveBattleManager();
        RefreshLabelFromBattle();
    }

    private void OnDisable()
    {
        GameEvents.OnEncounterAutoModeChanged -= HandleAutoModeChanged;
        GameEvents.OnSettingsApplied -= HandleSettingsApplied;
    }

    private void ResolveBattleManager()
    {
        if (_battle != null) return;
        _battle = FindFirstObjectByType<BattleManager>();
    }

    private void OnSpeedPressed()
    {
        if (!_isInteractable) return;

        ResolveBattleManager();
        if (_battle == null) return;

        _battle.CycleBattleSpeed();

        RefreshLabelFromBattle();
    }

    private void RefreshLabelFromBattle()
    {
        ResolveBattleManager();

        float s = 1f;

        if (_battle != null)
            s = Mathf.Clamp(_battle.BattleSpeed, 0.25f, 5f);
        else if (SaveManager.Data != null && SaveManager.Data.settings != null)
            s = Mathf.Clamp(SaveManager.Data.settings.battleSpeed, 0.25f, 5f);

        if (speedLabel != null)
        {
            speedLabel.text = $"Speed: {FormatSpeed(s)}";
        }
    }

    private string FormatSpeed(float s)
    {
        if (Mathf.Abs(s - 1f) < 0.01f) return "1x";
        if (Mathf.Abs(s - 2f) < 0.01f) return "2x";
        if (Mathf.Abs(s - 3f) < 0.01f) return "3x";
        return $"{s:0.##}x";
    }

    public void SetInteractable(bool canInteract)
    {
        _isInteractable = canInteract;

        if (speedButton != null)
            speedButton.interactable = canInteract;

        if (lockedBadge != null)
            lockedBadge.SetActive(!canInteract);
    }

    private void HandleAutoModeChanged()
    {
        bool isAuto = (EncounterManager.I != null) && EncounterManager.I.IsAutoMode;
        RefreshLabelFromBattle();
    }

    private void HandleSettingsApplied()
    {
        RefreshLabelFromBattle();
    }
}
