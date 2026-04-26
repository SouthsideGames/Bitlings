using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using System.Collections.Generic;
using System.Reflection;

public class JobAssignPanelUI : MonoBehaviour
{
    private sealed class EntryBinding
    {
        public JobMonsterEntryUI ui;
        public OwnedMonsterData owned;
        public string ownedUid;
        public MonsterDataSO def;
        public bool hasAssignment;
        public JobType assignedJob;
        public int assignedSlot;
        public float assignedHours;
    }

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

    // keep the pending owned data so we can validate fatigue + premium at confirm time
    private OwnedMonsterData _pendingOwned;

    private JobSiteState _cachedState;
    private readonly List<EntryBinding> _entryBindings = new List<EntryBinding>(64);
    private const float LiveRefreshSeconds = 1f;

    private void OnEnable()
    {
        // Some navigation paths re-show this panel without calling Open(...) again.
        // Rebuild from manager state so assignment/fatigue badges are always accurate.
        if (_job == JobType.None) return;
        RefreshUIAfterChange();
        StartLiveRefresh();
    }

    private void OnDisable()
    {
        StopLiveRefresh();
    }

    private void StartLiveRefresh()
    {
        CancelInvoke(nameof(RefreshLiveEntryStates));
        InvokeRepeating(nameof(RefreshLiveEntryStates), LiveRefreshSeconds, LiveRefreshSeconds);
    }

    private void StopLiveRefresh()
    {
        CancelInvoke(nameof(RefreshLiveEntryStates));
    }

    private void RefreshLiveEntryStates()
    {
        if (!isActiveAndEnabled) return;
        if (_entryBindings == null || _entryBindings.Count == 0) return;

        for (int i = 0; i < _entryBindings.Count; i++)
        {
            var b = _entryBindings[i];
            if (b == null || b.ui == null) continue;

            bool isFatigued = TryGetFatigueState(b.owned, b.ownedUid, out string etaText, out long remainingSeconds);
            float restProgress01 = ComputeRestProgress01(b.def, remainingSeconds);

            b.ui.SetResting(isFatigued, etaText, restProgress01);
            b.ui.SetAssignment(b.assignedJob, b.assignedSlot, hide: !b.hasAssignment);

            if (b.hasAssignment)
            {
                string hoursText = FormatAssignedHours(b.assignedHours);
                b.ui.SetTooltip("Assigned", $"{b.assignedJob} • Slot {b.assignedSlot + 1} • {hoursText}");
            }
            else if (isFatigued)
            {
                b.ui.SetTooltip("Resting", string.IsNullOrEmpty(etaText) ? "Recovering" : etaText);
            }
        }
    }

    private void SyncStateFromManager()
    {
        var s = JobManager.I?.States.Find(x => x.config != null && x.config.jobType == _job);
        _cachedState = s;
        _currentWorker = null;

        if (s != null && _slotIndex >= 0 && _slotIndex < s.workers.Count)
            _currentWorker = s.workers[_slotIndex];
    }

    private void ResetPendingSelection()
    {
        _pendingDef = null;
        _pendingId = null;
        _pendingOwned = null;

        UpdateConfirmInteractable();
    }

    private void RefreshUIAfterChange()
    {
        SyncStateFromManager();
        ResetPendingSelection();

        RefreshCurrentWorkerIcon();
        BuildList();
        UpdateOutputPreview(currentOnly: true);
        UpdateRemoveButtonState();
    }

    private void UpdateConfirmInteractable()
    {
        if (confirmBtn) confirmBtn.interactable = _pendingDef != null;
    }

    private void UpdateRemoveButtonState()
    {
        if (removeBtn)
        {
            removeBtn.gameObject.SetActive(true);
            removeBtn.interactable = _currentWorker != null;
        }
    }

    public void Open(JobType job, int slotIndex)
    {
        _job = job;
        _slotIndex = slotIndex;

        SyncStateFromManager();
        ResetPendingSelection();

        RefreshCurrentWorkerIcon(); // ✅ premium-aware

        BuildList();
        UpdateOutputPreview(currentOnly: true);

        if (confirmBtn)
        {
            confirmBtn.onClick.RemoveAllListeners();
            confirmBtn.onClick.AddListener(OnConfirm);
        }

        if (removeBtn)
        {
            removeBtn.onClick.RemoveAllListeners();
            removeBtn.onClick.AddListener(OnRemove);
        }

        UpdateConfirmInteractable();
        UpdateRemoveButtonState();

        OpenSelf();
        StartLiveRefresh();
    }

