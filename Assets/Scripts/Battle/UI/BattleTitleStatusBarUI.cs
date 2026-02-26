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

    [Tooltip("Label to show when there is no title to display.")]
    [SerializeField] private string noTitleLabel = "Unemployed";

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
        if (battle == null)
            battle = FindFirstObjectByType<BattleManager>(FindObjectsInactive.Include);

        if (infoButton)
        {
            infoButton.onClick.RemoveAllListeners();
            infoButton.onClick.AddListener(OpenInfo);
        }

        GameEvents.OnTeamChanged += HandleExternalChanged;
        GameEvents.OnBattleStateChanged += HandleExternalChanged;

        HookBattleChangedEvent();

        BattleLogger.OnTitleConditionChanged += HandleTitleConditionChanged;
        ForceRefresh();
    }

    void OnDisable()
    {
        GameEvents.OnTeamChanged -= HandleExternalChanged;
        GameEvents.OnBattleStateChanged -= HandleExternalChanged;

        UnhookBattleChangedEvent();

        BattleLogger.OnTitleConditionChanged -= HandleTitleConditionChanged;

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

        string key = battle.ActivePlayerTitleOwnerId;
        if (string.IsNullOrEmpty(key))
            key = battle.ActivePlayerMonsterId;

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
        if (battle != null && battle.InBattle && TitleManager.I != null)
        {
            string wildOwnerId = battle.WildCombatIdForTitles;
            if (!string.IsNullOrEmpty(wildOwnerId))
            {
                var states = TitleManager.I.GetActiveTitleUIStates(wildOwnerId);
                if (states != null && states.Count > 0)
                {
                    var t = TitleManager.I.GetTitleById(states[0].titleId);
                    if (t != null)
                    {
                        _currentTitle = t;
                        if (iconImage) iconImage.sprite = t.icon;
                        if (titleLabel) titleLabel.text = t.displayName;
                        SetVisible(true);
                        SetInfoInteractable(true);
                        return;
                    }
                }
            }
        }

        var em = EncounterManager.I;
        if (em == null)
        {
            // Wild bar should not disappear in battle, but if EM is missing, do something sensible.
            _currentTitle = null;
            if (iconImage) iconImage.sprite = null;
            if (titleLabel) titleLabel.text = string.IsNullOrEmpty(noTitleLabel) ? "Unemployed" : noTitleLabel;
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

        // In-battle, both sides should show a consistent "no title" state (Unemployed).
        // Out of battle, respect legacy behavior (optional hide for player).
        if (battle != null && battle.InBattle)
        {
            if (titleLabel) titleLabel.text = string.IsNullOrEmpty(noTitleLabel) ? "Unemployed" : noTitleLabel;
            SetVisible(true);
        }
        else
        {
            if (titleLabel) titleLabel.text = "";
            // For wild we never call ApplyEmpty() while in battle, but keep behavior consistent.
            bool visible = (target == TargetKind.Wild) ? true : !hideIfNoTitle;
            SetVisible(visible);
        }

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



private void HandleTitleConditionChanged(string ownerName, string titleName, bool isActive)
{
    if (!isActive) return;
    if (_currentTitle == null) return;

    // Match by title display name (safe across player/wild routing)
    if (!string.Equals(titleName, _currentTitle.displayName, StringComparison.Ordinal))
        return;

    PopIcon();
}

private void PopIcon()
{
    if (iconImage == null) return;
    var tr = iconImage.transform;
    if (!tr) return;

    LeanTween.cancel(tr.gameObject);
    tr.localScale = Vector3.one;
    LeanTween.scale(tr.gameObject, Vector3.one * 1.15f, 0.08f)
             .setEaseOutBack()
             .setOnComplete(() =>
             {
                 if (!tr) return;
                 LeanTween.scale(tr.gameObject, Vector3.one, 0.10f).setEaseInOutQuad();
             });
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
