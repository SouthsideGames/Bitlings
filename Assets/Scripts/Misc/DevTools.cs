using UnityEngine;
using NaughtyAttributes;
using UnityEngine.SceneManagement;
using System.Collections.Generic;
using System;

public class DevTools : MonoBehaviour
{

    [Header("Healing Test Refs")]
    [SerializeField] private MonsterLibrarySO library;
    [SerializeField] private HealingConfigSO healingConfig;
    [SerializeField] private EncounterPanelUI encounterPanel;

    [Header("Healing Test Vars")]
    [Range(0, 2)] public int teamIndex = 0;
    public int damageAmount = 50;
    public int hpPerMedkit = 50;
    public int grantMedkits = 10;

    [Header("Luck Debug Vars")]
    [Tooltip("How many Luck items to grant when pressing Give Luck.")]
    public int grantLuck = 5;

    [Tooltip("0..1 where 0.25 = +25% encounter rarity bias while active.")]
    [Range(0f, 1f)] public float luckBonus = 0.25f;

    [Tooltip("Hours for Luck boost duration when using the item.")]
    [Min(1)] public int luckDurationHours = 2;

    [Header("Evolution Debug Vars")]
    [Tooltip("Team slot to modify (uses the same index as your other tools).")]
    [Range(0, 2)] public int evoTeamIndex = 0;

    [Tooltip("How many levels to add to the selected team slot.")]
    [Min(1)] public int grantLevels = 1;

    [Tooltip("If > 0, set the selected team slot to this exact level instead of adding.")]
    [Min(0)] public int setLevelTo = 0;

    [Header("Job Debug")]
    public JobType debugJobType;

    [Header("Idle Battle Debug Vars")]
    public int idleDev_Encounters = 5;
    public int idleDev_OfflineSeconds = 600; // 10 minutes
    [SerializeField] private IdleBattleConfigSO idleConfig;



    void Awake()
    {
    #if !UNITY_EDITOR
        if (!enableInBuild)
        {
            Destroy(gameObject);
            return;
        }
    #endif
    }


    [Button("Clear Save (JSON)")]
    void Btn_ClearSave()
    {
        SaveManager.ClearAll();
        Debug.Log("Cleared JSON save. Reload scene to see Starter again.");
    }

    [Button("Refill Energy To Max")]
    void Btn_RefillEnergy()
    {
        SaveManager.Data.encounterPoints = SaveManager.Data.encounterMax;
        SaveManager.Save();
        encounterPanel.RefreshAll();
    }

    [Button("Give 1,000 Coins")]
    void Btn_GiveCoins()
    {
        ResourceManager.I.Add(ResourceType.Coins, 1000);
        SaveManager.Save();
    }

    [Button("Force Starter Next Boot")]
    void Btn_ForceStarter()
    {
        SaveManager.Data.owned?.Clear();
        SaveManager.Data.team?.Clear();
        SaveManager.Data.trainingMonsterId = null;
        SaveManager.Save();
    }

    // ===== Medkits testing =====

    [Button("Give Medkits")]
    void Btn_GiveMedkits()
    {
        ResourceBank.Add(ResourceType.Medkits, Mathf.Max(1, grantMedkits));
        Debug.Log($"Medkits: {ResourceBank.Get(ResourceType.Medkits)}");
    }

    [Button("Damage Team Slot")]
    void Btn_DamageTeamSlot()
    {
        var team = SaveManager.Data?.team;
        if (team == null || teamIndex < 0 || teamIndex >= team.Count) return;

        var owned = team[teamIndex];
        if (string.IsNullOrEmpty(owned.monsterId)) return;

        var def = library.GetById(owned.monsterId);
        int maxHP = HealingService.CalcMaxHP(def, owned.level);
        int curHP = owned.currentHP >= 0 ? Mathf.Min(owned.currentHP, maxHP) : maxHP;
        curHP = Mathf.Max(0, curHP - Mathf.Max(0, damageAmount));
        owned.currentHP = curHP;
        SaveManager.Data.team[teamIndex] = owned;
        SaveManager.Save();

        Debug.Log($"Damaged slot {teamIndex}. Now {curHP}/{maxHP}");
    }

