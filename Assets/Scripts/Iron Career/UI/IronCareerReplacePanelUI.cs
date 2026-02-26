using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: IronCareerReplacePanelUI
/// Choose one party slot to dismiss in order to add the offered hire.
/// </summary>
public sealed class IronCareerReplacePanelUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("(New) Vertical Mobile Layout")]
    [Tooltip("Optional CanvasGroup for fade + input gating.")]
    [SerializeField] private CanvasGroup panelGroup;

    [Tooltip("Optional parent where the incoming recruit card prefab is spawned.")]
    [SerializeField] private Transform incomingCardParent;

    [Tooltip("Prefab used for the locked incoming recruit card.")]
    [SerializeField] private IronCareerMonsterCardUI incomingCardPrefab;

    [Tooltip("Parent under ScrollRect Content (VerticalLayoutGroup) where party cards are spawned.")]
    [SerializeField] private Transform partyListParent;

    [Tooltip("Card prefab used for each party member.")]
    [SerializeField] private IronCareerMonsterCardUI partyCardPrefab;

    [Header("(New) Buttons")]
    [SerializeField] private Button confirmReplaceButton;
    [SerializeField] private Button cancelButton;

    [Header("(New) Text")]
    [SerializeField] private TextMeshProUGUI warningLabel;

    private readonly List<IronCareerMonsterCardUI> _spawnedPartyCards = new List<IronCareerMonsterCardUI>(3);
    private IronCareerMonsterCardUI _spawnedIncomingCard;
    private int _selectedIndex = -1;
    private bool _hardcore;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();

        // New layout wiring
        if (confirmReplaceButton) confirmReplaceButton.onClick.AddListener(OnConfirmPressed);
        if (cancelButton) cancelButton.onClick.AddListener(OnCancelPressed);
        SetConfirmInteractable(false);
    }

    private void OnDestroy()
    {
        if (confirmReplaceButton) confirmReplaceButton.onClick.RemoveAllListeners();
        if (cancelButton) cancelButton.onClick.RemoveAllListeners();

        if (_spawnedIncomingCard) Destroy(_spawnedIncomingCard.gameObject);
        for (int i = 0; i < _spawnedPartyCards.Count; i++)
            if (_spawnedPartyCards[i]) Destroy(_spawnedPartyCards[i].gameObject);
        _spawnedPartyCards.Clear();
    }

    public void Bind(IReadOnlyList<IronMonster> party)
    {
        // Backwards compatible entry point: no incoming recruit shown.
        Bind(party, offer: null, hardcoreMode: false);
    }

    /// <summary>
    /// Mobile/vertical bind: show incoming recruit + spawn party cards for selection.
    /// </summary>
    public void Bind(IReadOnlyList<IronMonster> party, IronMonster offer, bool hardcoreMode)
    {
        _hardcore = hardcoreMode;
        _selectedIndex = -1;
        SetConfirmInteractable(false);

        if (warningLabel)
            warningLabel.text = "\u26A0 Selected monster will be permanently dismissed. This cannot be undone.";

        RebuildIncomingCard(offer);
        RebuildPartyList(party);

        if (cancelButton)
        {
            cancelButton.gameObject.SetActive(!_hardcore);
            cancelButton.interactable = !_hardcore;
        }
    }

    private void RebuildIncomingCard(IronMonster offer)
    {
        if (_spawnedIncomingCard)
        {
            Destroy(_spawnedIncomingCard.gameObject);
            _spawnedIncomingCard = null;
        }

        if (incomingCardPrefab != null && incomingCardParent != null)
        {
            _spawnedIncomingCard = Instantiate(incomingCardPrefab, incomingCardParent);
            _spawnedIncomingCard.Bind(offer, isLocked: true, isSelectable: false);
            return;
        }

        Debug.LogWarning("[IronCareerReplacePanelUI] Missing incomingCardPrefab or incomingCardParent; incoming recruit card cannot be shown.");
    }

    private void RebuildPartyList(IReadOnlyList<IronMonster> party)
    {
        if (partyCardPrefab == null || partyListParent == null)
        {
            Debug.LogWarning("[IronCareerReplacePanelUI] Missing partyCardPrefab or partyListParent; cannot build replace list.");
            return;
        }

        // Clear
        for (int i = 0; i < _spawnedPartyCards.Count; i++)
        {
            if (_spawnedPartyCards[i]) Destroy(_spawnedPartyCards[i].gameObject);
        }
        _spawnedPartyCards.Clear();

        int count = Mathf.Min(3, party != null ? party.Count : 0);
        for (int i = 0; i < count; i++)
        {
            var m = party[i];
            if (m == null || m.def == null) continue;

            var card = Instantiate(partyCardPrefab, partyListParent);
            card.Bind(m, isLocked: false, isSelectable: true);
            card.SetSelected(false);

            int idx = i;
            card.SetOnClick(() => OnPartyCardPressed(idx));

            _spawnedPartyCards.Add(card);
        }
    }

    private void OnPartyCardPressed(int index)
    {
        _selectedIndex = index;
        for (int i = 0; i < _spawnedPartyCards.Count; i++)
        {
            if (_spawnedPartyCards[i]) _spawnedPartyCards[i].SetSelected(i == _selectedIndex);
        }
        SetConfirmInteractable(_selectedIndex >= 0);
    }

    private void OnConfirmPressed()
    {
        if (_selectedIndex < 0) return;
        manager?.OnReplaceChosen(_selectedIndex);
    }

    private void OnCancelPressed()
    {
        if (_hardcore) return;
        manager?.OnReplaceCancelled();
    }

    private void SetConfirmInteractable(bool on)
    {
        if (confirmReplaceButton) confirmReplaceButton.interactable = on;
        if (panelGroup)
        {
            panelGroup.blocksRaycasts = true;
            panelGroup.interactable = true;
        }
    }
}