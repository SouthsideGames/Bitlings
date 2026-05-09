using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class PromotionCeremonyUI : BaseCeremonyUI
{
    [Header("Portrait")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private CanvasGroup portraitGroup;
    [SerializeField] private RectTransform portraitRect;

    [Header("Vignette")]
    [SerializeField] private CanvasGroup vignetteGroup;

    [Header("Name Plate")]
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private RectTransform namePlateRect;
    [SerializeField] private CanvasGroup namePlateGroup;

    [Header("Badge")]
    [SerializeField] private Image rankBadgeImage;
    [SerializeField] private RectTransform rankBadgeRect;
    [SerializeField] private CanvasGroup rankBadgeGroup;

    [Header("Effects")]
    [SerializeField] private ParticleSystem confettiParticles;

    [Header("Audio")]
    [SerializeField] private AudioSource ceremonyAudioSource;
    [SerializeField] private AudioClip promotionToneClip;

    [Header("Config")]
    [SerializeField] private PromotionCurves curves;

    private string _rankName;
    private Vector2 _namePlateBasePos;
    private bool _namePlatePosCached;

    public void Prepare(Sprite rankBadgeSprite, string rankName, Sprite monsterPortrait)
    {
        _rankName = rankName ?? "Unknown";

        if (_sequenceCo != null)
        {
            StopCoroutine(_sequenceCo);
            _sequenceCo = null;
        }

        _isPlaying = false;
        _skipRequested = false;

        CancelAllTweens();

        if (ceremonyRootGroup != null)
            ceremonyRootGroup.alpha = 0f;

        if (portraitGroup != null)
            portraitGroup.alpha = 1f;

        if (namePlateGroup != null)
            namePlateGroup.alpha = 0f;

        if (rankBadgeGroup != null)
            rankBadgeGroup.alpha = 0f;

        if (nameLabel != null)
        {
            nameLabel.maxVisibleCharacters = 0;
            nameLabel.text = string.Empty;
        }

        if (portraitRect != null)
            portraitRect.localScale = Vector3.one;

        if (vignetteGroup != null)
            vignetteGroup.alpha = 0f;

        if (rankBadgeRect != null)
            rankBadgeRect.localScale = Vector3.zero;

        if (rankBadgeImage != null)
            rankBadgeImage.sprite = rankBadgeSprite;

        if (portraitImage != null)
            portraitImage.sprite = monsterPortrait;

        if (namePlateRect != null && !_namePlatePosCached)
        {
            _namePlateBasePos = namePlateRect.anchoredPosition;
            _namePlatePosCached = true;
        }

        if (namePlateRect != null)
            namePlateRect.anchoredPosition = _namePlateBasePos;

        if (ceremonyRoot != null)
            ceremonyRoot.anchoredPosition = Vector2.zero;

        // Set default confetti particle settings if needed
        if (confettiParticles != null)
        {
            var main = confettiParticles.main;
            var emission = confettiParticles.emission;
            var shape = confettiParticles.shape;

            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(8f, 0.1f, 0f);

            if (emission.burstCount == 0)
                emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0, 60) });

            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.3f;
        }
    }

    protected override IEnumerator CeremonySequence()
    {
        _isPlaying = true;

        if (ceremonyRootGroup != null)
            ceremonyRootGroup.alpha = 1f;

        Time.timeScale = 0.85f;
        yield return new WaitForSecondsRealtime(0.5f);
        Time.timeScale = 1f;
        yield return new WaitForSecondsRealtime(0.1f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        // Scale up portrait
        if (portraitRect != null)
        {
            LeanTween.scale(portraitRect, Vector3.one * 1.25f, 0.6f)
                .setEase(GetCurveOrDefault(curves != null ? curves.portraitScaleIn : LeanTweenType.easeOutQuad))
                .setIgnoreTimeScale(true);
        }

        // Fade in vignette
        if (vignetteGroup != null)
        {
            LeanTween.alphaCanvas(vignetteGroup, 1f, 0.6f)
                .setEase(GetCurveOrDefault(curves != null ? curves.vignetteIn : LeanTweenType.easeInQuad))
                .setIgnoreTimeScale(true);
        }

        yield return new WaitForSecondsRealtime(0.7f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        // Reveal name
        string ceremonyText = $"Promoted to {_rankName}!";
        if (nameLabel != null)
            nameLabel.text = ceremonyText;

        if (namePlateGroup != null)
            namePlateGroup.alpha = 1f;

        if (namePlateRect != null)
        {
            Vector2 nameStart = _namePlateBasePos + (Vector2.down * 50f);
            Vector2 nameEnd = _namePlateBasePos + (Vector2.up * 50f);
            namePlateRect.anchoredPosition = nameStart;

            LeanTween.value(namePlateRect.gameObject, nameStart, nameEnd, 0.8f)
                .setEase(GetCurveOrDefault(curves != null ? curves.nameTextFloat : LeanTweenType.easeOutCubic))
                .setIgnoreTimeScale(true)
                .setOnUpdate((Vector2 v) =>
                {
                    if (namePlateRect != null)
                        namePlateRect.anchoredPosition = v;
                });
        }

        if (nameLabel != null)
            StartCoroutine(RevealText(nameLabel, ceremonyText, 0.7f));

        yield return new WaitForSecondsRealtime(0.5f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        // Play sound
        PlayCeremonySfx(ceremonyAudioSource, promotionToneClip);

        // Play confetti
        if (confettiParticles != null)
            confettiParticles.Play();

        yield return new WaitForSecondsRealtime(0.2f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        // Fade out vignette
        if (vignetteGroup != null)
        {
            LeanTween.alphaCanvas(vignetteGroup, 0f, 0.4f)
                .setIgnoreTimeScale(true);
        }

        // Badge punch-in
        if (rankBadgeGroup != null)
            rankBadgeGroup.alpha = 1f;

        if (rankBadgeRect != null)
        {
            LeanTween.scale(rankBadgeRect, Vector3.one, 0.5f)
                .setEase(GetCurveOrDefault(curves != null ? curves.badgePunchIn : LeanTweenType.easeOutBack))
                .setIgnoreTimeScale(true);
        }

        yield return new WaitForSecondsRealtime(0.8f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        if (ceremonyRootGroup != null)
        {
            LeanTween.alphaCanvas(ceremonyRootGroup, 0f, 0.3f)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (ceremonyRootGroup != null)
                        ceremonyRootGroup.alpha = 0f;
                });
        }

        _isPlaying = false;
        _sequenceCo = null;
    }

    protected override IEnumerator SkipSequence()
    {
        CancelAllTweens();
        Time.timeScale = 1f;

        string ceremonyText = $"Promoted to {_rankName}!";
        if (nameLabel != null)
        {
            nameLabel.text = ceremonyText;
            nameLabel.maxVisibleCharacters = int.MaxValue;
        }

        if (namePlateGroup != null)
            namePlateGroup.alpha = 1f;

        if (portraitGroup != null)
            portraitGroup.alpha = 1f;

        if (rankBadgeRect != null)
            rankBadgeRect.localScale = Vector3.one;

        if (rankBadgeGroup != null)
            rankBadgeGroup.alpha = 1f;

        yield return new WaitForSecondsRealtime(0.3f);

        if (vignetteGroup != null)
            LeanTween.alphaCanvas(vignetteGroup, 0f, 0.2f).setIgnoreTimeScale(true);

        if (portraitGroup != null)
            LeanTween.alphaCanvas(portraitGroup, 0f, 0.2f).setIgnoreTimeScale(true);

        if (namePlateGroup != null)
            LeanTween.alphaCanvas(namePlateGroup, 0f, 0.2f).setIgnoreTimeScale(true);

        yield return new WaitForSecondsRealtime(0.25f);

        if (ceremonyRootGroup != null)
        {
            LeanTween.alphaCanvas(ceremonyRootGroup, 0f, 0.3f)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (ceremonyRootGroup != null)
                        ceremonyRootGroup.alpha = 0f;
                });
        }

        _isPlaying = false;
        _sequenceCo = null;
    }

    protected override void CancelAllTweens()
    {
        base.CancelAllTweens();
        if (portraitRect != null) LeanTween.cancel(portraitRect.gameObject);
        if (namePlateRect != null) LeanTween.cancel(namePlateRect.gameObject);
        if (rankBadgeRect != null) LeanTween.cancel(rankBadgeRect.gameObject);
    }

    private static LeanTweenType GetCurveOrDefault(LeanTweenType value)
    {
        return value;
    }
}
