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

    [Tooltip("Component on the parent where party cards are spawned.")]
    [SerializeField] private IronCareerPartyCardListUI partyCardList;

    [Header("(New) Buttons")]
    [SerializeField] private Button confirmReplaceButton;
    [SerializeField] private Button cancelButton;

    [Header("(New) Text")]
    [SerializeField] private TextMeshProUGUI warningLabel;

    private bool _hardcore;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();

        if (confirmReplaceButton) confirmReplaceButton.onClick.AddListener(OnConfirmPressed);
        if (cancelButton) cancelButton.onClick.AddListener(OnCancelPressed);
        if (partyCardList) partyCardList.OnSelectionChanged += OnPartySelectionChanged;
        SetConfirmInteractable(false);
    }

    private void OnDestroy()
    {
        if (confirmReplaceButton) confirmReplaceButton.onClick.RemoveAllListeners();
        if (cancelButton) cancelButton.onClick.RemoveAllListeners();
        if (partyCardList) partyCardList.OnSelectionChanged -= OnPartySelectionChanged;
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
        SetConfirmInteractable(false);

        if (warningLabel)
            warningLabel.text = "\u26A0 Selected monster will be permanently dismissed. This cannot be undone.";

        if (incomingCard) incomingCard.Bind(offer);
        if (partyCardList) partyCardList.Bind(party);

        if (cancelButton)
        {
            cancelButton.gameObject.SetActive(!_hardcore);
            cancelButton.interactable = !_hardcore;
        }
    }

    private void OnPartySelectionChanged(int index)
    {
        SetConfirmInteractable(index >= 0);
    }

    private void OnConfirmPressed()
    {
        if (partyCardList == null || partyCardList.SelectedIndex < 0) return;
        manager?.OnReplaceChosen(partyCardList.SelectedIndex);
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