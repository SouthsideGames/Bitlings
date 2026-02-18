using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public sealed class AchievementManager : MonoBehaviour
{
    public static AchievementManager I { get; private set; }

    [Header("Library")]
    [Tooltip("If null, manager will load AchievementLibrary from Resources/Achievements/AchievementLibrary")]
    [SerializeField] private AchievementLibrarySO library;

    [Header("Save Behavior")]
    [SerializeField] private bool saveOnEveryProgress = false;
    [SerializeField] private bool debugLogs = false;

    public event Action<AchievementEntrySO> OnUnlocked;
    public event Action<AchievementEntrySO, int, int> OnProgressed; // entry, newValue, goal

    private readonly Dictionary<string, AchievementEntrySO> _idToEntry =
        new Dictionary<string, AchievementEntrySO>(StringComparer.Ordinal);

    private bool _initialized;
    private int _maxWinStreakSeen;


    // ─────────────────────────────────────────────
    // Toast delivery reliability
    // ─────────────────────────────────────────────
    private readonly Queue<AchievementEntrySO> _pendingToasts = new Queue<AchievementEntrySO>();
    private readonly HashSet<string> _pendingToastIds = new HashSet<string>(StringComparer.Ordinal);

    private void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        DontDestroyOnLoad(gameObject);
    }

    private void OnEnable()
    {
        TryInitialize();
        HookEvents();

        SceneManager.sceneLoaded += OnSceneLoaded;
        FlushPendingToasts();
    }

    private void OnDisable()
    {
        UnhookEvents();
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void TryInitialize()
    {
        if (_initialized) return;

        SaveManager.LoadOrCreate();

        if (library == null)
            library = Resources.Load<AchievementLibrarySO>("Achievements/AchievementLibrary");

        if (library == null)
        {
            Debug.LogWarning("[AchievementManager] No AchievementLibrary found. Create one at Resources/Achievements/AchievementLibrary.");
            _initialized = true;
            return;
        }

        _idToEntry.Clear();
        for (int i = 0; i < library.entries.Count; i++)
        {
            var e = library.entries[i];
            if (e == null || string.IsNullOrEmpty(e.id)) continue;
            if (!_idToEntry.ContainsKey(e.id))
                _idToEntry.Add(e.id, e);
        }

        EnsureAllSaved();

        _maxWinStreakSeen = Mathf.Max(_maxWinStreakSeen, SaveManager.Data != null ? SaveManager.Data.winStreak : 0);

        EvaluateSnapshotAchievements(saveIfChanged: false);

        _initialized = true;
    }

    private void EnsureAllSaved()
    {
        var data = SaveManager.Data;
        if (data == null) return;

        data.achievements ??= new List<AchievementProgressData>();
        data.achievementMap ??= new Dictionary<string, AchievementProgressData>(StringComparer.Ordinal);

        data.achievementMap.Clear();
        for (int i = 0; i < data.achievements.Count; i++)
        {
            var a = data.achievements[i];
            if (a == null || string.IsNullOrEmpty(a.id)) continue;
            if (!data.achievementMap.ContainsKey(a.id))
                data.achievementMap.Add(a.id, a);
        }

        bool changed = false;

        foreach (var kv in _idToEntry)
        {
            string id = kv.Key;
            if (data.achievementMap.ContainsKey(id)) continue;

            // NOTE: seen=true means "not new" — we flip to false on unlock.
            var ap = new AchievementProgressData { id = id, value = 0, unlocked = false, unlockedUnix = 0, seen = true };
            data.achievements.Add(ap);
            data.achievementMap.Add(id, ap);
            changed = true;
        }

        if (changed)
            SaveManager.Save();
    }

    private void HookEvents()
    {
        GameEvents.MonsterCaptured += OnMonsterCaptured;
        GameEvents.MonsterEvolved += OnMonsterEvolved;
        GameEvents.BossDefeated += OnBossDefeated;
        GameEvents.BattleFinished += OnBattleFinished;
        GameEvents.ResourceAdded += OnResourceAdded;
        GameEvents.IdleBatchCompleted += OnIdleBatchCompleted;
        GameEvents.WinStreakChanged += OnWinStreakChanged;
        GameEvents.FavoritesChanged += OnFavoritesChanged;
        GameEvents.OnTeamChanged += OnTeamChanged;
    }

    private void UnhookEvents()
    {
        GameEvents.MonsterCaptured -= OnMonsterCaptured;
        GameEvents.MonsterEvolved -= OnMonsterEvolved;
        GameEvents.BossDefeated -= OnBossDefeated;
        GameEvents.BattleFinished -= OnBattleFinished;
        GameEvents.ResourceAdded -= OnResourceAdded;
        GameEvents.IdleBatchCompleted -= OnIdleBatchCompleted;
        GameEvents.WinStreakChanged -= OnWinStreakChanged;
        GameEvents.FavoritesChanged -= OnFavoritesChanged;
        GameEvents.OnTeamChanged -= OnTeamChanged;
    }

    // ─────────────────────────────────────────────────────────────
    // Public helpers for UI
    // ─────────────────────────────────────────────────────────────

    public AchievementProgressData GetProgress(string id)
    {
        if (SaveManager.Data == null) return null;
        SaveManager.Data.achievementMap ??= new Dictionary<string, AchievementProgressData>(StringComparer.Ordinal);
        SaveManager.Data.achievementMap.TryGetValue(id, out var p);
        return p;
    }

    public IReadOnlyList<AchievementEntrySO> GetAllEntries()
    {
        if (library == null) return Array.Empty<AchievementEntrySO>();
        return library.entries;
    }

    public void MarkAllUnlockedAsSeen()
    {
        var data = SaveManager.Data;
        if (data == null || data.achievements == null) return;

        bool changed = false;
        for (int i = 0; i < data.achievements.Count; i++)
        {
            var a = data.achievements[i];
            if (a == null) continue;

            if (a.unlocked && !a.seen)
            {
                a.seen = true;
                changed = true;
            }
        }

        if (changed)
            SaveManager.Save();
    }

    // ─────────────────────────────────────────────────────────────
    // Event handlers
    // ─────────────────────────────────────────────────────────────

    private void OnMonsterCaptured(string monsterId, MonsterType type)
    {
        ProgressAll(AchievementTrigger.TotalCaptures, 1);
        ProgressWhere(AchievementTrigger.CapturesByType, e => e.useTypeFilter && e.typeFilter.Equals(type), 1);
        EvaluateSnapshotAchievements(saveIfChanged: saveOnEveryProgress);
    }

    private void OnMonsterEvolved(string monsterId)
    {
        ProgressAll(AchievementTrigger.TotalEvolutions, 1);
    }

    private void OnBossDefeated(string bossId)
    {
        ProgressAll(AchievementTrigger.BossDefeats, 1);
    }

    private void OnBattleFinished(BattleResult r)
    {
        ProgressAll(AchievementTrigger.TotalBattles, 1);

        if (r.victory && !r.escaped)
            ProgressAll(AchievementTrigger.BattleWins, 1);

        if (r.victory && !r.escaped && r.damageTaken == 0)
            ProgressAll(AchievementTrigger.PerfectBattles, 1);
    }

    private void OnResourceAdded(ResourceType t, int amount)
    {
        ProgressWhere(
            AchievementTrigger.CreditsEarned,
            e => !e.useResourceFilter || e.resourceFilter.Equals(t),
            Mathf.Max(0, amount)
        );
    }

    private void OnIdleBatchCompleted(int count)
    {
        ProgressAll(AchievementTrigger.IdleBatchesCompleted, Mathf.Max(0, count));
    }

    private void OnWinStreakChanged(int streak)
    {
        _maxWinStreakSeen = Mathf.Max(_maxWinStreakSeen, streak);
        SetMaxProgress(AchievementTrigger.WinStreakMax, _maxWinStreakSeen);
    }

    private void OnFavoritesChanged()
    {
        EvaluateSnapshotAchievements(saveIfChanged: saveOnEveryProgress);
    }

    private void OnTeamChanged()
    {
        EvaluateSnapshotAchievements(saveIfChanged: saveOnEveryProgress);
    }

    // ─────────────────────────────────────────────────────────────
    // Core progress logic
    // ─────────────────────────────────────────────────────────────

    private void EvaluateSnapshotAchievements(bool saveIfChanged)
    {
        var data = SaveManager.Data;
        if (data == null) return;

        int favCount  = data.favoriteMonsterIds != null ? data.favoriteMonsterIds.Count : (data.favoriteMonsterIdsList?.Count ?? 0);
        int ownedCount = data.ownedIds != null ? data.ownedIds.Count : (data.ownedIdsList?.Count ?? 0);
        int typeCount  = data.seenTypes != null ? data.seenTypes.Count : (data.seenTypesList?.Count ?? 0);

        bool changed = false;

        changed |= SetMaxProgress(AchievementTrigger.FavoritesCount, favCount, allowSave: false);
        changed |= SetMaxProgress(AchievementTrigger.OwnMonstersCount, ownedCount, allowSave: false);
        changed |= SetMaxProgress(AchievementTrigger.DiscoverTypesCount, typeCount, allowSave: false);

        if (changed && saveIfChanged)
            SaveManager.Save();
    }

    private void ProgressAll(AchievementTrigger trig, int delta)
    {
        ProgressWhere(trig, _ => true, delta);
    }

    private void ProgressWhere(AchievementTrigger trig, Func<AchievementEntrySO, bool> predicate, int delta)
    {
        if (delta <= 0) return;

        bool anyChanged = false;

        foreach (var e in _idToEntry.Values)
        {
            if (e == null || e.trigger != trig) continue;
            if (!predicate(e)) continue;

            bool changed = AddProgress(e, delta);
            anyChanged |= changed;
        }

        if (anyChanged && saveOnEveryProgress)
            SaveManager.Save();
    }

    private bool SetMaxProgress(AchievementTrigger trig, int value, bool allowSave = true)
    {
        bool anyChanged = false;

        foreach (var e in _idToEntry.Values)
        {
            if (e == null || e.trigger != trig) continue;

            var p = GetOrCreateProgress(e.id);
            if (p == null || p.unlocked) continue;

            int clamped = Mathf.Max(0, value);
            if (clamped <= p.value) continue;

            p.value = clamped;
            anyChanged = true;

            FireProgress(e, p);

            if (p.value >= e.goal)
                Unlock(e, p);
        }

        if (anyChanged && allowSave && saveOnEveryProgress)
            SaveManager.Save();

        return anyChanged;
    }

    private bool AddProgress(AchievementEntrySO e, int delta)
    {
        var p = GetOrCreateProgress(e.id);
        if (p == null || p.unlocked) return false;

        int before = p.value;
        p.value = Mathf.Max(0, p.value + delta);

        if (p.value == before) return false;

        FireProgress(e, p);

        if (p.value >= e.goal)
            Unlock(e, p);

        return true;
    }

    private AchievementProgressData GetOrCreateProgress(string id)
    {
        var data = SaveManager.Data;
        if (data == null) return null;

        data.achievements ??= new List<AchievementProgressData>();
        data.achievementMap ??= new Dictionary<string, AchievementProgressData>(StringComparer.Ordinal);

        if (!data.achievementMap.TryGetValue(id, out var p) || p == null)
        {
            p = new AchievementProgressData { id = id, value = 0, unlocked = false, unlockedUnix = 0, seen = true };
            data.achievements.Add(p);
            data.achievementMap[id] = p;
            SaveManager.Save();
        }

        return p;
    }

    private void Unlock(AchievementEntrySO e, AchievementProgressData p)
    {
        if (p.unlocked) return;

        p.unlocked = true;
        p.value = Mathf.Max(p.value, e.goal);
        p.unlockedUnix = SaveManager.NowUnix();
        p.seen = false;

        if (debugLogs)
            Debug.Log($"[Achievement] Unlocked {e.id} - {e.displayName}");

        OnUnlocked?.Invoke(e);

        // Robust toast delivery: queue if toast UI is not present yet (battle/scene transitions).
        EnqueueToast(e);

        SaveManager.Save();
    }

    private void FireProgress(AchievementEntrySO e, AchievementProgressData p) => OnProgressed?.Invoke(e, p.value, e.goal);

    // ─────────────────────────────────────────────
    // Toast Reliability
    // ─────────────────────────────────────────────

    private void EnqueueToast(AchievementEntrySO entry)
    {
        if (entry == null) return;

        // If toast exists now, deliver immediately.
        if (AchievementToastUI.I != null)
        {
            AchievementToastUI.I.QueueUnlocked(entry);
            return;
        }

        // Otherwise queue and dedupe by id.
        if (string.IsNullOrEmpty(entry.id)) return;
        if (_pendingToastIds.Contains(entry.id)) return;

        _pendingToastIds.Add(entry.id);
        _pendingToasts.Enqueue(entry);
    }

    private void FlushPendingToasts()
    {
        var toast = AchievementToastUI.I;
        if (toast == null) return;
        if (_pendingToasts.Count == 0) return;

        while (_pendingToasts.Count > 0)
        {
            var e = _pendingToasts.Dequeue();
            if (e == null) continue;
            if (!string.IsNullOrEmpty(e.id)) _pendingToastIds.Remove(e.id);
            toast.QueueUnlocked(e);
        }
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        FlushPendingToasts();
    }

    private void Update()
    {
        if (_pendingToasts.Count > 0 && AchievementToastUI.I != null)
            FlushPendingToasts();
    }
}

