using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum SfxType
{
    None = 0,

    // Core/UI
    Click = 1,

    // Economy
    Collect = 3,
    Purchase = 4,
    Denied = 2,

    // Battle
    Attack = 5,
    Defend = 6,
    CritHit = 7,
    Victory = 8,
    Defeat = 9,
    Run = 17,
    Focus = 18,
    CaptureSuccess = 10,
    KO = 12,
    Clutch = 13,
    Heal = 19,

    // Progress
    LevelUp = 11,
    AchievementUnlocked = 20,

    //Encounters
    ShinyEncounter = 14,
    BossEncounter = 15,
    UnqiueEncounter = 16,

    // Battle countdown
    CountdownBeep = 21,
}

[Serializable]
public class SfxEntry
{
    public SfxType type;
    public List<AudioClip> clips = new();

    [Range(0f, 1f)] public float volume = 1f;
    [Range(0.5f, 1.5f)] public float pitchMin = 0.98f;
    [Range(0.5f, 1.5f)] public float pitchMax = 1.02f;

    [Min(0f)] public float cooldown = 0f;
}

public class AudioManager : MonoBehaviour
{
    public static AudioManager I { get; private set; }

    // ─────────────────────────────────────────────────────────────
    // Music
    // ─────────────────────────────────────────────────────────────
    [Header("Music")]
    [SerializeField] private AudioSource musicA;
    [SerializeField] private AudioSource musicB;

    [Tooltip("Starting/home music pool. One clip is selected randomly at startup. No fallback is used.")]
    [SerializeField] private List<AudioClip> startingMusicPool = new();

    [Header("Battle Music")]
    [Tooltip("Optional legacy single battle clip (kept so existing inspector setups don't break).")]
    [SerializeField] private AudioClip battleMusic;
    [Tooltip("Battle music pool. When a battle begins, one clip is selected randomly for that battle.")]
    [SerializeField] private List<AudioClip> battleMusicPool = new();

    [Header("Iron Career Battle Music")]
    [Tooltip("Optional legacy single Iron Career battle clip.")]
    [SerializeField] private AudioClip ironCareerBattleMusic;
    [Tooltip("Iron Career battle music pool. One clip is selected when the player presses Start Run.")]
    [SerializeField] private List<AudioClip> ironCareerBattleMusicPool = new();

    [Header("Boss Music")]
    [Tooltip("Optional legacy single boss clip (kept so existing inspector setups don't break).")]
    [SerializeField] private AudioClip bossMusic;
    [Tooltip("Boss music pool. When a boss becomes active, one clip is selected randomly for that boss.")]
    [SerializeField] private List<AudioClip> bossMusicPool = new();

    [Header("Results")]
    [SerializeField] private AudioClip victoryMusic;
    [SerializeField] private AudioClip defeatMusic;
    [SerializeField, Min(0f)] private float defaultCrossfade = 0.75f;

    private AudioSource _activeMusic;
    private Coroutine _xfadeCo;

    private AudioClip _currentStartingMusic; // chosen at startup (NO fallback)
    private AudioClip _currentBattleMusic;   // chosen when encounter button is pressed
    private AudioClip _currentIronCareerBattleMusic; // chosen when Iron Career start is pressed
    private AudioClip _currentBossMusic;     // chosen when boss starts

    // Session cache so panel switches / manager reinstantiation do not reroll startup music.
    private static AudioClip _sessionStartingMusic;
    private static bool _sessionStartingMusicChosen;

    // Boss state (set by your encounter/boss system)
    private bool _bossActive = false;

    // Transition tracking
    private bool _prevBossActive = false;

    // Dedicated RNG for music (isolated from UnityEngine.Random seeding)
    private System.Random _musicRng;

    // ─────────────────────────────────────────────────────────────
    // SFX
    // ─────────────────────────────────────────────────────────────
    [Header("SFX")]
    [Range(1, 16)] public int sfxPoolSize = 4;

