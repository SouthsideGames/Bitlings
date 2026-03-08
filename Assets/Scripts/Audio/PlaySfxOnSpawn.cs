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

        if (AudioManager.I != null)
        {
            AudioManager.I.PlayClipOneShot(clip, volumeMult, finalPitch);
            return;
        }

        AudioSource.PlayClipAtPoint(clip, transform.position, Mathf.Clamp(volumeMult, 0f, 2f));
    }
}