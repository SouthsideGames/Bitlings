using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;


public sealed class IronCareerRestPanelUI : MonoBehaviour
{
    public enum RestOption
    {
        None = 0,
        Heal25 = 1,
        RandomLevelUp = 2
    }

    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;

    [Header("Canvas")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleTMP;
    [SerializeField] private TextMeshProUGUI subtitleTMP;
    [SerializeField] private TextMeshProUGUI metaTMP;

    [Header("Party List")]
    [SerializeField] private TextMeshProUGUI partyHeaderTMP;
    [SerializeField] private Transform partyListParent;
    [SerializeField] private IronCareerRestPartyCardUI partyCardPrefab;

    [Header("Options")]
    [SerializeField] private TextMeshProUGUI optionsHeaderTMP;
    [SerializeField] private Transform optionListParent;
    [SerializeField] private IronCareerRestOptionItemUI restOptionPrefab;

    [Header("Bottom Bar")]
    [SerializeField] private Button confirmButton;
    [SerializeField] private TextMeshProUGUI confirmLabelTMP;
    [SerializeField] private Button continueButton;
    [SerializeField] private TextMeshProUGUI continueLabelTMP;

    [Header("Optional Feedback")]
    [SerializeField] private TextMeshProUGUI resultTMP;

    private readonly List<IronCareerRestPartyCardUI> _partyCards = new List<IronCareerRestPartyCardUI>(4);
    private readonly List<IronCareerRestOptionItemUI> _optionItems = new List<IronCareerRestOptionItemUI>(4);

    private RestOption _selected = RestOption.None;
    private bool _applied;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();

        if (confirmButton) confirmButton.onClick.AddListener(OnConfirm);
        if (continueButton) continueButton.onClick.AddListener(OnContinue);

        SetApplied(false);
        SetSelected(RestOption.None);
    }

    private void OnDestroy()
    {
        if (confirmButton) confirmButton.onClick.RemoveListener(OnConfirm);
        if (continueButton) continueButton.onClick.RemoveListener(OnContinue);
    }

    public void Bind(IReadOnlyList<IronMonster> party, int wins, bool hardcoreMode)
    {
        if (titleTMP) titleTMP.text = "REST NODE";
        if (subtitleTMP) subtitleTMP.text = "Choose one benefit. No revives allowed.";
        if (metaTMP) metaTMP.text = $"Win Streak: {wins}   Mode: {(hardcoreMode ? "Hardcore" : "Standard")}";

        if (partyHeaderTMP) partyHeaderTMP.text = "YOUR PARTY (Current HP)";
        if (optionsHeaderTMP) optionsHeaderTMP.text = "REST OPTIONS";

        if (confirmLabelTMP) confirmLabelTMP.text = "CONFIRM CHOICE";
        if (continueLabelTMP) continueLabelTMP.text = "CONTINUE";

        if (resultTMP) resultTMP.text = string.Empty;

        RebuildParty(party);
        RebuildOptions();

        SetOptionsInteractable(true);

        SetApplied(false);
        SetSelected(RestOption.None);

        if (canvasGroup)
        {
            canvasGroup.alpha = 1f;
            canvasGroup.interactable = true;
            canvasGroup.blocksRaycasts = true;
        }
    }

    private void RebuildParty(IReadOnlyList<IronMonster> party)
    {
        // Clear old
        for (int i = 0; i < _partyCards.Count; i++)
        {
            if (_partyCards[i]) Destroy(_partyCards[i].gameObject);
        }
        _partyCards.Clear();

        if (!partyListParent || !partyCardPrefab) return;

        int count = (party != null) ? party.Count : 0;
        for (int i = 0; i < count; i++)
        {
            var m = party[i];
            if (m == null || m.def == null) continue;

            var card = Instantiate(partyCardPrefab, partyListParent);
            card.Bind(m);
            _partyCards.Add(card);
        }
    }

    private void RebuildOptions()
    {
        for (int i = 0; i < _optionItems.Count; i++)
        {
            if (_optionItems[i]) Destroy(_optionItems[i].gameObject);
        }
        _optionItems.Clear();

        if (!optionListParent || !restOptionPrefab) return;

        // Option A: Heal 25%
        var heal = Instantiate(restOptionPrefab, optionListParent);
        heal.Bind(
            option: RestOption.Heal25,
            title: "🩹 HEAL PARTY (25%)",
            desc: "Heal all living monsters by 25% of their max HP. No revives.",
            preview: "Reliable attrition relief."
        );
        heal.SetSelected(false);
        heal.SetOnClick(() => OnOptionClicked(RestOption.Heal25));
        _optionItems.Add(heal);

        // Option B: Training (random)
        var train = Instantiate(restOptionPrefab, optionListParent);
        train.Bind(
            option: RestOption.RandomLevelUp,
            title: "⭐ TRAINING (+1 Level)",
            desc: "Random living monster gains +1 level. HP% is preserved.",
            preview: "High variance. Long-term scaling."
        );
        train.SetSelected(false);
        train.SetOnClick(() => OnOptionClicked(RestOption.RandomLevelUp));
        _optionItems.Add(train);
    }

    private void OnOptionClicked(RestOption option)
    {
        if (_applied) return;
        SetSelected(option);
    }

    private void SetSelected(RestOption option)
    {
        _selected = option;

        for (int i = 0; i < _optionItems.Count; i++)
        {
            if (_optionItems[i]) _optionItems[i].SetSelected(_optionItems[i].Option == option);
        }

        if (confirmButton) confirmButton.interactable = option != RestOption.None;
    }

    private void SetApplied(bool applied)
    {
        _applied = applied;

        if (confirmButton) confirmButton.gameObject.SetActive(!applied);
        if (continueButton) continueButton.gameObject.SetActive(applied);

        // Keep one of them interactable, prevent double apply.
        if (confirmButton) confirmButton.interactable = !applied && _selected != RestOption.None;
        if (continueButton) continueButton.interactable = applied;

        SetOptionsInteractable(!applied);
    }

    private void SetOptionsInteractable(bool interactable)
    {
        for (int i = 0; i < _optionItems.Count; i++)
        {
            if (_optionItems[i]) _optionItems[i].gameObject.SetActive(interactable);
        }
    }

    private void OnConfirm()
    {
        if (_applied) return;
        if (_selected == RestOption.None) return;

        if (!manager)
        {
            SetApplied(true);
            return;
        }

        switch (_selected)
        {
            case RestOption.Heal25:
                manager.OnRestHeal();
                if (resultTMP) resultTMP.text = "Healed party by 25% (living only).";
                break;

            case RestOption.RandomLevelUp:
                string who = manager.OnRestRandomLevelUp();
                if (resultTMP) resultTMP.text = string.IsNullOrEmpty(who) ? "Training applied." : $"Training: {who} gained +1 level.";
                break;
        }

        // Manager will not advance battle immediately anymore; this panel controls Continue.
        // So we call the manager's "rest applied" hook, then we show Continue.
        manager.OnRestAppliedOnly();
        SetApplied(true);
    }

    private void OnContinue()
    {
        if (!manager) return;
        manager.OnRestContinue();
    }
}