    [SerializeField] private List<AudioSource> sfxPool = new();
    private int _sfxIndex = 0;

    [Tooltip("Default cooldown per effect type (seconds).")]
    [Range(0f, 0.5f)] public float defaultCooldown = 0.08f;

    public List<SfxEntry> catalog = new();
    private readonly Dictionary<SfxType, SfxEntry> _map = new();
    private readonly Dictionary<SfxType, float> _nextPlayable = new();

    // ─────────────────────────────────────────────────────────────
    // Settings (volumes & mutes)
    // ─────────────────────────────────────────────────────────────
    private float _master01 = 0.8f;
    private float _music01 = 0.8f;
    private float _sfx01 = 0.9f;

    private bool _muteAll = false;
    private bool _muteMusic = false;
    private bool _muteSfx = false;

    // ─────────────────────────────────────────────────────────────
    // Battle result tracking (for victory/defeat music)
    // ─────────────────────────────────────────────────────────────
    private bool _hasLastBattleResult = false;
    private bool _lastBattleVictory = false;

    // Toggle for automatic swapping based on UI/battle state
    public bool autoSwapForEncounter = true;

    // Cached hook target to safely unsubscribe
    private EncounterManager _hookedEncounter;

    // ─────────────────────────────────────────────────────────────
    // Unity Lifecycle
    // ─────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;

        unchecked
        {
            int seed = (int)DateTime.UtcNow.Ticks ^ GetInstanceID();
            _musicRng = new System.Random(seed);
        }

        // Build SFX map
        _map.Clear();
        foreach (var e in catalog)
            if (!_map.ContainsKey(e.type))
                _map.Add(e.type, e);

        EnsureSfxPool();

        LoadSettings();
        ApplyVolumes();

        // Setup music
        _activeMusic = musicA;

        // Choose and cache session starting/home music (NO fallback) once per app session.
        if (!_sessionStartingMusicChosen)
        {
            _sessionStartingMusic = PickFromPoolNoFallback(startingMusicPool);
            _sessionStartingMusicChosen = true;
        }

        _currentStartingMusic = _sessionStartingMusic;

