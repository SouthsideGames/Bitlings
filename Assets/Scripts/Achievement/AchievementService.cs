using UnityEngine;
using System.Collections.Generic;

public class AchievementService : MonoBehaviour
{
    public static AchievementService I { get; private set; }

    private AchievementLibrarySO achievementLibrary;
    private MonsterLibrarySO monsterLibrary;

    Dictionary<string,int> prog = new Dictionary<string,int>();
    HashSet<string> done = new HashSet<string>();

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        if (achievementLibrary == null) achievementLibrary = Resources.Load<AchievementLibrarySO>("AchievementLibrary");

        if (monsterLibrary == null) monsterLibrary = Resources.Load<MonsterLibrarySO>("MonsterLibrary");
        Load();
        Wire();
        ForceRecheck();
    }

    void OnDestroy() { Unwire(); }

    void Wire()
    {
        GameEvents.BattleFinished   += OnBattleFinished;
        GameEvents.MonsterCaptured  += OnMonsterCaptured;
        GameEvents.MonsterEvolved   += OnMonsterEvolved;
        GameEvents.OnJobsChanged    += OnJobsChanged;
        GameEvents.OnResourcesChanged += OnResourcesChanged;
    }

    void Unwire()
    {
        GameEvents.BattleFinished   -= OnBattleFinished;
        GameEvents.MonsterCaptured  -= OnMonsterCaptured;
        GameEvents.MonsterEvolved   -= OnMonsterEvolved;
        GameEvents.OnJobsChanged    -= OnJobsChanged;
        GameEvents.OnResourcesChanged -= OnResourcesChanged;
    }

    void Load()
    {
        prog.Clear();
        done.Clear();
        var s = AchievementsSaveStore.Data;
        for (int i = 0; i < s.progress.Count; i++)
        {
            var kv = s.progress[i];
            if (!string.IsNullOrEmpty(kv.key)) prog[kv.key] = kv.value;
        }
        for (int i = 0; i < s.completed.Count; i++) done.Add(s.completed[i]);
    }

    void Persist()
    {
        var s = AchievementsSaveStore.Data;
        s.progress.Clear();
        foreach (var kv in prog) s.progress.Add(new ProgKV { key = kv.Key, value = kv.Value });
        s.completed = new List<string>(done);
        AchievementsSaveStore.Save();
    }

    public void AddProgress(string key, int amount)
    {
        if (amount <= 0) return;
        if (!prog.ContainsKey(key)) prog[key] = 0;
        prog[key] += amount;
        CheckAll();
        Persist();
    }

    public void Grant(string achievementId) { AddProgress(achievementId, 1); }
    public int GetProgress(string key) => prog.TryGetValue(key, out var v) ? v : 0;
    public bool IsCompleted(string id) => done.Contains(id);

    void OnBattleFinished(BattleResult r)
    {
        AddProgress("battles_total", 1);
        if (r.victory) AddProgress("wins_total", 1);
        if (r.victory && r.secondsSurvived <= 5f) AddProgress("speed_wins", 1);
    }

    void OnMonsterCaptured(string monsterId, MonsterType type)
    {
        AddProgress("captures_total", 1);
        AddProgress($"captures_type_{(int)type}", 1);
        ForceRecheck();
    }

    void OnMonsterEvolved(string monsterId)
    {
        AddProgress("evolutions_total", 1);
        ForceRecheck();
    }

    void OnJobsChanged() { AddProgress("jobs_touch_events", 1); }
    void OnResourcesChanged() { }

    public void ForceRecheck()
    {
        CheckAll();
        Persist();
    }

    void CheckAll()
    {
        if (achievementLibrary == null) return;
        var list = achievementLibrary.entries;
        for (int i = 0; i < list.Count; i++)
        {
            var a = list[i];
            if (a == null || string.IsNullOrEmpty(a.id)) continue;
            if (done.Contains(a.id)) continue;

            bool ok = false;
            switch (a.condition)
            {
                case AchievementConditionKind.Boolean:
                    ok = GetProgress(a.id) >= 1;
                    break;
                case AchievementConditionKind.CounterAtLeast:
                    ok = GetProgress(a.counterKey) >= Mathf.Max(1, a.targetValue);
                    break;
                case AchievementConditionKind.OwnAllOfType:
                    ok = OwnsAllOfType(a.requiredType);
                    break;
                case AchievementConditionKind.OwnAllOfIds:
                    ok = OwnsAllOfIds(a.requiredMonsterIds);
                    break;
            }
            if (ok) Complete(a);
        }
    }

    void Complete(AchievementEntrySO a)
    {
        done.Add(a.id);

        if (a.gemsReward > 0)
            ResourceManager.I.Add(ResourceType.Gems, a.gemsReward); // ← pay Gems via ResourceManager

        Persist();
        GameEvents.ShowRewardPopup?.Invoke(
            a.title,
            $"+{a.gemsReward} Gems",
            a.gemsReward,
            0
        );
    }


    bool OwnsMonsterId(string id)
    {
        var pd = SaveManager.Data;
        if (pd == null || pd.owned == null) return false;
        for (int i = 0; i < pd.owned.Count; i++)
            if (pd.owned[i].monsterId == id) return true;
        return false;
    }

    bool OwnsAllOfType(MonsterType type)
    {
        if (monsterLibrary == null) return false;
        var allOfType = monsterLibrary.GetAllOfType(type, true);
        if (allOfType == null || allOfType.Length == 0) return false;
        for (int i = 0; i < allOfType.Length; i++)
        {
            var def = allOfType[i];
            if (def == null) continue;
            if (!monsterLibrary.IsAvailable(def)) continue;
            if (!OwnsMonsterId(def.id)) return false;
        }
        return true;
    }

    bool OwnsAllOfIds(System.Collections.Generic.List<string> ids)
    {
        if (ids == null || ids.Count == 0) return false;
        for (int i = 0; i < ids.Count; i++)
            if (!OwnsMonsterId(ids[i])) return false;
        return true;
    }
}
