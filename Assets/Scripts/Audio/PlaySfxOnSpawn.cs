using System.Collections;
using UnityEngine;

[DisallowMultipleComponent]
public class PlaySfxOnSpawn : MonoBehaviour
{
    [Header("Clip")]
    [SerializeField] private AudioClip clip;

    [Header("Playback")]
    [SerializeField, Range(0f, 2f)] private float volumeMult = 1f;
    [SerializeField] private bool randomizePitch = false;
    [SerializeField, Range(0.1f, 3f)] private float pitch = 1f;
    [SerializeField, Range(0.1f, 3f)] private float pitchMin = 0.95f;
    [SerializeField, Range(0.1f, 3f)] private float pitchMax = 1.05f;
    [SerializeField] private bool playOnEnable = true;

    [Header("Duration Limit")]
    [SerializeField] private float maxDuration = 1f;
    [SerializeField, Range(0f, 1f)] private float fadeOutDuration = 0.25f;

    private void OnEnable()
    {
        if (!playOnEnable) return;
        Play();
    }

    public void Play()
    {
        if (!clip) return;

        float finalPitch = randomizePitch
            ? Random.Range(Mathf.Min(pitchMin, pitchMax), Mathf.Max(pitchMin, pitchMax))
            : pitch;

        float effectiveDuration = clip.length / Mathf.Max(finalPitch, 0.1f);

        if (effectiveDuration <= maxDuration)
        {
            PlayNormal(finalPitch);
        }
        else
        {
            StartCoroutine(PlayWithFadeOut(finalPitch));
        }
    }

    private void PlayNormal(float finalPitch)
    {
        if (AudioManager.I != null)
        {
            AudioManager.I.PlayClipOneShot(clip, volumeMult, finalPitch);
            return;
        }

        AudioSource.PlayClipAtPoint(clip, transform.position, Mathf.Clamp(volumeMult, 0f, 2f));
    }

    private IEnumerator PlayWithFadeOut(float finalPitch)
    {
        if (AudioManager.I == null) yield break;

        float sfxScale = AudioManager.I.GetEffectiveSfxScale();
        if (sfxScale <= 0f) yield break;

        var go = new GameObject("SFX_FadeOut");
        go.transform.SetParent(transform);
        var src = go.AddComponent<AudioSource>();

        float baseVolume = Mathf.Clamp(volumeMult, 0f, 2f);
        baseVolume *= sfxScale;

        src.clip = clip;
        src.volume = baseVolume;
        src.pitch = Mathf.Clamp(finalPitch, 0.1f, 3f);
        src.spatialBlend = 0f;
        src.playOnAwake = false;
        src.Play();

        float fadeStart = Mathf.Max(maxDuration - fadeOutDuration, 0f);
        float elapsed = 0f;

        while (elapsed < maxDuration)
        {
            elapsed += Time.unscaledDeltaTime;

            if (elapsed >= fadeStart)
            {
                float t = Mathf.Clamp01((elapsed - fadeStart) / fadeOutDuration);
                src.volume = Mathf.Lerp(baseVolume, 0f, t);
            }

            yield return null;
        }

        src.Stop();
        Destroy(go);
    }
}