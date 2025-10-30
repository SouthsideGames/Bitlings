using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TitleButtonUI : MonoBehaviour
{
    [Header("Wiring")]
    [SerializeField] private Button button;                 // main Title button
    [SerializeField] private TMP_Text label;                // text on main Title button
    [SerializeField] private TitleAssignPanelUI titleAssignPanel;

    [Header("New Title Badge/Button")]
    [Tooltip("A small badge/button (e.g., 'New!') that appears when a new title is unlocked for this monster.")]
    [SerializeField] private Button newTitleBtn;

    // Current monster context
    private string _ownedMonsterId;
    private MonsterDataSO _monsterDef;
    private int _level;

    // --------- Global notification plumbing ----------
    private const string PPREFIX = "TitleNewBadgeSeen_"; // PlayerPrefs key prefix

    private static event System.Action<string> OnGlobalNewTitleUnlocked; // ownedId
    private static readonly HashSet<string> _pendingOwnedIds = new();    // pending badges (session)

    /// <summary>
    /// Call this from your unlock code when a title is newly unlocked for a specific owned monster.
    /// Example: TitleButtonUI.NotifyNewTitleUnlocked(ownedId);
    /// </summary>
    public static void NotifyNewTitleUnlocked(string ownedMonsterId)
    {
        if (string.IsNullOrEmpty(ownedMonsterId)) return;
        _pendingOwnedIds.Add(ownedMonsterId);
        OnGlobalNewTitleUnlocked?.Invoke(ownedMonsterId);
    }
    // -------------------------------------------------

    private void Reset()
    {
        if (!button) button = GetComponent<Button>();
        if (!label)  label  = GetComponentInChildren<TMP_Text>(true);
        // newTitleBtn is optional—wire in inspector
    }

    private void Awake()
    {
        if (button != null) button.onClick.AddListener(OpenPanel);

        // live label updates when titles change
        TitleAssignPanelUI.OnTitlesChanged -= HandleTitlesChanged;
        TitleAssignPanelUI.OnTitlesChanged += HandleTitlesChanged;

        // new-unlock signal
        OnGlobalNewTitleUnlocked -= HandleGlobalNewTitleUnlocked;
        OnGlobalNewTitleUnlocked += HandleGlobalNewTitleUnlocked;

        // clicking the badge clears it (until another unlock happens)
        if (newTitleBtn != null)
        {
            newTitleBtn.onClick.RemoveAllListeners();
            newTitleBtn.onClick.AddListener(ClearNewBadgeForThisMonster);
        }
    }

    private void OnDestroy()
    {
        TitleAssignPanelUI.OnTitlesChanged -= HandleTitlesChanged;
        OnGlobalNewTitleUnlocked -= HandleGlobalNewTitleUnlocked;
    }

    private void OnEnable()
    {
        RefreshLabel();
        RefreshNewBadge();
    }

    public void Bind(string ownedMonsterId, MonsterDataSO def, int level)
    {
        _ownedMonsterId = ownedMonsterId;
        _monsterDef     = def;
        _level          = Mathf.Max(1, level);

        RefreshLabel();
        RefreshNewBadge();
    }

    // ─────────────────────────────────────────────────────────────
    // Label logic: show first equipped titleId, else UNEMPLOYED
    // ─────────────────────────────────────────────────────────────
    private void HandleTitlesChanged(string ownedId)
    {
        if (!string.IsNullOrEmpty(_ownedMonsterId) && ownedId == _ownedMonsterId)
        {
            RefreshLabel();
            // Changing equipment isn't necessarily a new unlock, so we DO NOT auto-clear the badge here.
        }
    }

    public void RefreshLabel()
    {
        if (label == null)
            return;

        if (string.IsNullOrEmpty(_ownedMonsterId) || _monsterDef == null)
        {
            label.text = "UNEMPLOYED";
            return;
        }

        List<TitleSO> equipped = TitleManager.I.GetEquippedList(_ownedMonsterId, _monsterDef, _level);

        if (equipped != null)
        {
            for (int i = 0; i < equipped.Count; i++)
            {
                var t = equipped[i];
                if (t != null)
                {
                    label.text = !string.IsNullOrEmpty(t.titleId) ? t.titleId : "UNEMPLOYED";
                    return;
                }
            }
        }

        label.text = "UNEMPLOYED";
    }

    // ─────────────────────────────────────────────────────────────
    // New badge logic
    // ─────────────────────────────────────────────────────────────
    private void HandleGlobalNewTitleUnlocked(string ownedId)
    {
        if (string.IsNullOrEmpty(_ownedMonsterId) || newTitleBtn == null) return;

        // Only show for the monster bound here
        if (ownedId == _ownedMonsterId)
        {
            // mark as pending this session
            _pendingOwnedIds.Add(ownedId);
            // clear "seen" so it shows again if they had previously dismissed
            PlayerPrefs.DeleteKey(PPREFIX + ownedId);

            ShowNewBadge(true);
        }
    }

    private void RefreshNewBadge()
    {
        if (newTitleBtn == null)
            return;

        if (string.IsNullOrEmpty(_ownedMonsterId))
        {
            ShowNewBadge(false);
            return;
        }

        // If we have a pending in this session OR no "seen" flag stored, show; else hide.
        bool sessionPending = _pendingOwnedIds.Contains(_ownedMonsterId);
        bool seenFlag = PlayerPrefs.GetInt(PPREFIX + _ownedMonsterId, 0) == 1;

        bool shouldShow = sessionPending && !seenFlag;
        ShowNewBadge(shouldShow);
    }

    private void ShowNewBadge(bool visible)
    {
        if (!newTitleBtn) return;

        if (visible && !newTitleBtn.gameObject.activeSelf)
        {
            newTitleBtn.gameObject.SetActive(true);

            // Optional little pop
            var t = newTitleBtn.transform;
            t.localScale = Vector3.one * 0.85f;
            LeanTween.scale(t.gameObject, Vector3.one, 0.12f).setEaseOutBack();
        }
        else if (!visible && newTitleBtn.gameObject.activeSelf)
        {
            newTitleBtn.gameObject.SetActive(false);
        }
    }

    private void ClearNewBadgeForThisMonster()
    {
        if (string.IsNullOrEmpty(_ownedMonsterId) || newTitleBtn == null) return;

        // mark as seen for this monster
        PlayerPrefs.SetInt(PPREFIX + _ownedMonsterId, 1);
        PlayerPrefs.Save();

        // no longer pending this session
        _pendingOwnedIds.Remove(_ownedMonsterId);

        ShowNewBadge(false);
    }

    // ─────────────────────────────────────────────────────────────
    // Open panel
    // ─────────────────────────────────────────────────────────────
    private void OpenPanel()
    {
        if (string.IsNullOrEmpty(_ownedMonsterId) || _monsterDef == null)
            return;

        if (titleAssignPanel == null)
        {
            var root = UIManager.I ? UIManager.I.GetRoot(PanelId.TitleDetail) : null;
            if (root != null)
                titleAssignPanel = root.GetComponentInChildren<TitleAssignPanelUI>(true);
        }

        if (titleAssignPanel == null)
        {
            Debug.LogWarning("[TitleButtonUI] TitleAssignPanelUI not found. Add it to the TitleDetail panel root.");
            return;
        }

        titleAssignPanel.Open(_ownedMonsterId, _monsterDef, _level);
    }
}
