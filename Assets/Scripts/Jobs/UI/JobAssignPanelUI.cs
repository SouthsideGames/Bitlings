using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Collections.Generic;

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

    [Header("Preview")]
    [SerializeField] private TextMeshProUGUI outputPreviewText;

    private JobType _job;
    private int _slotIndex;
    private WorkerRef _currentWorker;
    private MonsterDataSO _pendingDef;
    private string _pendingId; // ownedUID of the candidate

    private JobSiteState _cachedState; // for preview math

    // ─────────────────────────────────────────────────────────────
    // Open
    // ─────────────────────────────────────────────────────────────
    public void Open(JobType job, int slotIndex)
    {
        _job      = job;
        _slotIndex = slotIndex;

        var s = JobManager.I?.States.Find(x => x.config != null && x.config.jobType == _job);
        _cachedState  = s;
        _currentWorker = null;

        if (s != null && slotIndex >= 0 && slotIndex < s.workers.Count)
            _currentWorker = s.workers[slotIndex];

        // reset pending selection
        _pendingDef = null;
        _pendingId  = null;

        // current slot image
        if (currentImage)
        {
            if (_currentWorker != null && _currentWorker.def && _currentWorker.def.icon)
            {
                currentImage.sprite = _currentWorker.def.icon;
                currentImage.color  = Color.white;
            }
            else
            {
                currentImage.sprite = emptySlotSprite;
                currentImage.color  = new Color(1f, 1f, 1f, 0.6f);
            }
        }

        BuildList();

        // show current output/hr with no candidate applied
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
            removeBtn.interactable = (_currentWorker != null);
        }

        OpenSelf();
    }

    // ─────────────────────────────────────────────────────────────
    // Build list of eligible monsters (one normal + one shiny per species)
    // ─────────────────────────────────────────────────────────────
    void BuildList()
    {
        if (!listContent) return;

        for (int i = listContent.childCount - 1; i >= 0; i--)
            Destroy(listContent.GetChild(i).gameObject);

        var data = SaveManager.Data;
        if (data == null || data.owned == null)
        {
            Debug.LogWarning("[JobAssignPanelUI] No save data or owned list.");
            return;
        }
        if (monsterButtonPrefab == null)
        {
            Debug.LogError("[JobAssignPanelUI] monsterButtonPrefab is not assigned.");
            return;
        }

        // 1) De-dupe owned list to: at most 1 normal + 1 shiny per species
        int rawOwnedCount = data.owned.Count;

        // key = speciesId + "|S" or "|N"
        var bestByKey = new Dictionary<string, OwnedMonsterData>(64);
        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;

            // ensure unique ownedUID
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

        // 2) Build eligible entries
        var entries = new List<(MonsterDataSO def, string ownedUid, float score)>();
        int eligibleCount = 0;
        foreach (var kv in bestByKey)
        {
            var owned = kv.Value;
            var def = MonsterLibraryLocator.GetById(owned.monsterId);
            if (!def) continue;

            bool allowed = JobManager.I == null ? true : JobManager.I.IsTypeEligibleFor(_job, def.type);
            if (!allowed) continue;

            eligibleCount++;
            float score = EffectivenessScore(_job, def);
            entries.Add((def, owned.ownedUID, score));
        }

        // highest score first
        entries.Sort((a, b) => b.score.CompareTo(a.score));

        Debug.Log($"[JobAssignPanelUI] Job={_job} RawOwned={rawOwnedCount} DistinctOwned={bestByKey.Count} Eligible={eligibleCount}");

        // 3) Build UI
        if (entries.Count == 0)
        {
            var placeholder = new GameObject("NoEligibleHint", typeof(RectTransform));
            placeholder.transform.SetParent(listContent, false);
            var text = placeholder.AddComponent<TextMeshProUGUI>();
            text.text = "No eligible workers";
            text.alignment = TextAlignmentOptions.Center;
            text.enableAutoSizing = true;
            text.fontSizeMin = 18;
            text.fontSizeMax = 28;
            var rt = (RectTransform)placeholder.transform;
            rt.anchorMin = new Vector2(0, 1);
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot    = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0, 80);
            return;
        }

        foreach (var e in entries)
        {
            var go = Instantiate(monsterButtonPrefab, listContent);
            var ui = go.GetComponent<JobMonsterEntryUI>();

            if (!ui)
            {
                // very simple fallback
                var btn   = go.GetComponent<Button>();
                var label = go.GetComponentInChildren<TextMeshProUGUI>();
                if (label) label.text = e.def.displayName;
                if (btn)
                {
                    btn.onClick.RemoveAllListeners();
                    btn.onClick.AddListener(() =>
                    {
                        _pendingDef = e.def;
                        _pendingId  = e.ownedUid;

                        if (currentImage)
                        {
                            currentImage.sprite = _pendingDef.icon ? _pendingDef.icon : emptySlotSprite;
                            currentImage.color  = _pendingDef.icon ? Color.white : new Color(1, 1, 1, 0.6f);
                        }

                        UpdateOutputPreview(currentOnly: false);
                    });
                }
                continue;
            }

            if (ui.icon)
            {
                ui.icon.sprite  = e.def.icon;
                ui.icon.enabled = e.def.icon;
            }
            if (ui.nameText)  ui.nameText.text  = e.def.displayName;
            if (ui.scoreText) ui.scoreText.text = $"x{e.score:0.##}";
            if (ui.typeIcon)  ui.typeIcon.sprite = e.def.typeIcon;

            ui.button.onClick.RemoveAllListeners();
            ui.button.onClick.AddListener(() =>
            {
                _pendingDef = e.def;
                _pendingId  = e.ownedUid;

                if (currentImage)
                {
                    currentImage.sprite = _pendingDef.icon ? _pendingDef.icon : emptySlotSprite;
                    currentImage.color  = _pendingDef.icon ? Color.white : new Color(1, 1, 1, 0.6f);
                }

                UpdateOutputPreview(currentOnly: false);
                AudioManager.I.PlayClick();
            });
        }
    }

    float EffectivenessScore(JobType job, MonsterDataSO def)
    {
        return def.jobSkill
             * JobBalance.RarityMult(def.rarity)
             * JobBalance.EvolutionMult(def.evolutionStage)
             * JobBalance.AffinityMult(job, def.type);
    }

    // ─────────────────────────────────────────────────────────────
    // Confirm / Remove / Close
    // ─────────────────────────────────────────────────────────────
    void OnConfirm()
    {
        if (JobManager.I == null) { Close(); return; }
        if (_pendingDef == null) { Close(); return; }

        // defensive re-check
        if (!JobManager.I.IsTypeEligibleFor(_job, _pendingDef.type))
        {
            Debug.LogWarning($"[JobAssignPanelUI] {_pendingDef.displayName} not eligible for {_job}.");
            Close();
            return;
        }

        JobManager.I.RemoveFromAnyJob(_pendingId);
        JobManager.I.TryAssignWorkerAt(_job, _slotIndex, _pendingDef, _pendingId);
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

        // Ask Jobs panel to refresh
        var jobsUI = FindFirstObjectByType<JobPanelUI>();
        if (jobsUI) jobsUI.SendMessage("Refresh", SendMessageOptions.DontRequireReceiver);
    }

    // --- UIManager glue ---
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

    // ─────────────────────────────────────────────────────────────
    // Estimated Output/hr preview
    // ─────────────────────────────────────────────────────────────
    void UpdateOutputPreview(bool currentOnly)
    {
        if (!outputPreviewText)
            return;

        if (JobManager.I == null || _cachedState == null || _cachedState.config == null)
        {
            outputPreviewText.text = "";
            return;
        }

        float currentRate = ComputeRatePerHour_WithTitles(_cachedState);
        float previewRate = currentRate;

        if (!currentOnly && _pendingDef != null)
        {
            previewRate = ComputeRatePerHour_WithCandidate(_cachedState, _pendingDef, _pendingId, _slotIndex);
        }

        int cur   = Mathf.FloorToInt(currentRate);
        int next  = Mathf.FloorToInt(previewRate);
        int delta = next - cur;

        if (delta == 0)
        {
            outputPreviewText.text = $"Estimated Output: {next}/hr";
        }
        else
        {
            string sign = delta > 0 ? "+" : "-";
            outputPreviewText.text =
                $"Estimated Output: {next}/hr ({sign}{Mathf.Abs(delta)}/hr)";
        }
    }

    float ComputeRatePerHour_WithCandidate(JobSiteState src, MonsterDataSO cand, string ownedUid, int slotIndex)
    {
        if (src == null || src.config == null || cand == null)
            return 0f;

        // shallow clone of the state for simulation
        var sim = new JobSiteState
        {
            config        = src.config,
            workers       = new List<WorkerRef>(src.workers ?? new List<WorkerRef>()),
            slotFatigue01 = src.slotFatigue01,
            slotCooldownUntilUnix = src.slotCooldownUntilUnix,
            storedAmount  = src.storedAmount,
            cachedRatePerHour = src.cachedRatePerHour,
            level         = src.level,
            currentXP     = src.currentXP,
            maxXPForLevel = src.maxXPForLevel,
            fatigue01     = src.fatigue01,
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

    // ─────────────────────────────────────────────────────────────
    // Rate computation – mirrors JobPanelUI
    // ─────────────────────────────────────────────────────────────
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

            // EXCLUDE per-worker title rate mult here
            sum += mult;
        }

        float normalized = 1f + (sum / 3f);
        float perHour    = s.config.baseRatePerHour * normalized;

        // Boss/global debuff still applies
        perHour *= BossDebuffSystem.GetMultiplier(s.config.jobType, SaveManager.NowUnix());

        // EXCLUDE site-wide title aura here

        // Shiny stacking (attribute, not title)
        float shinyAura = ShinySystems.SiteShinyAuraMult(s.workers);
        int shinyCount  = CountShinies(s.workers);
        float shinySet  = 1f + (shinyCount >= 3 ? 0.12f :
                               (shinyCount == 2 ? 0.07f :
                               (shinyCount == 1 ? 0.03f : 0f)));

        // Average working slot fatigue
        float avgFatigue = AverageWorkingSlotFatigue(s);

        return perHour * shinyAura * shinySet * (1f - Mathf.Clamp01(avgFatigue));
    }

    float ComputeRatePerHour_WithTitles(JobSiteState s)
    {
        if (!HasAnyWorker(s.workers)) return 0f;

        // Start from the "no titles" version so numbers match JobPanelUI
        float perHour = ComputeRatePerHour_NoTitles(s);

        // Per-worker titles
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

        // Site-wide aura titles
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

    // ─────────────────────────────────────────────────────────────
    // Helpers (mirrors JobPanelUI)
    // ─────────────────────────────────────────────────────────────
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
}
