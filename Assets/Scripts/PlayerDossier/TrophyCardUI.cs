using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class TrophyCardUI : MonoBehaviour
{
    [Header("Card")]
    [SerializeField] private Image background;
    [SerializeField] private Image borderGlow;
    [SerializeField] private Image portrait;
    [SerializeField] private Image typeIcon;
    [SerializeField] private TextMeshProUGUI namePlate;
    [SerializeField] private TextMeshProUGUI retirementDateText;
    [SerializeField] private Button cardButton;

    [Header("Honor Candle")]
    [SerializeField] private Button candleButton;
    [SerializeField] private Image candleImage;
    [SerializeField] private Sprite candleOffSprite;
    [SerializeField] private Sprite candleLitSprite;

    [Header("Effects")]
    [SerializeField] private ParticleSystem candleParticles;
    [SerializeField] private HonorCeremonyUI honorCeremony;

    [Header("Fallback")]
    [SerializeField] private GameObject emptyStateRoot;

    public event Action<string> OnCardTapped;
    public event Action<string> OnHonorRequested;

    public string MentorUID => _mentorUid;
    public ParticleSystem CandleParticles => candleParticles;

    private string _mentorUid;
    private Coroutine _pulseCo;
    private bool _shouldPulseCandle;

    private void OnEnable()
    {
        TryStartPulse();
    }

    private void OnDisable()
    {
        StopPulse();
    }

    private void Awake()
    {
        if (cardButton != null)
            cardButton.onClick.AddListener(HandleCardTapped);

        if (candleButton != null)
            candleButton.onClick.AddListener(HandleCandleTapped);
    }

    private void OnDestroy()
    {
        if (cardButton != null)
            cardButton.onClick.RemoveListener(HandleCardTapped);

        if (candleButton != null)
            candleButton.onClick.RemoveListener(HandleCandleTapped);
    }

    public void Bind(MentorRecord mentor, TypeIconLibrary iconLibrary, bool honoredThisWeek, bool activeBonus)
    {
        _mentorUid = mentor != null ? mentor.mentorUID : null;

        if (mentor == null)
        {
            SetEmpty();
            return;
        }

        if (emptyStateRoot != null) emptyStateRoot.SetActive(false);

        SetTierColor(mentor.quality);

        var def = !string.IsNullOrEmpty(mentor.monsterId) ? MonsterLibraryLocator.GetById(mentor.monsterId) : null;
        if (portrait != null)
            portrait.sprite = def != null ? def.icon : null;

        if (typeIcon != null)
            typeIcon.sprite = iconLibrary != null ? iconLibrary.GetIcon(mentor.monsterType) : null;

        if (namePlate != null)
        {
            string displayName = string.IsNullOrWhiteSpace(mentor.displayName) ? "Unknown" : mentor.displayName;
            bool hasEpithet = !string.IsNullOrWhiteSpace(mentor.epithet) && mentor.driftTier > 0;
            namePlate.text = hasEpithet ? (displayName + " the " + mentor.epithet) : displayName;
        }

        if (retirementDateText != null)
            retirementDateText.text = "Retired Day " + mentor.retiredDay.ToString("N0");

        SetCandleState(honoredThisWeek, activeBonus);
    }

    public void SetEmpty()
    {
        _mentorUid = null;

        if (emptyStateRoot != null) emptyStateRoot.SetActive(true);
        if (namePlate != null) namePlate.text = "Awaiting a retired monster...";
        if (retirementDateText != null) retirementDateText.text = string.Empty;
        if (portrait != null) portrait.sprite = null;
        if (typeIcon != null) typeIcon.sprite = null;
        SetCandleState(false, false);
    }

    private void SetTierColor(MentorQuality q)
    {
        Color c = Hex("#CD7F32");
        switch (q)
        {
            case MentorQuality.Silver: c = Hex("#A8A9AD"); break;
            case MentorQuality.Gold: c = Hex("#B8860B"); break;
            case MentorQuality.Legend: c = Hex("#4B0082"); break;
        }

        if (background != null) background.color = c;
        if (borderGlow != null) borderGlow.color = new Color(c.r, c.g, c.b, 0.75f);
    }

    private void SetCandleState(bool lit, bool pulse)
    {
        if (candleImage != null)
            candleImage.sprite = lit ? candleLitSprite : candleOffSprite;

        _shouldPulseCandle = pulse && candleImage != null;
        StopPulse();

        if (candleImage != null)
            candleImage.transform.localScale = Vector3.one;

        TryStartPulse();
    }

    private void TryStartPulse()
    {
        if (!_shouldPulseCandle || _pulseCo != null || !isActiveAndEnabled || candleImage == null)
            return;

        _pulseCo = StartCoroutine(PulseCandle());
    }

    private void StopPulse()
    {
        if (_pulseCo == null)
            return;

        StopCoroutine(_pulseCo);
        _pulseCo = null;
    }

    private IEnumerator PulseCandle()
    {
        while (true)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 2f;
                float s = Mathf.Lerp(1f, 1.15f, Mathf.SmoothStep(0f, 1f, t));
                candleImage.transform.localScale = new Vector3(s, s, s);
                yield return null;
            }

            t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 2f;
                float s = Mathf.Lerp(1.15f, 1f, Mathf.SmoothStep(0f, 1f, t));
                candleImage.transform.localScale = new Vector3(s, s, s);
                yield return null;
            }
        }
    }

    private void HandleCardTapped()
    {
        if (string.IsNullOrEmpty(_mentorUid)) return;
        OnCardTapped?.Invoke(_mentorUid);
    }

    private void HandleCandleTapped()
    {
        if (string.IsNullOrEmpty(_mentorUid)) return;
        OnHonorRequested?.Invoke(_mentorUid);
    }

    public void PlayHonorCeremony(string bonusDescription)
    {
        if (honorCeremony != null)
            honorCeremony.PlayHonorCeremony(bonusDescription);
    }

    private static Color Hex(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
        return Color.white;
    }
}
