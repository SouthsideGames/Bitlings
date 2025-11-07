using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public enum SfxType
{
    None = 0,

    // Core/UI
    Click,
    Select,
    Back,
    UIOpen,
    UIClose,

    // Economy
    CurrencyGain,
    Purchase,
    PurchaseFail,
    Upgrade,

    // Battle
    HitLight,
    HitHeavy,
    CritHit,
    Victory,
    Defeat,
    CaptureSuccess,
    CaptureFail,

    // Jobs
    JobAssign,
    JobComplete,
    JobError,

    // Progress
    LevelUp,
    Unlock,
    Achievement,

    // Titles
    TitleEquip,
    TitleUnequip,
}

[Serializable]
public class SfxEntry
{
    public SfxType type;
    public List<AudioClip> clips = new();

    [Range(0f, 1f)] public float volume = 1f;
    [Range(0.5f, 1.5f)] public float pitchMin = 0.98f;
    [Range(0.5f, 1.5f)] public float pitchMax = 1.02f;

    [Tooltip("Per-type cooldown to limit spam (seconds). 0 = use manager default.")]
    [Min(0f)] public float cooldown = 0f;

    [Header("Optional: music ducking")]
    public bool duckMusic = false;
    [Range(-24f, 0f)] public float duckDb = -6f;
    [Min(0f)] public float duckDuration = 0.25f;
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager I { get; private set; }

    // ───────────────────────────────────────────────────────────── Mixer / Buses
    [Header("Mixer")]
    public AudioMixer mixer;
    public AudioMixerGroup masterGroup;
    public AudioMixerGroup musicGroup;
    public AudioMixerGroup sfxGroup;

    [Tooltip("Exposed mixer parameter names (case-sensitive).")]
    public string masterVolParam = "MasterVol";
    public string musicVolParam  = "MusicVol";
    public string sfxVolParam    = "SfxVol";

    // ─────────────────────────────────────────────────────────────────── Music
    [Header("Music")]
    [SerializeField] private AudioSource musicA;
    [SerializeField] private AudioSource musicB;
    [SerializeField] private AudioClip startingMusic;
    [SerializeField] private AudioClip battleMusic;
    [SerializeField, Min(0f)] private float defaultCrossfade = 0.75f;

    private AudioSource _activeMusic;
    private Coroutine _xfadeCo;

    // ──────────────────────────────────────────────────────────────────── SFX
    [Header("SFX")]
    [Range(1, 16)] public int sfxPoolSize = 4;
    [SerializeField] private List<AudioSource> sfxPool = new();
    private int _sfxPoolIndex = 0;

    [Tooltip("Default cooldown to prevent rapid spam (seconds).")]
    [Range(0f, 0.5f)] public float defaultSfxCooldown = 0.08f;

    public List<SfxEntry> sfxCatalog = new();

    private readonly Dictionary<SfxType, SfxEntry> _sfxMap = new();
    private readonly Dictionary<SfxType, float> _nextPlayableAt = new();

    // ─────────────────────────────────────────────────────────── Auto Routing
    [Header("Auto Music Routing")]
    public bool autoSwapForEncounter = true;

    // ───────────────────────────────────────────────────────────── Settings
    // Store as 0..1 linear values and per-bus mutes
    private float _master01 = 0.8f;
    private float _music01  = 0.8f;
    private float _sfx01    = 0.9f;
    private bool  _muteAll   = false;
    private bool  _muteMusic = false;
    private bool  _muteSfx   = false;