    [Button("Heal (Medkits First, Then Coins)")]
    void Btn_Heal_MedkitsFirst()
    {
        var team = SaveManager.Data?.team;
        if (team == null || teamIndex < 0 || teamIndex >= team.Count) return;

        var owned = team[teamIndex];
        if (string.IsNullOrEmpty(owned.monsterId)) return;

        var def = library.GetById(owned.monsterId);
        int maxHP = HealingService.CalcMaxHP(def, owned.level);
        int curHP = owned.currentHP >= 0 ? Mathf.Min(owned.currentHP, maxHP) : maxHP;
        int missing = HealingService.MissingHP(curHP, maxHP);
        if (missing <= 0) { Debug.Log("Already full."); return; }

        int kitsNeeded = Mathf.CeilToInt((float)missing / Mathf.Max(1, hpPerMedkit));
        int haveKits = ResourceBank.Get(ResourceType.Medkits);

        if (haveKits >= kitsNeeded && kitsNeeded > 0)
        {
            if (!ResourceBank.TrySpend(ResourceType.Medkits, kitsNeeded)) return;
        }
        else
        {
            if (haveKits > 0 && !ResourceBank.TrySpend(ResourceType.Medkits, haveKits)) return;
            int coinsNeeded = HealingService.CoinsToHealFull(healingConfig, owned.level, missing);
            if (!ResourceManager.I.TrySpend(ResourceType.Coins, coinsNeeded)) { Debug.Log("Not enough coins."); return; }
        }

        owned.currentHP = maxHP;
        SaveManager.Data.team[teamIndex] = owned;
        SaveManager.Save();
        Debug.Log($"Healed slot {teamIndex} to full with Medkits-first fallback.");
    }

    [Button("Log Healing Status")]
    void Btn_LogHealingStatus()
    {
        var team = SaveManager.Data?.team;
        if (team == null || teamIndex < 0 || teamIndex >= team.Count) return;

        var owned = team[teamIndex];
        if (string.IsNullOrEmpty(owned.monsterId)) { Debug.Log("Empty slot"); return; }

        var def = library.GetById(owned.monsterId);
        int maxHP = HealingService.CalcMaxHP(def, owned.level);
        int curHP = owned.currentHP >= 0 ? Mathf.Min(owned.currentHP, maxHP) : maxHP;
        int kits = ResourceBank.Get(ResourceType.Medkits);
        Debug.Log($"Slot {teamIndex}: {def.displayName} L{owned.level} HP {curHP}/{maxHP} | Medkits={kits} | Coins={ResourceManager.I.Get(ResourceType.Coins)}");
    }

    // Helpers to quickly stock Medkits from jobs (optional)

    [Button("Collect Grove")]
    void Btn_CollectGrove()
    {
        var got = JobManager.I?.Collect(JobType.Grove) ?? 0;
        Debug.Log($"Collected Grove: +{got} {JobOutput.Output(JobType.Grove)}");
    }

    [Button("Give 500 Materials")]
    void Btn_GiveMaterials()
    {
        ResourceBank.Add(ResourceType.Materials, 500);
        Debug.Log($"Materials now: {ResourceBank.Get(ResourceType.Materials)}");
    }

    [Button("Give 5 Lures")]
    void Btn_GiveLures()
    {
        ResourceBank.Add(ResourceType.Lures, 5);
        Debug.Log($"Lures now: {ResourceBank.Get(ResourceType.Lures)}");
    }

    [Button("Use Lure: Water x3")]
    void Btn_UseWaterLure()
    {
        if (!ResourceBank.TrySpend(ResourceType.Lures, 1)) { Debug.Log("No Lures."); return; }
        EncounterManager.I?.AddLure(MonsterType.Water, 0.30f, 3);
        Debug.Log("Applied Water lure (+30%) for next 3 encounters.");
    }

    [Button("Clear Active Lure")]
    void Btn_ClearLure()
    {
        var list = SaveManager.Data?.activeLures;
        if (list == null || list.Count == 0) { Debug.Log("No active lure to clear."); return; }
        list.Clear();
        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        Debug.Log("Active lure cleared.");
    }

