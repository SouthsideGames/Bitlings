using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Phase 3.A: IronCareerPostScreenUI
/// Party overview after hire/replace.
/// Shows HP + Title + (single) carried status icon.
/// </summary>
public sealed class IronCareerPostScreenUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private IronCareerManager manager;
    [SerializeField] private StatusLibrarySO statusLibrary;

    [Header("Root")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Header")]
    [SerializeField] private TextMeshProUGUI titleTMP; // "POST-BATTLE STATUS"
    [SerializeField] private TextMeshProUGUI metaTMP;  // Floor / Streak / Mode

    [Header("Body")]
    [SerializeField] private ScrollRect bodyScrollRect;

    [Header("SummarySection")]
    [SerializeField] private GameObject summarySection;
    [SerializeField] private TextMeshProUGUI summaryTMP;

    [Header("Party")]
    [SerializeField] private TextMeshProUGUI partyHeaderTMP;
    [SerializeField] private Transform partyListParent; // VerticalLayoutGroup under ScrollRect Content
    [SerializeField] private IronCareerMonsterCardUI partyCardPrefab;

    [Header("Carry (single status)")]
    [SerializeField] private Image statusIcon;
    [SerializeField] private TextMeshProUGUI statusName;

    [Header("ChecksSection")]
    [SerializeField] private GameObject checksSection;
    [SerializeField] private TextMeshProUGUI checksTMP;

    [Header("MilestoneSection")]
    [SerializeField] private GameObject milestoneSection;
    [SerializeField] private TextMeshProUGUI milestoneTMP;

    [Header("BottomBar")]
    [SerializeField] private Button continueButton;
    [SerializeField] private Button quitButton;

    private readonly List<IronCareerMonsterCardUI> _spawnedPartyCards = new List<IronCareerMonsterCardUI>(3);

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();
        if (continueButton) continueButton.onClick.AddListener(() => manager?.OnPostContinue());
        if (quitButton) quitButton.onClick.AddListener(() => manager?.RequestQuit());

        if (titleTMP && string.IsNullOrEmpty(titleTMP.text))
            titleTMP.text = "POST-BATTLE STATUS";
    }

    private void OnDestroy()
    {
        if (continueButton) continueButton.onClick.RemoveAllListeners();
        if (quitButton) quitButton.onClick.RemoveAllListeners();
    }

    public void Bind(IReadOnlyList<IronMonster> party, IronFieldStatusSnapshot carry, int wins)
    {
        // Back-compat path: no outcome provided.
        BindInternal(party, carry, wins, hasOutcome: false, outcome: default);
    }

    public void Bind(IReadOnlyList<IronMonster> party, IronFieldStatusSnapshot carry, int wins, IronBattleOutcome outcome)
    {
        BindInternal(party, carry, wins, hasOutcome: true, outcome: outcome);
    }

    private void BindInternal(IReadOnlyList<IronMonster> party, IronFieldStatusSnapshot carry, int wins, bool hasOutcome, IronBattleOutcome outcome)
    {
        // Header/meta
        int safeWins = Mathf.Max(0, wins);
        int floor = safeWins + 1;
        string mode = (manager != null && manager.IsHardcoreMode) ? "Hardcore" : "Standard";
        if (metaTMP) metaTMP.text = $"Floor: {floor}   Win Streak: {safeWins}   Mode: {mode}";

        // Summary
        if (summarySection) summarySection.SetActive(hasOutcome);
        if (hasOutcome && summaryTMP)
        {
            string wildName = (outcome.wildDef != null) ? outcome.wildDef.displayName : "Unknown";
            int wildLvl = Mathf.Max(1, outcome.wildLevel);
            int turns = Mathf.Max(0, outcome.turnsSurvived);
            int secs = Mathf.Max(0, Mathf.RoundToInt(outcome.secondsSurvived));
            string result = outcome.victory ? "VICTORY" : (outcome.escaped ? "ESCAPED" : "DEFEAT");
            summaryTMP.text = $"Result: {result}\nWild: {wildName} (Lv {wildLvl})\nTurns: {turns}   Time: {secs}s";
        }

        // Party list (mobile vertical). If assigned, prefer spawned cards. Otherwise legacy 3-slot stays visible.
        bool canSpawnList = (partyListParent != null && partyCardPrefab != null);
        if (canSpawnList)
        {
            ClearSpawnedCards();
            int count = (party != null) ? Mathf.Min(3, party.Count) : 0;
            for (int i = 0; i < count; i++)
            {
                var m = party[i];
                if (m == null || m.def == null) continue;

                var card = Instantiate(partyCardPrefab, partyListParent);
                card.Bind(m, isLocked: true, isSelectable: false);
                card.SetLocked(true); // Post screen cards are informational.
                card.SetSelected(false);
                card.SetOnClick(null);
                _spawnedPartyCards.Add(card);
            }

            if (partyHeaderTMP && string.IsNullOrEmpty(partyHeaderTMP.text))
                partyHeaderTMP.text = "YOUR PARTY (HP CARRIES FORWARD)";
        }

        var type = carry.type;
        if (statusIcon) statusIcon.sprite = statusLibrary ? statusLibrary.GetIcon(type) : null;
        if (statusName)
        {
            if (type == StatusType.None) statusName.text = "";
            else statusName.text = statusLibrary ? statusLibrary.GetDisplayName(type) : type.ToString();
        }

        // Checks + Milestones
        if (checksSection) checksSection.SetActive(true);
        if (checksTMP)
        {
            bool partyReady = party != null && party.Count > 0;
            bool hasEvolve = manager != null && manager.HasForcedEvolutionAvailable();
            checksTMP.text =
                $"{(partyReady ? "✅" : "❌")} Party Ready\n" +
                $"{(hasEvolve ? "⚠" : "✅")} Forced Evolution {(hasEvolve ? "Available" : "None")}";
        }

        if (milestoneSection) milestoneSection.SetActive(true);
        if (milestoneTMP)
        {
            int mod = safeWins % 3;
            if (mod == 0)
                milestoneTMP.text = "Milestone: Rest Node available now.";
            else
                milestoneTMP.text = $"Milestone: Rest Node in {3 - mod} win(s).";
        }

    }

    private void ClearSpawnedCards()
    {
        for (int i = _spawnedPartyCards.Count - 1; i >= 0; i--)
        {
            if (_spawnedPartyCards[i] != null)
                Destroy(_spawnedPartyCards[i].gameObject);
        }
        _spawnedPartyCards.Clear();
    }
}
