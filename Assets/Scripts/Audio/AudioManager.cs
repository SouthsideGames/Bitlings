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

    //Encounters
    ShinyEncounter = 14,
    BossEncounter = 15,
    UnqiueEncounter = 16,
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
    private AudioClip _currentBattleMusic;   // chosen when entering a battle
    private AudioClip _currentBossMusic;     // chosen when boss starts

    // Boss state (set by your encounter/boss system)
    private bool _bossActive = false;

    // Transition tracking so we only re-roll when entering states
    private bool _prevInBattleEncounter = false;
    private bool _prevBossActive = false;

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

        // Choose and cache session starting/home music (NO fallback)
        _currentStartingMusic = PickFromPoolNoFallback(startingMusicPool);

        // If pool is misconfigured, we intentionally play nothing.
        if (_currentStartingMusic != null)
            PlayMusic(_currentStartingMusic, true, defaultCrossfade);
    }

    private void OnEnable()
    {
        TryHookUiEvents();

        // Listen for battle end to know victory vs defeat
        GameEvents.BattleFinished += OnBattleFinished;
    }

    private void OnDisable()
    {
        if (UIManager.I != null)
            UIManager.I.OnPanelChanged -= OnPanelChanged;

        GameEvents.BattleFinished -= OnBattleFinished;
    }

    private void Start()
    {
        // In case UIManager came up after AudioManager
        TryHookUiEvents();
    }

    private void TryHookUiEvents()
    {
        if (UIManager.I != null)
        {
            UIManager.I.OnPanelChanged -= OnPanelChanged;
            UIManager.I.OnPanelChanged += OnPanelChanged;
        }
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
        _prevInBattleEncounter = false;
        // We don't force music swap here; it happens when the summary opens.
    }

    // ─────────────────────────────────────────────────────────────
    // Public API — Boss notifications
    // Call these when your boss appears / ends.
    // ─────────────────────────────────────────────────────────────
    public void NotifyBossStarted()
    {
        _bossActive = true;
        // Pick immediately so the boss track is stable for this boss.
        _currentBossMusic = PickBossMusicForThisBoss();
        RefreshMusicState();
    }

    public void NotifyBossEnded()
    {
        _bossActive = false;
        _currentBossMusic = null;
        RefreshMusicState();
    }

    // Convenience (if you prefer a single setter)
    public void SetBossActive(bool active)
    {
        if (active) NotifyBossStarted();
        else NotifyBossEnded();
    }

    // ─────────────────────────────────────────────────────────────
    // Music selection
    // ─────────────────────────────────────────────────────────────
    private static AudioClip PickFromPoolNoFallback(List<AudioClip> pool)
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

        return valid[UnityEngine.Random.Range(0, valid.Count)];
    }

    private AudioClip PickBattleMusicForThisBattle()
    {
        // Prefer pool when available
        var fromPool = PickFromPoolNoFallback(battleMusicPool);
        if (fromPool != null) return fromPool;

        // Keep legacy single clip support (does not affect "no fallback" policy for starting music)
        return battleMusic;
    }

    private AudioClip PickBossMusicForThisBoss()
    {
        // Prefer pool when available
        var fromPool = PickFromPoolNoFallback(bossMusicPool);
        if (fromPool != null) return fromPool;

        // Keep legacy single clip support
        return bossMusic;
    }

    /// <summary>
    /// Optional: re-roll the starting/home music from the pool (NO fallback).
    /// If playImmediately is true, it will crossfade to the new starting track right away.
    /// </summary>
    public void RerollStartingMusic(bool playImmediately = true)
    {
        _currentStartingMusic = PickFromPoolNoFallback(startingMusicPool);
        if (playImmediately && _currentStartingMusic != null)
            PlayMusic(_currentStartingMusic, true, defaultCrossfade);
    }

    /// <summary>
    /// Optional: re-roll battle music immediately (usually you won't call this manually).
    /// </summary>
    public void RerollBattleMusic(bool playImmediately = false)
    {
        _currentBattleMusic = PickBattleMusicForThisBattle();
        if (playImmediately && _currentBattleMusic != null)
            PlayMusic(_currentBattleMusic, true, defaultCrossfade);
    }

    /// <summary>
    /// Optional: re-roll boss music immediately (usually you won't call this manually).
    /// </summary>
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

        // Avoid restarting/crossfading to the same clip repeatedly.
        if (IsMusicAlreadyPlaying(clip))
            return;

        if (crossfade < 0f)
            crossfade = defaultCrossfade;

        var next = (_activeMusic == musicA) ? musicB : musicA;
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

    public void PreviewSfx(SfxType type = SfxType.Click)
    {
        if (type == SfxType.None)
            type = SfxType.Click;

        PlaySfx(type);
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
    // UI / Battle-driven music switching
    // ─────────────────────────────────────────────────────────────
    private void OnPanelChanged(PanelId id, bool opened)
    {
        if (!autoSwapForEncounter) return;

        // Whenever these panels change, re-evaluate the music state.
        if (id == PanelId.Encounter ||
            id == PanelId.PostBattleSummary ||
            id == PanelId.Home)
        {
            UpdateMusicForCurrentState();
        }
    }

    // If you ever hook EncounterManager state events, call this.
    private void OnEncounterStateChanged()
    {
        if (!autoSwapForEncounter) return;
        UpdateMusicForCurrentState();
    }

    private void UpdateMusicForCurrentState()
    {
        var ui = UIManager.I;

        bool inBattle = EncounterManager.I != null && EncounterManager.I.IsInBattle;
        bool encounterOpen = ui != null && ui.IsOpen(PanelId.Encounter);
        bool summaryOpen = ui != null && ui.IsOpen(PanelId.PostBattleSummary);

        bool inBattleEncounter = inBattle && encounterOpen && !summaryOpen;

        // Detect transitions into battle encounter to re-roll battle music once per battle.
        if (inBattleEncounter && !_prevInBattleEncounter)
        {
            _currentBattleMusic = PickBattleMusicForThisBattle();
        }
        _prevInBattleEncounter = inBattleEncounter;

        // Detect transitions into boss to re-roll boss music once per boss.
        if (_bossActive && !_prevBossActive)
        {
            _currentBossMusic = PickBossMusicForThisBoss();
        }
        _prevBossActive = _bossActive;

        // 1) Post-battle summary → victory or defeat music
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

        // 2) Boss music when: boss active AND we are in the battle encounter view
        if (inBattleEncounter && _bossActive && _currentBossMusic != null)
        {
            PlayMusic(_currentBossMusic, true, defaultCrossfade);
            return;
        }

        // 3) Battle music only when: in battle encounter view
        if (inBattleEncounter && _currentBattleMusic != null)
        {
            PlayMusic(_currentBattleMusic, true, defaultCrossfade);
            return;
        }

        // 4) Everything else → starting/home music (NO fallback)
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
