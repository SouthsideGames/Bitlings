using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class LevelUpCeremonyUI : BaseCeremonyUI
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

    [Header("Stats")]
    [SerializeField] private TMP_Text statDeltaLabel;
    [SerializeField] private CanvasGroup statDeltaGroup;

    [Header("Effects")]
    [SerializeField] private GameObject lightSweep;
    [SerializeField] private ParticleSystem fountainParticles;

    [Header("Audio")]
    [SerializeField] private AudioSource ceremonyAudioSource;
    [SerializeField] private AudioClip levelUpToneClip;

    [Header("Config")]
    [SerializeField] private LevelUpCurves curves;

    private MonsterDataSO _def;
    private int _newLevel;
    private string _statDeltaText;
    private bool _isPremium;
    private Vector2 _namePlateBasePos;
    private bool _namePlatePosCached;

    public void Prepare(MonsterDataSO def, int newLevel, string statDeltaText, bool isPremium)
    {
        _def = def;
        _newLevel = newLevel;
        _statDeltaText = statDeltaText ?? string.Empty;
        _isPremium = isPremium;

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

        if (statDeltaGroup != null)
            statDeltaGroup.alpha = 0f;

        if (nameLabel != null)
        {
            nameLabel.maxVisibleCharacters = 0;
            nameLabel.text = string.Empty;
        }

        if (portraitRect != null)
            portraitRect.localScale = Vector3.one;

        if (vignetteGroup != null)
            vignetteGroup.alpha = 0f;

        if (lightSweep != null)
            lightSweep.SetActive(false);

        if (portraitImage != null)
            portraitImage.sprite = def != null ? MonsterNameFormatter.GetIcon(def, isPremium, backIcon: false) : null;

        if (namePlateRect != null && !_namePlatePosCached)
        {
            _namePlateBasePos = namePlateRect.anchoredPosition;
            _namePlatePosCached = true;
        }

        if (namePlateRect != null)
            namePlateRect.anchoredPosition = _namePlateBasePos;

        if (ceremonyRoot != null)
            ceremonyRoot.anchoredPosition = Vector2.zero;

        // Set default fountain particle settings if needed
        if (fountainParticles != null)
        {
            var main = fountainParticles.main;
            var emission = fountainParticles.emission;
            var shape = fountainParticles.shape;

            // Only set defaults if not already configured
            if (shape.shapeType != ParticleSystemShapeType.Cone)
                shape.shapeType = ParticleSystemShapeType.Cone;

            if (emission.burstCount == 0)
                emission.SetBursts(new ParticleSystem.Burst[] { new ParticleSystem.Burst(0, 80) });

            main.simulationSpace = ParticleSystemSimulationSpace.World;
            
            var startColor = main.startColor;
            if (startColor.mode == ParticleSystemGradientMode.Color)
                main.startColor = new Color(1f, 0.94f, 0.47f, 1f); // (255, 240, 120)
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
            LeanTween.scale(portraitRect, Vector3.one * 1.2f, 0.5f)
                .setEase(GetCurveOrDefault(curves != null ? curves.portraitScaleIn : LeanTweenType.easeOutQuad))
                .setIgnoreTimeScale(true);
        }

        // Fade in vignette
        if (vignetteGroup != null)
        {
            LeanTween.alphaCanvas(vignetteGroup, 1f, 0.5f)
                .setEase(GetCurveOrDefault(curves != null ? curves.vignetteIn : LeanTweenType.easeInQuad))
                .setIgnoreTimeScale(true);
        }

        yield return new WaitForSecondsRealtime(0.5f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        // Activate light sweep
        if (lightSweep != null)
            lightSweep.SetActive(true);

        // Play fountain particles
        if (fountainParticles != null)
            fountainParticles.Play();

        // Play sound
        PlayCeremonySfx(ceremonyAudioSource, levelUpToneClip);

        yield return new WaitForSecondsRealtime(0.2f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        // Reveal name and level
        string ceremonyText = $"{(_def != null ? _def.displayName : "Bitling")} reached Level {_newLevel}!";
        if (nameLabel != null)
            nameLabel.text = ceremonyText;

        if (namePlateGroup != null)
            namePlateGroup.alpha = 1f;

        if (namePlateRect != null)
        {
            Vector2 nameStart = _namePlateBasePos + (Vector2.down * 40f);
            Vector2 nameEnd = _namePlateBasePos + (Vector2.up * 40f);
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
            StartCoroutine(RevealText(nameLabel, ceremonyText, 0.6f));

        yield return new WaitForSecondsRealtime(0.4f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        // Show stat delta if provided
        if (!string.IsNullOrEmpty(_statDeltaText) && statDeltaLabel != null)
        {
            statDeltaLabel.text = _statDeltaText;
            if (statDeltaGroup != null)
                statDeltaGroup.alpha = 1f;

            yield return new WaitForSecondsRealtime(1.0f);
            if (_skipRequested)
            {
                yield return StartCoroutine(SkipSequence());
                yield break;
            }
        }

        // Fade out
        if (vignetteGroup != null)
        {
            LeanTween.alphaCanvas(vignetteGroup, 0f, 0.4f)
                .setIgnoreTimeScale(true);
        }

        if (portraitGroup != null)
        {
            LeanTween.alphaCanvas(portraitGroup, 0f, 0.6f)
                .setIgnoreTimeScale(true);
        }

        if (namePlateGroup != null)
        {
            LeanTween.alphaCanvas(namePlateGroup, 0f, 0.6f)
                .setIgnoreTimeScale(true);
        }

        if (statDeltaGroup != null)
        {
            LeanTween.alphaCanvas(statDeltaGroup, 0f, 0.6f)
                .setIgnoreTimeScale(true);
        }

        yield return new WaitForSecondsRealtime(0.8f);

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

        string ceremonyText = $"{(_def != null ? _def.displayName : "Bitling")} reached Level {_newLevel}!";
        if (nameLabel != null)
        {
            nameLabel.text = ceremonyText;
            nameLabel.maxVisibleCharacters = int.MaxValue;
        }

        if (namePlateGroup != null)
            namePlateGroup.alpha = 1f;

        if (portraitGroup != null)
            portraitGroup.alpha = 1f;

        if (statDeltaLabel != null && !string.IsNullOrEmpty(_statDeltaText))
        {
            statDeltaLabel.text = _statDeltaText;
            if (statDeltaGroup != null)
                statDeltaGroup.alpha = 1f;
        }

        yield return new WaitForSecondsRealtime(0.3f);

        if (vignetteGroup != null)
            LeanTween.alphaCanvas(vignetteGroup, 0f, 0.2f).setIgnoreTimeScale(true);

        if (portraitGroup != null)
            LeanTween.alphaCanvas(portraitGroup, 0f, 0.2f).setIgnoreTimeScale(true);

        if (namePlateGroup != null)
            LeanTween.alphaCanvas(namePlateGroup, 0f, 0.2f).setIgnoreTimeScale(true);

        if (statDeltaGroup != null)
            LeanTween.alphaCanvas(statDeltaGroup, 0f, 0.2f).setIgnoreTimeScale(true);

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
    }

    private static LeanTweenType GetCurveOrDefault(LeanTweenType value)
    {
        return value;
    }
}
