using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum SfxType
{
    Click,
    ShinySpawn,
    CritHit,
    CaptureSuccess,
    CurrencyGain,
    LevelUp,
    Evolution
}

[System.Serializable]
public struct SfxEntry
{
    public SfxType type;
    public AudioClip[] variations;
    [Range(0.5f, 2f)] public float pitchMin;
    [Range(0.5f, 2f)] public float pitchMax;

    public float PitchMinOrDefault => (pitchMin <= 0f) ? 1f : pitchMin;
    public float PitchMaxOrDefault => (pitchMax <= 0f || pitchMax < PitchMinOrDefault) ? 1f : pitchMax;
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager I { get; private set; }

    [Header("Music Sources (created if null)")]
    [SerializeField] private AudioSource musicA;
    [SerializeField] private AudioSource musicB;

    [Header("SFX Source (created if null)")]
    [SerializeField] private AudioSource sfxSource;

    [Header("Music")]
    [SerializeField, Min(0f)] private float defaultFadeSeconds = 1.0f;
    [SerializeField] private AudioClip startingMusic;   // treats as Overworld / default
    [SerializeField] private AudioClip battleMusic;     // NEW: battle loop

    [Header("Auto-Swap (Encounter)")]
    [SerializeField] private bool autoSwapForEncounter = true; // NEW: enable/disable routing
    [SerializeField, Min(0f)] private float encounterFadeSeconds = 0.75f; // NEW: swap fade

    [Header("SFX Catalog")]
    [SerializeField] private List<SfxEntry> sfxCatalog = new List<SfxEntry>();

    private readonly Dictionary<SfxType, SfxEntry> _sfxMap = new Dictionary<SfxType, SfxEntry>();
    private Coroutine _xfadeCo;
    private bool _aIsActive = true;

    void Awake()
    {
        if(I != null && I != this)
        {
            enabled = false;
            return;
        }
        
        I = this;

        EnsureAudioSources();
        BuildSfxMap();
        EnsureSettingsDefaults();  
        ApplyVolumes();

        if (SettingsManager.I != null)
            SettingsManager.I.OnSettingsChanged += OnExternalSettingsChanged;

        TryHookRoutingEvents();

        if (startingMusic) PlayMusic(startingMusic, defaultFadeSeconds, true, 1f);
    }

    void OnEnable()
    {
        TryHookRoutingEvents();
        EvaluateMusicRouting(); // pick correct track if we re-enabled while in battle
    }

    void Start()
    {
        EvaluateMusicRouting();
    }

    void OnDisable()
    {
        UnhookRoutingEvents();
    }

    void OnDestroy()
    {
        if (SettingsManager.I != null)
            SettingsManager.I.OnSettingsChanged -= OnExternalSettingsChanged;

        UnhookRoutingEvents();
    }

    void OnExternalSettingsChanged() => ApplyVolumes();

    public void OnMasterSlider(float v) => SetMasterVolume(v);
    public void OnMusicSlider (float v) => SetMusicVolume(v);
    public void OnSfxSlider   (float v) => SetSfxVolume(v);

    public void OnMuteAllToggle  (bool on) { var s = S; s.muteAll = on; SaveAndApply(); }
    public void OnMuteMusicToggle(bool on) { var s = S; s.muteMusic = on; SaveAndApply(); }
    public void OnMuteSfxToggle  (bool on) { var s = S; s.muteSfx = on; SaveAndApply(); }

    public float GetMasterVolume() => Mathf.Clamp01(S.masterVolume);
    public float GetMusicVolume()  => Mathf.Clamp01(S.musicVolume);
    public float GetSfxVolume()    => Mathf.Clamp01(S.sfxVolume);

    public void SetMasterVolume(float v01)
    {
        var s = S;
        s.masterVolume = Mathf.Clamp01(v01);
        SaveAndApply();
    }

    public void SetMusicVolume(float v01)
    {
        var s = S;
        s.musicVolume = Mathf.Clamp01(v01);
        SaveAndApply();
    }

    public void SetSfxVolume(float v01)
    {
        var s = S;
        s.sfxVolume = Mathf.Clamp01(v01);
        SaveAndApply();
    }

