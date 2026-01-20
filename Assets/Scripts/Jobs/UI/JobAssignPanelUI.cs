using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using System.Reflection;

public class JobAssignPanelUI : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private PanelId panelId = PanelId.JobAssign;

    [Header("Wiring")]
    [SerializeField] private Image currentImage;
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private RectTransform listContent;
    [SerializeField] private GameObject monsterButtonPrefab;
    [SerializeField] private Button confirmBtn;
    [SerializeField] private Button removeBtn;

    [Header("Empty State")]
    [SerializeField] private GameObject noWorkersRoot;
    [SerializeField] private GameObject scrollViewRoot;

    [Header("Preview")]
    [SerializeField] private TextMeshProUGUI outputPreviewText;

    private JobType _job;
    private int _slotIndex;
    private WorkerRef _currentWorker;
    private MonsterDataSO _pendingDef;
    private string _pendingId;

    // NEW: keep the pending owned data so we can validate fatigue at confirm time.
    private OwnedMonsterData _pendingOwned;

    private JobSiteState _cachedState;

    public void Open(JobType job, int slotIndex)
    {
        _job = job;
        _slotIndex = slotIndex;

        var s = JobManager.I?.States.Find(x => x.config != null && x.config.jobType == _job);
        _cachedState = s;
        _currentWorker = null;

        if (s != null && slotIndex >= 0 && slotIndex < s.workers.Count)
            _currentWorker = s.workers[slotIndex];

        _pendingDef = null;
        _pendingId = null;
        _pendingOwned = null;

        if (currentImage)
        {
            if (_currentWorker != null && _currentWorker.def && _currentWorker.def.icon)
            {
                currentImage.sprite = _currentWorker.def.icon;
                currentImage.color = Color.white;
            }
            else
            {
                currentImage.sprite = emptySlotSprite;
            }
        }

        BuildList();
        UpdateOutputPreview(currentOnly: true);

        if (confirmBtn)
        {
            confirmBtn.onClick.RemoveAllListeners();
            confirmBtn.onClick.AddListener(OnConfirm);
            confirmBtn.interactable = true;
        }

        if (removeBtn)
        {
            removeBtn.gameObject.SetActive(true);
            removeBtn.onClick.RemoveAllListeners();
            removeBtn.onClick.AddListener(OnRemove);
            removeBtn.interactable = _currentWorker != null;
        }

        OpenSelf();
    }

    void BuildList()
    {
        if (!listContent) return;

        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);

        SetNoWorkersState(false);

        var data = SaveManager.Data;
        if (data == null || data.owned == null)
        {
            SetNoWorkersState(true);
            return;
        }

        if (monsterButtonPrefab == null)
        {
            SetNoWorkersState(true);
            return;
        }

        // Pick "best" owned monster per monsterId+shiny key.
        var bestByKey = new Dictionary<string, OwnedMonsterData>(64);
        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;

            if (string.IsNullOrEmpty(o.ownedUID))
                o.ownedUID = System.Guid.NewGuid().ToString("N");

            string key = o.monsterId + (o.isShiny ? "|S" : "|N");
            if (!bestByKey.TryGetValue(key, out var cur))
            {
                bestByKey[key] = o;
            }
            else
            {
                bool better =
                    o.level > cur.level ||
                    (o.level == cur.level && o.currentXP > cur.currentXP) ||
                    (o.level == cur.level && o.currentXP == cur.currentXP && o.shinyTier > cur.shinyTier);

                if (better) bestByKey[key] = o;
            }
        }

        // NEW: include owned reference + fatigue display.
        var entries = new List<(MonsterDataSO def, OwnedMonsterData owned, string ownedUid, float score)>();
        foreach (var kv in bestByKey)
        {
            var owned = kv.Value;
            var def = MonsterLibraryLocator.GetById(owned.monsterId);
            if (!def) continue;

            bool allowed = JobManager.I == null ? true : JobManager.I.IsTypeEligibleFor(_job, def.type);
            if (!allowed) continue;

            float score = EffectivenessScore(_job, def);
            entries.Add((def, owned, owned.ownedUID, score));
        }

        entries.Sort((a, b) => b.score.CompareTo(a.score));

        if (entries.Count == 0)
        {
            SetNoWorkersState(true);
            return;
        }

        SetNoWorkersState(false);

        foreach (var e in entries)
        {
            bool isFatigued = TryGetFatigueState(e.owned, e.ownedUid, out string etaText);

            var go = Instantiate(monsterButtonPrefab, listContent);
            var ui = go.GetComponent<JobMonsterEntryUI>();

            if (!ui)
            {
                var btn = go.GetComponent<Button>();
                var label = go.GetComponentInChildren<TextMeshProUGUI>();

                if (label)
                {
                    label.text = isFatigued
                        ? $"{e.def.displayName} (Fatigued{(string.IsNullOrEmpty(etaText) ? "" : $" • {etaText}")})"
                        : e.def.displayName;
                }

                if (btn)
                {
                    btn.interactable = !isFatigued;
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        if (isFatigued)
                        {
                            AudioManager.I?.PlayDenied();
                            return;
                        }

                        _pendingDef = e.def;
                        _pendingId = e.ownedUid;
                        _pendingOwned = e.owned;

                        if (currentImage)
                        {
                            currentImage.sprite = _pendingDef.icon ? _pendingDef.icon : emptySlotSprite;
                            currentImage.color = _pendingDef.icon ? Color.white : new Color(1, 1, 1, 0.6f);
                        }

                        UpdateOutputPreview(currentOnly: false);
                        AudioManager.I?.PlayClick();
                    });
                }
                continue;
            }

            if (ui.icon)
            {
                ui.icon.sprite = e.def.icon;
                ui.icon.enabled = e.def.icon;
            }
            if (ui.nameText) ui.nameText.text = e.def.displayName;
            if (ui.scoreText) ui.scoreText.text = $"x{e.score:0.##}";
            if (ui.typeIcon) ui.typeIcon.sprite = e.def.typeIcon;

            // NEW: fatigue presentation + interaction
            ui.SetFatigued(isFatigued, etaText);

            ui.button.onClick.RemoveAllListeners();
            ui.button.onClick.AddListener(() =>
            {
                if (isFatigued)
                {
                    AudioManager.I?.PlayDenied();
                    return;
                }

                _pendingDef = e.def;
                _pendingId = e.ownedUid;
                _pendingOwned = e.owned;

                if (currentImage)
                {
                    currentImage.sprite = _pendingDef.icon ? _pendingDef.icon : emptySlotSprite;
                    currentImage.color = _pendingDef.icon ? Color.white : new Color(1, 1, 1, 0.6f);
                }

                UpdateOutputPreview(currentOnly: false);
                AudioManager.I?.PlayClick();
            });
        }
    }

    private void SetNoWorkersState(bool noWorkers)
    {
        if (noWorkersRoot) noWorkersRoot.SetActive(noWorkers);
        if (scrollViewRoot) scrollViewRoot.SetActive(!noWorkers);
    }

    float EffectivenessScore(JobType job, MonsterDataSO def)
    {
        return def.jobSkill
             * JobBalance.RarityMult(def.rarity)
             * JobBalance.EvolutionMult(def.evolutionStage)
             * JobBalance.AffinityMult(job, def.type);
    }

    void OnConfirm()
    {
        if (JobManager.I == null) { Close(); return; }
        if (_pendingDef == null) { Close(); return; }

        // NEW: hard-block assignment if fatigued (even if UI somehow allowed it).
        if (TryGetFatigueState(_pendingOwned, _pendingId, out _))
        {
            AudioManager.I?.PlayDenied();
            return;
        }

        if (!JobManager.I.IsTypeEligibleFor(_job, _pendingDef.type))
        {
            Close();
            return;
        }

        JobManager.I.RemoveFromAnyJob(_pendingId);
        JobManager.I.TryAssignWorkerAt(_job, _slotIndex, _pendingDef, _pendingId);
        GameEvents.OnJobsChanged?.Invoke();
        GameEvents.Tutorial_FirstJobAssigned?.Invoke();
        Close();
    }

    void OnRemove()
    {
        if (JobManager.I == null) { Close(); return; }
        if (_currentWorker != null)
        {
            string id = !string.IsNullOrEmpty(_currentWorker.monsterId)
                        ? _currentWorker.monsterId
                        : _currentWorker.def?.id;
            if (!string.IsNullOrEmpty(id))
                JobManager.I.RemoveWorker(_job, id);
        }
        Close();
    }

    void Close()
    {
        CloseSelf();

        var jobsUI = FindFirstObjectByType<JobPanelUI>();
        if (jobsUI) jobsUI.SendMessage("Refresh", SendMessageOptions.DontRequireReceiver);
    }

    void OpenSelf()
    {
        if (UIManager.I) UIManager.I.Show(panelId);
        else gameObject.SetActive(true);
    }

    void CloseSelf()
    {
        if (UIManager.I) UIManager.I.Hide(panelId);
        else gameObject.SetActive(false);
    }

    void UpdateOutputPreview(bool currentOnly)
    {
        if (!outputPreviewText) return;

        if (JobManager.I == null || _cachedState == null || _cachedState.config == null)
        {
            outputPreviewText.text = "";
            return;
        }

        float currentRate = ComputeRatePerHour_WithTitles(_cachedState);
        float previewRate = currentRate;

        if (!currentOnly && _pendingDef != null)
            previewRate = ComputeRatePerHour_WithCandidate(_cachedState, _pendingDef, _pendingId, _slotIndex);

        int cur = Mathf.FloorToInt(currentRate);
        int next = Mathf.FloorToInt(previewRate);
        int delta = next - cur;

        if (delta == 0) outputPreviewText.text = $"Estimated Output: {next}/hr";
        else
        {
            string sign = delta > 0 ? "+" : "-";
            outputPreviewText.text = $"Estimated Output: {next}/hr ({sign}{Mathf.Abs(delta)}/hr)";
        }
    }

    float ComputeRatePerHour_WithCandidate(JobSiteState src, MonsterDataSO cand, string ownedUid, int slotIndex)
    {
        if (src == null || src.config == null || cand == null) return 0f;

        var sim = new JobSiteState
        {
            config = src.config,
            workers = new List<WorkerRef>(src.workers ?? new List<WorkerRef>()),
            slotFatigue01 = src.slotFatigue01,
            slotCooldownUntilUnix = src.slotCooldownUntilUnix,
            storedAmount = src.storedAmount,
            cachedRatePerHour = src.cachedRatePerHour,
            level = src.level,
            currentXP = src.currentXP,
            maxXPForLevel = src.maxXPForLevel,
            fatigue01 = src.fatigue01,
            allowClinicRelief = src.allowClinicRelief
        };

        int cap = Mathf.Max(1, sim.config.maxWorkers);
        while (sim.workers.Count < cap) sim.workers.Add(null);

        if (slotIndex < 0 || slotIndex >= sim.workers.Count)
            slotIndex = Mathf.Clamp(slotIndex, 0, sim.workers.Count - 1);

        string key = !string.IsNullOrEmpty(ownedUid) ? ownedUid : cand.id;
        sim.workers[slotIndex] = new WorkerRef { def = cand, monsterId = key };

        return ComputeRatePerHour_WithTitles(sim);
    }

    float ComputeRatePerHour_NoTitles(JobSiteState s)
    {
        if (!HasAnyWorker(s.workers)) return 0f;

        float sum = 0f;
        for (int i = 0; i < s.workers.Count; i++)
        {
            var w = s.workers[i];
            if (w?.def == null) continue;

            float mult = w.def.jobSkill
                       * JobBalance.RarityMult(w.def.rarity)
                       * JobBalance.EvolutionMult(w.def.evolutionStage)
                       * JobBalance.AffinityMult(s.config.jobType, w.def.type);

            sum += mult;
        }

        float normalized = 1f + (sum / 3f);
        float perHour = s.config.baseRatePerHour * normalized;

        perHour *= BossDebuffSystem.GetMultiplier(s.config.jobType, SaveManager.NowUnix());

        float shinyAura = ShinySystems.SiteShinyAuraMult(s.workers);
        int shinyCount = CountShinies(s.workers);
        float shinySet = 1f + (shinyCount >= 3 ? 0.12f :
                               (shinyCount == 2 ? 0.07f :
                               (shinyCount == 1 ? 0.03f : 0f)));

        float avgFatigue = AverageWorkingSlotFatigue(s);

        return perHour * shinyAura * shinySet * (1f - Mathf.Clamp01(avgFatigue));
    }

    float ComputeRatePerHour_WithTitles(JobSiteState s)
    {
        if (!HasAnyWorker(s.workers)) return 0f;

        float perHour = ComputeRatePerHour_NoTitles(s);

        try
        {
            for (int i = 0; i < s.workers.Count; i++)
            {
                var w = s.workers[i];
                if (w?.def == null) continue;

                string wid = GetBestId(w);
                perHour *= Mathf.Max(0f, TitlesAdapter.GetJobRateMult(wid, s.config.jobType));
            }
        }
        catch { }

        try
        {
            var auras = TitlesAdapter.BuildJobAuras(SaveManager.Data?.team);
            if (auras != null &&
                auras.TryGetValue(s.config.jobType, out float auraPct) &&
                Mathf.Abs(auraPct) > 0.0001f)
            {
                perHour *= (1f + auraPct);
            }
        }
        catch { }

        return perHour;
    }

    bool HasAnyWorker(List<WorkerRef> workers)
    {
        if (workers == null) return false;
        for (int i = 0; i < workers.Count; i++)
        {
            var w = workers[i];
            if (w != null && w.def != null) return true;
        }
        return false;
    }

    float AverageWorkingSlotFatigue(JobSiteState s)
    {
        if (s == null || s.workers == null || s.slotFatigue01 == null) return 0f;

        float sum = 0f;
        int count = 0;
        int max = Mathf.Min(s.workers.Count, s.slotFatigue01.Length);
        for (int i = 0; i < max; i++)
        {
            if (s.workers[i] != null && s.workers[i].def != null)
            {
                sum += Mathf.Clamp01(s.slotFatigue01[i]);
                count++;
            }
        }

        return count == 0 ? 0f : sum / count;
    }

    string GetBestId(WorkerRef w)
    {
        if (w == null) return null;
        if (!string.IsNullOrEmpty(w.monsterId)) return w.monsterId;
        return w.def ? w.def.id : null;
    }

    int CountShinies(List<WorkerRef> workers)
    {
        if (workers == null || workers.Count == 0) return 0;
        int c = 0;
        for (int i = 0; i < workers.Count; i++)
        {
            if (IsWorkerShiny(workers[i])) c++;
        }
        return c;
    }

    bool IsWorkerShiny(WorkerRef w)
    {
        if (w == null) return false;

        var ownedId = w.monsterId;
        if (!string.IsNullOrEmpty(ownedId))
        {
            var ownedList = SaveManager.Data?.owned;
            if (ownedList != null)
            {
                for (int i = 0; i < ownedList.Count; i++)
                {
                    var om = ownedList[i];
                    if (om != null && om.ownedUID == ownedId)
                        return om.isShiny;
                }
            }
        }

        var def = w.def;
        if (!def) return false;

        try
        {
            var f = def.GetType().GetField("isShiny");
            if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(def);

            var p = def.GetType().GetProperty("IsShiny");
            if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(def, null);
        }
        catch { }

        return false;
    }

    // ---------------------------
    // NEW: Fatigue detection (defensive, reflection-based)
    // ---------------------------
    private bool TryGetFatigueState(OwnedMonsterData owned, string ownedUid, out string etaText)
    {
        etaText = null;
        if (owned == null && string.IsNullOrEmpty(ownedUid)) return false;

        long now = SaveManager.NowUnix();

        // 1) Prefer a direct JobManager method if you have one (we search by name to avoid compile breaks).
        // Expected patterns: IsMonsterFatigued(string uid), GetMonsterFatigueUntil(string uid), etc.
        try
        {
            var jm = JobManager.I;
            if (jm != null)
            {
                var t = jm.GetType();

                // bool IsMonsterFatigued(string uid)
                var mIs = t.GetMethod("IsMonsterFatigued", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mIs != null && mIs.ReturnType == typeof(bool))
                {
                    var ps = mIs.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    {
                        bool r = (bool)mIs.Invoke(jm, new object[] { ownedUid });
                        if (r) return true;
                    }
                }

                // long GetMonsterFatigueUntil(string uid)
                var mUntil = t.GetMethod("GetMonsterFatigueUntil", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mUntil != null && (mUntil.ReturnType == typeof(long) || mUntil.ReturnType == typeof(int)))
                {
                    var ps = mUntil.GetParameters();
                    if (ps.Length == 1 && ps[0].ParameterType == typeof(string))
                    {
                        long until = Convert.ToInt64(mUntil.Invoke(jm, new object[] { ownedUid }));
                        if (until > now)
                        {
                            etaText = FormatEta(until - now);
                            return true;
                        }
                    }
                }
            }
        }
        catch { }

        // 2) Fall back to OwnedMonsterData fields/properties (common naming patterns).
        // Examples: fatigueUntilUnix, fatiguedUntilUnix, jobFatigueUntilUnix, etc.
        if (owned != null)
        {
            if (TryReadUntilUnix(owned, out long untilUnix))
            {
                if (untilUnix > now)
                {
                    etaText = FormatEta(untilUnix - now);
                    return true;
                }
            }

            // If there's a simple boolean flag.
            if (TryReadBool(owned, new[] { "isFatigued", "fatigued", "IsFatigued" }, out bool isFatigued) && isFatigued)
                return true;
        }

        return false;
    }

    private bool TryReadUntilUnix(object obj, out long untilUnix)
    {
        untilUnix = 0;
        if (obj == null) return false;

        string[] names =
        {
            "fatigueUntilUnix",
            "fatiguedUntilUnix",
            "jobFatigueUntilUnix",
            "fatigueEndsUnix",
            "fatigueEndUnix",
            "cooldownUntilUnix",
            "restUntilUnix"
        };

        var t = obj.GetType();

        for (int i = 0; i < names.Length; i++)
        {
            // field
            var f = t.GetField(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null)
            {
                try
                {
                    untilUnix = Convert.ToInt64(f.GetValue(obj));
                    return true;
                }
                catch { }
            }

            // property
            var p = t.GetProperty(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead)
            {
                try
                {
                    untilUnix = Convert.ToInt64(p.GetValue(obj, null));
                    return true;
                }
                catch { }
            }
        }

        return false;
    }

    private bool TryReadBool(object obj, string[] names, out bool value)
    {
        value = false;
        if (obj == null) return false;

        var t = obj.GetType();

        for (int i = 0; i < names.Length; i++)
        {
            var f = t.GetField(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(bool))
            {
                try { value = (bool)f.GetValue(obj); return true; } catch { }
            }

            var p = t.GetProperty(names[i], BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.CanRead && p.PropertyType == typeof(bool))
            {
                try { value = (bool)p.GetValue(obj, null); return true; } catch { }
            }
        }

        return false;
    }

    private string FormatEta(long seconds)
    {
        if (seconds <= 0) return "0s";

        long m = seconds / 60;
        long s = seconds % 60;
        if (m <= 0) return $"{s}s";

        long h = m / 60;
        m = m % 60;

        if (h <= 0) return $"{m}m";
        return $"{h}h {m}m";
    }
}
 