        // If pool is misconfigured, we intentionally play nothing.
        if (_currentStartingMusic != null)
            PlayMusic(_currentStartingMusic, true, defaultCrossfade);
    }

    private void OnEnable()
    {
        TryHookUiEvents();
        TryHookEncounterEvents();

        GameEvents.BattleFinished += OnBattleFinished;

        if (autoSwapForEncounter)
            RefreshMusicState();
    }

    private void OnDisable()
    {
        if (UIManager.I != null)
            UIManager.I.OnPanelChanged -= OnPanelChanged;

        UnhookEncounterEvents();

        GameEvents.BattleFinished -= OnBattleFinished;
    }

    private void Start()
    {
        // In case UIManager / EncounterManager came up after AudioManager
        TryHookUiEvents();
        TryHookEncounterEvents();

        if (autoSwapForEncounter)
            RefreshMusicState();
    }

    private void TryHookUiEvents()
    {
        if (UIManager.I != null)
        {
            UIManager.I.OnPanelChanged -= OnPanelChanged;
            UIManager.I.OnPanelChanged += OnPanelChanged;
        }
    }

    private void TryHookEncounterEvents()
    {
        var em = EncounterManager.I;
        if (em == null) return;

        if (_hookedEncounter == em) return;

        UnhookEncounterEvents();
        _hookedEncounter = em;
        _hookedEncounter.OnStateChanged += OnEncounterStateChanged;
    }

    private void UnhookEncounterEvents()
    {
        if (_hookedEncounter != null)
        {
            _hookedEncounter.OnStateChanged -= OnEncounterStateChanged;
            _hookedEncounter = null;
        }
    }

    private void OnEncounterStateChanged()
    {
        if (!autoSwapForEncounter) return;
        UpdateMusicForCurrentState();
    }

    private void OnBattleFinished(BattleResult result)
    {
        _hasLastBattleResult = true;
        _lastBattleVictory = result.victory;

        // End-of-battle: clear boss/battle selections so next encounter re-rolls cleanly
        _bossActive = false;
        _currentBossMusic = null;
        _currentBattleMusic = null;

        _prevBossActive = false;
    }

    // ─────────────────────────────────────────────────────────────
    // Public API — Boss notifications
    // ─────────────────────────────────────────────────────────────
    public void NotifyBossStarted()
    {
        _bossActive = true;
        _currentBossMusic = PickBossMusicForThisBoss();
        RefreshMusicState();
    }

    public void NotifyBossEnded()
    {
        _bossActive = false;
        _currentBossMusic = null;
        RefreshMusicState();
    }

    public void SetBossActive(bool active)
    {
        if (active) NotifyBossStarted();
        else NotifyBossEnded();
    }

    // ─────────────────────────────────────────────────────────────
    // Music selection
    // ─────────────────────────────────────────────────────────────
    private AudioClip PickFromPoolNoFallback(List<AudioClip> pool)
    {
        if (pool == null || pool.Count == 0)
            return null;

        List<AudioClip> valid = null;

        for (int i = 0; i < pool.Count; i++)
        {
            var c = pool[i];
            if (c == null) continue;

            valid ??= new List<AudioClip>();
            valid.Add(c);
        }

        if (valid == null || valid.Count == 0)
            return null;

        int idx = _musicRng.Next(0, valid.Count);
        return valid[idx];
    }

    private AudioClip PickBattleMusicForThisBattle()
    {
        var fromPool = PickFromPoolNoFallback(battleMusicPool);
        if (fromPool != null) return fromPool;
        return battleMusic;
    }

    private AudioClip PickIronCareerBattleMusicForRun()
    {
        var fromPool = PickFromPoolNoFallback(ironCareerBattleMusicPool);
        if (fromPool != null) return fromPool;
        return ironCareerBattleMusic;
    }

    private AudioClip PickBossMusicForThisBoss()
    {
        var fromPool = PickFromPoolNoFallback(bossMusicPool);
        if (fromPool != null) return fromPool;
        return bossMusic;
    }

    public void RerollStartingMusic(bool playImmediately = true)
    {
        _sessionStartingMusic = PickFromPoolNoFallback(startingMusicPool);
        _sessionStartingMusicChosen = true;
        _currentStartingMusic = _sessionStartingMusic;

        if (playImmediately && _currentStartingMusic != null)
            PlayMusic(_currentStartingMusic, true, defaultCrossfade);
    }

    public void RerollBattleMusic(bool playImmediately = false)
    {
        _currentBattleMusic = PickBattleMusicForThisBattle();
        if (playImmediately && _currentBattleMusic != null)
            PlayMusic(_currentBattleMusic, true, defaultCrossfade);
    }

    public void PickEncounterBattleMusicOnButtonPress(bool playImmediately = true)
    {
        _currentBattleMusic = PickBattleMusicForThisBattle();

        if (playImmediately && _currentBattleMusic != null)
            PlayMusic(_currentBattleMusic, true, defaultCrossfade);
    }

    public void PickIronCareerBattleMusicOnStartPress(bool playImmediately = true)
    {
        _currentIronCareerBattleMusic = PickIronCareerBattleMusicForRun();

        if (playImmediately && _currentIronCareerBattleMusic != null)
            PlayMusic(_currentIronCareerBattleMusic, true, defaultCrossfade);
    }

    public void RerollBossMusic(bool playImmediately = false)
    {
        _currentBossMusic = PickBossMusicForThisBoss();
        if (playImmediately && _currentBossMusic != null)
            PlayMusic(_currentBossMusic, true, defaultCrossfade);
    }

    // ─────────────────────────────────────────────────────────────
    // Public API for Music
    // ─────────────────────────────────────────────────────────────
    public void PlayMusic(AudioClip clip, bool loop = true, float crossfade = -1f)
    {
        if (!clip) return;

        if (IsMusicAlreadyPlaying(clip))
            return;

        if (crossfade < 0f)
            crossfade = defaultCrossfade;

        var next = (_activeMusic == musicA) ? musicB : musicA;
        if (next == null) return;

        next.clip = clip;
        next.loop = loop;
        next.volume = 0f;
        next.Play();

        if (_xfadeCo != null) StopCoroutine(_xfadeCo);
        _xfadeCo = StartCoroutine(CO_Crossfade(_activeMusic, next, crossfade));

        _activeMusic = next;
    }

    public void StopMusic(float fadeOut = 0.25f)
    {
        if (_activeMusic == null || !_activeMusic.isPlaying) return;
        if (_xfadeCo != null) StopCoroutine(_xfadeCo);
        _xfadeCo = StartCoroutine(CO_FadeOut(_activeMusic, fadeOut));
    }

    private bool IsMusicAlreadyPlaying(AudioClip clip)
    {
        if (clip == null) return false;

        if (musicA != null && musicA.isPlaying && musicA.clip == clip) return true;
        if (musicB != null && musicB.isPlaying && musicB.clip == clip) return true;

        return false;
    }

    // ─────────────────────────────────────────────────────────────
    // Public API — SFX
    // ─────────────────────────────────────────────────────────────
    public void PlaySfx(SfxType type)
    {
        if (type == SfxType.None) return;

        if (!_map.TryGetValue(type, out var entry)) return;
        if (!PassCooldown(type, entry)) return;
        if (entry.clips == null || entry.clips.Count == 0) return;

        var clip = entry.clips[UnityEngine.Random.Range(0, entry.clips.Count)];
        var src = NextSfxSource();

        src.pitch = UnityEngine.Random.Range(entry.pitchMin, entry.pitchMax);

        float volume = entry.volume * GetSfxScale();
        src.PlayOneShot(clip, volume);
    }

    /// <summary>
    /// Plays an SFX with an optional pitch & volume multiplier.
    /// Additive API (keeps existing inspector-driven pitchMin/pitchMax ranges intact).
    /// </summary>
    public void PlaySfx(SfxType type, float pitchMult, float volumeMult = 1f)
    {
        if (type == SfxType.None) return;

        if (!_map.TryGetValue(type, out var entry)) return;
        if (!PassCooldown(type, entry)) return;
        if (entry.clips == null || entry.clips.Count == 0) return;

        var clip = entry.clips[UnityEngine.Random.Range(0, entry.clips.Count)];
        var src = NextSfxSource();

        float basePitch = UnityEngine.Random.Range(entry.pitchMin, entry.pitchMax);
        src.pitch = Mathf.Clamp(basePitch * Mathf.Max(0.01f, pitchMult), 0.1f, 3f);

        float volume = entry.volume * GetSfxScale() * Mathf.Clamp(volumeMult, 0f, 2f);
        src.PlayOneShot(clip, volume);
    }

    public void PreviewSfx(SfxType type = SfxType.Click)
    {
        if (type == SfxType.None)
            type = SfxType.Click;

        PlaySfx(type);
    }

    public void PlayClipOneShot(AudioClip clip, float volumeMult = 1f, float pitch = 1f)
    {
        if (clip == null) return;

        float sfxScale = GetSfxScale();
        if (sfxScale <= 0f) return;

        var src = NextSfxSource();
        src.pitch = Mathf.Clamp(pitch, 0.1f, 3f);

        float volume = sfxScale * Mathf.Clamp(volumeMult, 0f, 2f);
        src.PlayOneShot(clip, volume);
    }

    // ─────────────────────────────────────────────────────────────
    // Volumes & Mutes (called by SettingsPanel)
    // ─────────────────────────────────────────────────────────────
    public float GetMasterVolume() => _master01;
    public float GetMusicVolume() => _music01;
    public float GetSfxVolume() => _sfx01;

    public void SetMasterVolume(float v)
    {
        _master01 = Mathf.Clamp01(v);
        SaveSettings();
        ApplyVolumes();
    }

    public void SetMusicVolume(float v)
    {
        _music01 = Mathf.Clamp01(v);
        SaveSettings();
        ApplyVolumes();
    }

    public void SetSfxVolume(float v)
    {
        _sfx01 = Mathf.Clamp01(v);
        SaveSettings();
        ApplyVolumes();
    }

    public void OnMuteAllToggle(bool on)
    {
        _muteAll = on;
        SaveSettings();
        ApplyVolumes();
    }

    public void OnMuteMusicToggle(bool on)
    {
        _muteMusic = on;
        SaveSettings();
        ApplyVolumes();
    }

    public void OnMuteSfxToggle(bool on)
    {
        _muteSfx = on;
        SaveSettings();
        ApplyVolumes();
    }

    // ─────────────────────────────────────────────────────────────
    // Volume Logic
    // ─────────────────────────────────────────────────────────────
    private float GetMusicScale()
    {
        if (_muteAll || _muteMusic) return 0f;
        return _master01 * _music01;
    }

    private float GetSfxScale()
    {
        if (_muteAll || _muteSfx) return 0f;
        return _master01 * _sfx01;
    }

    private void ApplyVolumes()
    {
        float musicScale = GetMusicScale();

        if (_activeMusic == musicA)
        {
            if (musicA != null) musicA.volume = musicScale;
            if (musicB != null) musicB.volume = 0f;
        }
        else
        {
            if (musicB != null) musicB.volume = musicScale;
            if (musicA != null) musicA.volume = 0f;
        }
    }

    // ─────────────────────────────────────────────────────────────
    // Internals — SFX Pool
    // ─────────────────────────────────────────────────────────────
    private void EnsureSfxPool()
    {
        sfxPool.RemoveAll(x => x == null);

        while (sfxPool.Count < sfxPoolSize)
        {
            var go = new GameObject($"SFX_{sfxPool.Count}");
            go.transform.SetParent(transform);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = false;
            sfxPool.Add(src);
        }
    }

    private AudioSource NextSfxSource()
    {
        var src = sfxPool[_sfxIndex];
        _sfxIndex = (_sfxIndex + 1) % sfxPool.Count;
        return src;
    }

    private bool PassCooldown(SfxType type, SfxEntry entry)
    {
        float now = Time.unscaledTime;

        _nextPlayable.TryGetValue(type, out float nextAt);
        float cd = (entry.cooldown > 0f) ? entry.cooldown : defaultCooldown;

        if (now < nextAt) return false;

        _nextPlayable[type] = now + cd;
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    // Internals — Settings Load/Save
    // ─────────────────────────────────────────────────────────────
    private void LoadSettings()
    {
        var s = SaveManager.Data?.settings;
        if (s == null) return;

        _master01 = s.masterVolume;
        _music01 = s.musicVolume;
        _sfx01 = s.sfxVolume;

        _muteAll = s.muteAll;
        _muteMusic = s.muteMusic;
        _muteSfx = s.muteSfx;
    }

    private void SaveSettings()
    {
        var s = SaveManager.Data?.settings;
        if (s == null) return;

        s.masterVolume = _master01;
        s.musicVolume = _music01;
        s.sfxVolume = _sfx01;

        s.muteAll = _muteAll;
        s.muteMusic = _muteMusic;
        s.muteSfx = _muteSfx;

        SaveManager.Save();
    }

    // ─────────────────────────────────────────────────────────────
    // Crossfade Coroutines
    // ─────────────────────────────────────────────────────────────
    private IEnumerator CO_Crossfade(AudioSource from, AudioSource to, float dur)
    {
        dur = Mathf.Max(0.01f, dur);
        float t = 0f;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(t / dur);

            float musicScale = GetMusicScale();

            if (to != null) to.volume = a * musicScale;
            if (from != null) from.volume = (1f - a) * musicScale;

            yield return null;
        }

        float finalScale = GetMusicScale();
        if (to != null) to.volume = finalScale;

        if (from != null)
        {
            from.Stop();
            from.clip = null;
            from.volume = 0f;
        }

        _xfadeCo = null;
    }

    private IEnumerator CO_FadeOut(AudioSource src, float dur)
    {
        if (src == null) yield break;

        float start = src.volume;
        float t = 0f;

        while (t < dur)
        {
            t += Time.unscaledDeltaTime;
            float a = Mathf.Clamp01(1f - t / dur);
            src.volume = start * a;
            yield return null;
        }

        src.Stop();
        src.clip = null;
        src.volume = 0f;
    }

    // ─────────────────────────────────────────────────────────────
    // UI-driven music switching
    // ─────────────────────────────────────────────────────────────
    private void OnPanelChanged(PanelId id, bool opened)
    {
        if (!autoSwapForEncounter) return;

        if (id == PanelId.Encounter ||
            id == PanelId.PostBattleSummary ||
            id == PanelId.Home)
        {
            UpdateMusicForCurrentState();
        }
    }

    private void UpdateMusicForCurrentState()
    {
        var ui = UIManager.I;

        bool encounterOpen = ui != null && ui.IsOpen(PanelId.Encounter);
        bool summaryOpen = ui != null && ui.IsOpen(PanelId.PostBattleSummary);

        // Home can be shown while Encounter remains active in the hierarchy (or was closed
        // outside of UIManager). If Home is open, we treat it as higher priority than Encounter
        // for music purposes.
        bool homeOpen = ui != null && ui.IsOpen(PanelId.Home);

        // When a battle is actively running, music should stay on the battle/boss track even if
        // the Encounter panel temporarily closes (e.g., dedicated battle view, blinder overlays,
        // auto-battle UI swaps, etc.).
        bool isInBattle = (EncounterManager.I != null) && EncounterManager.I.IsInBattle;
        bool isIronCareerBattle = IronCareerRuntime.IsActive && isInBattle;

        // "Encounter View" means the encounter panel is visible and we are NOT on the results screen.
        // Additionally:
        // - If Home is open, we consider Encounter "not the active view" unless a battle is running.
        // - If a battle is running, we treat that as Encounter/Battle view for music.
        bool encounterViewOpen = (encounterOpen && !summaryOpen && !homeOpen) || isInBattle;

        // Boss re-roll once when boss becomes active.
        if (_bossActive && !_prevBossActive)
        {
            _currentBossMusic = PickBossMusicForThisBoss();
        }
        _prevBossActive = _bossActive;

        // 1) Post-battle summary → victory/defeat music
        if (summaryOpen)
        {
            AudioClip clip = null;

            if (_hasLastBattleResult)
                clip = _lastBattleVictory ? victoryMusic : defeatMusic;

            if (clip != null)
                PlayMusic(clip, true, defaultCrossfade);
            else if (_currentStartingMusic != null)
                PlayMusic(_currentStartingMusic, true, defaultCrossfade);

            return;
        }

        // 2) Encounter view: boss music if active, else battle music
        if (encounterViewOpen)
        {
            if (isIronCareerBattle)
            {
                if (_currentIronCareerBattleMusic != null)
                {
                    PlayMusic(_currentIronCareerBattleMusic, true, defaultCrossfade);
                    return;
                }
            }

            if (_bossActive && _currentBossMusic != null)
            {
                PlayMusic(_currentBossMusic, true, defaultCrossfade);
                return;
            }

            if (_currentBattleMusic != null)
            {
                PlayMusic(_currentBattleMusic, true, defaultCrossfade);
                return;
            }
        }

        // 3) Everything else → starting/home music (NO fallback)
        if (_currentStartingMusic != null)
            PlayMusic(_currentStartingMusic, true, defaultCrossfade);
    }

    public void RefreshMusicState()
    {
        UpdateMusicForCurrentState();
    }

    public void PlayDenied() => PlaySfx(SfxType.Denied);
    public void PlayClick() => PlaySfx(SfxType.Click);
}
