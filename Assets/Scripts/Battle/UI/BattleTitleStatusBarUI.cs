using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Reflection;

public sealed class BattleTitleStatusBarUI : MonoBehaviour
{
    private enum TargetKind { Player, Wild }

    [Header("Refs")]
    [SerializeField] private BattleManager battle;

    [Header("Target")]
    [SerializeField] private TargetKind target = TargetKind.Player;

    [Header("UI")]
    [SerializeField] private GameObject visualsRoot; // child container to hide/show
    [SerializeField] private Image iconImage;
    [SerializeField] private TextMeshProUGUI titleLabel;
    [SerializeField] private Button infoButton;

    [Header("Rules")]
    [Tooltip("For Wild target, this is ignored (wild bar always shows Unemployed or a title).")]
    [SerializeField] private bool hideIfNoTitle = true;

    [Tooltip("If true, wild bar shows rolled title first; if none rolled, shows first always-on title if present; otherwise Unemployed.")]
    [SerializeField] private bool wildPreferRolledTitle = true;

    private string _lastKey;
    private TitleSO _currentTitle;

    private EventInfo _battleChangedEvent;
    private object _battleEventOwner;

    void Awake()
    {
        if (visualsRoot == null) visualsRoot = gameObject;
    }

    void OnEnable()
    {
        if (infoButton)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OpenInfo);
        }

        GameEvents.OnTeamChanged += HandleExternalChanged;
        GameEvents.OnBattleStateChanged += HandleExternalChanged;

        HookBattleChangedEvent();
        ForceRefresh();
    }

    void OnDisable()
    {
        GameEvents.OnTeamChanged -= HandleExternalChanged;
        GameEvents.OnBattleStateChanged -= HandleExternalChanged;

        UnhookBattleChangedEvent();

        if (infoButton) infoButton.onClick.RemoveAllListeners();
    }

    private void HandleExternalChanged() => ForceRefresh();

    public void ForceRefresh()
    {
        if (battle == null || !battle.InBattle)
        {
            ApplyEmpty();
            return;
        }

        if (target == TargetKind.Wild)
        {
            RefreshWild_FromEncounter();
            return;
        }

        // Player path
        if (TitleManager.I == null)
        {
            ApplyEmpty();
            return;
        }

        string key = battle.ActivePlayerMonsterId;

        if (key == _lastKey && _currentTitle != null)
            return;

        _lastKey = key;
        RefreshForPlayerMonsterId(key);
    }

    // ---------------------------
    // Wild display (Encounter-scoped)
    // ---------------------------
    private void RefreshWild_FromEncounter()
    {
        var em = EncounterManager.I;
        if (em == null)
        {
            // Wild bar should not disappear in battle, but if EM is missing, do something sensible.
            _currentTitle = null;
            if (iconImage) iconImage.sprite = null;
            if (titleLabel) titleLabel.text = "Unemployed";
            SetVisible(true);
            SetInfoInteractable(false);
            return;
        }

        TitleSO chosen = null;

        if (wildPreferRolledTitle && em.WildRolledTitle != null)
        {
            chosen = em.WildRolledTitle;
        }
        else
        {
            // If no rolled title, show first active title if present (typically always-on)
            var actives = em.WildActiveTitles;
            if (actives != null && actives.Count > 0 && actives[0] != null)
                chosen = actives[0];
        }

        _currentTitle = chosen;

        if (_currentTitle != null)
        {
            if (iconImage) iconImage.sprite = _currentTitle.icon;
            if (titleLabel) titleLabel.text = _currentTitle.displayName;
            SetVisible(true);
            SetInfoInteractable(true);
            return;
        }

        // Truly no title: show unemployed label text only
        if (iconImage) iconImage.sprite = null;
        if (titleLabel) titleLabel.text = em.WildTitleLabel; // "Unemployed" (or your configured label)
        SetVisible(true);
        SetInfoInteractable(false);
    }

    // ---------------------------
    // Player display (TitleManager)
    // ---------------------------
    private void RefreshForPlayerMonsterId(string monsterId)
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
        SetInfoInteractable(true);
    }

    private void ApplyEmpty()
    {
        _currentTitle = null;

        if (iconImage) iconImage.sprite = null;
        if (titleLabel) titleLabel.text = "";

        // For wild we never call ApplyEmpty() while in battle, but keep behavior consistent.
        bool visible = (target == TargetKind.Wild) ? true : !hideIfNoTitle;
        SetVisible(visible);
        SetInfoInteractable(false);
    }

    private void SetVisible(bool visible)
    {
        if (visualsRoot != null)
            visualsRoot.SetActive(visible);
    }

    private void SetInfoInteractable(bool interactable)
    {
        if (!infoButton) return;

        infoButton.interactable = interactable;

        // Optional: dim the icon button when disabled
        var cg = infoButton.GetComponent<CanvasGroup>();
        if (cg) cg.alpha = interactable ? 1f : 0.35f;
    }

    private void OpenInfo()
    {
        if (_currentTitle == null) return;

        var id = $"title.{_currentTitle.titleId}";
        InfoRouter.Open(
            id,
            _currentTitle.displayName,
            target == TargetKind.Wild ? "Wild Title" : "Active Title",
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
