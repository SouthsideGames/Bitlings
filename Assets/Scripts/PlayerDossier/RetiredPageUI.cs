using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class RetiredPageUI : MonoBehaviour
{
    private const int MaxSlots = 6;

    [Header("State")]
    [SerializeField] private GameObject emptyStatePanel;
    [SerializeField] private GameObject gridRoot;
    [SerializeField] private Transform gridParent;

    [Header("Aggregate Stats")]
    [SerializeField] private TextMeshProUGUI legendsCountText;

    [Header("Cards")]
    [SerializeField] private TrophyCardUI cardPrefab;
    [SerializeField] private TypeIconLibrary typeIconLibrary;
    [SerializeField] private CareerNarrativePanelUI narrativePanel;

    private readonly List<TrophyCardUI> _cards = new List<TrophyCardUI>(6);

    private void OnEnable()
    {
        GameEvents.MentorRetired += HandleRefreshEvent;
        GameEvents.OnOwnedMonstersChanged += HandleRefreshNoArg;
        GameEvents.HonorApplied += HandleRefreshEvent;

        Refresh();
    }

    private void OnDisable()
    {
        GameEvents.MentorRetired -= HandleRefreshEvent;
        GameEvents.OnOwnedMonstersChanged -= HandleRefreshNoArg;
        GameEvents.HonorApplied -= HandleRefreshEvent;
    }

    public void Refresh()
    {
        var mentors = SaveManager.GetMentorHallSnapshot();
        int count = mentors != null ? Mathf.Min(MaxSlots, mentors.Count) : 0;

        if (legendsCountText != null) legendsCountText.text = "Retired: " + count + "/6";

        bool hasAny = mentors != null && mentors.Count > 0;
        if (emptyStatePanel != null) emptyStatePanel.SetActive(!hasAny);
        if (gridRoot != null) gridRoot.SetActive(hasAny);

        RebuildCards(mentors);
    }

    public TrophyCardUI GetNextEmptyTrophyCard()
    {
        var mentors = SaveManager.GetMentorHallSnapshot();
        RebuildCards(mentors);

        if (emptyStatePanel != null)
            emptyStatePanel.SetActive(false);
        if (gridRoot != null)
            gridRoot.SetActive(true);

        int occupied = mentors != null ? Mathf.Clamp(mentors.Count, 0, MaxSlots) : 0;
        if (occupied < _cards.Count)
            return _cards[occupied];

        return _cards.Count > 0 ? _cards[_cards.Count - 1] : null;
    }

    private void RebuildCards(IReadOnlyList<MentorRecord> mentors)
    {
        if (gridParent == null || cardPrefab == null)
            return;

        for (int i = 0; i < _cards.Count; i++)
            if (_cards[i] != null) Destroy(_cards[i].gameObject);
        _cards.Clear();

        int filledSlots = mentors != null ? Mathf.Min(MaxSlots, mentors.Count) : 0;
        int totalSlots = MaxSlots;
        string honoredUid = SaveManager.GetCurrentWeekHonoredUID();
        var activeBonus = HonorService.GetActiveBonus();

        for (int i = 0; i < totalSlots; i++)
        {
            var card = Instantiate(cardPrefab, gridParent);
            _cards.Add(card);

            MentorRecord mentor = i < filledSlots ? mentors[i] : null;
            bool isHonored = mentor != null && !string.IsNullOrEmpty(honoredUid) && honoredUid == mentor.mentorUID;
            bool isBonusLive = isHonored && activeBonus != null && activeBonus.expiresAtUnix > SaveManager.NowUnix();

            card.Bind(mentor, typeIconLibrary, isHonored, isBonusLive);
            card.OnCardTapped += OpenNarrative;
            card.OnHonorRequested += TryHonor;
        }
    }

    private void OpenNarrative(string mentorUID)
    {
        if (narrativePanel == null) return;
        narrativePanel.Show(mentorUID);
    }

    private void TryHonor(string mentorUID)
    {
        if (!HonorService.CanHonor(mentorUID))
        {
            if (!string.IsNullOrEmpty(SaveManager.GetCurrentWeekHonoredUID()))
                GameEvents.RaiseToast("Honor already used this week.");
            else
                GameEvents.RaiseToast("This retired monster cannot be honored right now.");
            return;
        }

        string error = HonorService.HonorLegend(mentorUID);
        if (!string.IsNullOrEmpty(error))
        {
            GameEvents.RaiseToast(error);
            return;
        }

        string bonusDescription = BuildHonorBonusDescription(HonorService.GetActiveBonus());
        var card = FindCardByMentorUid(mentorUID);
        if (card != null && card.gameObject.activeInHierarchy)
        {
            card.PlayHonorCeremony(bonusDescription);
            StartCoroutine(RefreshAfterHonorCeremony());
            return;
        }

        Refresh();
    }

    private TrophyCardUI FindCardByMentorUid(string mentorUID)
    {
        if (string.IsNullOrEmpty(mentorUID))
            return null;

        for (int i = 0; i < _cards.Count; i++)
        {
            var card = _cards[i];
            if (card == null)
                continue;

            if (card.MentorUID == mentorUID)
                return card;
        }

        return null;
    }

    private IEnumerator RefreshAfterHonorCeremony()
    {
        yield return new WaitForSecondsRealtime(1.1f);
        Refresh();
    }

    private static string BuildHonorBonusDescription(HonorBonusState bonus)
    {
        if (bonus == null)
            return "Inspired";

        var parts = new List<string>(4);

        if (bonus.atkPct > 0f)
            parts.Add("+" + Mathf.RoundToInt(bonus.atkPct * 100f) + "% ATK");
        if (bonus.defPct > 0f)
            parts.Add("+" + Mathf.RoundToInt(bonus.defPct * 100f) + "% DEF");
        if (bonus.xpMul > 1f)
            parts.Add("+" + Mathf.RoundToInt((bonus.xpMul - 1f) * 100f) + "% XP");
        if (bonus.jobMul > 1f)
            parts.Add("+" + Mathf.RoundToInt((bonus.jobMul - 1f) * 100f) + "% JOB");

        return parts.Count > 0 ? string.Join(" / ", parts) : "Inspired";
    }

    private void HandleRefreshEvent(string _)
    {
        Refresh();
    }

    private void HandleRefreshNoArg()
    {
        Refresh();
    }
}