    // ────────────────────────────────────────────────────────────── Lifecycle
    private void Awake()
    {
        if (I != null && I != this)
        {
            gameObject.SetActive(false);
            Destroy(this);
            return;
        }
        I = this;
        DontDestroyOnLoad(gameObject);

        // Map catalog
        _sfxMap.Clear();
        foreach (var e in sfxCatalog)
            if (!_sfxMap.ContainsKey(e.type)) _sfxMap.Add(e.type, e);

        // Route music to Music group
        if (musicA != null) musicA.outputAudioMixerGroup = musicGroup;
        if (musicB != null) musicB.outputAudioMixerGroup = musicGroup;
        _activeMusic = musicA;

        EnsureSfxPool();

        LoadOrDefaultSettings();
        ApplyMixerVolumes();

        if (autoSwapForEncounter)
        {
            // UI panels (instance event with (PanelId id, bool opened))
            if (UIManager.I != null)
                UIManager.I.OnPanelChanged += OnPanelChanged;

            // Encounter state (parameterless Action)
            if (EncounterManager.I != null)
                EncounterManager.I.OnStateChanged += OnEncounterStateChanged;
        }

        if (startingMusic != null)
            PlayMusic(startingMusic, loop: true, crossfade: defaultCrossfade);
    }

    private void OnDestroy()
    {
        if (I == this) I = null;

        if (autoSwapForEncounter)
        {
            if (UIManager.I != null)
                UIManager.I.OnPanelChanged -= OnPanelChanged;

            if (EncounterManager.I != null)
                EncounterManager.I.OnStateChanged -= OnEncounterStateChanged;
        }
    }

    // ─────────────────────────────────────────────────────────────── Public API
    // Music
    public void PlayMusic(AudioClip clip, bool loop = true, float crossfade = -1f)
    {
        if (!clip) return;
        if (crossfade < 0f) crossfade = defaultCrossfade;

        var next = (_activeMusic == musicA) ? musicB : musicA;
        next.clip = clip;
        next.loop = loop;
        next.volume = 0f;
        next.outputAudioMixerGroup = musicGroup;
        next.Play();

        if (_xfadeCo != null) StopCoroutine(_xfadeCo);
        _xfadeCo = StartCoroutine(C0_Crossfade(_activeMusic, next, crossfade));
        _activeMusic = next;
    }

    public void StopMusic(float fadeOut = 0.25f)
    {
        if (_activeMusic == null || !_activeMusic.isPlaying) return;
        if (_xfadeCo != null) StopCoroutine(_xfadeCo);
        _xfadeCo = StartCoroutine(C0_FadeOutStop(_activeMusic, fadeOut));
    }

    // SFX (typed)
    public void PlaySfx(SfxType type)
    {
        if (type == SfxType.None) return;
        if (!_sfxMap.TryGetValue(type, out var entry)) return;

        if (!PassCooldown(type, entry)) return;

        if (entry.clips == null || entry.clips.Count == 0) return;
        var clip = entry.clips[UnityEngine.Random.Range(0, entry.clips.Count)];

        var src = NextSfxSource();
        src.outputAudioMixerGroup = sfxGroup;
        src.pitch = UnityEngine.Random.Range(entry.pitchMin, entry.pitchMax);
        src.spatialBlend = 0f;
        src.panStereo = 0f;
        src.PlayOneShot(clip, entry.volume);

        if (entry.duckMusic) StartCoroutine(C0_TempDuckMusic(entry.duckDb, entry.duckDuration));
    }

    // SFX (positional or 2D panned)
    public void PlaySfxAt(SfxType type, Vector3 position, float spatialBlend = 1f, float panStereo = 0f)
    {
        if (type == SfxType.None) return;
        if (!_sfxMap.TryGetValue(type, out var entry)) return;
        if (!PassCooldown(type, entry)) return;
        if (entry.clips == null || entry.clips.Count == 0) return;

        var clip = entry.clips[UnityEngine.Random.Range(0, entry.clips.Count)];

        var go = new GameObject($"SFX_{type}");
        var src = go.AddComponent<AudioSource>();
        src.outputAudioMixerGroup = sfxGroup;
        src.clip = clip;
        src.volume = entry.volume;
        src.pitch = UnityEngine.Random.Range(entry.pitchMin, entry.pitchMax);
        src.spatialBlend = Mathf.Clamp01(spatialBlend);
        src.panStereo = Mathf.Clamp(panStereo, -1f, 1f);
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 2f;
        src.maxDistance = 25f;

        if (spatialBlend >= 1f) go.transform.position = position;

        src.Play();
        Destroy(go, clip.length + 0.1f);

        if (entry.duckMusic) StartCoroutine(C0_TempDuckMusic(entry.duckDb, entry.duckDuration));
    }

