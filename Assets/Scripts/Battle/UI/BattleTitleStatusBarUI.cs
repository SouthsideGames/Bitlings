using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Reflection;

public sealed class BattleTitleStatusBarUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private BattleManager battle;

    [Header("UI")]
    [SerializeField] private GameObject visualsRoot; // NEW: child container to hide/show
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private Button infoButton;

    [Header("Rules")]
    [SerializeField] private bool hideIfNoTitle = true;

    private string _lastMonsterId;
    private TitleSO _currentTitle;

    private EventInfo _battleChangedEvent;
    private object _battleEventOwner;

    void Awake()
    {
        // If not assigned, default to this object (but we still won’t disable this GO)
        if (visualsRoot == null) visualsRoot = gameObject;
    }

    void OnEnable()
    {
        if (infoButton)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OpenInfo);
        }

        GameEvents.OnTeamChanged += HandleTeamChanged; // fallback signal

        HookBattleChangedEvent();

        ForceRefresh();
    }

    void OnDisable()
    {
        GameEvents.OnTeamChanged -= HandleTeamChanged;
        UnhookBattleChangedEvent();

        if (infoButton) infoButton.onClick.RemoveAllListeners();
    }

    private void HandleTeamChanged() => ForceRefresh();

    public void ForceRefresh()
    {
        // If battle isn’t ready yet, just hide visuals. Do NOT deactivate this GO.
        if (battle == null || !battle.InBattle || TitleManager.I == null)
        {
            ApplyEmpty();
            return;
        }

        var monsterId = battle.ActivePlayerMonsterId;

        // Only skip refresh if the monster hasn’t changed and we already have a title.
        if (monsterId == _lastMonsterId && _currentTitle != null)
            return;

        _lastMonsterId = monsterId;
        RefreshForMonster(monsterId);
    }

    private void RefreshForMonster(string monsterId)
    {
        _currentTitle = null;

        if (string.IsNullOrEmpty(monsterId))
        {
            ApplyEmpty();
            return;
        }

        var states = TitleManager.I.GetActiveTitleUIStates(monsterId);
        if (states == null || states.Count == 0)
        {
            ApplyEmpty();
            return;
        }

        var s = states[0];
        var title = TitleManager.I.GetTitleById(s.titleId);
        if (!title)
        {
            ApplyEmpty();
            return;
        }

        _currentTitle = title;

        if (iconImage) iconImage.sprite = title.icon;
        if (titleLabel) titleLabel.text = title.displayName;

        SetVisible(true);
    }

    private void ApplyEmpty()
    {
        _currentTitle = null;

        if (iconImage) iconImage.sprite = null;
        if (titleLabel) titleLabel.text = "";

        SetVisible(!hideIfNoTitle);
    }

    private void SetVisible(bool visible)
    {
        // Only toggle the visuals container (child), never the whole GameObject.
        if (visualsRoot != null)
            visualsRoot.SetActive(visible);
    }

    private void OpenInfo()
    {
        if (_currentTitle == null) return;

        var id = $"title.{_currentTitle.titleId}";
        InfoRouter.Open(
            id,
            _currentTitle.displayName,
            "Active Title",
            _currentTitle.description
        );

        AudioManager.I?.PlayClick();
    }

    // ---- Optional BattleManager event hook (reflection) ----
    private void HookBattleChangedEvent()
    {
        if (battle == null) return;

        var t = battle.GetType();
        _battleChangedEvent =
            t.GetEvent("OnActiveMonsterChanged") ??
            t.GetEvent("OnStateChanged") ??
            t.GetEvent("OnTurnChanged");

        if (_battleChangedEvent == null) return;

        try
        {
            var handler = Delegate.CreateDelegate(_battleChangedEvent.EventHandlerType, this, nameof(OnBattleChanged));
            _battleChangedEvent.AddEventHandler(battle, handler);
            _battleEventOwner = handler;
        }
        catch
        {
            _battleChangedEvent = null;
            _battleEventOwner = null;
        }
    }

    private void UnhookBattleChangedEvent()
    {
        if (battle == null || _battleChangedEvent == null || _battleEventOwner == null) return;

        try { _battleChangedEvent.RemoveEventHandler(battle, (Delegate)_battleEventOwner); }
        catch { }

        _battleChangedEvent = null;
        _battleEventOwner = null;
    }

    private void OnBattleChanged() => ForceRefresh();
}