    [Button("Give 5 CaptureBands")]
    void Btn_GiveCaptureBands()
    {
        ResourceBank.Add(ResourceType.CaptureBands, 5);
        Debug.Log($"CaptureBands now: {ResourceBank.Get(ResourceType.CaptureBands)}");
    }

    [Button("Use CaptureBand (2h)")]
    void Btn_UseCaptureBand()
    {
        if (!ResourceBank.TrySpend(ResourceType.CaptureBands, 1)) { Debug.Log("No CaptureBands."); return; }
        long expiry = SaveManager.NowUnix() + 2 * 3600L;
        SaveManager.Data.activeCaptureBands.Clear();
        SaveManager.Data.activeCaptureBands.Add(new CaptureBandData { bonus = 0.25f, expireUnix = expiry });
        SaveManager.Save();
        Debug.Log("Applied CaptureBand (+25%) for 2h.");
    }

    [Button("Give 5 AttackBoosters")]
    void Btn_GiveAtkBoosters()
    {
        ResourceBank.Add(ResourceType.AttackBoosters, 5);
        Debug.Log($"AttackBoosters: {ResourceBank.Get(ResourceType.AttackBoosters)}");
    }

    [Button("Give 5 HPBoosters")]
    void Btn_GiveHPBoosters()
    {
        ResourceBank.Add(ResourceType.HPBoosters, 5);
        Debug.Log($"HPBoosters: {ResourceBank.Get(ResourceType.HPBoosters)}");
    }

    [Button("Give Luck")]
    void Btn_GiveLuck()
    {
        ResourceBank.Add(ResourceType.Luck, Mathf.Max(1, grantLuck));
        Debug.Log($"Luck items now: {ResourceBank.Get(ResourceType.Luck)}");
    }

    [Button("Use Luck (duration)")]
    void Btn_UseLuck()
    {
        if (!ResourceBank.TrySpend(ResourceType.Luck, 1)) { Debug.Log("No Luck items."); return; }

        long now = SaveManager.NowUnix();
        long expiry = now + Mathf.Max(1, luckDurationHours) * 3600L;

        if (SaveManager.Data.activeLuckBoosts == null)
            SaveManager.Data.activeLuckBoosts = new List<LuckBoostData>();

        SaveManager.Data.activeLuckBoosts.Clear();
        SaveManager.Data.activeLuckBoosts.Add(new LuckBoostData
        {
            bonus = Mathf.Clamp01(luckBonus),
            expireUnix = expiry
        });

        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        Debug.Log($"Applied Luck +{Mathf.RoundToInt(Mathf.Clamp01(luckBonus) * 100f)}% for {luckDurationHours}h.");
    }

    [Button("Clear Active Luck")]
    void Btn_ClearLuck()
    {
        var list = SaveManager.Data?.activeLuckBoosts;
        if (list == null || list.Count == 0) { Debug.Log("No active Luck to clear."); return; }
        list.Clear();
        SaveManager.Save();
        GameEvents.OnResourcesChanged?.Invoke();
        Debug.Log("Active Luck cleared.");
    }

    [Button("Log Luck Status")]
    void Btn_LogLuck()
    {
        var list = SaveManager.Data?.activeLuckBoosts;
        if (list == null || list.Count == 0) { Debug.Log("Luck: none active."); return; }

        var cur = list[0];
        if (cur == null) { Debug.Log("Luck: none active."); return; }

        long rem = cur.expireUnix - SaveManager.NowUnix();
        rem = Math.Max(0L, rem);
        int pct = Mathf.RoundToInt(Mathf.Clamp01(cur.bonus) * 100f);
        Debug.Log($"Luck active: +{pct}% | Remaining: {FormatHMS(rem)}");
    }

    string FormatHMS(long seconds)
    {
        if (seconds < 0) return "--";
        var t = TimeSpan.FromSeconds(seconds);
        return (t.TotalHours >= 1.0)
            ? $"{(int)t.TotalHours}h {t.Minutes}m {t.Seconds}s"
            : $"{t.Minutes}m {t.Seconds}s";
    }

