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

    [Tooltip("Component on the parent where the incoming recruit card is spawned.")]
    [SerializeField] private IronCareerIncomingCardUI incomingCard;

    [Tooltip("Parent where party cards are spawned.")]
    [SerializeField] private Transform partyCardParent;

    [Tooltip("Prefab for each party member card.")]
    [SerializeField] private IronCareerPartyCardUI partyCardPrefab;

    [Header("(New) Buttons")]
    [SerializeField] private Button confirmReplaceButton;
    [SerializeField] private Button cancelButton;

    [Header("(New) Text")]
    [SerializeField] private TextMeshProUGUI warningLabel;

    private readonly List<IronCareerPartyCardUI> _spawnedPartyCards = new List<IronCareerPartyCardUI>(3);
    private int _selectedIndex = -1;
    private bool _hardcore;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();

        if (confirmReplaceButton) confirmReplaceButton.onClick.AddListener(OnConfirmPressed);
        if (cancelButton) cancelButton.onClick.AddListener(OnCancelPressed);
        SetConfirmInteractable(false);
    }

    private void OnDestroy()
    {
        if (confirmReplaceButton) confirmReplaceButton.onClick.RemoveAllListeners();
        if (cancelButton) cancelButton.onClick.RemoveAllListeners();

        for (int i = 0; i < _spawnedPartyCards.Count; i++)
            if (_spawnedPartyCards[i]) Destroy(_spawnedPartyCards[i].gameObject);
        _spawnedPartyCards.Clear();
    }

    public void Bind(IReadOnlyList<IronMonster> party)
    {
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

        if (incomingCard) incomingCard.Bind(offer);
        RebuildPartyCards(party);

        if (cancelButton)
        {
            cancelButton.gameObject.SetActive(!_hardcore);
            cancelButton.interactable = !_hardcore;
        }
    }

    private void RebuildPartyCards(IReadOnlyList<IronMonster> party)
    {
        for (int i = 0; i < _spawnedPartyCards.Count; i++)
            if (_spawnedPartyCards[i]) Destroy(_spawnedPartyCards[i].gameObject);
        _spawnedPartyCards.Clear();

        if (partyCardPrefab == null || partyCardParent == null)
        {
            Debug.LogWarning("[IronCareerReplacePanelUI] Missing partyCardPrefab or partyCardParent.");
            return;
        }

        int count = Mathf.Min(3, party != null ? party.Count : 0);
        for (int i = 0; i < count; i++)
        {
            var m = party[i];
            if (m == null || m.def == null) continue;

            var card = Instantiate(partyCardPrefab, partyCardParent);
            card.Bind(m);
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