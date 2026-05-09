using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Evolution ceremony UI — celebrates a monster's transformation to new form.
/// Extends BaseCeremonyUI for shared skip-on-tap, audio, and text reveal plumbing.
/// Methods moved to base: Update(), Play(), PlayCeremonySfx(), RevealText()
/// </summary>
public sealed class EvolutionCeremonyUI : BaseCeremonyUI
{
    [Header("Panel")]

    [Header("Portrait")]
    [SerializeField] private Image beforePortraitImage;
    [SerializeField] private Image afterPortraitImage;
    [SerializeField] private CanvasGroup portraitGroup;
    [SerializeField] private RectTransform portraitRect;
    [SerializeField] private ParticleSystem energyBurstParticles;

    [Header("Vignette")]
    [SerializeField] private CanvasGroup vignetteGroup;

    [Header("Name Plate")]
    [SerializeField] private TMP_Text nameLabel;
    [SerializeField] private CanvasGroup namePlateGroup;
    [SerializeField] private RectTransform namePlateRect;

    [Header("Audio")]
    [SerializeField] private AudioSource ceremonyAudioSource;
    [SerializeField] private AudioClip evolutionToneClip;

    [Header("Config")]
    [SerializeField] private EvolutionCurves curves;

    [Header("Skip")]

    private MonsterDataSO _oldDef;
    private MonsterDataSO _newDef;
    private Vector2 _namePlateBasePos;
    private bool _namePlatePosCached;

    public void Prepare(MonsterDataSO oldDef, MonsterDataSO newDef)
    {
        _oldDef = oldDef;
        _newDef = newDef;

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

        if (nameLabel != null)
        {
            nameLabel.maxVisibleCharacters = 0;
            nameLabel.text = string.Empty;
        }

        if (portraitRect != null)
            portraitRect.localScale = Vector3.one;

        if (vignetteGroup != null)
            vignetteGroup.alpha = 0f;

        if (beforePortraitImage != null)
        {
            beforePortraitImage.sprite = oldDef != null ? oldDef.icon : null;
            var c = beforePortraitImage.color;
            c.a = 1f;
            beforePortraitImage.color = c;
        }

        if (afterPortraitImage != null)
        {
            afterPortraitImage.sprite = newDef != null ? newDef.icon : null;
            var c = afterPortraitImage.color;
            c.a = 0f;
            afterPortraitImage.color = c;
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

        // Old form scales up
        if (portraitRect != null)
        {
            LeanTween.scale(portraitRect, Vector3.one * 1.25f, 0.6f)
                .setEase(GetCurveOrDefault(curves != null ? curves.portraitFlashIn : LeanTweenType.easeOutQuad))
                .setIgnoreTimeScale(true);
        }

        // Vignette fades in
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

        // Flash punch + energy burst
        if (portraitRect != null)
        {
            LeanTween.scale(portraitRect, Vector3.one * 1.5f, 0.1f)
                .setIgnoreTimeScale(true);
        }

        if (energyBurstParticles != null)
            energyBurstParticles.Play();

        yield return new WaitForSecondsRealtime(0.15f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        // Cross-dissolve: old form out, new form in
        if (beforePortraitImage != null)
        {
            LeanTween.value(beforePortraitImage.gameObject, 1f, 0f, 0.5f)
                .setEase(GetCurveOrDefault(curves != null ? curves.portraitFlashIn : LeanTweenType.easeInCubic))
                .setIgnoreTimeScale(true)
                .setOnUpdate((float a) =>
                {
                    if (beforePortraitImage != null)
                    {
                        var col = beforePortraitImage.color;
                        col.a = a;
                        beforePortraitImage.color = col;
                    }
                });
        }

        if (afterPortraitImage != null)
        {
            LeanTween.value(afterPortraitImage.gameObject, 0f, 1f, 0.5f)
                .setEase(GetCurveOrDefault(curves != null ? curves.newFormReveal : LeanTweenType.easeOutCubic))
                .setIgnoreTimeScale(true)
                .setOnUpdate((float a) =>
                {
                    if (afterPortraitImage != null)
                    {
                        var col = afterPortraitImage.color;
                        col.a = a;
                        afterPortraitImage.color = col;
                    }
                });
        }

        yield return new WaitForSecondsRealtime(0.6f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        PlayCeremonySfx(ceremonyAudioSource, evolutionToneClip);

        string ceremonyName = ResolveCeremonyName(_newDef);
        if (nameLabel != null)
            nameLabel.text = ceremonyName;

        if (namePlateGroup != null)
            namePlateGroup.alpha = 1f;

        if (namePlateRect != null)
        {
            Vector2 nameStart = _namePlateBasePos + (Vector2.down * 10f);
            Vector2 nameEnd = _namePlateBasePos + (Vector2.up * 10f);
            namePlateRect.anchoredPosition = nameStart;

            LeanTween.value(namePlateRect.gameObject, nameStart, nameEnd, 0.8f)
                .setEase(GetCurveOrDefault(curves != null ? curves.namePunchIn : LeanTweenType.easeOutBack))
                .setIgnoreTimeScale(true)
                .setOnUpdate((Vector2 v) =>
                {
                    if (namePlateRect != null)
                        namePlateRect.anchoredPosition = v;
                });
        }

        if (nameLabel != null)
            StartCoroutine(RevealText(nameLabel, ceremonyName, 1.0f));

        yield return new WaitForSecondsRealtime(1.5f);
        if (_skipRequested)
        {
            yield return StartCoroutine(SkipSequence());
            yield break;
        }

        if (portraitGroup != null)
        {
            LeanTween.alphaCanvas(portraitGroup, 0f, 0.8f)
                .setEase(LeanTweenType.easeInQuad)
                .setIgnoreTimeScale(true);
        }

        if (namePlateGroup != null)
        {
            LeanTween.alphaCanvas(namePlateGroup, 0f, 0.8f)
                .setEase(LeanTweenType.easeInQuad)
                .setIgnoreTimeScale(true);
        }

        if (vignetteGroup != null)
        {
            LeanTween.alphaCanvas(vignetteGroup, 0f, 0.4f)
                .setIgnoreTimeScale(true);
        }

        yield return new WaitForSecondsRealtime(1.0f);

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

        // Jump straight to new form portrait
        if (beforePortraitImage != null)
        {
            var col = beforePortraitImage.color;
            col.a = 0f;
            beforePortraitImage.color = col;
        }

        if (afterPortraitImage != null)
        {
            var col = afterPortraitImage.color;
            col.a = 1f;
            afterPortraitImage.color = col;
        }

        // Name reveal instantly
        string ceremonyName = ResolveCeremonyName(_newDef);
        if (nameLabel != null)
        {
            nameLabel.text = ceremonyName;
            nameLabel.maxVisibleCharacters = int.MaxValue;
        }

        if (namePlateGroup != null)
            namePlateGroup.alpha = 1f;

        yield return new WaitForSecondsRealtime(0.5f);

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
    }

    private static LeanTweenType GetCurveOrDefault(LeanTweenType value)
    {
        return value;
    }

    private static string ResolveCeremonyName(MonsterDataSO def)
    {
        string name = def != null && !string.IsNullOrWhiteSpace(def.displayName)
            ? def.displayName
            : "Unknown";
        return name + "!";
    }

}
