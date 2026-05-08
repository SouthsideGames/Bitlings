using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

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

    [Header("Vignette")]
    [SerializeField] private CanvasGroup vignetteGroup;

    [Header("Name Plate")]
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private RectTransform namePlateRect;
    [SerializeField] private CanvasGroup namePlateGroup;

    [Header("Light Sweep")]
    [SerializeField] private GameObject lightSweep;

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

        if (lightSweep != null)
            lightSweep.SetActive(false);

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

        bool skipPressed = false;

        var mouse = Mouse.current;
        if (mouse != null)
            skipPressed = mouse.leftButton.wasPressedThisFrame;

        if (!skipPressed)
        {
            var ts = Touchscreen.current;
            if (ts != null && ts.primaryTouch != null)
                skipPressed = ts.primaryTouch.press.wasPressedThisFrame;
        }

        if (!skipPressed)
        {
            var kb = Keyboard.current;
            if (kb != null)
                skipPressed = kb.anyKey.wasPressedThisFrame;
        }

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
            LeanTween.alphaCanvas(vignetteGroup, 1f, 0.6f)
                .setEase(GetCurveOrDefault(curves != null ? curves.vignetteIn : LeanTweenType.easeInQuad))
                .setIgnoreTimeScale(true);
        }

        yield return new WaitForSecondsRealtime(0.7f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipToTrophy());
            yield break;
        }

        string honoreeName = ResolveDisplayName(_record);
        string ceremonyMessage = ResolveCeremonyMessage(honoreeName);
        if (nameLabel != null)
            nameLabel.text = ceremonyMessage;

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

            PlayLightSweep();

        if (nameLabel != null)
            StartCoroutine(RevealText(nameLabel, ceremonyMessage, 1.0f));

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

        PlayCeremonySfx(ceremonyAudioSource, retirementToneClip);

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
        }

        if (targetTrophyCard.CandleParticles != null)
            targetTrophyCard.CandleParticles.Play();

        yield return new WaitForSecondsRealtime(0.6f);
    }

    private void PlayLightSweep()
    {
        if (lightSweep != null)
            lightSweep.SetActive(true);
    }

    private IEnumerator SkipToTrophy()
    {
        LeanTween.cancel(gameObject);
        if (portraitRect != null) LeanTween.cancel(portraitRect.gameObject);
        if (namePlateRect != null) LeanTween.cancel(namePlateRect.gameObject);
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

    private static string ResolveDisplayName(MentorRecord record)
    {
        if (record == null)
            return "Unknown";

        string baseName = string.IsNullOrWhiteSpace(record.displayName) ? "Unknown" : record.displayName;
        bool hasEpithet = !string.IsNullOrWhiteSpace(record.epithet) && record.driftTier >= 1;
        return hasEpithet ? (baseName + " the " + record.epithet) : baseName;
    }

    private static string ResolveCeremonyMessage(string displayName)
    {
        string safeName = string.IsNullOrWhiteSpace(displayName) ? "Unknown" : displayName;
        return "Thank you for your service " + safeName;
    }

    private void PlayCeremonySfx(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null)
            return;

        if (AudioManager.I != null)
        {
            float sfxScale = AudioManager.I.GetEffectiveSfxScale();
            if (sfxScale <= 0f)
                return;

            source.PlayOneShot(clip, sfxScale);
            return;
        }

        source.PlayOneShot(clip);
    }
}