    [Button("Give Levels & Offer Evolution (Team Slot)")]
    void Btn_GiveLevelsAndOfferEvolution()
    {
        var team = SaveManager.Data?.team;
        if (team == null || team.Count == 0) { Debug.Log("No team."); return; }
        if (evoTeamIndex < 0 || evoTeamIndex >= team.Count) { Debug.Log("evoTeamIndex out of range."); return; }

        var m = team[evoTeamIndex];
        if (string.IsNullOrEmpty(m.monsterId)) { Debug.Log("Empty team slot."); return; }

        var def = library != null ? library.GetById(m.monsterId) : null;
        if (def == null) { Debug.Log("Monster def not found in library."); return; }

        int oldLevel = m.level;

        if (setLevelTo > 0)
            m.level = Mathf.Clamp(setLevelTo, 1, LevelRules.MaxLevel);
        else
            m.level = Mathf.Clamp(m.level + Mathf.Max(1, grantLevels), 1, LevelRules.MaxLevel);

        m.currentXP = 0; // clean slate for clarity after jump
        team[evoTeamIndex] = m;

        // keep the training display in sync if this is the training target
        if (!string.IsNullOrEmpty(SaveManager.Data.trainingMonsterId) &&
            SaveManager.Data.trainingMonsterId == m.monsterId)
        {
            SaveManager.Data.trainingMonsterLevel = m.level;
        }

        SaveManager.Save();

        // fire a single leveled event with the new level (you can loop if you want per-level SFX)
        if (m.level > oldLevel)
            GameEvents.MonsterLeveled?.Invoke(m.monsterId, m.level);

        // if level reached evo threshold, offer evolution panel
        if (def.evolutionLevel > 0 && def.evolutionForm != null && m.level >= def.evolutionLevel)
            GameEvents.EvolutionOffered?.Invoke(m.monsterId);

        Debug.Log($"[Dev] Team slot {evoTeamIndex}: set {def.displayName} to L{m.level}. Evo offered: {(def.evolutionForm != null && m.level >= def.evolutionLevel)}");
    }

    [Button("Offer Evolution Now (No Level Change)")]
    void Btn_OfferEvolutionNow()
    {
        var team = SaveManager.Data?.team;
        if (team == null || team.Count == 0) { Debug.Log("No team."); return; }
        if (evoTeamIndex < 0 || evoTeamIndex >= team.Count) { Debug.Log("evoTeamIndex out of range."); return; }

        var m = team[evoTeamIndex];
        if (string.IsNullOrEmpty(m.monsterId)) { Debug.Log("Empty team slot."); return; }

        var def = library != null ? library.GetById(m.monsterId) : null;
        if (def == null) { Debug.Log("Monster def not found in library."); return; }

        GameEvents.EvolutionOffered?.Invoke(m.monsterId);
        Debug.Log($"[Dev] Forced evolution offer for {def.displayName} (L{m.level}).");
    }

    [Button("Make Team Lead Shiny")]
    void Btn_MakeLeadShiny()
    {
        var data = SaveManager.Data;
        if (data?.team == null || data.team.Count == 0) { Debug.Log("No lead."); return; }
        data.team[0].isShiny = true;
        SaveManager.Save();
        Debug.Log("Lead marked shiny.");
    }

    [Button("Make ShadowMarket Slot1 Worker Shiny")]
    void Btn_MakeShadowMarketWorkerShiny()
    {
        var jm = JobManager.I;
        if (jm == null) return;
        var st = jm.States.Find(s => s != null && s.config != null && s.config.jobType == JobType.ShadowMarket);
        if (st == null || st.workers.Count == 0 || st.workers[0] == null) { Debug.Log("No worker in ShadowMarket slot 1."); return; }

        var owned = ShinySystems.ResolveOwned(st.workers[0]);
        if (owned != null)
        {
            owned.isShiny = true;
            SaveManager.Save();
            Debug.Log("ShadowMarket slot 1 worker set shiny.");
        }
        else
        {
            Debug.Log("Couldn’t resolve worker to an Owned copy. Assign using ownedUID for perfect mapping.");
        }
    }



