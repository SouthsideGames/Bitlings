using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Base class for all ceremony UIs (Retirement, Evolution, Level-Up, Promotion).
/// Provides shared skip-on-tap detection, audio playback, typewriter text reveal, and animation cancellation.
/// Subclasses implement the specific ceremony sequence and skip behavior.
/// </summary>
public abstract class BaseCeremonyUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] protected CanvasGroup ceremonyRootGroup;
    [SerializeField] protected RectTransform ceremonyRoot;

    [Header("Skip")]
    [SerializeField] protected bool _skipRequested = false;

    protected Coroutine _sequenceCo;
    protected bool _isPlaying;

    /// <summary>
    /// Plays the ceremony sequence. Call after Prepare().
    /// </summary>
    public void Play()
    {
        if (_sequenceCo != null)
            StopCoroutine(_sequenceCo);

        gameObject.SetActive(true);
        _skipRequested = false;
        _sequenceCo = StartCoroutine(CeremonySequence());
    }

    protected virtual void Update()
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

    /// <summary>
    /// Executes the main ceremony animation sequence.
    /// Subclasses override to define their specific behavior.
    /// </summary>
    protected abstract IEnumerator CeremonySequence();

    /// <summary>
    /// Executes the skip-to-end behavior.
    /// Subclasses override to define instant end state and fade out.
    /// </summary>
    protected abstract IEnumerator SkipSequence();

    /// <summary>
    /// Cancels all LeanTween animations on this GameObject and ceremonyRoot.
    /// Subclasses should override, call base.CancelAllTweens(), then cancel their own RectTransforms.
    /// </summary>
    protected virtual void CancelAllTweens()
    {
        LeanTween.cancel(gameObject);
        if (ceremonyRoot != null)
            LeanTween.cancel(ceremonyRoot.gameObject);
    }

    /// <summary>
    /// Plays a ceremony sound effect via AudioManager's SFX scale, falling back to direct playback if unavailable.
    /// </summary>
    protected void PlayCeremonySfx(AudioSource source, AudioClip clip)
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

    /// <summary>
    /// Typewriter-style text reveal coroutine.
    /// Incrementally shows characters in the label over the given duration.
    /// </summary>
    protected IEnumerator RevealText(TMP_Text label, string fullText, float totalDuration)
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
}
