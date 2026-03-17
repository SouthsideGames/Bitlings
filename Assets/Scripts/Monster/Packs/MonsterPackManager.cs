// Assets/Scripts/Monster/MonsterPackManager.cs
using UnityEngine;
using System;
using System.Collections.Generic;

public class MonsterPackManager : MonoBehaviour
{
    public static MonsterPackManager I { get; private set; }

    [Header("References (Optional)")]
    [Tooltip("If set, this overrides all other ways of finding the pack library.")]
    [SerializeField] private MonsterPackLibrarySO packLibraryOverride;

    private MonsterPackLibrarySO _packLibrary;
    private MonsterLibrarySO _monsterLibrary;

    [Header("Pack Seasons (Optional)")]
    [Tooltip("If set, seasons are enabled and only active-season packs are shown/purchasable.")]
    [SerializeField] private MonsterPackSeasonRotationSO seasonRotationOverride;

    [Header("Tuning")]
    [Tooltip("Global discount applied to all pack costs (0..1). 0.15 = 15% off.")]
    [Range(0f, 1f)][SerializeField] private float globalDiscount01 = 0f;

    private MonsterPackSeasonRotationSO _seasonRotation;

    // Cache active season pack ids so UI & purchase checks are fast
    private int _cachedSeasonIndex = int.MinValue;
    private readonly HashSet<string> _activeSeasonPackIds = new HashSet<string>(StringComparer.Ordinal);

    /// <summary> Fired after a pack is unlocked. Parameter = packId. </summary>
    public static event Action<string> OnPackUnlocked;

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
            // Fail-soft: keep the manager instance alive so callers can safely query.
            // Pack shop features will remain unavailable until the library is provided.
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
                "[MonsterPackManager] Could not resolve MonsterPackLibrarySO. " +
                "Set it in the Inspector OR create Assets/Resources/MonsterPackLibrary.asset. " +
                "Disabling MonsterPackManager.");
#endif
            enabled = false;
            return;
        }

        _packLibrary.Warmup();

        _monsterLibrary = MonsterLibraryLocator.Lib;
        if (!_monsterLibrary)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
                "[MonsterPackManager] MonsterLibraryLocator.Lib could not load. " +
                "Ensure Assets/Resources/MonsterLibrary.asset exists.");
#endif
        }

        // Seasons: Inspector override → Locator → none (seasons disabled)
        _seasonRotation = seasonRotationOverride
            ? seasonRotationOverride
            : MonsterPackSeasonLocator.Seasons;

        RefreshActiveSeasonCache(force: true);

        // Ensure save list exists
        if (SaveManager.Data != null)
            SaveManager.Data.unlockedPacks ??= new List<string>();

#if UNITY_EDITOR
        foreach (var p in _packLibrary.PacksReadOnly)
        {
            if (!p) continue;
            if (p.costType != ResourceType.PackVoucher)
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                Debug.LogWarning($"[MonsterPackManager] '{p.name}' has costType={p.costType} but shop is shards-only.");
#endif
        }
