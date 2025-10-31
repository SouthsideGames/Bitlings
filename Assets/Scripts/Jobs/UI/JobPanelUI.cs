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

        // We’ll render: "<rate>/hr  (+X%)"
        public TextMeshProUGUI rate;

        public Button collectBtn;

        [Header("Full State (optional)")]
        public GameObject fullBadge; // set in prefab if you want a "FULL" indicator

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

    void OnEnable()
    {
        JobManager.I?.ProcessOfflineAllSites();

        Refresh();
        GameEvents.OnJobsChanged += Refresh;
        GameEvents.OnResourcesChanged += Refresh;
    }

    void OnDisable()
    {
        GameEvents.OnJobsChanged -= Refresh;
        GameEvents.OnResourcesChanged -= Refresh;
    }

    public void Refresh()
    {
        if (JobManager.I == null || tiles == null) return;

        foreach (var t in tiles)
        {
            var s = JobManager.I.States.Find(x => x.config.jobType == t.job);
            if (s == null || s.config == null) continue;

            // Title: "Quarry (2/3)"
            if (t.title) t.title.text = $"{t.job} ({CountWorkers(s)}/{s.config.maxWorkers})";

            // Stored: "Stored: 52/200"
            int cap = JobManager.I.GetEffectiveStorageCap(s.config);
            int storedWhole = Mathf.FloorToInt(s.storedAmount);
            if (t.stored) t.stored.text = $"Stored: {storedWhole}/{cap}";

            // Rate with delta & color
            if (t.rate)
            {
                // base = no titles (still includes shinies, species, rarity, evo, affinity, boss debuff)
                float baseHr    = ComputeRatePerHour_NoTitles(s);

                // boosted = with titles (per-worker multipliers + site aura titles)
                float boostedHr = ComputeRatePerHour_WithTitles(s);

                // Show integer-ish per hour as your old UI did
                int shown = Mathf.FloorToInt(boostedHr);
                float deltaPct = 0f;
                if (baseHr > 0.0001f)
                    deltaPct = (boostedHr / baseHr - 1f) * 100f;

                // Text: "123/hr  (+15%)"
                if (Mathf.Abs(deltaPct) >= 0.5f)
                    t.rate.text = $"{shown}/hr  {(deltaPct >= 0 ? "+" : string.Empty)}{deltaPct:0}%";
                else
                    t.rate.text = $"{shown}/hr";

                // Colorize
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
                    Refresh();
                });
            }

            // FULL badge (optional)
            if (t.fullBadge) t.fullBadge.SetActive(cap > 0 && storedWhole >= cap);

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
                    var spr = worker.def.icon ? worker.def.icon : emptySlotSprite;
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
    // Rate computation (duplicated from JobManager.ComputeRatePerHour, but with toggles)
    // base: excludes titles entirely; boosted: includes job-auras and per-worker title multipliers
    // Both include shinies, species multipliers, boss debuff, and use *current working slots* fatigue impact.
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

            // EXCLUDE per-worker title rate mult here

            sum += mult;
        }

        float normalized = 1f + (sum / 3f);
        float perHour = s.config.baseRatePerHour * normalized;

        // Boss/global debuff still applies
        perHour *= BossDebuffSystem.GetMultiplier(s.config.jobType, SaveManager.NowUnix());

        // EXCLUDE site-wide title aura here

        // Shiny stacking (attribute, not a title)
        float shinyAura = ShinySystems.SiteShinyAuraMult(s.workers);
        int shinyCount  = CountShinies(s.workers);
        float shinySet  = 1f + (shinyCount >= 3 ? 0.12f : (shinyCount == 2 ? 0.07f : (shinyCount == 1 ? 0.03f : 0f)));

        // Average working slot fatigue (same logic as manager)
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

            // INCLUDE per-worker title rate mult
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

        // Boss/global debuff
        perHour *= BossDebuffSystem.GetMultiplier(s.config.jobType, SaveManager.NowUnix());

        // INCLUDE site-wide aura titles
        try
        {
            var auras = TitlesAdapter.BuildJobAuras(SaveManager.Data?.team);
            if (auras != null && auras.TryGetValue(s.config.jobType, out float auraPct) && auraPct != 0f)
                perHour *= (1f + auraPct);
        }
        catch { }

        // Shiny stacking (attribute)
        float shinyAura = ShinySystems.SiteShinyAuraMult(s.workers);
        int shinyCount  = CountShinies(s.workers);
        float shinySet  = 1f + (shinyCount >= 3 ? 0.12f : (shinyCount == 2 ? 0.07f : (shinyCount == 1 ? 0.03f : 0f)));

        // Same fatigue model as manager
        float avgFatigue = AverageWorkingSlotFatigue(s);

        return perHour * shinyAura * shinySet * (1f - Mathf.Clamp01(avgFatigue));
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
    // Helpers (mirrors from JobManager to keep this self-contained)
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

        // Prefer owned-instance record
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

        // Fallback to def via reflection if present
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
