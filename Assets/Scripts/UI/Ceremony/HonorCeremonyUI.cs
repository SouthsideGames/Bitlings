using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed class HonorCeremonyUI : MonoBehaviour
{
    [SerializeField] private ParticleSystem candleFlameParticles;
    [SerializeField] private Image candleImage;
    [SerializeField] private Sprite candleUnlit;
    [SerializeField] private Sprite candleLit;
    [SerializeField] private TMP_Text bonusFloatyText;
    [SerializeField] private CanvasGroup floatyGroup;
    [SerializeField] private RectTransform floatyRect;
    [SerializeField] private RectTransform candleRect;
    [SerializeField] private AudioSource honorAudioSource;
    [SerializeField] private AudioClip candleIgniteClip;
    [SerializeField] private RetirementCurves curves;

    public ParticleSystem CandleFlameParticles => candleFlameParticles;

    private Coroutine _sequenceCo;
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

    public void SetCandleState(bool lit, bool pulse)
    {
        if (candleImage != null)
            candleImage.sprite = lit ? candleLit : candleUnlit;

        if (lit)
            PlayCandleFlame();
        else
            StopCandleFlame();

        _shouldPulseCandle = pulse && candleRect != null;
        StopPulse();

        if (candleRect != null)
            candleRect.localScale = Vector3.one;

        TryStartPulse();
    }

    public void PlayCandleFlame()
    {
        if (candleFlameParticles != null && !candleFlameParticles.isPlaying)
            candleFlameParticles.Play();
    }

    public void StopCandleFlame()
    {
        if (candleFlameParticles != null && candleFlameParticles.isPlaying)
            candleFlameParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void TryStartPulse()
    {
        if (!_shouldPulseCandle || _pulseCo != null || !isActiveAndEnabled || candleRect == null)
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
                candleRect.localScale = new Vector3(s, s, s);
                yield return null;
            }

            t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime * 2f;
                float s = Mathf.Lerp(1.15f, 1f, Mathf.SmoothStep(0f, 1f, t));
                candleRect.localScale = new Vector3(s, s, s);
                yield return null;
            }
        }
    }

    public void PlayHonorCeremony(string bonusDescription)
    {
        LeanTween.cancel(gameObject);

        if (_sequenceCo != null)
            StopCoroutine(_sequenceCo);

        StopPulse();
        _sequenceCo = StartCoroutine(HonorSequence(bonusDescription));
    }

    private IEnumerator HonorSequence(string bonusDescription)
    {
        if (candleImage != null)
            candleImage.sprite = candleUnlit;

        StopCandleFlame();

        if (floatyGroup != null)
            floatyGroup.alpha = 0f;

        Vector2 startPos = floatyRect != null ? floatyRect.anchoredPosition : Vector2.zero;

        if (candleRect != null)
        {
            candleRect.localScale = Vector3.one;
            LeanTween.scale(candleRect, Vector3.one * 1.35f, 0.15f)
                .setEase(LeanTweenType.easeOutQuad)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (candleRect == null)
                        return;

                    LeanTween.scale(candleRect, Vector3.one, 0.15f)
                        .setEase(LeanTweenType.easeInQuad)
                        .setIgnoreTimeScale(true);
                });
        }

        yield return new WaitForSecondsRealtime(0.15f);

        PlayCeremonySfx(honorAudioSource, candleIgniteClip);

        PlayCandleFlame();

        if (candleImage != null)
            candleImage.sprite = candleLit;

        yield return new WaitForSecondsRealtime(0.2f);

        if (bonusFloatyText != null)
            bonusFloatyText.text = string.IsNullOrWhiteSpace(bonusDescription) ? "Inspired" : bonusDescription;

        if (floatyGroup != null)
            floatyGroup.alpha = 1f;

        if (floatyRect != null)
        {
            Vector2 endPos = startPos + (Vector2.up * 60f);
            LeanTween.value(floatyRect.gameObject, startPos, endPos, 1.0f)
                .setEase(curves != null ? curves.nameTextFloat : LeanTweenType.easeOutCubic)
                .setIgnoreTimeScale(true)
                .setOnUpdate((Vector2 v) =>
                {
                    if (floatyRect != null)
                        floatyRect.anchoredPosition = v;
                });
        }

        yield return new WaitForSecondsRealtime(0.4f);

        if (floatyGroup != null)
        {
            LeanTween.alphaCanvas(floatyGroup, 0f, 0.6f)
                .setEase(LeanTweenType.easeInQuad)
                .setIgnoreTimeScale(true)
                .setOnComplete(() =>
                {
                    if (floatyGroup != null)
                        floatyGroup.alpha = 0f;

                    if (floatyRect != null)
                        floatyRect.anchoredPosition = startPos;
                });
        }

        yield return new WaitForSecondsRealtime(0.7f);
        _sequenceCo = null;
        TryStartPulse();
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