    private void RefreshCurrentWorkerIcon()
    {
        if (!currentImage) return;

        if (_currentWorker != null && _currentWorker.def != null)
        {
            // IMPORTANT:
            // WorkerRef.monsterId = species id
            // WorkerRef.ownedUID  = owned instance id (premium lives here)
            bool isPremium = IsWorkerPremium(_currentWorker);
            // If this slot is in legacy "species-only" mode (no ownedUID), ResolveOwned can return null when the
            // player owns multiple copies. In that case, fall back to the saved preferred variant.
            if (!isPremium && _currentWorker.def != null && !string.IsNullOrEmpty(_currentWorker.def.id))
                isPremium = MonsterVariantPreference.IsPreferredPremium(_currentWorker.def.id);
            // Jobs should use the FRONT icon (MonsterDataSO.icon / MonsterDataSO.premiumIcon).
            var spr = MonsterNameFormatter.GetIcon(_currentWorker.def, isPremium, backIcon: false);
            if (spr == null) spr = _currentWorker.def.icon;

            if (spr != null)
            {
                currentImage.sprite = spr;
                currentImage.color = Color.white;
                return;
            }
        }

        currentImage.sprite = emptySlotSprite;
        currentImage.color = new Color(1, 1, 1, 0.6f);
    }

    void BuildList()
    {
        if (!listContent) return;

        _entryBindings.Clear();

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

        // Build one entry per species, but ALWAYS respect the player's saved premium/non-premium preference.
        // This mirrors battle behavior and prevents "selected premium" from silently assigning the base variant.
        //
        // NOTE: We still only show one entry per monsterId to keep the picker clean.
        var seenMonsterIds = new HashSet<string>(64);
        for (int i = 0; i < data.owned.Count; i++)
        {
            var o = data.owned[i];
            if (o == null || string.IsNullOrEmpty(o.monsterId)) continue;
            if (string.IsNullOrEmpty(o.ownedUID)) o.ownedUID = Guid.NewGuid().ToString("N");
            seenMonsterIds.Add(o.monsterId);
        }

        // include owned reference + fatigue display
        var entries = new List<(MonsterDataSO def, OwnedMonsterData owned, string ownedUid, float score)>();
        foreach (var monsterId in seenMonsterIds)
        {
            var owned = MonsterVariantPreference.GetPreferredOwned(monsterId);
            if (owned == null || string.IsNullOrEmpty(owned.ownedUID)) continue;

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

        float currentRatePerHour = JobManager.I ? JobManager.I.EstimateSiteOutputPerHour(_job) : 0f;

        foreach (var e in entries)
        {
            bool isFatigued = TryGetFatigueState(e.owned, e.ownedUid, out string etaText, out long remainingSeconds);
            bool isPremium = (e.owned != null) && (e.owned.isPremium || e.owned.premiumTier > 0);
            float restProgress01 = ComputeRestProgress01(e.def, remainingSeconds);

            float candidateResultPerHour = 0f;
            float candidateDeltaPerHour = 0f;
            bool hasRatePreview = JobManager.I != null;
            if (hasRatePreview)
            {
                candidateResultPerHour = JobManager.I.EstimateSiteOutputPerHour(_job, e.def, e.ownedUid, _slotIndex);
                candidateDeltaPerHour = candidateResultPerHour - currentRatePerHour;
            }

            bool hasAssignment = TryGetAssignmentState(e.ownedUid, e.def ? e.def.id : null, out JobType assignedJob, out int assignedSlot, out float assignedHours);

            var go = Instantiate(monsterButtonPrefab, listContent);
            var ui = go.GetComponent<JobMonsterEntryUI>();

            // If the prefab doesn't have JobMonsterEntryUI, fall back to generic Button + label,
            // but STILL set a premium-aware currentImage preview on click.
            if (!ui)
            {
                var btn = go.GetComponent<Button>();
                var label = go.GetComponentInChildren<TextMeshProUGUI>();

                if (label)
                {
                    string name = MonsterNameFormatter.Format(e.def, isPremium);
                    label.text = isFatigued
                        ? $"{name} (Resting{(string.IsNullOrEmpty(etaText) ? "" : $" • {etaText}")})"
                        : name;
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
                            GameEvents.RaiseToast(string.IsNullOrEmpty(etaText) ? "Worker resting." : $"Worker resting: {etaText}");
                            return;
                        }

                        _pendingDef = e.def;
                        _pendingId = e.ownedUid;
                        _pendingOwned = e.owned;

                        RefreshPendingPreviewIcon(); // ✅ premium-aware

                        UpdateConfirmInteractable();

                        UpdateOutputPreview(currentOnly: false);
                        AudioManager.I?.PlayClick();
                    });
                }
                continue;
            }

