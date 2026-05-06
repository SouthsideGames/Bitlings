using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class RetirementCeremonyUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private CanvasGroup ceremonyRootGroup;
    [SerializeField] private RectTransform ceremonyRoot;

    [Header("Portrait")]
    [SerializeField] private Image portraitImage;
    [SerializeField] private CanvasGroup portraitGroup;
    [SerializeField] private RectTransform portraitRect;
    [SerializeField] private ParticleSystem embersParticles;

    [Header("Embers Tuning")]
    [SerializeField] private bool applyEmbersTuningOnPrepare = true;
    [SerializeField] private Vector2 embersLifetime = new Vector2(1.3f, 2.4f);
    [SerializeField] private Vector2 embersStartSpeed = new Vector2(8f, 22f);
    [SerializeField] private Vector2 embersStartSize = new Vector2(1.2f, 3.2f);
    [SerializeField] private int embersBurstCount = 32;
    [SerializeField] private float embersGravity = -0.02f;
    [SerializeField] private Vector2 embersDriftX = new Vector2(-8f, 8f);
    [SerializeField] private Vector2 embersDriftY = new Vector2(10f, 24f);
    [SerializeField] private Vector2 embersAngularVelocity = new Vector2(-70f, 70f);
    [SerializeField] private float embersWidth = 520f;

    [Header("Vignette")]
    [SerializeField] private CanvasGroup vignetteGroup;

    [Header("Name Plate")]
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private RectTransform namePlateRect;
    [SerializeField] private CanvasGroup namePlateGroup;

    [Header("Light Sweep")]
    [SerializeField] private Image lightSweepImage;
    [SerializeField] private CanvasGroup lightSweepGroup;
    [SerializeField] private RectTransform lightSweepRect;

    [Header("Audio")]
    [SerializeField] private AudioSource ceremonyAudioSource;
    [SerializeField] private AudioClip retirementToneClip;

    [Header("Config")]
    [SerializeField] private RetirementCurves curves;
    [SerializeField] private TrophyCardUI targetTrophyCard;

    [Header("Skip")]
    [SerializeField] private bool _skipRequested = false;

    private MentorRecord _record;
    private Coroutine _sequenceCo;
    private bool _isPlaying;
    private Vector2 _namePlateBasePos;
    private bool _namePlatePosCached;

    public void Prepare(MentorRecord record, TrophyCardUI trophyCard)
    {
        _record = record;
        targetTrophyCard = trophyCard;

        if (applyEmbersTuningOnPrepare)
            ApplyEmbersTuning();

        if (_sequenceCo != null)
        {
            StopCoroutine(_sequenceCo);
            _sequenceCo = null;
        }

        _isPlaying = false;
        _skipRequested = false;

        LeanTween.cancel(gameObject);
        if (portraitRect != null) LeanTween.cancel(portraitRect.gameObject);
        if (namePlateRect != null) LeanTween.cancel(namePlateRect.gameObject);
        if (lightSweepRect != null) LeanTween.cancel(lightSweepRect.gameObject);
        if (targetTrophyCard != null) LeanTween.cancel(targetTrophyCard.gameObject);

        if (ceremonyRootGroup != null)
            ceremonyRootGroup.alpha = 0f;

        if (portraitGroup != null)
            portraitGroup.alpha = 1f;

        if (namePlateGroup != null)
            namePlateGroup.alpha = 0f;

        if (nameLabel != null)
        {
            nameLabel.maxVisibleCharacters = 0;
            nameLabel.text = string.Empty;
        }

        if (portraitRect != null)
            portraitRect.localScale = Vector3.one;

        if (vignetteGroup != null)
            vignetteGroup.alpha = 0f;

        if (lightSweepGroup != null)
            lightSweepGroup.alpha = 0f;

        if (targetTrophyCard != null)
            targetTrophyCard.gameObject.SetActive(false);

        if (portraitImage != null)
        {
            var def = record != null && !string.IsNullOrEmpty(record.monsterId)
                ? MonsterLibraryLocator.GetById(record.monsterId)
                : null;
            portraitImage.sprite = def != null ? def.icon : null;
        }

        if (namePlateRect != null && !_namePlatePosCached)
        {
            _namePlateBasePos = namePlateRect.anchoredPosition;
            _namePlatePosCached = true;
        }

        if (namePlateRect != null)
            namePlateRect.anchoredPosition = _namePlateBasePos;

        if (ceremonyRoot != null)
            ceremonyRoot.anchoredPosition = Vector2.zero;
    }

    public void Play()
    {
        if (_sequenceCo != null)
            StopCoroutine(_sequenceCo);

        gameObject.SetActive(true);
        _skipRequested = false;
        _sequenceCo = StartCoroutine(CeremonySequence());
    }

    private void Update()
    {
        if (!_isPlaying)
            return;

        bool skipPressed = Input.GetMouseButtonDown(0);
        if (!skipPressed && Input.touchCount > 0)
            skipPressed = Input.GetTouch(0).phase == TouchPhase.Began;

        if (skipPressed)
            _skipRequested = true;
    }

    private IEnumerator CeremonySequence()
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
            yield return StartCoroutine(SkipToTrophy());
            yield break;
        }

        if (portraitRect != null)
        {
            LeanTween.scale(portraitRect, Vector3.one * 1.25f, 0.6f)
                .setEase(GetCurveOrDefault(curves != null ? curves.portraitScaleIn : LeanTweenType.easeOutQuad))
                .setIgnoreTimeScale(true);
        }

        if (vignetteGroup != null)
        {
            LeanTween.alphaCanvas(vignetteGroup, 0.65f, 0.6f)
                .setEase(GetCurveOrDefault(curves != null ? curves.vignetteIn : LeanTweenType.easeInQuad))
                .setIgnoreTimeScale(true);
        }

        yield return new WaitForSecondsRealtime(0.7f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipToTrophy());
            yield break;
        }

        string fullName = ResolveDisplayName(_record);
        if (nameLabel != null)
            nameLabel.text = fullName;

        if (namePlateGroup != null)
            namePlateGroup.alpha = 1f;

        if (namePlateRect != null)
        {
            Vector2 nameStart = _namePlateBasePos + (Vector2.down * 10f);
            Vector2 nameEnd = _namePlateBasePos + (Vector2.up * 10f);
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
            StartCoroutine(RevealText(nameLabel, fullName, 1.0f));

        yield return new WaitForSecondsRealtime(1.3f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipToTrophy());
            yield break;
        }

        var mainCam = Camera.main;
        if (mainCam != null)
        {
            Vector3 start = mainCam.transform.localPosition;
            LeanTween.moveLocal(mainCam.gameObject, start + new Vector3(0.04f, 0.04f, 0f), 0.05f)
                .setEasePunch()
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (mainCam != null)
                        mainCam.transform.localPosition = start;
                });
        }