    public void PlayMusic(AudioClip clip, float fadeSeconds = -1f, bool loop = true, float pitch = 1f)
    {
        if (!clip) return;
        if (fadeSeconds < 0f) fadeSeconds = defaultFadeSeconds;

        var active = _aIsActive ? musicA : musicB;
        if (active && active.clip == clip) return; 

        StopCurrentXfade();
        _xfadeCo = StartCoroutine(Co_CrossfadeTo(clip, fadeSeconds, loop, pitch));
    }

    public void StopMusic(float fadeSeconds = -1f)
    {
        if (fadeSeconds < 0f) fadeSeconds = defaultFadeSeconds;
        StopCurrentXfade();
        _xfadeCo = StartCoroutine(Co_FadeOutAll(fadeSeconds));
    }

    // ====== SFX ======
    public void PlaySfx(SfxType type)
    {
        if (!sfxSource) return;
        if (!_sfxMap.TryGetValue(type, out var entry)) return;
        var clips = entry.variations;
        if (clips == null || clips.Length == 0) return;

        var clip = clips.Length == 1 ? clips[0] : clips[Random.Range(0, clips.Length)];
        if (!clip) return;

        float pMin = entry.PitchMinOrDefault;
        float pMax = entry.PitchMaxOrDefault;
        if (pMax < pMin) pMax = pMin;
        sfxSource.pitch = Random.Range(pMin, pMax);

        if (S.muteAll || S.muteSfx) return;

        float vol = Mathf.Clamp01(S.masterVolume * S.sfxVolume);
        sfxSource.PlayOneShot(clip, vol);
    }

    public void PlaySfxClip(AudioClip clip, float pitch = 1f)
    {
        if (!sfxSource || !clip) return;
        if (S.muteAll || S.muteSfx) return;

        sfxSource.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
        float vol = Mathf.Clamp01(S.masterVolume * S.sfxVolume);
        sfxSource.PlayOneShot(clip, vol);
    }

    // ====== Routing: Encounter ↔ Overworld ======
    void TryHookRoutingEvents()
    {
        if (!autoSwapForEncounter) return;

        if (UIManager.I != null)
            UIManager.I.OnPanelChanged -= OnPanelChangedThenRoute; // ensure no dupes
        if (EncounterManager.I != null)
            EncounterManager.I.OnStateChanged -= OnEncounterStateChangedThenRoute;

        if (UIManager.I != null)
            UIManager.I.OnPanelChanged += OnPanelChangedThenRoute;
        if (EncounterManager.I != null)
            EncounterManager.I.OnStateChanged += OnEncounterStateChangedThenRoute;
    }

    void UnhookRoutingEvents()
    {
        if (UIManager.I != null)
            UIManager.I.OnPanelChanged -= OnPanelChangedThenRoute;
        if (EncounterManager.I != null)
            EncounterManager.I.OnStateChanged -= OnEncounterStateChangedThenRoute;
    }

    void OnPanelChangedThenRoute(PanelId id, bool isOpen)
    {
        if (!autoSwapForEncounter) return;
        if (id == PanelId.Encounter) EvaluateMusicRouting();
    }

    void OnEncounterStateChangedThenRoute()
    {
        if (!autoSwapForEncounter) return;
        EvaluateMusicRouting();
    }

    void EvaluateMusicRouting()
    {
        if (!autoSwapForEncounter) return;

        var ui  = UIManager.I;
        var enc = EncounterManager.I;

        bool encounterPanelOpen = ui != null && ui.IsOpen(PanelId.Encounter);
        bool inBattle           = enc != null && enc.IsInBattle;

        if (encounterPanelOpen && inBattle && battleMusic)
        {
            PlayMusic(battleMusic, encounterFadeSeconds, loop: true, pitch: 1f);
        }
        else if (startingMusic)
        {
            PlayMusic(startingMusic, encounterFadeSeconds, loop: true, pitch: 1f);
        }
    }

    SettingsState S
    {
        get
        {
            if (SettingsManager.I != null) return SettingsManager.I.S;
            return SaveManager.Data?.settings ?? (SaveManager.Data.settings = new SettingsState());
        }
    }