            // ✅ Premium icon + premium name
            if (ui.icon)
            {
                var spr = MonsterNameFormatter.GetIcon(e.def, isPremium, backIcon: false);
                if (spr == null) spr = e.def.icon;

                ui.icon.sprite = spr;
                ui.icon.enabled = (spr != null);
            }

            if (ui.nameText) ui.nameText.text = MonsterNameFormatter.Format(e.def, isPremium);
            if (ui.scoreText)
            {
                if (hasRatePreview)
                    ui.scoreText.text = $"{candidateDeltaPerHour:+0.##;-0.##;0}/hr";
                else
                    ui.scoreText.text = "?/hr";
            }
            ui.SetFatigueInfo(e.def ? e.def.fatigueCooldownHours : 0f);
            if (ui.typeIcon) ui.typeIcon.sprite = e.def.typeIcon;

            // assignment + fatigue/rest presentation
            ui.SetAssignment(assignedJob, assignedSlot, hide: !hasAssignment);
            ui.SetResting(isFatigued, etaText, restProgress01);

            if (hasAssignment)
            {
                string hoursText = FormatAssignedHours(assignedHours);
                string subtitle = $"{assignedJob} • Slot {assignedSlot + 1} • {hoursText}";
                ui.SetTooltip("Assigned", subtitle);
            }
            else if (isFatigued)
            {
                ui.SetTooltip("Resting", string.IsNullOrEmpty(etaText) ? "Recovering" : etaText);
            }

            ui.button.onClick.RemoveAllListeners();
            ui.button.onClick.AddListener(() =>
            {
                if (isFatigued)
                {
                    AudioManager.I?.PlayDenied();
                    GameEvents.RaiseToast(string.IsNullOrEmpty(etaText) ? "Worker resting." : $"Worker resting: {etaText}");
                    return;
                }

                _pendingDef = e.def;
                _pendingId = e.ownedUid;
                _pendingOwned = e.owned;

                RefreshPendingPreviewIcon(); // ✅ premium-aware

                UpdateConfirmInteractable();

                UpdateOutputPreview(currentOnly: false);
                AudioManager.I?.PlayClick();
            });

