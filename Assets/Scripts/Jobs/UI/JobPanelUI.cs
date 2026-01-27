using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System;
using System.Collections.Generic;

public class JobPanelUI : MonoBehaviour
{
    [System.Serializable]
    public class JobTile
    {
        public JobType job;
        public TextMeshProUGUI title;
        public TextMeshProUGUI stored;

        public TextMeshProUGUI rate;

        public Button collectBtn;

        [Header("Full State (optional)")]
        public GameObject fullBadge;

        [Header("Slots (size = maxWorkers)")]
        public JobSlotUI[] slots;
    }

    [SerializeField] private JobTile[] tiles;

    [Header("Visuals")]
    [SerializeField] private Sprite emptySlotSprite;
    [SerializeField] private Color emptySlotColor = new Color(1f, 1f, 1f, 0.35f);
    [SerializeField] private Color filledSlotColor = Color.white;

    [Header("Rate Colors")]
    [SerializeField] private Color rateNeutral = new Color(0.85f, 0.85f, 0.85f); // equal
    [SerializeField] private Color rateUp     = new Color(0.22f, 0.85f, 0.35f); // boost
    [SerializeField] private Color rateDown   = new Color(0.90f, 0.30f, 0.30f); // penalty

    [Header("Capacity Delta Colors")]
    [SerializeField] private Color capUpColor   = new Color(0.22f, 0.85f, 0.35f); // green
    [SerializeField] private Color capDownColor = new Color(0.90f, 0.30f, 0.30f); // red

    [Header("Collect FX")]
    [SerializeField] private float collectPunchScale = 1.1f;
    [SerializeField] private float collectPunchTime  = 0.15f;

    void OnEnable()
    {
        JobManager.I?.ProcessOfflineAllSites();

        Refresh();
        GameEvents.OnJobsChanged += Refresh;
        GameEvents.OnResourcesChanged += Refresh;
        GameEvents.JobGlobalModsChanged += Refresh;
    }

    void OnDisable()
    {
        GameEvents.OnJobsChanged -= Refresh;
        GameEvents.OnResourcesChanged -= Refresh;
        GameEvents.JobGlobalModsChanged -= Refresh;
    }

