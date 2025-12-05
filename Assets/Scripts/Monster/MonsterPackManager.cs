// Assets/Scripts/Monster/MonsterPackManager.cs
using UnityEngine;
using System;

public class MonsterPackManager : MonoBehaviour
{
    public static MonsterPackManager I { get; private set; }

    [Header("References (Optional)")]
    [Tooltip("If set, this overrides all other ways of finding the pack library.")]
    [SerializeField] private MonsterPackLibrarySO packLibraryOverride;

    private MonsterPackLibrarySO _packLibrary;  // Loaded: Override → Locator → Resources
    private MonsterLibrarySO     _monsterLibrary;   // Resolved via MonsterLibraryLocator

    [Header("Tuning")]
    [Tooltip("Global discount applied to all pack costs (0..1). 0.15 = 15% off.")]
    [Range(0f, 1f)] [SerializeField] private float globalDiscount01 = 0f;

    /// <summary> Fired after a pack is unlocked. Parameter = packId. </summary>
    public static event Action<string> OnPackUnlocked;

    // ─────────────────────────────────────────────────────────────────────────────
    // Initialization
    // ─────────────────────────────────────────────────────────────────────────────
    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;

        // 1) Inspector override → 2) Locator (Resources cached) → 3) Direct Resources (paranoid fallback)
        _packLibrary = packLibraryOverride
                       ? packLibraryOverride
                       : (MonsterPackLibraryLocator.Lib != null
                          ? MonsterPackLibraryLocator.Lib
                          : Resources.Load<MonsterPackLibrarySO>("MonsterPackLibrary"));

        if (!_packLibrary)
        {
            Debug.LogError("[MonsterPackManager] Could not resolve MonsterPackLibrarySO. " +
                           "Set it in the Inspector OR create Assets/Resources/MonsterPackLibrary.asset");
            return;
        }

        // Warmup any internal indexes
        _packLibrary.Warmup();

        // Load monster library via locator (this is already your desired path)
        _monsterLibrary = MonsterLibraryLocator.Lib;
        if (!_monsterLibrary)
        {
            Debug.LogWarning("[MonsterPackManager] MonsterLibraryLocator.Lib could not load. " +
                             "Ensure Assets/Resources/MonsterLibrary.asset exists.");
        }

        // Ensure save list exists
        if (SaveManager.Data != null)
            SaveManager.Data.unlockedPacks ??= new System.Collections.Generic.List<string>();

#if UNITY_EDITOR
        // Warn if any pack has a non-shard costType
        foreach (var p in _packLibrary.PacksReadOnly)
        {
            if (!p) continue;
            if (p.costType != ResourceType.PackShard)
                Debug.LogWarning($"[MonsterPackManager] '{p.name}' has costType={p.costType} but shop is shards-only.");
        }
#endif

        AutoUnlockDefaults();
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Queries
    // ─────────────────────────────────────────────────────────────────────────────

    public bool TryGetPack(string packId, out MonsterPackSO pack)
    {
        pack = _packLibrary ? _packLibrary.Get(packId) : null;
        return pack != null;
    }

    public bool IsUnlocked(string packId)
    {
        var data = SaveManager.Data;
        return data != null && data.unlockedPacks != null && data.unlockedPacks.Contains(packId);
    }

    /// <summary>
    /// Calculates the effective shard cost for a pack using per-pack sale + global discount.
    /// </summary>
    public bool TryGetEffectiveCost(MonsterPackSO pack, out int finalCost, out ResourceType currency)
    {
        finalCost = 0;
        currency  = ResourceType.PackShard; // shards-only
        if (!pack) return false;

        int baseCost = Mathf.Max(0, pack.baseCost);
        float combinedMul = (1f - Mathf.Clamp01(pack.saleOff01)) * (1f - Mathf.Clamp01(globalDiscount01));
        finalCost = Mathf.CeilToInt(baseCost * Mathf.Clamp(combinedMul, 0f, 1f));
        return true;
    }

    public bool CanPurchase(string packId, out string reason)
    {
        reason = null;

        if (_packLibrary == null) { reason = "Pack library missing"; return false; }
        if (SaveManager.Data == null) { reason = "Save not loaded"; return false; }

        var pack = _packLibrary.Get(packId);
        if (!pack) { reason = "Pack not found"; return false; }
        if (IsUnlocked(packId)) { reason = "Already unlocked"; return false; }

        if (!TryGetEffectiveCost(pack, out int cost, out _))
        {
            reason = "Invalid pack cost";
            return false;
        }

        int have = ResourceBank.Get(ResourceType.PackShard);
        if (have < cost) { reason = "Not enough Shards"; return false; }

        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Actions
    // ─────────────────────────────────────────────────────────────────────────────

    public bool Purchase(string packId)
    {
        if (!CanPurchase(packId, out _)) return false;

        var pack = _packLibrary.Get(packId);
        if (!TryGetEffectiveCost(pack, out int cost, out _))
            return false;

        // Spend shards
        if (!ResourceManager.I.TrySpend(ResourceType.PackShard, cost))
            return false;

        Unlock(packId);

        // Optional popup
        GameEvents.ShowRewardPopup?.Invoke(pack.displayName, "Pack Unlocked", 0, 0);

        AudioManager.I.PlaySfx(SfxType.Purchase);
        return true;
    }

    public void Unlock(string packId)
    {
        var data = SaveManager.Data;
        if (data == null) return;

        data.unlockedPacks ??= new System.Collections.Generic.List<string>();
        if (!data.unlockedPacks.Contains(packId))
        {
            data.unlockedPacks.Add(packId);
            SaveManager.Save(); // persist
        }

        try { OnPackUnlocked?.Invoke(packId); } catch { /* ignore */ }

        // Optionally: register contained monsters in MonsterLibrary
        RegisterUnlockedMonsters(packId);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    private void AutoUnlockDefaults()
    {
        if (_packLibrary == null || _packLibrary.PacksReadOnly == null) return;
        if (SaveManager.Data == null) return;

        var list = SaveManager.Data.unlockedPacks ??= new System.Collections.Generic.List<string>();

        bool changed = false;
        foreach (var p in _packLibrary.PacksReadOnly)
        {
            if (!p || !p.unlockByDefault) continue;
            if (!list.Contains(p.id))
            {
                list.Add(p.id);
                changed = true;
            }
        }
        if (changed) SaveManager.Save();
    }

    /// <summary>
    /// When a pack is unlocked, automatically mark contained monsters as “seen” in the SaveManager,
    /// so they show up in the Codex or draft lists.
    /// </summary>
    private void RegisterUnlockedMonsters(string packId)
    {
        if (_monsterLibrary == null) return;
        if (_packLibrary == null) return;

        var pack = _packLibrary.Get(packId);
        if (pack == null || pack.monsters == null || pack.monsters.Count == 0) return;

        var seen = SaveManager.Data?.seenTypes;
        if (seen == null) return;

        foreach (var monster in pack.monsters)
        {
            if (monster == null) continue;
            seen.Add(monster.type);
        }
    }

    // Optional tuning API
    public void SetGlobalDiscount01(float v) => globalDiscount01 = Mathf.Clamp01(v);
}
