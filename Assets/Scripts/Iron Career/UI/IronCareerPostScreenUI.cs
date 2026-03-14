using System.Collections;
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
    [SerializeField] private IronCareerPostCardUI partyCardPrefab;

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

    private readonly List<IronCareerPostCardUI> _spawnedPartyCards = new List<IronCareerPostCardUI>(3);
    private bool _continueQueued;
    private Color _statusNameBaseColor = Color.white;
    private Color _partyHeaderBaseColor = Color.white;

    private void Awake()
    {
        if (!manager) manager = FindFirstObjectByType<IronCareerManager>();
        if (continueButton) continueButton.onClick.AddListener(OnContinuePressed);
        if (quitButton) quitButton.onClick.AddListener(() => manager?.RequestQuit());

        if (titleTMP && string.IsNullOrEmpty(titleTMP.text))
            titleTMP.text = "POST-BATTLE STATUS";

        if (statusName)
            _statusNameBaseColor = statusName.color;

        if (partyHeaderTMP)
            _partyHeaderBaseColor = partyHeaderTMP.color;
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

    private void OnContinuePressed()
    {
        if (_continueQueued) return;
        _continueQueued = true;
        if (continueButton) continueButton.interactable = false;
        StartCoroutine(CoContinueNextFrame());
    }

    private IEnumerator CoContinueNextFrame()
    {
        yield return null;
        manager?.OnPostContinue();
        _continueQueued = false;
        if (continueButton) continueButton.interactable = true;
    }

    private void BindInternal(IReadOnlyList<IronMonster> party, IronFieldStatusSnapshot carry, int wins, bool hasOutcome, IronBattleOutcome outcome)
    {
        if (bodyScrollRect) bodyScrollRect.normalizedPosition = new Vector2(0f, 1f);

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
            string result;
            if (outcome.victory)
                result = "VICTORY";
            else if (outcome.wildEscaped)
                result = "WILD FLED";
            else if (outcome.escaped)
                result = "ESCAPED";
            else
                result = "DEFEAT";
            summaryTMP.text = $"<b>{result}</b>\nWild: {wildName} (Lv {wildLvl})\nTurns Survived: {turns}\nTime Survived: {secs}s";
        }

        // Party list (mobile vertical). If assigned, prefer spawned cards. Otherwise legacy 3-slot stays visible.
        bool canSpawnList = partyListParent != null && partyCardPrefab != null;
        if (canSpawnList)
        {
            ClearSpawnedCards();
            int count = (party != null) ? Mathf.Min(3, party.Count) : 0;
            for (int i = 0; i < count; i++)
            {
                var m = party[i];
                if (m == null || m.def == null) continue;

                var card = Instantiate(partyCardPrefab, partyListParent);
                card.Bind(m);
                card.gameObject.SetActive(true);
                _spawnedPartyCards.Add(card);

            }

            // Force layout rebuild so the VerticalLayoutGroup sizes/positions the new cards.
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(partyListParent as RectTransform);

            if (partyHeaderTMP)
            {
                string baseHeader = "YOUR PARTY (HP CARRIES FORWARD)";
                partyHeaderTMP.text = (count > 0)
                    ? baseHeader
                    : $"{baseHeader}\n{BuildMutedLine("No active party members.", _partyHeaderBaseColor, 0.7f)}";
                partyHeaderTMP.color = _partyHeaderBaseColor;
            }
        }
#if UNITY_EDITOR
        else
        {
            if (!partyListParent) Debug.LogWarning("[IronCareerPostScreenUI] partyListParent is not assigned. Party cards will not spawn.");
            if (!partyCardPrefab) Debug.LogWarning("[IronCareerPostScreenUI] partyCardPrefab is not assigned. Party cards will not spawn.");
        }
#endif
        if (!canSpawnList && partyHeaderTMP && string.IsNullOrEmpty(partyHeaderTMP.text))
        {
            partyHeaderTMP.text = "YOUR PARTY (HP CARRIES FORWARD)";
            partyHeaderTMP.color = _partyHeaderBaseColor;
        }

        var type = carry.type;
        if (statusIcon)
        {
            var icon = (type != StatusType.None && statusLibrary) ? statusLibrary.GetIcon(type) : null;
            statusIcon.sprite = icon;
            statusIcon.gameObject.SetActive(icon != null);
        }
        if (statusName)
        {
            if (type == StatusType.None)
            {
                statusName.text = "No carried status";
                var faded = _statusNameBaseColor;
                faded.a = Mathf.Clamp01(_statusNameBaseColor.a * 0.7f);
                statusName.color = faded;
            }
            else
            {
                statusName.text = statusLibrary ? statusLibrary.GetDisplayName(type) : "Unknown Status";
                statusName.color = _statusNameBaseColor;
#if UNITY_EDITOR
                if (!statusLibrary)
                    Debug.LogWarning("[IronCareerPostScreenUI] StatusLibrarySO is not assigned. Carry status name/icon fallback is being used.");
#endif
            }
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

    private static string BuildMutedLine(string line, Color baseColor, float alphaMultiplier)
    {
        int r = Mathf.Clamp(Mathf.RoundToInt(baseColor.r * 255f), 0, 255);
        int g = Mathf.Clamp(Mathf.RoundToInt(baseColor.g * 255f), 0, 255);
        int b = Mathf.Clamp(Mathf.RoundToInt(baseColor.b * 255f), 0, 255);
        int a = Mathf.Clamp(Mathf.RoundToInt(baseColor.a * Mathf.Clamp01(alphaMultiplier) * 255f), 0, 255);
        return $"<color=#{r:X2}{g:X2}{b:X2}{a:X2}>{line}</color>";
    }
}
