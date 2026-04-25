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
            if (t.title)
            {
                t.title.text = t.job.ToString().Replace("_", " ");
            }

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

            // Stored is whole-units only (no fractional resources).
            int storedWhole = s.storedUnits;
            string storedShown = storedWhole.ToString();
            int nextProgressPct = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(s.storedRemainder) * 100f), 0, 99);
            string progressSuffix = (storedWhole <= 0 && nextProgressPct > 0)
                ? $" ({nextProgressPct}% to next)"
                : string.Empty;
            if (t.stored)
            {
                if (deltaCap == 0)
                {
                    t.stored.text = $"Stored: {storedShown}/{capWithTitles}{progressSuffix}";
                }
                else
                {
                    var c = deltaCap > 0 ? capUpColor : capDownColor;
                    string hex = ColorUtility.ToHtmlStringRGB(c);
                    string sign = deltaCap > 0 ? "+" : ""; // negatives already have '-'
                    t.stored.text = $"Stored: {storedShown}/{capWithTitles} <color=#{hex}>({sign}{deltaCap})</color>{progressSuffix}";
                }
            }

            // ─────────────────────────────────────────────────────────────
            // Rate with delta & color (base vs. boosted by titles/auras)
            // ─────────────────────────────────────────────────────────────
            if (t.rate)
            {
                float baseHr    = ComputeRatePerHour_NoTitles(s);
                float boostedHr = ComputeRatePerHour_WithTitles(s);

                // Whole-number display only.
                // If production is < 1/hr, show time-per-1 instead of "0/hr".
                string shownText;
                int wholePerHour = Mathf.FloorToInt(boostedHr);
                if (wholePerHour >= 1)
                {
                    shownText = $"{wholePerHour}/hr";
                }
                else if (boostedHr > 0.0001f)
                {
                    float secondsPerUnit = 3600f / boostedHr;
                    int minutes = Mathf.Max(1, Mathf.RoundToInt(secondsPerUnit / 60f));
                    int h = minutes / 60;
                    int m = minutes % 60;

                    if (h <= 0) shownText = $"1/{m}m";
                    else if (m == 0) shownText = $"1/{h}h";
                    else shownText = $"1/{h}h {m}m";
                }
                else
                {
                    shownText = "0/hr";
                }

                float deltaPct = 0f;
                if (baseHr > 0.0001f)
                    deltaPct = (boostedHr / baseHr - 1f) * 100f;

                string rateText;
                if (Mathf.Abs(deltaPct) >= 0.5f)
                    rateText = $"{shownText}  {(deltaPct >= 0 ? "+" : string.Empty)}{deltaPct:0}%";
                else
                    rateText = $"{shownText}";

                t.rate.text = rateText;

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
                    if (got > 0)
                    {
                        ResourceType outType = JobOutput.Output(t.job);
                        ResourceFlyAnimationUI.PlayToHome(outType, got, t.collectBtn.transform);
                    }
                    AudioManager.I?.PlaySfx(SfxType.Collect);
                    PlayCollectFX(t);
                    Refresh();
                });
            }

            // FULL badge (optional)
            if (t.fullBadge) t.fullBadge.SetActive(capWithTitles > 0 && storedWhole >= capWithTitles);

            // Slots
            int max = t.slots != null ? t.slots.Length : 0;
            int cap = Mathf.Clamp(s.config.maxWorkers, 1, 3);
            for (int i = 0; i < max; i++)
            {
                var ui = t.slots[i];
                if (!ui) continue;

                ui.job = t.job;
                ui.slotIndex = i;

                // If the tile has more slot buttons than this site supports, show them as locked and prevent assignment.
                if (i >= cap)
                {
                    ui.job = t.job;
                    ui.slotIndex = i;
                    ui.SetEmpty(emptySlotSprite, new Color(emptySlotColor.r, emptySlotColor.g, emptySlotColor.b, 0.15f));
                    ui.SetInteractable(false);
                    continue;
                }

                WorkerRef worker = (i < s.workers.Count) ? s.workers[i] : null;
                if (worker != null && worker.def != null)
                {
                    // ✅ FIX: use premium-aware resolver + ensure the SLOT uses MonsterDataSO.premiumIcon (front)
                    bool premium = ResolveWorkerPremium(worker);

                    // IMPORTANT: Don't pass raw Sprite here (it can be stomped by other callers / refresh paths).
                    // Let JobSlotUI resolve from def + premium so it always uses def.icon / def.premiumIcon (front).
                    ui.SetWorker(worker.def, premium, filledSlotColor);
                }
                else
                {
                    ui.SetEmpty(emptySlotSprite, emptySlotColor);
                }

                ui.SetInteractable(true);
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

        float premiumAura = PremiumSystems.SitePremiumAuraMult(s.workers);
        int premiumCount  = CountPremiums(s.workers);
        float premiumSet  = 1f + (premiumCount >= 3 ? 0.12f : (premiumCount == 2 ? 0.07f : (premiumCount == 1 ? 0.03f : 0f)));

        float avgFatigue = AverageWorkingSlotFatigue(s);

        return perHour * premiumAura * premiumSet * (1f - Mathf.Clamp01(avgFatigue));
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

        float premiumAura2 = PremiumSystems.SitePremiumAuraMult(s.workers);
        int premiumCount2  = CountPremiums(s.workers);
        float premiumSet2  = 1f + (premiumCount2 >= 3 ? 0.12f : (premiumCount2 == 2 ? 0.07f : (premiumCount2 == 1 ? 0.03f : 0f)));

        float avgFatigue2 = AverageWorkingSlotFatigue(s);

        return perHour * premiumAura2 * premiumSet2 * (1f - Mathf.Clamp01(avgFatigue2));
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

    private static int CountPremiums(List<WorkerRef> workers)
    {
        if (workers == null || workers.Count == 0) return 0;
        int c = 0;
        for (int i = 0; i < workers.Count; i++) if (IsWorkerPremium(workers[i])) c++;
        return c;
    }

    private static bool IsWorkerPremium(WorkerRef w)
    {
        if (w == null) return false;

        // Prefer ownedUID (exact owned instance) and support legacy monsterId-as-uid.
        var owned = PremiumSystems.ResolveOwned(w);
        if (owned != null)
            return (owned.isPremium || owned.premiumTier > 0);

        var def = w.def;
        if (!def) return false;
        try
        {
            var f = def.GetType().GetField("isPremium");
            if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(def);

            var p = def.GetType().GetProperty("IsPremium");
            if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(def, null);
        }
        catch { }

        return false;
    }

    // ✅ NEW: icon-facing premium resolver (WorkerRef -> ownedUID -> monsterId fallback)
    private static bool ResolveWorkerPremium(WorkerRef w)
    {
        if (w == null) return false;

        // 1) Check using the IsWorkerPremium helper method.
        if (IsWorkerPremium(w))
            return true;

        // 2) If we have a stable ownedUID, resolve the actual owned instance.
        string uid = w.ownedUID;
        if (!string.IsNullOrEmpty(uid))
        {
            var om = FindOwnedByUid(uid);
            if (om != null)
                return om.isPremium || om.premiumTier > 0;
        }

        // 3) Fallback by species id.
        // IMPORTANT: If multiple copies exist (premium + non-premium), we must respect the user's saved preference,
        // not "any premium exists".
        string id = w.monsterId;
        if (!string.IsNullOrEmpty(id))
            return MonsterVariantPreference.IsPreferredPremium(id);

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