    public void Refresh()
    {
        if (JobManager.I == null || tiles == null) return;

        foreach (var t in tiles)
        {
            var s = JobManager.I.States.Find(x => x.config != null && x.config.jobType == t.job);
            if (s == null || s.config == null) continue;

            // Title: "Quarry (2/3)"
            if (t.title) t.title.text = $"{t.job} ({CountWorkers(s)}/{s.config.maxWorkers})";

            // ─────────────────────────────────────────────────────────────
            // Capacity text with colored delta (titles vs. no titles)
            // ─────────────────────────────────────────────────────────────
            int capWithTitles = JobManager.I.GetEffectiveStorageCap(s.config);

            int baseCap = s.config.storageCap;

            int extraFromSave = 0;
            if (SaveManager.Data != null)
            {
                try { extraFromSave = SaveManager.Data.GetJobStorageExtra(s.config.jobType); }
                catch { extraFromSave = 0; }
            }

            int preMultNoTitles = Mathf.Max(0, baseCap + extraFromSave);

            float lvlMul = JobLeveling.StorageMultForLevel(s.level);

            int capNoTitles = Mathf.Max(0, Mathf.RoundToInt(preMultNoTitles * lvlMul));

            int deltaCap = capWithTitles - capNoTitles;

            int storedWhole = Mathf.FloorToInt(s.storedAmount);
            if (t.stored)
            {
                if (deltaCap == 0)
                {
                    t.stored.text = $"Stored: {storedWhole}/{capWithTitles}";
                }
                else
                {
                    var c = deltaCap > 0 ? capUpColor : capDownColor;
                    string hex = ColorUtility.ToHtmlStringRGB(c);
                    string sign = deltaCap > 0 ? "+" : ""; // negatives already have '-'
                    t.stored.text = $"Stored: {storedWhole}/{capWithTitles} <color=#{hex}>({sign}{deltaCap})</color>";
                }
            }

            // ─────────────────────────────────────────────────────────────
            // Rate with delta & color (base vs. boosted by titles/auras)
            // ─────────────────────────────────────────────────────────────
            if (t.rate)
            {
                float baseHr    = ComputeRatePerHour_NoTitles(s);
                float boostedHr = ComputeRatePerHour_WithTitles(s);

                // NEW: show decimals for low rates (Clinic/Sanctum/etc.)
                string shownText = boostedHr < 10f
                    ? boostedHr.ToString("0.##")
                    : Mathf.FloorToInt(boostedHr).ToString();

                float deltaPct = 0f;
                if (baseHr > 0.0001f)
                    deltaPct = (boostedHr / baseHr - 1f) * 100f;

                if (Mathf.Abs(deltaPct) >= 0.5f)
                    t.rate.text = $"{shownText}/hr  {(deltaPct >= 0 ? "+" : string.Empty)}{deltaPct:0}%";
                else
                    t.rate.text = $"{shownText}/hr";

                if (deltaPct > 0.5f)       t.rate.color = rateUp;
                else if (deltaPct < -0.5f) t.rate.color = rateDown;
                else                       t.rate.color = rateNeutral;
            }

            // Collect button
            if (t.collectBtn)
            {
                t.collectBtn.onClick.RemoveAllListeners();
                t.collectBtn.interactable = storedWhole > 0;
                t.collectBtn.onClick.AddListener(() =>
                {
                    int got = JobManager.I.Collect(t.job);
                    AudioManager.I.PlaySfx(SfxType.Collect);
                    PlayCollectFX(t);
                    Refresh();
                });
            }

            // FULL badge (optional)
            if (t.fullBadge) t.fullBadge.SetActive(capWithTitles > 0 && storedWhole >= capWithTitles);

            // Slots
            int max = t.slots != null ? t.slots.Length : 0;
            for (int i = 0; i < max; i++)
            {
                var ui = t.slots[i];
                if (!ui) continue;

                ui.job = t.job;
                ui.slotIndex = i;

                WorkerRef worker = (i < s.workers.Count) ? s.workers[i] : null;
                if (worker != null && worker.def != null)
                {
                    // ✅ FIX: use shiny-aware icon
                    bool shiny = ResolveWorkerShiny(worker);

                    Sprite spr = MonsterNameFormatter.GetIcon(worker.def, shiny, backIcon: false);
                    if (spr == null) spr = worker.def.icon;
                    if (spr == null) spr = emptySlotSprite;

                    ui.SetWorker(spr, filledSlotColor);
                }
                else
                {
                    ui.SetEmpty(emptySlotSprite, emptySlotColor);
                }

                ui.WireToPicker();
            }
        }
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Rate computation (base vs boosted)
    // ─────────────────────────────────────────────────────────────────────────────

    private float ComputeRatePerHour_NoTitles(JobSiteState s)
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
        int shinyCount  = CountShinies(s.workers);
        float shinySet  = 1f + (shinyCount >= 3 ? 0.12f : (shinyCount == 2 ? 0.07f : (shinyCount == 1 ? 0.03f : 0f)));

        float avgFatigue = AverageWorkingSlotFatigue(s);

        return perHour * shinyAura * shinySet * (1f - Mathf.Clamp01(avgFatigue));
    }

    private float ComputeRatePerHour_WithTitles(JobSiteState s)
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

            try
            {
                string wid = GetBestId(w);
                mult *= Mathf.Max(0f, TitlesAdapter.GetJobRateMult(wid, s.config.jobType));
            }
            catch { }