    void EnsureAudioSources()
    {
        if (!musicA)
        {
            musicA = gameObject.AddComponent<AudioSource>();
            musicA.playOnAwake = false;
            musicA.loop = true;
        }
        if (!musicB)
        {
            musicB = gameObject.AddComponent<AudioSource>();
            musicB.playOnAwake = false;
            musicB.loop = true;
        }
        if (!sfxSource)
        {
            sfxSource = gameObject.AddComponent<AudioSource>();
            sfxSource.playOnAwake = false;
            sfxSource.loop = false;
        }
    }

    void BuildSfxMap()
    {
        _sfxMap.Clear();
        for (int i = 0; i < sfxCatalog.Count; i++)
        {
            var e = sfxCatalog[i];
            if (_sfxMap.ContainsKey(e.type)) continue;
            _sfxMap.Add(e.type, e);
        }
    }

    void EnsureSettingsDefaults()
    {
        if (S.masterVolume < 0f) S.masterVolume = 0.8f;
        if (S.musicVolume  < 0f) S.musicVolume  = 0.8f;
        if (S.sfxVolume    < 0f) S.sfxVolume    = 0.9f;

        SaveManager.Save();
    }

    void ApplyVolumes()
    {
        bool muteAll   = S.muteAll;
        bool muteMusic = S.muteMusic;
        bool muteSfx   = S.muteSfx;

        float master = Mathf.Clamp01(S.masterVolume);
        float music  = Mathf.Clamp01(S.musicVolume);
        float sfx    = Mathf.Clamp01(S.sfxVolume);

        float musicOut = (muteAll || muteMusic) ? 0f : Mathf.Clamp01(master * music);
        float sfxOut   = (muteAll || muteSfx)   ? 0f : Mathf.Clamp01(master * sfx);

        if (musicA) musicA.volume   = musicOut;
        if (musicB) musicB.volume   = musicOut;
        if (sfxSource) sfxSource.volume = sfxOut;
    }

    void SaveAndApply()
    {
        SaveManager.Save();
        ApplyVolumes();
    }

    void StopCurrentXfade()
    {
        if (_xfadeCo != null)
        {
            StopCoroutine(_xfadeCo);
            _xfadeCo = null;
        }
    }

    IEnumerator Co_CrossfadeTo(AudioClip nextClip, float fadeSeconds, bool loop, float pitch)
    {
        var from = _aIsActive ? musicA : musicB;
        var to   = _aIsActive ? musicB : musicA;

        if (!to) yield break;

        to.clip = nextClip;
        to.pitch = Mathf.Clamp(pitch, 0.1f, 3f);
        to.loop  = loop;

        float baseVol = (S.muteAll || S.muteMusic) ? 0f : Mathf.Clamp01(S.masterVolume * S.musicVolume);

        to.volume = 0f;
        to.Play();

        float t = 0f;
        float inv = (fadeSeconds <= 0f) ? 1f : 1f / fadeSeconds;

        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.Clamp01(t * inv);
            if (to)   to.volume   = baseVol * k;
            if (from) from.volume = baseVol * (1f - k);
            yield return null;
        }

        if (to)   to.volume = baseVol;
        if (from)
        {
            from.Stop();
            from.clip = null;
        }

        _aIsActive = !_aIsActive;
        _xfadeCo = null;
    }

    IEnumerator Co_FadeOutAll(float fadeSeconds)
    {
        var a = musicA;
        var b = musicB;

        float baseVol = (S.muteAll || S.muteMusic) ? 0f : Mathf.Clamp01(S.masterVolume * S.musicVolume);

        float t = 0f;
        float inv = (fadeSeconds <= 0f) ? 1f : 1f / fadeSeconds;

        while (t < fadeSeconds)
        {
            t += Time.unscaledDeltaTime;
            float k = 1f - Mathf.Clamp01(t * inv);
            if (a) a.volume = baseVol * k;
            if (b) b.volume = baseVol * k;
            yield return null;
        }

        if (a) { a.Stop(); a.clip = null; a.volume = baseVol; }
        if (b) { b.Stop(); b.clip = null; b.volume = baseVol; }
        _xfadeCo = null;
    }
}