    // ── Settings API expected by SettingsPanel ──────────────────────────────────
    public float GetMasterVolume() => _master01;
    public float GetMusicVolume()  => _music01;
    public float GetSfxVolume()    => _sfx01;

    public void SetMasterVolume(float v)
    {
        _master01 = Mathf.Clamp01(v);
        MirrorToSave_StateOnly();
        ApplyMixerVolumes();
    }
    public void SetMusicVolume(float v)
    {
        _music01 = Mathf.Clamp01(v);
        MirrorToSave_StateOnly();
        ApplyMixerVolumes();
    }
    public void SetSfxVolume(float v)
    {
        _sfx01 = Mathf.Clamp01(v);
        MirrorToSave_StateOnly();
        ApplyMixerVolumes();
    }

    public void OnMuteAllToggle(bool on)
    {
        _muteAll = on;
        MirrorToSave_StateOnly();
        ApplyMixerVolumes();
    }
    public void OnMuteMusicToggle(bool on)
    {
        _muteMusic = on;
        MirrorToSave_StateOnly();
        ApplyMixerVolumes();
    }
    public void OnMuteSfxToggle(bool on)
    {
        _muteSfx = on;
        MirrorToSave_StateOnly();
        ApplyMixerVolumes();
    }

    // Backwards-compat helper if anything calls these old names
    public void SetMaster01(float v) => SetMasterVolume(v);
    public void SetMusic01 (float v) => SetMusicVolume(v);
    public void SetSfx01   (float v) => SetSfxVolume(v);

    // Compatibility hook for SettingsManager.SendMessage("ApplyVolumes")
    public void ApplyVolumes() => ApplyMixerVolumes();

    // ───────────────────────────────────────────────────────────── Internals
    private bool PassCooldown(SfxType type, SfxEntry entry)
    {
        float now = Time.unscaledTime;
        _nextPlayableAt.TryGetValue(type, out float nextAt);
        float cd = (entry.cooldown > 0f) ? entry.cooldown : defaultSfxCooldown;
        if (now < nextAt) return false;
        _nextPlayableAt[type] = now + cd;
        return true;
    }

    private void EnsureSfxPool()
    {
        sfxPool.RemoveAll(a => a == null);

        while (sfxPool.Count < sfxPoolSize)
        {
            var go = new GameObject($"SFX_Pool_{sfxPool.Count}");
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            src.outputAudioMixerGroup = sfxGroup;
            src.spatialBlend = 0f;
            sfxPool.Add(src);
        }

        while (sfxPool.Count > sfxPoolSize)
        {
            var last = sfxPool[sfxPool.Count - 1];
            if (last != null) Destroy(last.gameObject);
            sfxPool.RemoveAt(sfxPool.Count - 1);
        }
    }

    private AudioSource NextSfxSource()
    {
        if (sfxPool.Count == 0) EnsureSfxPool();
        var src = sfxPool[_sfxPoolIndex];
        _sfxPoolIndex = (_sfxPoolIndex + 1) % sfxPool.Count;
        return src;
    }

    private void LoadOrDefaultSettings()
    {
        try
        {
            // Prefer SaveManager.Data.settings
            var s = SaveManager.Data?.settings;
            if (s != null)
            {
                _master01  = Mathf.Clamp01(s.masterVolume);
                _music01   = Mathf.Clamp01(s.musicVolume);
                _sfx01     = Mathf.Clamp01(s.sfxVolume);
                _muteAll   = s.muteAll;
                _muteMusic = s.muteMusic;
                _muteSfx   = s.muteSfx;
                return;
            }
        }
        catch { /* ignore and use defaults */ }

        // sensible defaults
        _master01  = 0.8f;
        _music01   = 0.8f;
        _sfx01     = 0.9f;
        _muteAll   = false;
        _muteMusic = false;
        _muteSfx   = false;
    }