            sum += mult;
        }

        float normalized = 1f + (sum / 3f);
        float perHour = s.config.baseRatePerHour * normalized;

        perHour *= BossDebuffSystem.GetMultiplier(s.config.jobType, SaveManager.NowUnix());

        try
        {
            var auras = TitlesAdapter.BuildJobAuras(SaveManager.Data?.team);
            if (auras != null && auras.TryGetValue(s.config.jobType, out float auraPct) && Math.Abs(auraPct) > 0.0001f)
                perHour *= (1f + auraPct);
        }
        catch { }

        float shinyAura2 = ShinySystems.SiteShinyAuraMult(s.workers);
        int shinyCount2  = CountShinies(s.workers);
        float shinySet2  = 1f + (shinyCount2 >= 3 ? 0.12f : (shinyCount2 == 2 ? 0.07f : (shinyCount2 == 1 ? 0.03f : 0f)));

        float avgFatigue2 = AverageWorkingSlotFatigue(s);

        return perHour * shinyAura2 * shinySet2 * (1f - Mathf.Clamp01(avgFatigue2));
    }

    private static float AverageWorkingSlotFatigue(JobSiteState s)
    {
        if (s == null || s.workers == null || s.slotFatigue01 == null) return 0f;
        float sum = 0f; int count = 0;
        int cap = Mathf.Min(s.workers.Count, s.slotFatigue01.Length);
        for (int i = 0; i < cap; i++)
        {
            var w = s.workers[i];
            if (w?.def == null) continue;
            sum += Mathf.Clamp01(s.slotFatigue01[i]);
            count++;
        }
        return count > 0 ? sum / count : 0f;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers (mirror manager to keep UI self-contained)
    // ─────────────────────────────────────────────────────────────────────────────

    private int CountWorkers(JobSiteState s)
    {
        int count = 0;
        for (int i = 0; i < s.workers.Count; i++)
            if (s.workers[i] != null && s.workers[i].def != null) count++;
        return count;
    }

    private static bool HasAnyWorker(List<WorkerRef> workers)
    {
        if (workers == null) return false;
        for (int i = 0; i < workers.Count; i++)
            if (workers[i]?.def != null || !string.IsNullOrEmpty(workers[i]?.monsterId))
                return true;
        return false;
    }

    private static string GetBestId(WorkerRef w)
    {
        if (w == null) return null;
        if (!string.IsNullOrEmpty(w.monsterId)) return w.monsterId;
        return w.def ? w.def.id : null;
    }

    private static int CountShinies(List<WorkerRef> workers)
    {
        if (workers == null || workers.Count == 0) return 0;
        int c = 0;
        for (int i = 0; i < workers.Count; i++) if (IsWorkerShiny(workers[i])) c++;
        return c;
    }

    private static bool IsWorkerShiny(WorkerRef w)
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
                    if (om != null && om.monsterId == ownedId) return om.isShiny;
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

    // ✅ NEW: icon-facing shiny resolver (WorkerRef -> ownedUID -> monsterId fallback)
    private static bool ResolveWorkerShiny(WorkerRef w)
    {
        if (w == null) return false;

        // 1) Check using the IsWorkerShiny helper method.
        if (IsWorkerShiny(w))
            return true;

        // 2) If we have a stable ownedUID, resolve the actual owned instance.
        string uid = w.ownedUID;
        if (!string.IsNullOrEmpty(uid))
        {
            var om = FindOwnedByUid(uid);
            if (om != null)
                return om.isShiny || om.shinyTier > 0;
        }

        // 3) Fallback by monsterId (less precise if multiple copies exist, but better than always normal).
        string id = w.monsterId;
        if (!string.IsNullOrEmpty(id))
        {
            var ownedList = SaveManager.Data?.owned;
            if (ownedList != null)
            {
                for (int i = 0; i < ownedList.Count; i++)
                {
                    var om = ownedList[i];
                    if (om == null) continue;
                    if (!string.Equals(om.monsterId, id, StringComparison.Ordinal)) continue;
                    if (om.isShiny || om.shinyTier > 0) return true;
                }
            }

            var team = SaveManager.Data?.team;
            if (team != null)
            {
                for (int i = 0; i < team.Count; i++)
                {
                    var om = team[i];
                    if (om == null) continue;
                    if (!string.Equals(om.monsterId, id, StringComparison.Ordinal)) continue;
                    if (om.isShiny || om.shinyTier > 0) return true;
                }
            }
        }

        return false;
    }

    private static OwnedMonsterData FindOwnedByUid(string uid)
    {
        var data = SaveManager.Data;
        if (data == null || string.IsNullOrEmpty(uid)) return null;

        var owned = data.owned;
        if (owned != null)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                var om = owned[i];
                if (om != null && !string.IsNullOrEmpty(om.ownedUID) && om.ownedUID == uid)
                    return om;
            }
        }

        var team = data.team;
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var om = team[i];
                if (om != null && !string.IsNullOrEmpty(om.ownedUID) && om.ownedUID == uid)
                    return om;
            }
        }

        return null;
    }

    // ─────────────────────────────────────────────────────────────
    // FX
    // ─────────────────────────────────────────────────────────────
    private void PlayCollectFX(JobTile t)
    {
        if (t == null || t.collectBtn == null) return;

        RectTransform target = t.collectBtn.transform as RectTransform;
        if (!target) return;

        target.localScale = Vector3.one;

        LeanTween.scale(target.gameObject,
                        Vector3.one * collectPunchScale,
                        collectPunchTime)
                .setEaseOutBack()
                .setOnComplete(() =>
                {
                    if (target)
                        target.localScale = Vector3.one;
                });
    }
}
