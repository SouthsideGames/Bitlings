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

    private Coroutine _sequenceCo;

    public void PlayHonorCeremony(string bonusDescription)
    {
        LeanTween.cancel(gameObject);

        if (_sequenceCo != null)
            StopCoroutine(_sequenceCo);

        _sequenceCo = StartCoroutine(HonorSequence(bonusDescription));
    }

    private IEnumerator HonorSequence(string bonusDescription)
    {
        if (candleImage != null)
            candleImage.sprite = candleUnlit;

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

        if (honorAudioSource != null && candleIgniteClip != null)
            honorAudioSource.PlayOneShot(candleIgniteClip);

        if (candleFlameParticles != null)
            candleFlameParticles.Play();

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
    }
}