            _entryBindings.Add(new EntryBinding
            {
                ui = ui,
                owned = e.owned,
                ownedUid = e.ownedUid,
                def = e.def,
                hasAssignment = hasAssignment,
                assignedJob = assignedJob,
                assignedSlot = assignedSlot,
                assignedHours = assignedHours
            });
        }
    }

    private void RefreshPendingPreviewIcon()
    {
        if (!currentImage) return;

        if (_pendingDef != null)
        {
            bool isPremium = (_pendingOwned != null) && (_pendingOwned.isPremium || _pendingOwned.premiumTier > 0);
            var spr = MonsterNameFormatter.GetIcon(_pendingDef, isPremium, backIcon: false);
            if (spr == null) spr = _pendingDef.icon;

            if (spr != null)
            {
                currentImage.sprite = spr;
                currentImage.color = Color.white;
                return;
            }
        }

        currentImage.sprite = emptySlotSprite;
        currentImage.color = new Color(1, 1, 1, 0.6f);
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
        if (_pendingDef == null) { UpdateConfirmInteractable(); return; }

        // Centralized job eligibility (jobs are NOT HP-gated by design).
        if (!EligibilityRules.CanAssignWorkerToJobSlot(_job, _slotIndex, _pendingDef, _pendingId, out string reason))
        {
            AudioManager.I?.PlayDenied();
            if (!string.IsNullOrEmpty(reason)) GameEvents.RaiseToast(reason);
            return;
        }

        JobManager.I.RemoveFromAnyJob(_pendingId);
        bool assigned = JobManager.I.TryAssignWorkerAt(_job, _slotIndex, _pendingDef, _pendingId);
        if (!assigned)
        {
            AudioManager.I?.PlayDenied();
            GameEvents.RaiseToast("Could not assign worker.");
            RefreshUIAfterChange();
            return;
        }

        GameEvents.OnJobsChanged?.Invoke();
        GameEvents.Tutorial_FirstJobAssigned?.Invoke();
        GameEvents.RaiseToast("WORKER ASSIGNED");

        RefreshUIAfterChange();
    }

    void OnRemove()
    {
        if (JobManager.I == null) { Close(); return; }
        if (_currentWorker != null)
        {
            // Prefer ownedUID (exact instance, preserves premium + avoids ambiguity).
            // Fallback to species id for legacy saves.
            string id = !string.IsNullOrEmpty(_currentWorker.ownedUID)
                        ? _currentWorker.ownedUID
                        : (!string.IsNullOrEmpty(_currentWorker.monsterId)
                            ? _currentWorker.monsterId
                            : _currentWorker.def?.id);
            if (!string.IsNullOrEmpty(id))
            {
                bool removed = JobManager.I.RemoveWorker(_job, id);
                if (removed) GameEvents.RaiseToast("WORKER REMOVED");
            }
        }

        RefreshUIAfterChange();
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

        // IMPORTANT:
        // Use JobManager as the single source of truth for output math.
        // This prevents UI panels from drifting from runtime production.
        float currentRate = JobManager.I.EstimateSiteOutputPerHour(_job);
        float previewRate = currentRate;

        if (!currentOnly && _pendingDef != null)
            previewRate = JobManager.I.EstimateSiteOutputPerHour(_job, _pendingDef, _pendingId, _slotIndex);

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
            storedUnits = src.storedUnits,
            storedRemainder = src.storedRemainder,
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

        float premiumAura = PremiumSystems.SitePremiumAuraMult(s.workers);
        int premiumCount = CountPremiums(s.workers);
        float premiumSet = 1f + (premiumCount >= 3 ? 0.12f :
                               (premiumCount == 2 ? 0.07f :
                               (premiumCount == 1 ? 0.03f : 0f)));

        float avgFatigue = AverageWorkingSlotFatigue(s);

        return perHour * premiumAura * premiumSet * (1f - Mathf.Clamp01(avgFatigue));
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

    int CountPremiums(List<WorkerRef> workers)
    {
        if (workers == null || workers.Count == 0) return 0;
        int c = 0;
        for (int i = 0; i < workers.Count; i++)
        {
            if (IsWorkerPremium(workers[i])) c++;
        }
        return c;
    }

    bool IsWorkerPremium(WorkerRef w)
    {
        if (w == null) return false;

        // Prefer the stable ownedUID identity (new format). Fall back to legacy monsterId-as-uid.
        var owned = PremiumSystems.ResolveOwned(w);
        if (owned != null)
            return (owned.isPremium || owned.premiumTier > 0);

        var def = w.def;
        if (!def) return false;

        // Fallback: reflection on def flags (legacy)
        try
        {
            var f = def.GetType().GetField("isPremium", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(def);

            var p = def.GetType().GetProperty("isPremium", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(def, null);

            var p2 = def.GetType().GetProperty("IsPremium", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (p2 != null && p2.PropertyType == typeof(bool)) return (bool)p2.GetValue(def, null);
        }
        catch { }

        return false;
    }

    // ---------------------------
    // Fatigue detection (defensive, reflection-based)
    // ---------------------------
    private bool TryGetFatigueState(OwnedMonsterData owned, string ownedUid, out string etaText, out long remainingSeconds)
    {
        etaText = null;
        remainingSeconds = 0;

        var jm = JobManager.I;
        if (jm == null) return false;

        // Prefer exact owned instance, then species fallback for legacy references.
        if (!string.IsNullOrEmpty(ownedUid) && jm.TryGetWorkerCooldownRemainingSeconds(ownedUid, out long remByUid) && remByUid > 0)
        {
            remainingSeconds = remByUid;
            etaText = FormatEta(remByUid);
            return true;
        }

        string speciesId = owned != null ? owned.monsterId : null;
        if (!string.IsNullOrEmpty(speciesId) && jm.TryGetWorkerCooldownRemainingSeconds(speciesId, out long remBySpecies) && remBySpecies > 0)
        {
            remainingSeconds = remBySpecies;
            etaText = FormatEta(remBySpecies);
            return true;
        }

        return false;
    }

    private bool TryGetAssignmentState(string ownedUid, string speciesId, out JobType job, out int slotIndex, out float hours)
    {
        job = JobType.None;
        slotIndex = -1;
        hours = 0f;

        var jm = JobManager.I;
        if (jm == null) return false;

        var candidateKeys = BuildAssignmentCandidateKeys(ownedUid, speciesId);
        for (int i = 0; i < candidateKeys.Count; i++)
        {
            var key = candidateKeys[i];
            if (string.IsNullOrEmpty(key)) continue;
            if (jm.TryGetWorkerAssignment(key, out job, out slotIndex, out hours))
                return true;
        }

        // Fallback: direct state scan (defensive against key-shape drift between save/runtime).
        if (TryFindAssignmentByStateScan(jm, ownedUid, speciesId, candidateKeys, out job, out slotIndex, out string matchedKey))
        {
            var bestKey = !string.IsNullOrEmpty(matchedKey)
                ? matchedKey
                : (!string.IsNullOrEmpty(ownedUid) ? ownedUid : speciesId);
            var hrs = jm.GetCurrentJobAndHours(bestKey);
            if (hrs.job != JobType.None) hours = Mathf.Max(0f, hrs.hours);
            return true;
        }

        return false;
    }

    private bool TryFindAssignmentByStateScan(JobManager jm, string ownedUid, string speciesId, List<string> candidateKeys, out JobType job, out int slotIndex, out string matchedKey)
    {
        job = JobType.None;
        slotIndex = -1;
        matchedKey = null;

        if (jm == null || jm.States == null) return false;

        for (int si = 0; si < jm.States.Count; si++)
        {
            var st = jm.States[si];
            if (st == null || st.config == null || st.workers == null) continue;

            for (int wi = 0; wi < st.workers.Count; wi++)
            {
                var w = st.workers[wi];
                if (!IsWorkerMatch(w, ownedUid, speciesId, candidateKeys)) continue;

                job = st.config.jobType;
                slotIndex = wi;
                matchedKey = GetBestWorkerLookupKey(w);
                return true;
            }
        }

        return false;
    }

    private bool IsWorkerMatch(WorkerRef w, string ownedUid, string speciesId, List<string> candidateKeys)
    {
        if (w == null) return false;

        string workerOwnedUid = !string.IsNullOrEmpty(w.ownedUID) ? w.ownedUID : null;
        string workerKey = GetBestWorkerLookupKey(w);
        string workerSpeciesId = GetWorkerSpeciesId(w);

        if (candidateKeys != null && candidateKeys.Count > 0)
        {
            for (int i = 0; i < candidateKeys.Count; i++)
            {
                var k = candidateKeys[i];
                if (string.IsNullOrEmpty(k)) continue;

                if (!string.IsNullOrEmpty(workerOwnedUid) && string.Equals(workerOwnedUid, k, StringComparison.Ordinal))
                    return true;

                if (!string.IsNullOrEmpty(workerKey) && string.Equals(workerKey, k, StringComparison.Ordinal))
                    return true;

                if (!string.IsNullOrEmpty(w.monsterId) && string.Equals(w.monsterId, k, StringComparison.Ordinal))
                    return true;
            }
        }

        if (!string.IsNullOrEmpty(ownedUid))
        {
            // Legacy saves can drift identity shape between ownedUID and monsterId fields.
            if (!string.IsNullOrEmpty(workerOwnedUid) && string.Equals(workerOwnedUid, ownedUid, StringComparison.Ordinal))
                return true;

            if (!string.IsNullOrEmpty(workerKey) && string.Equals(workerKey, ownedUid, StringComparison.Ordinal))
                return true;

            if (!string.IsNullOrEmpty(w.monsterId) && string.Equals(w.monsterId, ownedUid, StringComparison.Ordinal))
                return true;
        }

        if (!string.IsNullOrEmpty(speciesId))
        {
            if (!string.IsNullOrEmpty(workerSpeciesId) && string.Equals(workerSpeciesId, speciesId, StringComparison.Ordinal))
                return true;

            if (w.def != null && !string.IsNullOrEmpty(w.def.id) && string.Equals(w.def.id, speciesId, StringComparison.Ordinal))
                return true;

            if (!string.IsNullOrEmpty(w.monsterId) && string.Equals(w.monsterId, speciesId, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private List<string> BuildAssignmentCandidateKeys(string ownedUid, string speciesId)
    {
        var keys = new List<string>(8);

        if (!string.IsNullOrEmpty(ownedUid)) keys.Add(ownedUid);
        if (!string.IsNullOrEmpty(speciesId)) keys.Add(speciesId);

        var data = SaveManager.Data;
        if (data?.owned != null && !string.IsNullOrEmpty(speciesId))
        {
            for (int i = 0; i < data.owned.Count; i++)
            {
                var om = data.owned[i];
                if (om == null) continue;
                if (!string.Equals(om.monsterId, speciesId, StringComparison.Ordinal)) continue;
                if (string.IsNullOrEmpty(om.ownedUID)) continue;

                bool exists = false;
                for (int k = 0; k < keys.Count; k++)
                {
                    if (string.Equals(keys[k], om.ownedUID, StringComparison.Ordinal))
                    {
                        exists = true;
                        break;
                    }
                }

                if (!exists) keys.Add(om.ownedUID);
            }
        }

        return keys;
    }

    private string GetBestWorkerLookupKey(WorkerRef w)
    {
        if (w == null) return null;
        if (!string.IsNullOrEmpty(w.ownedUID)) return w.ownedUID;
        if (!string.IsNullOrEmpty(w.monsterId)) return w.monsterId;
        return w.def != null ? w.def.id : null;
    }

    private string GetWorkerSpeciesId(WorkerRef w)
    {
        if (w == null) return null;

        if (w.def != null && !string.IsNullOrEmpty(w.def.id))
            return w.def.id;

        // Standard shape: ownedUID set, monsterId is species.
        if (!string.IsNullOrEmpty(w.monsterId) && string.IsNullOrEmpty(w.ownedUID))
        {
            // Could be either species id or legacy key; keep as candidate.
            return ResolveSpeciesIdFromAnyKey(w.monsterId) ?? w.monsterId;
        }

        if (!string.IsNullOrEmpty(w.ownedUID))
        {
            var byOwned = ResolveSpeciesIdFromOwnedUid(w.ownedUID);
            if (!string.IsNullOrEmpty(byOwned)) return byOwned;
        }

        if (!string.IsNullOrEmpty(w.monsterId))
        {
            var byAny = ResolveSpeciesIdFromAnyKey(w.monsterId);
            if (!string.IsNullOrEmpty(byAny)) return byAny;
            return w.monsterId;
        }

        return null;
    }

    private string ResolveSpeciesIdFromOwnedUid(string ownedUid)
    {
        if (string.IsNullOrEmpty(ownedUid)) return null;

        var data = SaveManager.Data;
        if (data?.owned == null) return null;

        for (int i = 0; i < data.owned.Count; i++)
        {
            var om = data.owned[i];
            if (om == null) continue;
            if (string.IsNullOrEmpty(om.ownedUID)) continue;
            if (!string.Equals(om.ownedUID, ownedUid, StringComparison.Ordinal)) continue;

            return string.IsNullOrEmpty(om.monsterId) ? null : om.monsterId;
        }

        return null;
    }

    private string ResolveSpeciesIdFromAnyKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;

        // First, treat key as ownedUID.
        var fromOwnedUid = ResolveSpeciesIdFromOwnedUid(key);
        if (!string.IsNullOrEmpty(fromOwnedUid)) return fromOwnedUid;

        // Then, treat key as species id if it resolves in library.
        var def = MonsterLibraryLocator.GetById(key);
        if (def != null && !string.IsNullOrEmpty(def.id)) return def.id;

        return null;
    }

    private float ComputeRestProgress01(MonsterDataSO def, long remainingSeconds)
    {
        if (def == null) return 0f;
        if (remainingSeconds <= 0) return 0f;

        long total = Mathf.RoundToInt(Mathf.Max(0f, def.fatigueCooldownHours) * 3600f);
        if (total <= 0) return 0f;

        return Mathf.Clamp01(1f - ((float)remainingSeconds / total));
    }

    private string FormatAssignedHours(float hours)
    {
        if (hours <= 0.01f) return "just assigned";

        if (hours < 1f)
        {
            int minutes = Mathf.Max(1, Mathf.RoundToInt(hours * 60f));
            return $"{minutes}m";
        }

        int wholeHours = Mathf.FloorToInt(hours);
        int mins = Mathf.RoundToInt((hours - wholeHours) * 60f);
        if (mins <= 0) return $"{wholeHours}h";
        return $"{wholeHours}h {mins}m";
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