    private void MirrorToSave_StateOnly()
    {
        try
        {
            var s = SaveManager.Data?.settings;
            if (s == null) return;

            s.masterVolume = _master01;
            s.musicVolume  = _music01;
            s.sfxVolume    = _sfx01;
            s.muteAll      = _muteAll;
            s.muteMusic    = _muteMusic;
            s.muteSfx      = _muteSfx;

            SaveManager.Save();
        }
        catch { /* non-fatal */ }
    }

    private void ApplyMixerVolumes()
    {
        if (mixer == null) return;

        // Master
        float masterDb = Lin01ToDb(_muteAll ? 0f : _master01);
        mixer.SetFloat(masterVolParam, masterDb);

        // Music: respect Master * Music and individual mute
        float musicLin = (_muteAll || _muteMusic) ? 0f : (_master01 * _music01);
        mixer.SetFloat(musicVolParam, Lin01ToDb(musicLin));

        // SFX: respect Master * SFX and individual mute
        float sfxLin = (_muteAll || _muteSfx) ? 0f : (_master01 * _sfx01);
        mixer.SetFloat(sfxVolParam, Lin01ToDb(sfxLin));
    }

    private static float Lin01ToDb(float v01)
    {
        if (v01 <= 0.0001f) return -80f; // silence
        return Mathf.Log10(v01) * 20f;
    }

    private IEnumerator C0_Crossfade(AudioSource from, AudioSource to, float dur)
    {
        dur = Mathf.Max(0.01f, dur);
        float t = 0f;

        if (to   != null) to.outputAudioMixerGroup = musicGroup;
        if (from != null) from.outputAudioMixerGroup = musicGroup;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / dur);
            if (to   != null) to.volume   = a;
            if (from != null) from.volume = 1f - a;
            yield return null;
        }

        if (to   != null) to.volume = 1f;
        if (from != null)
        {
            from.Stop();
            from.clip = null;
            from.volume = 1f;
        }
    }

    private IEnumerator C0_FadeOutStop(AudioSource src, float dur)
    {
        if (src == null) yield break;
        float start = src.volume;
        float t = 0f;
        dur = Mathf.Max(0.01f, dur);

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(1f - t / dur);
            src.volume = start * a;
            yield return null;
        }

        src.Stop();
        src.clip = null;
        src.volume = 1f;
    }

    private IEnumerator C0_TempDuckMusic(float duckDb, float duration)
    {
        if (mixer == null || string.IsNullOrEmpty(musicVolParam)) yield break;

        float original;
        if (!mixer.GetFloat(musicVolParam, out original)) original = 0f;

        float target = Mathf.Clamp(original + duckDb, -80f, 0f);

        float t = 0f;
        const float ease = 0.06f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / ease;
            mixer.SetFloat(musicVolParam, Mathf.Lerp(original, target, Mathf.SmoothStep(0, 1, t)));
            yield return null;
        }

        yield return new WaitForSecondsRealtime(duration);

        t = 0f;
        while (t < 1f)
        {
            t += Time.unscaledDeltaTime / ease;
            mixer.SetFloat(musicVolParam, Mathf.Lerp(target, original, Mathf.SmoothStep(0, 1, t)));
            yield return null;
        }
        mixer.SetFloat(musicVolParam, original);
    }

    // ─────────────────────────────────────────────────────────── Auto Routing
    private void OnPanelChanged(PanelId id, bool opened)
    {
        if (!autoSwapForEncounter) return;
        if (id == PanelId.Encounter) DecideMusicByState();
    }

    private void OnEncounterStateChanged()
    {
        if (!autoSwapForEncounter) return;
        DecideMusicByState();
    }

    private void DecideMusicByState()
    {
        bool inBattle = EncounterManager.I != null && EncounterManager.I.IsInBattle;
        bool encounterOpen = UIManager.I != null && UIManager.I.IsOpen(PanelId.Encounter);

        if (inBattle && encounterOpen && battleMusic != null)
            PlayMusic(battleMusic, loop: true, crossfade: defaultCrossfade);
        else if (startingMusic != null)
            PlayMusic(startingMusic, loop: true, crossfade: defaultCrossfade);
    }
}