#endif

        AutoUnlockDefaults();
    }

    // ─────────────────────────────────────────────
    // Seasons
    // ─────────────────────────────────────────────

    public bool SeasonsEnabled =>
        _seasonRotation != null &&
        _seasonRotation.seasons != null &&
        _seasonRotation.seasons.Count > 0;

    private void RefreshActiveSeasonCache(bool force = false)
    {
        if (!SeasonsEnabled)
        {
            _activeSeasonPackIds.Clear();
            _cachedSeasonIndex = int.MinValue;
            return;
        }

        long now = SaveManager.NowUnix();
        int idx = _seasonRotation.GetSeasonIndex(now);

        // If same season and not forced, keep existing cache (DO NOT clear)
        if (!force && idx == _cachedSeasonIndex)
            return;

        bool seasonChanged = _cachedSeasonIndex != int.MinValue && idx != _cachedSeasonIndex;
        _cachedSeasonIndex = idx;

        _activeSeasonPackIds.Clear();

        var ids = _seasonRotation.GetActivePackIds(now);
        if (ids == null) return;

        for (int i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            if (!string.IsNullOrEmpty(id))
                _activeSeasonPackIds.Add(id);
        }

        if (seasonChanged)
            GameEvents.PackSeasonChanged?.Invoke();
    }

    public int GetCurrentSeasonNumber1Based()
    {
        if (!SeasonsEnabled) return 0;
        RefreshActiveSeasonCache();
        return _cachedSeasonIndex + 1;
    }

    public string GetCurrentSeasonName()
    {
        if (!SeasonsEnabled || _seasonRotation == null) return string.Empty;

        long now = SaveManager.NowUnix();
        var active = _seasonRotation.GetActiveSeason(now);
        return active != null ? (active.seasonName ?? string.Empty) : string.Empty;
    }

    public string GetNextSeasonName()
    {
        if (!SeasonsEnabled || _seasonRotation == null) return string.Empty;

        long now = SaveManager.NowUnix();
        var next = _seasonRotation.GetNextSeason(now);
        return next != null ? (next.seasonName ?? string.Empty) : string.Empty;
    }

    public long GetCurrentSeasonEndUnix()
    {
        if (!SeasonsEnabled || _seasonRotation == null) return 0;
        long now = SaveManager.NowUnix();
        return _seasonRotation.GetSeasonEndUnix(now);
    }

    public bool IsPackOfferedThisSeason(string packId)
    {
        // If no seasons configured, behave like today (all packs allowed)
        if (!SeasonsEnabled) return true;
        if (string.IsNullOrEmpty(packId)) return false;

        RefreshActiveSeasonCache();
        return _activeSeasonPackIds.Contains(packId);
    }

    /// <summary>
    /// True if this pack appears in any earlier season entry (index < current) in the rotation list.
    /// Used for a "Returning" badge in the shop UI.
    /// </summary>
    public bool IsReturningPackThisSeason(string packId)
    {
        if (!SeasonsEnabled || string.IsNullOrEmpty(packId) || _seasonRotation == null) return false;

        RefreshActiveSeasonCache();
        int currentIdx = _cachedSeasonIndex;
        if (currentIdx <= 0) return false;

        // Earlier seasons only (keeps Season 1 clean / non-returning)
        for (int i = 0; i < currentIdx; i++)
        {
            var s = _seasonRotation.seasons[i];
            if (s == null || s.packIds == null) continue;
            if (s.packIds.Contains(packId))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Use this for your Shop UI to populate the grid when seasons are enabled.
    /// If seasons are disabled, returns all packs (current behavior).
    /// </summary>
    public List<MonsterPackSO> GetActiveSeasonPacks()
    {
        var result = new List<MonsterPackSO>();
        if (_packLibrary == null) return result;

        if (!SeasonsEnabled)
        {
            foreach (var p in _packLibrary.PacksReadOnly)
                if (p != null) result.Add(p);
            return result;
        }

        RefreshActiveSeasonCache();

        // Preserve the season order defined in the rotation asset
        var ids = _seasonRotation.GetActivePackIds(SaveManager.NowUnix());
        if (ids == null) return result;

        for (int i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            if (string.IsNullOrEmpty(id)) continue;

            var p = _packLibrary.Get(id);
            if (p != null) result.Add(p);
        }

        return result;
    }

    /// <summary>
    /// Returns next season packs in the order defined in the rotation asset.
    /// If seasons are not enabled, returns empty list.
    /// </summary>
    public List<MonsterPackSO> GetNextSeasonPacks()
    {
        var result = new List<MonsterPackSO>();
        if (!SeasonsEnabled || _seasonRotation == null || _packLibrary == null) return result;

        long now = SaveManager.NowUnix();
        var ids = _seasonRotation.GetNextPackIds(now);
        if (ids == null) return result;

        for (int i = 0; i < ids.Count; i++)
        {
            var id = ids[i];
            if (string.IsNullOrEmpty(id)) continue;

            var p = _packLibrary.Get(id);
            if (p != null) result.Add(p);
        }

        return result;
    }

    // ─────────────────────────────────────────────
    // Queries
    // ─────────────────────────────────────────────

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
    /// Calculates the effective cost for a pack using per-pack sale + global discount.
    /// </summary>
    public bool TryGetEffectiveCost(MonsterPackSO pack, out int finalCost, out ResourceType currency)
    {
        finalCost = 0;
        currency = ResourceType.PackVoucher;
        if (!pack) return false;

        int baseCost = Mathf.Max(0, pack.baseCost);
        float combinedMul = (1f - Mathf.Clamp01(pack.saleOff01)) * (1f - Mathf.Clamp01(globalDiscount01));
        float worldMul = 1f;
        if (WorldEventSystem.I != null)
            worldMul = Mathf.Max(0f, WorldEventSystem.I.GetShopPriceMultiplier());

        finalCost = Mathf.CeilToInt(baseCost * Mathf.Clamp(combinedMul, 0f, 1f) * worldMul);
        return true;
    }

    public bool CanPurchase(string packId, out string reason)
    {
        reason = null;

        if (_packLibrary == null) { reason = "Pack library missing"; return false; }
        if (SaveManager.Data == null) { reason = "Save not loaded"; return false; }

        var pack = _packLibrary.Get(packId);
        if (!pack) { reason = "Pack not found"; return false; }

        // Season gate
        if (!IsPackOfferedThisSeason(packId))
        {
            reason = "Not available this season";
            return false;
        }

        if (IsUnlocked(packId)) { reason = "Already unlocked"; return false; }

        if (!TryGetEffectiveCost(pack, out int cost, out _))
        {
            reason = "Invalid pack cost";
            return false;
        }

        int have = ResourceBank.Get(ResourceType.PackVoucher);
        if (have < cost) { reason = "Not enough Pack Vouchers"; return false; }

        return true;
    }

    // ─────────────────────────────────────────────
    // Actions
    // ─────────────────────────────────────────────

    public bool Purchase(string packId)
    {
        if (!CanPurchase(packId, out _)) return false;

        var pack = _packLibrary.Get(packId);
        if (!TryGetEffectiveCost(pack, out int cost, out _))
            return false;

        if (!ResourceManager.I.TrySpend(ResourceType.PackVoucher, cost))
            return false;

        Unlock(packId);

        GameEvents.ShowRewardPopup?.Invoke(pack.displayName, "Pack Unlocked", 0, 0);
        AudioManager.I.PlaySfx(SfxType.Purchase);
        return true;
    }

    public void Unlock(string packId)
    {
        var data = SaveManager.Data;
        if (data == null) return;

        data.unlockedPacks ??= new List<string>();

        bool added = false;
        if (!data.unlockedPacks.Contains(packId))
        {
            data.unlockedPacks.Add(packId);
            added = true;
        }

        if (added)
            SaveManager.Save();

        MonsterCatalog.Invalidate();
        GameEvents.OnResourcesChanged?.Invoke();

        try { OnPackUnlocked?.Invoke(packId); } catch { /* ignore */ }

        RegisterUnlockedMonsters(packId);
    }

    // ─────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────

    private void AutoUnlockDefaults()
    {
        if (_packLibrary == null || _packLibrary.PacksReadOnly == null) return;
        if (SaveManager.Data == null) return;

        var list = SaveManager.Data.unlockedPacks ??= new List<string>();

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

    private void RegisterUnlockedMonsters(string packId)
    {
        if (_packLibrary == null) return;

        var pack = _packLibrary.Get(packId);
        if (pack == null || pack.monsters == null || pack.monsters.Count == 0) return;

        var data = SaveManager.Data;
        if (data == null) return;

        data.discoveredMonsterIds ??= new HashSet<string>();
        data.seenTypes ??= new HashSet<MonsterType>();

        bool changed = false;

        foreach (var monster in pack.monsters)
        {
            if (monster == null) continue;

            if (!string.IsNullOrEmpty(monster.id) && data.discoveredMonsterIds.Add(monster.id))
                changed = true;

            if (data.seenTypes.Add(monster.type))
                changed = true;
        }

        if (changed)
            SaveManager.Save();
    }

    // Optional tuning API
    public void SetGlobalDiscount01(float v) => globalDiscount01 = Mathf.Clamp01(v);

    /// <summary>
    /// Unlocks every pack in the pack library, then registers their monsters as discovered.
    /// Saves once. Intended for cheats / QA.
    /// Returns how many packs were newly unlocked.
    /// </summary>
    public int Cheat_UnlockAllPacks()
    {
        if (_packLibrary == null || SaveManager.Data == null) return 0;

        var data = SaveManager.Data;
        data.unlockedPacks ??= new System.Collections.Generic.List<string>();

        int added = 0;

        foreach (var p in _packLibrary.PacksReadOnly)
        {
            if (!p || string.IsNullOrEmpty(p.id)) continue;
            if (data.unlockedPacks.Contains(p.id)) continue;

            data.unlockedPacks.Add(p.id);
            added++;

            // Make contained monsters visible in Codex/draft pools
            RegisterUnlockedMonsters(p.id);

            try { OnPackUnlocked?.Invoke(p.id); } catch { /* ignore */ }
        }

        if (added > 0)
        {
            SaveManager.Save();
            MonsterCatalog.Invalidate();
            GameEvents.OnResourcesChanged?.Invoke();
        }

        return added;
    }
}