#if UNITY_IOS || UNITY_ANDROID
        Handheld.Vibrate();
#endif

        if (ceremonyAudioSource != null && retirementToneClip != null)
            ceremonyAudioSource.PlayOneShot(retirementToneClip);

        yield return new WaitForSecondsRealtime(0.5f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipToTrophy());
            yield break;
        }

        if (embersParticles != null)
            embersParticles.Play();

        yield return new WaitForSecondsRealtime(0.3f);

        if (portraitGroup != null)
        {
            LeanTween.alphaCanvas(portraitGroup, 0f, 1.2f)
                .setEase(GetCurveOrDefault(curves != null ? curves.portraitFadeOut : LeanTweenType.easeInCubic))
                .setIgnoreTimeScale(true);
        }

        if (namePlateGroup != null)
        {
            LeanTween.alphaCanvas(namePlateGroup, 0f, 0.8f)
                .setEase(LeanTweenType.easeInQuad)
                .setIgnoreTimeScale(true);
        }

        yield return new WaitForSecondsRealtime(1.5f);

        yield return StartCoroutine(TrophyArrival());
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

    private IEnumerator RevealText(TMP_Text label, string fullText, float totalDuration)
    {
        if (label == null)
            yield break;

        label.maxVisibleCharacters = 0;
        label.ForceMeshUpdate();

        int total = label.textInfo.characterCount;
        if (total <= 0)
        {
            label.maxVisibleCharacters = int.MaxValue;
            yield break;
        }

        float delay = totalDuration / total;
        for (int i = 0; i <= total; i++)
        {
            label.maxVisibleCharacters = i;
            yield return new WaitForSecondsRealtime(delay);
        }
    }

    private IEnumerator TrophyArrival()
    {
        if (vignetteGroup != null)
            LeanTween.alphaCanvas(vignetteGroup, 0f, 0.4f).setIgnoreTimeScale(true);

        if (targetTrophyCard == null)
            yield break;

        targetTrophyCard.gameObject.SetActive(true);

        RectTransform cardRect = targetTrophyCard.GetComponent<RectTransform>();
        if (cardRect != null)
        {
            cardRect.localScale = Vector3.zero;
            LeanTween.scale(cardRect, Vector3.one, 0.5f)
                .setEase(GetCurveOrDefault(curves != null ? curves.trophyPunchIn : LeanTweenType.easeOutBack))
                .setIgnoreTimeScale(true);

            PlayLightSweep(cardRect);
        }

        if (targetTrophyCard.CandleParticles != null)
            targetTrophyCard.CandleParticles.Play();

        yield return new WaitForSecondsRealtime(0.6f);
    }

    private void PlayLightSweep(RectTransform cardRect)
    {
        if (cardRect == null || lightSweepRect == null || lightSweepGroup == null)
            return;

        float cardWidth = cardRect.rect.width;
        lightSweepRect.anchoredPosition = new Vector2(-cardWidth, 0f);
        lightSweepGroup.alpha = 0.6f;

        LeanTween.moveX(lightSweepRect, cardWidth, 0.6f)
            .setEase(GetCurveOrDefault(curves != null ? curves.lightSweep : LeanTweenType.easeOutQuad))
            .setIgnoreTimeScale(true);

        LeanTween.alphaCanvas(lightSweepGroup, 0f, 0.6f)
            .setEase(LeanTweenType.easeInQuad)
            .setIgnoreTimeScale(true);
    }

    private IEnumerator SkipToTrophy()
    {
        LeanTween.cancel(gameObject);
        if (portraitRect != null) LeanTween.cancel(portraitRect.gameObject);
        if (namePlateRect != null) LeanTween.cancel(namePlateRect.gameObject);
        if (lightSweepRect != null) LeanTween.cancel(lightSweepRect.gameObject);

        Time.timeScale = 1f;

        if (vignetteGroup != null)
            LeanTween.alphaCanvas(vignetteGroup, 0f, 0.2f).setIgnoreTimeScale(true);

        if (portraitGroup != null)
            LeanTween.alphaCanvas(portraitGroup, 0f, 0.2f).setIgnoreTimeScale(true);

        if (namePlateGroup != null)
            LeanTween.alphaCanvas(namePlateGroup, 0f, 0.2f).setIgnoreTimeScale(true);

        yield return new WaitForSecondsRealtime(0.25f);

        yield return StartCoroutine(TrophyArrival());
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

    private static LeanTweenType GetCurveOrDefault(LeanTweenType value)
    {
        return value;
    }

    private void ApplyEmbersTuning()
    {
        if (embersParticles == null)
            return;

        var main = embersParticles.main;
        main.scalingMode = ParticleSystemScalingMode.Local;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.startLifetime = new ParticleSystem.MinMaxCurve(
            Mathf.Max(0.05f, embersLifetime.x),
            Mathf.Max(0.1f, embersLifetime.y));
        main.startSpeed = new ParticleSystem.MinMaxCurve(
            Mathf.Max(0f, embersStartSpeed.x),
            Mathf.Max(0f, embersStartSpeed.y));
        main.startSize = new ParticleSystem.MinMaxCurve(
            Mathf.Max(0.01f, embersStartSize.x),
            Mathf.Max(0.02f, embersStartSize.y));
        main.gravityModifier = embersGravity;
        main.maxParticles = Mathf.Max(8, embersBurstCount + 8);

        var emission = embersParticles.emission;
        emission.rateOverTime = 0f;
        emission.burstCount = 1;
        emission.SetBurst(0, new ParticleSystem.Burst(0f, (short)Mathf.Clamp(embersBurstCount, 1, 200)));

        var shape = embersParticles.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(Mathf.Max(10f, embersWidth), 1f, 1f);

        var velocityOverLifetime = embersParticles.velocityOverLifetime;
        velocityOverLifetime.enabled = true;
        velocityOverLifetime.space = ParticleSystemSimulationSpace.Local;
        velocityOverLifetime.x = new ParticleSystem.MinMaxCurve(embersDriftX.x, embersDriftX.y);
        velocityOverLifetime.y = new ParticleSystem.MinMaxCurve(embersDriftY.x, embersDriftY.y);

        var rotationOverLifetime = embersParticles.rotationOverLifetime;
        rotationOverLifetime.enabled = true;
        rotationOverLifetime.z = new ParticleSystem.MinMaxCurve(embersAngularVelocity.x * Mathf.Deg2Rad, embersAngularVelocity.y * Mathf.Deg2Rad);

        embersParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private static string ResolveDisplayName(MentorRecord record)
    {
        if (record == null)
            return "Unknown";

        string baseName = string.IsNullOrWhiteSpace(record.displayName) ? "Unknown" : record.displayName;
        bool hasEpithet = !string.IsNullOrWhiteSpace(record.epithet) && record.driftTier >= 1;
        return hasEpithet ? (baseName + " the " + record.epithet) : baseName;
    }
}
