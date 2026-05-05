using System.Collections.Generic;
using TMPro;
using UnityEngine;

public sealed class RetiredPageUI : MonoBehaviour
{
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
        int count = mentors != null ? Mathf.Min(6, mentors.Count) : 0;

        if (legendsCountText != null) legendsCountText.text = "Retired: " + count + "/6";

        bool hasAny = mentors != null && mentors.Count > 0;
        if (emptyStatePanel != null) emptyStatePanel.SetActive(!hasAny);
        if (gridRoot != null) gridRoot.SetActive(hasAny);

        RebuildCards(mentors);
    }

    private void RebuildCards(IReadOnlyList<MentorRecord> mentors)
    {
        if (gridParent == null || cardPrefab == null)
            return;

        for (int i = 0; i < _cards.Count; i++)
            if (_cards[i] != null) Destroy(_cards[i].gameObject);
        _cards.Clear();

        int totalSlots = 6;
        string honoredUid = SaveManager.GetCurrentWeekHonoredUID();
        var activeBonus = HonorService.GetActiveBonus();

        for (int i = 0; i < totalSlots; i++)
        {
            var card = Instantiate(cardPrefab, gridParent);
            _cards.Add(card);

            MentorRecord mentor = (mentors != null && i < mentors.Count) ? mentors[i] : null;
            bool isHonored = mentor != null && !string.IsNullOrEmpty(honoredUid) && honoredUid == mentor.mentorUID;
            bool isBonusLive = isHonored && activeBonus != null && activeBonus.expiresAtUnix > SaveManager.NowUnix();

            if (mentor == null)
            {
                card.SetEmpty();
            }
            else
            {
                card.Bind(mentor, typeIconLibrary, isHonored, isBonusLive);
            }

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
            GameEvents.RaiseToast(error);

        Refresh();
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