    [Button("Unlock Job Site")]
    public void DebugForceUnlock()
    {
        if (SaveManager.Data == null) return;

        SaveManager.Data.unlockedJobSites ??= new System.Collections.Generic.HashSet<JobType>();
        SaveManager.Data.unlockedJobSites.Add(debugJobType);
        SaveManager.Save();

        if (JobManager.I)
            JobManager.I.Invoke("RefreshAllJobSiteViewsInScene", 0f);

        Debug.Log($"[Jobs] DebugForceUnlock({debugJobType})");
    }

    [Button]
    public void DebugLogUnlocks()
    {
        var list = (SaveManager.Data?.unlockedJobSitesList != null)
            ? string.Join(", ", SaveManager.Data.unlockedJobSitesList)
            : "null";
        Debug.Log($"[Jobs] unlockedJobSitesList: {list}");

        var set = (SaveManager.Data?.unlockedJobSites != null)
            ? string.Join(", ", SaveManager.Data.unlockedJobSites)
            : "null";
        Debug.Log($"[Jobs] unlockedJobSites (HashSet): {set}");
    }
    
    // ===== Idle Battle: quick test buttons =====
    [Button("Idle: Run Encounters Now")]
    void Btn_Idle_RunEncounters()
    {
        if (!IdleBattleManager.I) { Debug.LogWarning("IdleBattleManager not present."); return; }
        IdleBattleManager.I.Dev_RunEncounters(Mathf.Max(1, idleDev_Encounters));
        Debug.Log($"[Idle] Ran {Mathf.Max(1, idleDev_Encounters)} encounters.");
    }

    [Button("Idle: Simulate Offline Seconds")]
    void Btn_Idle_SimulateOffline()
    {
        if (!IdleBattleManager.I) { Debug.LogWarning("IdleBattleManager not present."); return; }
        int s = Mathf.Max(1, idleDev_OfflineSeconds);
        IdleBattleManager.I.Dev_SimulateOfflineSeconds(s);
        Debug.Log($"[Idle] Simulated {s} seconds offline.");
    }

    [Button("Idle: Open Summary")]
    void Btn_Idle_OpenSummary()
    {
        if (!IdleBattleManager.I) { Debug.LogWarning("IdleBattleManager not present."); return; }
        IdleBattleManager.I.Dev_OpenSummary();
    }

    [Button("Idle: Clear Idle Log")]
    void Btn_Idle_ClearLog()
    {
        if (!IdleBattleManager.I) { Debug.LogWarning("IdleBattleManager not present."); return; }
        IdleBattleManager.I.Dev_ClearIdleLog();
        Debug.Log("[Idle] Cleared summary log.");
    }

    [Button("Idle: Toggle AUTO ON")]
    void Btn_Idle_AutoOn()
    {
        IdleBattleManager.I?.EnableAuto();
        Debug.Log("[Idle] AUTO enabled.");
    }

    [Button("Idle: Toggle AUTO OFF")]
    void Btn_Idle_AutoOff()
    {
        IdleBattleManager.I?.DisableAuto();
        Debug.Log("[Idle] AUTO disabled.");
    }

    // ===== Optional live tuning (SO) =====
    [Button("Idle: 1s per encounter")]
    void Btn_Idle_OneSecondPace()
    {
        if (!idleConfig) { Debug.LogWarning("Assign IdleBattleConfigSO in DevTools."); return; }
        idleConfig.secondsPerEncounter = 1f;
        Debug.Log("[Idle] secondsPerEncounter = 1");
    }

    [Button("Idle: Reset Pace (4s)")]
    void Btn_Idle_DefaultPace()
    {
        if (!idleConfig) { Debug.LogWarning("Assign IdleBattleConfigSO in DevTools."); return; }
        idleConfig.secondsPerEncounter = 4f;
        Debug.Log("[Idle] secondsPerEncounter = 4");
    }

    [Button("Idle: Max Offline = 8h")]
    void Btn_Idle_MaxOffline8h()
    {
        if (!idleConfig) { Debug.LogWarning("Assign IdleBattleConfigSO in DevTools."); return; }
        idleConfig.maxOfflineHours = 8;
        Debug.Log("[Idle] maxOfflineHours = 8");
    }



}