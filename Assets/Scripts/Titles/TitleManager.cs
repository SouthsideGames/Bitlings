using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class TitleManager : MonoBehaviour
{
    public static TitleManager I { get; private set; }

    [Header("Library / Lookup")]
    [Tooltip("If null, manager will scan Resources for all TitleSO assets on Awake.")]
    [SerializeField] private List<TitleSO> preloadTitles = new List<TitleSO>();

    [Header("Debug")]
    [SerializeField] private bool debugEffectiveness = false;

    // id -> TitleSO
    private readonly Dictionary<string, TitleSO> _idToTitle = new Dictionary<string, TitleSO>();

    private string _activeBattleMonsterId;

    // Battle sessions can include multiple combatants (e.g., player + wild).
    // Most per-battle state is keyed by combatant id; we track participants so
    // turn-based effects (TurnBooster) can apply to all relevant combatants.
    private bool _battleSessionActive;
    private readonly HashSet<string> _battleParticipants = new HashSet<string>(StringComparer.Ordinal);
    private readonly List<string> _scratchParticipants = new List<string>(8);

    // ─────────────────────────────────────────────────────────────────────
    // Per-battle state (TurnBooster / EventStacks / BattleStart)
    // ─────────────────────────────────────────────────────────────────────
    private readonly Dictionary<string, int> _turnStacks = new();           // grows on OnTurnAdvanced up to max (TurnBooster)
    private readonly Dictionary<string, int> _eventStacks = new();          // grows on triggers (EventStacks)
    private readonly Dictionary<string, int> _eventMax = new();             // cache max for UI/debug (optional)
    private readonly Dictionary<string, int> _eventDecayPerTurn = new();    // how many stacks to decay each turn
    private readonly List<string> _scratchEventKeys = new List<string>(16);
    private readonly Dictionary<string, int> _flatStartUntilTurn = new();   // inclusive last turn index where flat buff applies
    private readonly Dictionary<string, int> _flatStartAmountAtk = new();   // flat ATK from BattleStartFlatTitleSO
    private readonly Dictionary<string, int> _flatStartAmountDef = new();   // flat DEF from BattleStartFlatTitleSO
    private readonly Dictionary<string, int> _flatStartAmountSpd = new();   // flat SPD from BattleStartFlatTitleSO
    private readonly Dictionary<string, int> _flatStartAmountHp  = new();   // flat HP from BattleStartFlatTitleSO
    private readonly Dictionary<string, int> _flatStartRemainingTurns = new(); // remaining OWNER turns for BattleStartFlatTitleSO
    private readonly Dictionary<string, float> _shieldRemaining = new();    // BattleStartShieldTitleSO: remaining shield HP
    private int _turnIndex;

    // ─────────────────────────────────────────────────────────────────────
    // Active Title UI state (Status Bar / Info Button)
    // ─────────────────────────────────────────────────────────────────────
    public struct ActiveTitleUIState
    {
        public string titleId;
        public string displayName;
        public Sprite icon;
        public int stacks;     // 0 if not applicable
        public bool isActive;  // true = highlight (active), false = dim (inactive)
    }

    /// <summary>
    /// Returns the SINGLE equipped title id for a monster (excluding always-on defaults).
    /// If none equipped, returns "".
    /// </summary>
    public string GetEquippedTitleId(string ownedMonsterId)
    {
        if (string.IsNullOrEmpty(ownedMonsterId)) return "";

        var def = MonsterLibraryLocator.GetById(ownedMonsterId);
        if (!def || !def.titleTrack) return "";

        var tiers = def.titleTrack.tiers;
        if (tiers == null || tiers.Count == 0) return "";

        var save = TitleSaveStore.GetOrCreateEquip(ownedMonsterId);
        if (save == null || save.tierSelections == null) return "";

        // Enforced as "only one total" by Equip(), but we still scan defensively.
        for (int i = 0; i < save.tierSelections.Count; i++)
        {
            var tid = save.tierSelections[i];
            if (!string.IsNullOrEmpty(tid))
                return tid;
        }

        return "";
    }

    /// <summary>
    /// UI helper (legacy): returns equipped titles for a monster with "active" flags + stacks based on current battle state.
    /// With the one-title rule, this will typically return either:
    /// - default always-on titles (if you use them), plus
    /// - a single picked title (at most one)
    /// </summary>
    public List<ActiveTitleUIState> GetActiveTitleUIStates(string ownedMonsterId)
    {
        var res = new List<ActiveTitleUIState>();
        if (string.IsNullOrEmpty(ownedMonsterId)) return res;

        var def = MonsterLibraryLocator.GetById(ownedMonsterId);
        int lvl = GetLevelOr1(ownedMonsterId);
        var equipped = GetEquippedList(ownedMonsterId, def, lvl);
        if (equipped == null) return res;

        for (int i = 0; i < equipped.Count; i++)
        {
            var t = equipped[i];
            if (!t) continue;

            var s = new ActiveTitleUIState
            {
                titleId = t.titleId,
                displayName = string.IsNullOrEmpty(t.displayName) ? t.titleId : t.displayName,
                icon = TryReadSprite(t, out var icon) ? icon : null,
                stacks = 0,
                isActive = true
            };

            // If we are not in a battle, treat equipped titles as active for UI consistency.
            bool inBattle = _turnStacks.Count > 0 || _eventStacks.Count > 0 || _shieldRemaining.Count > 0 || _flatStartUntilTurn.Count > 0;

            if (inBattle)
            {
                if (t is TurnBoosterTitleSO tb)
                {
                    _turnStacks.TryGetValue(ownedMonsterId, out int st);
                    s.stacks = st;
                    s.isActive = st > 0;
                }
                else if (t is EventStacksTitleSO)
                {
                    _eventStacks.TryGetValue(ownedMonsterId, out int st);
                    s.stacks = st;
                    s.isActive = st > 0;
                }
                else if (t is BattleStartShieldTitleSO)
                {
                    _shieldRemaining.TryGetValue(ownedMonsterId, out float shield);
                    s.isActive = shield > 0.01f;
                }
                else if (t is BattleStartFlatTitleSO)
                {
                    if (_flatStartRemainingTurns.TryGetValue(ownedMonsterId, out int rem))
                        s.isActive = rem > 0;
                }
                else
                {
                    s.isActive = true;
                }
            }

            res.Add(s);
        }

        return res;
    }

    private static bool TryReadSprite(object obj, out Sprite sprite)
    {
        sprite = null;
        if (obj == null) return false;

        try
        {
            var t = obj.GetType();

            var f = t.GetField("icon");
            if (f != null && f.FieldType == typeof(Sprite))
            {
                sprite = (Sprite)f.GetValue(obj);
                return sprite != null;
            }

            var p = t.GetProperty("icon");
            if (p != null && p.PropertyType == typeof(Sprite))
            {
                sprite = (Sprite)p.GetValue(obj, null);
                return sprite != null;
            }

            var p2 = t.GetProperty("Icon");
            if (p2 != null && p2.PropertyType == typeof(Sprite))
            {
                sprite = (Sprite)p2.GetValue(obj, null);
                return sprite != null;
            }
        }
        catch { }

        return false;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Unity
    // ─────────────────────────────────────────────────────────────────────
    private void Awake()
    {
        if (I != null && I != this)
        {
            Destroy(gameObject);
            return;
        }
        I = this;
        BuildIndex();
    }

    private void BuildIndex()
    {
        _idToTitle.Clear();

        var allCandidates = new List<TitleSO>(64);

        if (preloadTitles != null)
        {
            for (int i = 0; i < preloadTitles.Count; i++)
            {
                var t = preloadTitles[i];
                if (!t || string.IsNullOrEmpty(t.titleId)) continue;
                allCandidates.Add(t);

                if (!_idToTitle.ContainsKey(t.titleId))
                    _idToTitle.Add(t.titleId, t);
            }
        }

        var all = Resources.LoadAll<TitleSO>("");
        for (int i = 0; i < all.Length; i++)
        {
            var t = all[i];
            if (!t || string.IsNullOrEmpty(t.titleId)) continue;
            allCandidates.Add(t);

            if (!_idToTitle.ContainsKey(t.titleId))
                _idToTitle.Add(t.titleId, t);
        }

        // Duplicate titleId detection (non-LINQ, editor-friendly)
        if (allCandidates.Count > 0)
        {
            var byId = new Dictionary<string, List<TitleSO>>(StringComparer.Ordinal);
            for (int i = 0; i < allCandidates.Count; i++)
            {
                var t = allCandidates[i];
                if (!t || string.IsNullOrEmpty(t.titleId)) continue;

                if (!byId.TryGetValue(t.titleId, out var list))
                {
                    list = new List<TitleSO>(2);
                    byId.Add(t.titleId, list);
                }
                list.Add(t);
            }

            foreach (var kv in byId)
            {
                var list = kv.Value;
                if (list == null || list.Count <= 1) continue;
                Debug.LogError(BuildDuplicateTitleIdLog(kv.Key, list));
            }
        }

        // Debug: report how many titles were indexed so we can verify runtime loading.
        if (_idToTitle.Count == 0)
            Debug.LogWarning("TitleManager: no TitleSO assets indexed. Ensure TitleSO assets are in a Resources folder or assigned to 'preloadTitles' in the inspector.");
        else
            Debug.Log($"TitleManager: indexed {_idToTitle.Count} TitleSO assets.");
    }

    private static string BuildDuplicateTitleIdLog(string titleId, IEnumerable<TitleSO> titles)
    {
        var sb = new System.Text.StringBuilder(256);
        sb.Append("[Titles] Duplicate titleId detected: ").Append(titleId).Append("\n");
        sb.Append("These are different TitleSO assets sharing the same titleId. This will break equip/UI state.\n");

        int i = 0;
        foreach (var t in titles)
        {
            if (!t) continue;

            sb.Append("  • [").Append(i).Append("] ").Append(t.name).Append(" (").Append(t.GetType().Name).Append(")");

#if UNITY_EDITOR
            try
            {
                string path = UnityEditor.AssetDatabase.GetAssetPath(t);
                if (!string.IsNullOrEmpty(path))
                    sb.Append("  ->  ").Append(path);
            }
            catch { }
#endif
            sb.Append("\n");
            i++;
        }

        sb.Append("Fix: ensure every TitleSO has a unique titleId, and remove/merge duplicates.");
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public: Query available titles for a monster (by level & track)
    // ─────────────────────────────────────────────────────────────────────
    public List<List<TitleSO>> GetAvailableByTier(MonsterDataSO def, int level)
    {
        var result = new List<List<TitleSO>>();
        if (!def || !def.titleTrack) return result;

        var tiers = def.titleTrack.tiers;
        if (tiers == null) return result;

        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];
            if (level >= Mathf.Max(1, tier.levelRequired))
                result.Add(new List<TitleSO>(tier.unlockChoices ?? new List<TitleSO>()));
            else
                result.Add(new List<TitleSO>()); // locked
        }
        return result;
    }

    public int GetTierCount(MonsterDataSO def) => def && def.titleTrack ? def.titleTrack.tiers?.Count ?? 0 : 0;

    public string GetEquippedTitleIdForTier(string monsterId, MonsterDataSO def, int tierIndex)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack) return "";
        var tiers = def.titleTrack.tiers;
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Count) return "";

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);
        if (save == null || save.tierSelections == null) return "";

        if (tierIndex >= save.tierSelections.Count) return "";
        return save.tierSelections[tierIndex];
    }

    // ─────────────────────────────────────────────────────────────────────
    // Public: Equip / Get Equipped
    // (Fires JobGlobalModsChanged for UI/logic that depends on titles)
    // ─────────────────────────────────────────────────────────────────────

    public bool Equip(string monsterId, MonsterDataSO def, int tierIndex, TitleSO choose)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack) return false;
        var tiers = def.titleTrack.tiers;
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Count) return false;
        if (!choose) return false;

        // Must be among that tier's choices
        var tier = tiers[tierIndex];
        if (tier.unlockChoices == null) return false;

        // Must be among that tier's choices (non-LINQ)
        bool found = false;
        for (int i = 0; i < tier.unlockChoices.Count; i++)
        {
            var t = tier.unlockChoices[i];
            if (t && t.titleId == choose.titleId) { found = true; break; }
        }
        if (!found) return false;

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);
        if (save == null) return false;

        // Ensure list exists + correct size
        if (save.tierSelections == null) save.tierSelections = new List<string>();
        save.tierSelections.Clear();
        for (int i = 0; i < tiers.Count; i++) save.tierSelections.Add("");

        bool changed = save.tierSelections[tierIndex] != choose.titleId;
        save.tierSelections[tierIndex] = choose.titleId;

        TitleSaveStore.Save();
        if (changed) RaiseTitleChange();
        return true;
    }

    public bool EquipById(string monsterId, MonsterDataSO def, int tierIndex, string titleId)
    {
        if (string.IsNullOrEmpty(titleId)) return false;
        if (!_idToTitle.TryGetValue(titleId, out var so) || !so) return false;
        return Equip(monsterId, def, tierIndex, so);
    }

    public bool Unequip(string monsterId, MonsterDataSO def, int tierIndex)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack) return false;
        var tiers = def.titleTrack.tiers;
        if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Count) return false;

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);
        if (save == null) return false;

        if (save.tierSelections == null) save.tierSelections = new List<string>();
        while (save.tierSelections.Count < tiers.Count) save.tierSelections.Add("");

        bool changed = !string.IsNullOrEmpty(save.tierSelections[tierIndex]);
        save.tierSelections[tierIndex] = "";

        TitleSaveStore.Save();
        if (changed) RaiseTitleChange();
        return true;
    }

    /// <summary>
    /// Returns always-on titles + equipped (per unlocked tier). Null for locked/no pick.
    /// Note: With the one-title rule, at most one tier will have a non-empty selection.
    /// </summary>
    public List<TitleSO> GetEquippedList(string monsterId, MonsterDataSO def, int level)
    {
        var res = new List<TitleSO>();

        // Always-on defaults
        if (def && def.defaultAlwaysOnTitles != null)
            res.AddRange(def.defaultAlwaysOnTitles);

        if (string.IsNullOrEmpty(monsterId) || !def || !def.titleTrack)
            return res;

        var tiers = def.titleTrack.tiers;
        if (tiers == null) return res;

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);
        if (save == null) return res;

        if (save.tierSelections == null) save.tierSelections = new List<string>();

        for (int i = 0; i < tiers.Count; i++)
        {
            var tier = tiers[i];

            // Locked tier -> keep placeholder null for legacy callers that expect aligned list
            if (level < Mathf.Max(1, tier.levelRequired)) { res.Add(null); continue; }

            string tid = (i < save.tierSelections.Count) ? save.tierSelections[i] : "";
            if (!string.IsNullOrEmpty(tid) && _idToTitle.TryGetValue(tid, out var t) && t) res.Add(t);
            else res.Add(null);
        }

        return res;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Adapter-visible: return monster’s equipped titles
    // ─────────────────────────────────────────────────────────────────────
    // Called by TitlesAdapter via reflection: GetTitlesForMonster(string)
    public List<TitleSO> GetTitlesForMonster(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return new List<TitleSO>();
        var def = MonsterLibraryLocator.GetById(monsterId);
        int lvl = GetLevelOr1(monsterId);
        return GetEquippedList(monsterId, def, lvl);
    }

    public TitleSO GetTitleById(string titleId)
    {
        if (string.IsNullOrEmpty(titleId)) return null;
        return _idToTitle.TryGetValue(titleId, out var so) ? so : null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Evaluation helpers
    // ─────────────────────────────────────────────────────────────────────

    // Single/Conditional/Dual stat application + boosters
    public float GetStatValue(string monsterId, MonsterDataSO def, int level, StatKind stat, in TitleContext ctx, float baseValue)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float current = baseValue;

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];

            if (t is StatBoosterTitleSO sb && sb.stat == stat)
                current = TitleUtility.ApplyOp(current, sb.operation, sb.value);

            else if (t is ConditionalStatBoosterTitleSO cb && cb.stat == stat)
            {
                if (TitleUtility.CheckCondition(cb, ctx))
                    current = TitleUtility.ApplyOp(current, cb.operation, cb.value);
            }
            else if (t is DuoStatBoosterTitleSO dsb && dsb.enabled)
            {
                if (dsb.statA == stat) current = TitleUtility.ApplyOp(current, dsb.opA, dsb.valueA);
                if (dsb.statB == stat) current = TitleUtility.ApplyOp(current, dsb.opB, dsb.valueB);
            }
            else if (t is DuoConditionalStatBoosterTitleSO dcb)
            {
                if (TitleUtility.CheckCondition(dcb.condition, dcb.threshold01, dcb.countN, in ctx))
                {
                    if (dcb.statA == stat)
                        current = TitleUtility.ApplyOp(current, dcb.opA, dcb.valueA);
                    if (dcb.statB == stat)
                        current = TitleUtility.ApplyOp(current, dcb.opB, dcb.valueB);
                }
            }
        }

        
// ── BattleStartFlatTitleSO (temporary flat bonus at battle start; ticks down on OWNER turns)
if (_flatStartRemainingTurns.TryGetValue(monsterId, out int remTurns) && remTurns > 0)
{
    if (stat == StatKind.Attack && _flatStartAmountAtk.TryGetValue(monsterId, out int fAtk)) current += fAtk;
    else if (stat == StatKind.Defense && _flatStartAmountDef.TryGetValue(monsterId, out int fDef)) current += fDef;
    else if (stat == StatKind.Speed && _flatStartAmountSpd.TryGetValue(monsterId, out int fSpd)) current += fSpd;
    else if (stat == StatKind.HP && _flatStartAmountHp.TryGetValue(monsterId, out int fHp)) current += fHp;
}

        // ── TurnBoosterTitleSO (percent per turn up to max stacks)
        var tb = GetFirstTitle<TurnBoosterTitleSO>(monsterId, def, level);
        if (tb != null && MatchesStat(stat, tb.stat) && _turnStacks.TryGetValue(monsterId, out int tStacks) && tStacks > 0)
        {
            float pct = Mathf.Max(0f, tb.percentPerTurn) / 100f;
            current *= 1f + pct * Mathf.Min(tStacks, Mathf.Max(1, tb.maxStacks));
        }

        // ── EventStacksTitleSO (percent per stack; grows on triggers; optional decay)
        var es = GetFirstTitle<EventStacksTitleSO>(monsterId, def, level);
        if (es != null && MatchesStat(stat, es.stat) && _eventStacks.TryGetValue(monsterId, out int eStacks) && eStacks > 0)
        {
            float pct = Mathf.Max(0f, es.percentPerStack) / 100f;
            current *= 1f + pct * Mathf.Min(eStacks, Mathf.Max(1, es.maxStacks));
        }

        // ── ClutchBoosterTitleSO (threshold via ctx.hpPct)
        var clutch = GetFirstTitle<ClutchBoosterTitleSO>(monsterId, def, level);
        float hp01 = ReadHp01(ctx);
        if (clutch != null && hp01 <= Mathf.Clamp01(clutch.hpBelowThreshold01))
        {
            if (stat == StatKind.Attack && clutch.atkPct > 0f) current *= (1f + (clutch.atkPct / 100f));
            if (stat == StatKind.Defense && clutch.defPct > 0f) current *= (1f + (clutch.defPct / 100f));
            if (stat == StatKind.Speed && clutch.spdPct > 0f) current *= (1f + (clutch.spdPct / 100f));
        }

        return current;
    }

    public float GetEffectivenessMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as EffectivenessModTitleSO;
            if (!t || t.mode != EffectivenessMode.Multiply) continue;

            float before = mul;
            mul *= Mathf.Max(0f, t.amount);

            if (debugEffectiveness)
            {
                string msg = $"[EffectivenessMod] {monsterId} MULT x{t.amount:0.00}: {before:0.00} → {mul:0.00}";
                TryBattleLog(msg);
            }
        }

        if (debugEffectiveness)
        {
            string summary = $"[EffectivenessMod] FINAL MULT for {monsterId} = x{mul:0.00}";
            TryBattleLog(summary);
        }

        return mul;
    }

    public float GetEffectivenessAdd(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float add = 0f;

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as EffectivenessModTitleSO;
            if (!t || t.mode != EffectivenessMode.Add) continue;
            add += t.amount;
        }

        if (debugEffectiveness && Mathf.Abs(add) > 0.0001f)
        {
            string msg = $"[EffectivenessMod] FINAL ADD for {monsterId} = +{add:0.00} effectiveness";
            TryBattleLog(msg);
        }

        return add;
    }

    public float GetIncomingEffectivenessMult(string monsterId, MonsterDataSO def, int level, MonsterType incomingType)
    {
        // 1) Start with generic defensive multiplier (nullifiers, etc.)
        float mul = GetIncomingEffectivenessMultiplier(monsterId, def, level);

        if (incomingType == MonsterType.None)
            return Mathf.Max(0f, mul);

        // 2) Apply any per-type resist titles that match the incoming attack type.
        var titles = GetEquippedList(monsterId, def, level);
        if (titles != null)
        {
            for (int i = 0; i < titles.Count; i++)
            {
                if (titles[i] is TypeResistTitleSO tr && tr.resistTypes != null && tr.resistTypes.Length > 0)
                {
                    for (int k = 0; k < tr.resistTypes.Length; k++)
                    {
                        if (tr.resistTypes[k] == incomingType)
                        {
                            mul *= Mathf.Max(0f, tr.incomingMultiplier);
                            break;
                        }
                    }
                }
            }
        }

        return Mathf.Max(0f, mul);
    }

    // Adapter expects fields: cannotBeCrit, percentReduce, flatReduce
    public struct TitleDamageFilter
    {
        public bool cannotBeCrit;
        public float percentReduce;   // 0.20 = 20% less damage after DEF
        public int flatReduce;        // subtract after % reduce
    }

    public TitleDamageFilter GetDamageFilter(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        var f = new TitleDamageFilter { cannotBeCrit = false, percentReduce = 0f, flatReduce = 0 };

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as DamageFilterTitleSO;
            if (!t) continue;

            float reduceFromMult = Mathf.Clamp01(1f - Mathf.Max(0f, t.percentMultiplier));
            f.flatReduce += Mathf.Max(0, t.flatReduce);
            f.percentReduce = Mathf.Clamp01(f.percentReduce + reduceFromMult);
            if (t.cannotBeCrit) f.cannotBeCrit = true;
        }
        return f;
    }

    public object GetDamageFilterBoxed(string monsterId, MonsterDataSO def, int level) => GetDamageFilter(monsterId, def, level);

    // --- Job boosters (while assigned) ---
    public float GetJobFatigueMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
            if (titles[i] is JobFatigueBoosterTitleSO jb)
                mul *= Mathf.Max(0f, jb.fatigueMultiplier);
        return mul;
    }

    public float GetJobFatigueMultiplier(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as JobFatigueBoosterTitleSO;
            if (!t) continue;

            bool applies = false;
            try
            {
                var m = t.GetType().GetMethod("AppliesTo");
                if (m != null) applies = (bool)m.Invoke(t, new object[] { site });
                else
                {
                    var f = t.GetType().GetField("targetJobSite");
                    if (f != null)
                    {
                        var val = (JobType)f.GetValue(t);
                        applies = (val == JobType.None || val == site);
                    }
                    else applies = true;
                }
            }
            catch { applies = true; }

            if (applies) mul *= Mathf.Max(0f, t.fatigueMultiplier);
        }
        return Mathf.Max(0f, mul);
    }

    public float GetJobAuraPercent(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float sum = 0f;
        for (int i = 0; i < titles.Count; i++)
        {
            var ja = titles[i] as JobAuraTitleSO;
            if (!ja) continue;

            bool applies = false;
            try
            {
                var m = ja.GetType().GetMethod("AppliesTo");
                if (m != null) applies = (bool)m.Invoke(ja, new object[] { site });
                else
                {
                    var f = ja.GetType().GetField("targetJobSite");
                    if (f != null)
                    {
                        var val = (JobType)f.GetValue(ja);
                        applies = (val == JobType.None || val == site);
                    }
                    else applies = true;
                }
            }
            catch { applies = true; }

            if (applies) sum += Mathf.Max(0f, ja.siteAuraPercent);
        }
        return sum;
    }

    public int GetJobCapacityBonusFlat(string monsterId, MonsterDataSO def, int level, JobType site)
    {
        var titles = GetEquippedList(monsterId, def, level);
        int sum = 0;
        for (int i = 0; i < titles.Count; i++)
        {
            var jc = titles[i] as JobCapacityBoosterTitleSO;
            if (!jc) continue;

            bool applies = false;
            try
            {
                var m = jc.GetType().GetMethod("AppliesTo");
                if (m != null) applies = (bool)m.Invoke(jc, new object[] { site });
                else
                {
                    var f = jc.GetType().GetField("targetJobSite");
                    if (f != null)
                    {
                        var val = (JobType)f.GetValue(jc);
                        applies = (val == JobType.None || val == site);
                    }
                    else applies = true;
                }
            }
            catch { applies = true; }

            if (applies) sum += Mathf.Max(0, jc.capacityBonusFlat);
        }
        return sum;
    }

    public float GetJobRateMult(string monsterId, JobType site)
    {
        var def = MonsterLibraryLocator.GetById(monsterId);
        int lvl = 1;
        var titles = GetEquippedList(monsterId, def, lvl);

        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            // Job-rate modifiers are read generically via reflection and (optionally) respect a site restriction field.
            if (TryReadFloat(t, out var v, "rateMultiplier", "jobRateMultiplier", "productionMultiplier", "jobProdMult"))
            {
                bool applies = true;
                try
                {
                    // Prefer an AppliesTo(JobType) method if present.
                    var m = t.GetType().GetMethod("AppliesTo");
                    if (m != null)
                    {
                        applies = (bool)m.Invoke(t, new object[] { site });
                    }
                    else
                    {
                        // Common restriction field names used by job-related titles.
                        var f = t.GetType().GetField("restrictTo") ?? t.GetType().GetField("targetJobSite");
                        if (f != null && f.FieldType == typeof(JobType))
                        {
                            var val = (JobType)f.GetValue(t);
                            applies = (val == JobType.None || val == site);
                        }
                    }
                }
                catch { applies = true; }

                if (applies)
                    mul *= Mathf.Max(0f, v);
            }
        }
        return Mathf.Max(0f, mul);
    }

    public float GetStatValueRouter(string monsterId, MonsterDataSO def, int level, string statKind, TitleContext ctx, float baseValue)
    {
        // Battle uses both "real stat" keys (HP/Attack/Defense/Speed) and "mod keys" (atkFlat/atkPct/etc).
        if (string.IsNullOrEmpty(statKind))
            return baseValue;

        string key = statKind.Trim();

        // Mod-key routing (BattleManager requests these with baseValue=0)
        if (IsModKey(key))
        {
            var mods = GetBattleStatModsRuntime(monsterId, def, level, in ctx);
            if (key.Equals("atkFlat", StringComparison.OrdinalIgnoreCase)) return mods.atkFlat;
            if (key.Equals("defFlat", StringComparison.OrdinalIgnoreCase)) return mods.defFlat;
            if (key.Equals("spdFlat", StringComparison.OrdinalIgnoreCase)) return mods.spdFlat;
            if (key.Equals("atkPct", StringComparison.OrdinalIgnoreCase)) return mods.atkPct;
            if (key.Equals("defPct", StringComparison.OrdinalIgnoreCase)) return mods.defPct;
            if (key.Equals("spdPct", StringComparison.OrdinalIgnoreCase)) return mods.spdPct;
            if (key.Equals("hpPct",  StringComparison.OrdinalIgnoreCase)) return mods.hpPct;
            return baseValue;
        }

        // Normal stat routing (returns final stat value)
        var norm = NormalizeStatKey(key);
        if (!Enum.TryParse<StatKind>(norm, ignoreCase: true, out var kind))
            return baseValue;

        return GetStatValue(monsterId, def, level, kind, in ctx, baseValue);
    }

    private static bool IsModKey(string key)
    {
        return key.Equals("atkFlat", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("defFlat", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("spdFlat", StringComparison.OrdinalIgnoreCase) ||
               key.Equals("atkPct",  StringComparison.OrdinalIgnoreCase) ||
               key.Equals("defPct",  StringComparison.OrdinalIgnoreCase) ||
               key.Equals("spdPct",  StringComparison.OrdinalIgnoreCase) ||
               key.Equals("hpPct",   StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadFloat(object obj, out float value, params string[] names)
    {
        value = 0f;
        if (obj == null || names == null) return false;
        var t = obj.GetType();

        for (int i = 0; i < names.Length; i++)
        {
            var f = t.GetField(names[i]);
            if (f != null)
            {
                try { value = Convert.ToSingle(f.GetValue(obj)); return true; } catch { }
            }
            var p = t.GetProperty(names[i]);
            if (p != null)
            {
                try { value = Convert.ToSingle(p.GetValue(obj, null)); return true; } catch { }
            }
        }
        return false;
    }

    public float GetIncomingEffectivenessMultiplier(string monsterId, MonsterDataSO def, int level)
    {
        var titles = GetEquippedList(monsterId, def, level);
        float mul = 1f;
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i] as EffectivenessNullifyTitleSO;
            if (t)
                mul *= Mathf.Max(0f, t.incomingEffectivenessMultiplier);
        }
        return mul;
    }

    // ─────────────────────────────────────────────────────────────────────
    // UI-friendly wrappers
    // ─────────────────────────────────────────────────────────────────────
    public bool AssignTitleToMonster(string monsterId, MonsterDataSO def, int tierIndex, TitleSO choose)
    {
        return Equip(monsterId, def, tierIndex, choose);
    }

    public bool RemoveTitleFromMonster(string monsterId, MonsterDataSO def, int tierIndex)
    {
        return Unequip(monsterId, def, tierIndex);
    }

    public bool ToggleTitleOnMonster(string monsterId, MonsterDataSO def, int tierIndex, TitleSO choose)
    {
        if (string.IsNullOrEmpty(monsterId) || !def || choose == null) return false;
        string current = GetEquippedTitleIdForTier(monsterId, def, tierIndex);
        if (current == choose.titleId) return Unequip(monsterId, def, tierIndex);
        return Equip(monsterId, def, tierIndex, choose);
    }

    public void ClearAllFor(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return;

        var save = TitleSaveStore.GetOrCreateEquip(monsterId);
        if (save == null || save.tierSelections == null || save.tierSelections.Count == 0) return;

        bool changed = false;
        for (int i = 0; i < save.tierSelections.Count; i++)
        {
            if (!string.IsNullOrEmpty(save.tierSelections[i]))
            {
                save.tierSelections[i] = "";
                changed = true;
            }
        }

        if (changed)
        {
            TitleSaveStore.Save();
            RaiseTitleChange();
        }
    }

    // ─────────────────────────────────────────────────────────────────────
    // Events: notify the game that title-driven job math changed
    // ─────────────────────────────────────────────────────────────────────
    private void RaiseTitleChange()
    {
        GameEvents.JobGlobalModsChanged?.Invoke();
        GameEvents.OnJobsChanged?.Invoke();
    }

    private static string NormalizeStatKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return key;
        switch (key.Trim().ToUpperInvariant())
        {
            case "ATK": return "Attack";
            case "DEF": return "Defense";
            case "SPD": return "Speed";
            case "HP": return "HP";
            default: return key;
        }
    }

    // Keep legacy spelling (if other code calls it)
    public float GetcreditMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        float mul = 1f;

        var def = MonsterLibraryLocator.GetById(monsterId);
        int lvl = 1;

        var titles = GetEquippedList(monsterId, def, lvl);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (t is CreditBonusOnVictoryTitleSO credit)
            {
                if (TryReadFloat(credit, out var credVal, "CreditMultiplier", "creditMultiplier", "creditsMultiplier", "rewardcreditMult"))
                {
                    Debug.Log($"[TitleManager] Credit title '{credit.titleId}' multiplier={credVal}");
                    mul *= Mathf.Max(0f, credVal);
                }
                else
                {
                    Debug.Log($"[TitleManager] Credit title '{credit.titleId}' has no readable multiplier field - dumping fields...");
                    DumpTitleFields(credit);
                }
                continue;
            }

            if (TryReadFloat(t, out var v, "CreditMultiplier", "creditMultiplier", "creditsMultiplier", "rewardcreditMult"))
                mul *= Mathf.Max(0f, v);
        }

        return Mathf.Max(0f, mul);
    }

    // Optional nicer alias (won’t break existing callers)
    public float GetCreditMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
        => GetcreditMultOnVictory(monsterId, wild, wildLevel);

    private void DumpTitleFields(TitleSO t)
    {
        if (t == null)
        {
            Debug.Log("[TitleManager] DumpTitleFields: title is null");
            return;
        }

        var ty = t.GetType();
        var fields = ty.GetFields();
        string outStr = $"[TitleManager] Fields for {t.titleId} ({ty.Name}): ";
        for (int i = 0; i < fields.Length; i++)
        {
            try { var val = fields[i].GetValue(t); outStr += $"{fields[i].Name}={val}, "; } catch { outStr += $"{fields[i].Name}=<err>, "; }
        }
        var props = ty.GetProperties();
        if (props != null && props.Length > 0)
        {
            outStr += " | Props: ";
            for (int i = 0; i < props.Length; i++)
            {
                try { var val = props[i].GetValue(t, null); outStr += $"{props[i].Name}={val}, "; } catch { outStr += $"{props[i].Name}=<err>, "; }
            }
        }

        Debug.Log(outStr);
    }

    public float GetGrowthCoreMultOnVictory(string monsterId, MonsterDataSO wild, int wildLevel)
    {
        float mul = 1f;

        var def = MonsterLibraryLocator.GetById(monsterId);
        int lvl = 1;

        var titles = GetEquippedList(monsterId, def, lvl);
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (t is GrowthCoreBonusOnVictoryTitleSO gc)
            {
                mul *= Mathf.Max(0f, gc.growthCoreMultiplier);
                continue;
            }

            if (TryReadFloat(t, out var v, "growthCoreMultiplier", "coreMultiplier", "rewardGrowthMult"))
                mul *= Mathf.Max(0f, v);
        }

        return Mathf.Max(0f, mul);
    }

    // ─────────────────────────────────────────────────────────────────────
    // TitlesAdapter event relays (safe no-ops if unused)
    // ─────────────────────────────────────────────────────────────────────
    public void OnBattleStart(string activeMonsterId, MonsterDataSO wild, int wildLevel)
    {
        // BattleManager currently calls OnBattleStart for BOTH the player combatant
        // and the wild combatant. Previously, each call cleared per-battle state,
        // causing the second call to overwrite the first (and breaking player-side
        // turn-based titles like TurnBooster).
        //
        // Fix: clear per-battle state ONLY once per battle session, then register
        // each combatant as a participant.
        if (!_battleSessionActive)
        {
            _turnStacks.Clear();
            _eventStacks.Clear();
            _eventMax.Clear();
            _eventDecayPerTurn.Clear();
            _flatStartUntilTurn.Clear();
            _flatStartAmountAtk.Clear();
            _flatStartAmountDef.Clear();
            _flatStartAmountSpd.Clear();
            _flatStartAmountHp.Clear();
            _flatStartRemainingTurns.Clear();
            _shieldRemaining.Clear();

            _battleParticipants.Clear();
            _scratchParticipants.Clear();

            _turnIndex = 0;
            _battleSessionActive = true;
        }

        _activeBattleMonsterId = activeMonsterId; // legacy/diagnostics

        if (!string.IsNullOrEmpty(activeMonsterId))
        {
            _battleParticipants.Add(activeMonsterId);
            ApplyBattleStartBonuses(activeMonsterId);
        }
    }

    public void OnBattleEnd(string activeMonsterId, bool victory, MonsterDataSO wild, int wildLevel)
    {
        // BattleManager may call OnBattleEnd for multiple combatants. We only
        // fully clear per-battle state once all registered participants have
        // ended the battle.
        if (!string.IsNullOrEmpty(activeMonsterId))
            _battleParticipants.Remove(activeMonsterId);

        if (_battleParticipants.Count > 0)
        {
            // Keep session alive for remaining combatant(s).
            _activeBattleMonsterId = activeMonsterId;
            return;
        }

        _turnStacks.Clear();
        _eventStacks.Clear();
        _eventMax.Clear();
        _eventDecayPerTurn.Clear();
        _flatStartUntilTurn.Clear();
        _flatStartAmountAtk.Clear();
        _flatStartAmountDef.Clear();
        _flatStartAmountSpd.Clear();
        _flatStartAmountHp.Clear();
        _flatStartRemainingTurns.Clear();
        _shieldRemaining.Clear();

        _battleParticipants.Clear();
        _scratchParticipants.Clear();
        _battleSessionActive = false;

        _activeBattleMonsterId = "";
        _turnIndex = 0;
    }


    public void OnTurnAdvanced(int turnIndex)
    {
        _turnIndex = Mathf.Max(0, turnIndex);

        // decay event stacks (no allocations)
        _scratchEventKeys.Clear();
        foreach (var kv in _eventStacks)
            _scratchEventKeys.Add(kv.Key);

        for (int i = 0; i < _scratchEventKeys.Count; i++)
        {
            var id = _scratchEventKeys[i];
            if (!_eventStacks.TryGetValue(id, out var cur)) continue;

            int decay = _eventDecayPerTurn.TryGetValue(id, out var d) ? d : 0;
            if (decay > 0)
                _eventStacks[id] = Mathf.Max(0, cur - decay);
        }

        // TurnBooster: apply stack growth to ALL battle participants that have a TurnBooster title.
        // This ensures both player and wild combatants can use turn-based abilities.
        if (_battleParticipants.Count <= 0) return;

        // Copy keys defensively (HashSet cannot be safely iterated if mutated by other calls).
        _scratchParticipants.Clear();
        foreach (var id in _battleParticipants)
            _scratchParticipants.Add(id);

        for (int i = 0; i < _scratchParticipants.Count; i++)
        {
            var id = _scratchParticipants[i];
            if (string.IsNullOrEmpty(id)) continue;

            var def = MonsterLibraryLocator.GetById(id);
            int lvl = GetLevelOr1(id);
            var tb = GetFirstTitle<TurnBoosterTitleSO>(id, def, lvl);
            if (tb == null) continue;

            _turnStacks.TryGetValue(id, out int cur);
            int max = Mathf.Max(1, tb.maxStacks);
            int next = Mathf.Min(cur + 1, max);
            _turnStacks[id] = next;

            BattleLogger.LogTitleActivation(
                ownerName: def != null ? def.displayName : id,
                titleName: string.IsNullOrEmpty(tb.displayName) ? tb.titleId : tb.displayName,
                summary: $"+1 stack ({next}/{max})"
            );
        }
    }

    /// owner’s turns (not global rounds).
    /// </summary>
    public void OnCombatantTurnEnded(string combatantId)
    {
        if (string.IsNullOrEmpty(combatantId)) return;

        if (_flatStartRemainingTurns.TryGetValue(combatantId, out int rem) && rem > 0)
        {
            rem = Mathf.Max(0, rem - 1);
            if (rem <= 0)
            {
                _flatStartRemainingTurns.Remove(combatantId);
            }
            else
            {
                _flatStartRemainingTurns[combatantId] = rem;
            }
        }
    }

    /// <summary> Remaining BattleStartShield HP for UI (0 if none). </summary>
    public float GetBattleStartShieldRemaining(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return 0f;
        return _shieldRemaining.TryGetValue(monsterId, out var v) ? Mathf.Max(0f, v) : 0f;
    }



    public void OnAttackLanded(string attackerId, bool wasCrit)
    {
        if (string.IsNullOrEmpty(attackerId)) return;

        var def = MonsterLibraryLocator.GetById(attackerId);
        int lvl = GetLevelOr1(attackerId);
        var es = GetFirstTitle<EventStacksTitleSO>(attackerId, def, lvl);
        if (es == null) return;

        bool trigger = (es.trigger == EventTriggerKind.OnAttack) || (wasCrit && es.trigger == EventTriggerKind.OnCrit);
        if (!trigger) return;

        BumpEventStacks(attackerId, Mathf.Max(1, es.maxStacks), Mathf.Max(0, es.decayPerTurn));
    }

    public void OnHitTaken(string defenderId, int damage, bool wasCrit)
    {
        if (string.IsNullOrEmpty(defenderId)) return;

        // Shield consumption (visual/log only here; actual damage reduction should occur in damage pipeline)
        if (_shieldRemaining.TryGetValue(defenderId, out float shield) && shield > 0f && damage > 0)
        {
            float used = Mathf.Min(shield, damage);
            float next = Mathf.Max(0f, shield - used);
            _shieldRemaining[defenderId] = next;

            var def = MonsterLibraryLocator.GetById(defenderId);
            int lvl = GetLevelOr1(defenderId);
            var shieldTitle = GetFirstTitle<BattleStartShieldTitleSO>(defenderId, def, lvl);
            if (shieldTitle != null)
            {
                BattleLogger.LogTitleActivation(
                    ownerName: def != null ? def.displayName : defenderId,
                    titleName: string.IsNullOrEmpty(shieldTitle.displayName) ? shieldTitle.titleId : shieldTitle.displayName,
                    summary: $"absorbed {Mathf.RoundToInt(used)} (rem {Mathf.RoundToInt(next)})"
                );
            }
        }

        var def2 = MonsterLibraryLocator.GetById(defenderId);
        int lvl2 = GetLevelOr1(defenderId);
        var es = GetFirstTitle<EventStacksTitleSO>(defenderId, def2, lvl2);
        if (es == null) return;

        bool trigger = (es.trigger == EventTriggerKind.OnHitTaken) || (wasCrit && es.trigger == EventTriggerKind.OnCrit);
        if (!trigger) return;

        BumpEventStacks(defenderId, Mathf.Max(1, es.maxStacks), Mathf.Max(0, es.decayPerTurn));
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────
    
    private void ApplyBattleStartBonuses(string id)
    {
        var def = MonsterLibraryLocator.GetById(id);
        int lvl = GetLevelOr1(id);

        // Flat start buff (ATK/DEF/SPD/HP)
        var flat = GetFirstTitle<BattleStartFlatTitleSO>(id, def, lvl);
        if (flat != null)
        {
            int amt = Mathf.Max(0, flat.flatAmount);
            int dur = Mathf.Max(1, flat.durationTurns <= 0 ? 1 : flat.durationTurns);

            // dur=1 => only turnIndex 0. dur=2 => turnIndex 0 and 1.
            _flatStartUntilTurn[id] = _turnIndex + (dur - 1); // legacy round-index marker
            _flatStartRemainingTurns[id] = dur; // owner-turn based duration

            switch (flat.stat)
            {
                case BattleStatKind.ATK: _flatStartAmountAtk[id] = amt; break;
                case BattleStatKind.DEF: _flatStartAmountDef[id] = amt; break;
                case BattleStatKind.SPD: _flatStartAmountSpd[id] = amt; break;
                case BattleStatKind.HP:  _flatStartAmountHp[id]  = amt; break;
            }

            BattleLogger.LogTitleActivation(
                ownerName: def != null ? def.displayName : id,
                titleName: string.IsNullOrEmpty(flat.displayName) ? flat.titleId : flat.displayName,
                summary: $"+{amt} {flat.stat} for {dur} turn(s)"
            );
        }

        var shield = GetFirstTitle<BattleStartShieldTitleSO>(id, def, lvl);
        if (shield != null)
        {
            float maxHP = 1f;

            try
            {
                OwnedMonsterData owned = null;
                var data = SaveManager.Data;
                if (data != null)
                {
                    if (data.team != null)
                    {
                        for (int i = 0; i < data.team.Count; i++)
                        {
                            var om = data.team[i];
                            if (om != null && om.monsterId == id) { owned = om; break; }
                        }
                    }
                    if (owned == null && data.owned != null)
                    {
                        for (int i = 0; i < data.owned.Count; i++)
                        {
                            var om = data.owned[i];
                            if (om != null && om.monsterId == id) { owned = om; break; }
                        }
                    }
                }

                if (owned != null)
                    maxHP = Mathf.Max(1f, ProgressionStatCalc.GetTotalMaxHP(owned));
                else
                    maxHP = Mathf.Max(1f, BattleCalc.CalcHP(def, Mathf.Max(1, lvl)));
            }
            catch
            {
                maxHP = Mathf.Max(1f, BattleCalc.CalcHP(def, Mathf.Max(1, lvl)));
            }

            float shieldHP = Mathf.Max(0f, maxHP * (Mathf.Max(0f, shield.shieldPct) / 100f));
            _shieldRemaining[id] = shieldHP;

            BattleLogger.LogTitleActivation(
                ownerName: def != null ? def.displayName : id,
                titleName: string.IsNullOrEmpty(shield.displayName) ? shield.titleId : shield.displayName,
                summary: $"+Shield {Mathf.RoundToInt(shieldHP)}"
            );
        }
    }


    private void BumpEventStacks(string id, int maxStacks, int decayPerTurn)
    {
        _eventStacks.TryGetValue(id, out int cur);
        int next = Mathf.Min(cur + 1, maxStacks);

        _eventStacks[id] = next;
        _eventMax[id] = maxStacks;
        _eventDecayPerTurn[id] = Mathf.Max(0, decayPerTurn);

        var def = MonsterLibraryLocator.GetById(id);
        int lvl = GetLevelOr1(id);
        var es = GetFirstTitle<EventStacksTitleSO>(id, def, lvl);
        if (es != null && next != cur)
        {
            BattleLogger.LogTitleActivation(
                ownerName: def != null ? def.displayName : id,
                titleName: string.IsNullOrEmpty(es.displayName) ? es.titleId : es.displayName,
                summary: $"+1 stack ({next}/{maxStacks})"
            );
        }
    }

    private static bool MatchesStat(StatKind stat, BattleStatKind bsk)
    {
        return (stat == StatKind.Attack && bsk == BattleStatKind.ATK)
            || (stat == StatKind.Defense && bsk == BattleStatKind.DEF)
            || (stat == StatKind.Speed && bsk == BattleStatKind.SPD)
            || (stat == StatKind.HP && bsk == BattleStatKind.HP);
    }

    private T GetFirstTitle<T>(string monsterId, MonsterDataSO def, int level) where T : TitleSO
    {
        var list = GetEquippedList(monsterId, def, level);
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] is T got) return got;
        }
        return null;
    }

    private static int GetLevelOr1(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId)) return 1;

        var owned = SaveManager.Data?.owned;
        if (owned != null)
        {
            for (int i = 0; i < owned.Count; i++)
            {
                var om = owned[i];
                if (om != null && om.monsterId == monsterId)
                    return Mathf.Max(1, om.level);
            }
        }

        var team = SaveManager.Data?.team;
        if (team != null)
        {
            for (int i = 0; i < team.Count; i++)
            {
                var e = team[i];
                if (e != null && e.monsterId == monsterId)
                    return Mathf.Max(1, e.level);
            }
        }

        return 1;
    }

    private static void TryBattleLog(string msg)
    {
        try { BattleLogger.Log(msg, LogScope.Battle); } catch { }
        Debug.Log(msg);
    }

    private static float ReadHp01(TitleContext ctx)
    {
        try
        {
            var t = ctx.GetType();

            var f = t.GetField("hpPct") ?? t.GetField("hp01") ?? t.GetField("hp") ?? t.GetField("health01");
            if (f != null)
                return Mathf.Clamp01(Convert.ToSingle(f.GetValue(ctx)));

            var p = t.GetProperty("hpPct") ?? t.GetProperty("hp01") ?? t.GetProperty("HP01") ?? t.GetProperty("Health01");
            if (p != null)
                return Mathf.Clamp01(Convert.ToSingle(p.GetValue(ctx, null)));
        }
        catch { }

        return 1f;
    }

    public Sprite TryGetIconByTitleName(string titleName)
    {
        if (string.IsNullOrEmpty(titleName)) return null;

        foreach (var kv in _idToTitle)
        {
            var so = kv.Value;
            if (!so) continue;
            if (string.Equals(so.displayName, titleName, StringComparison.OrdinalIgnoreCase))
                return so.icon;
        }

        if (_idToTitle.TryGetValue(titleName, out var byId) && byId)
            return byId.icon;

        return null;
    }

    // ─────────────────────────────────────────────────────────────────────
    // Battle mods (flat + pct) including conditional + stateful battle titles
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns complete battle stat mods for this combatant, including:
    /// - StatBooster / DuoStatBooster
    /// - ConditionalStatBooster / DuoConditionalStatBooster (evaluated against ctx)
    /// - BattleStartFlat (while remaining turns > 0)
    /// - TurnBooster stacks, EventStacks, ClutchBooster (multipliers)
    /// </summary>
    private TitleStatMods GetBattleStatModsRuntime(string monsterId, MonsterDataSO def, int level, in TitleContext ctx)
    {
        if (string.IsNullOrEmpty(monsterId))
            return default;

        if (!def)
            def = MonsterLibraryLocator.GetById(monsterId);

        if (!def)
            return default;

        // Resolve baseline stats so HP flat can be converted to pct correctly.
        float baseHP, baseATK, baseDEF, baseSPD;

        OwnedMonsterData ownedForBase = null;
        var data = SaveManager.Data;
        if (data != null)
        {
            if (data.team != null)
            {
                for (int i = 0; i < data.team.Count; i++)
                {
                    var om = data.team[i];
                    if (om != null && om.monsterId == monsterId) { ownedForBase = om; break; }
                }
            }
            if (ownedForBase == null && data.owned != null)
            {
                for (int i = 0; i < data.owned.Count; i++)
                {
                    var om = data.owned[i];
                    if (om != null && om.monsterId == monsterId) { ownedForBase = om; break; }
                }
            }
        }

        if (ownedForBase != null)
        {
            var ps = ProgressionStatCalc.Get(ownedForBase);
            baseHP  = Mathf.Max(1f, ps.totalHP);
            baseATK = Mathf.Max(1f, ps.totalATK);
            baseDEF = Mathf.Max(1f, ps.totalDEF);
            baseSPD = Mathf.Max(1f, ps.totalSPD);
        }
        else
        {
            baseHP  = Mathf.Max(1f, BattleCalc.CalcHP(def, level));
            baseATK = Mathf.Max(1f, BattleCalc.CalcBaseAttack(def, level, 0, 0));
            baseDEF = Mathf.Max(1f, BattleCalc.CalcDefense(def, level));
            baseSPD = Mathf.Max(1f, BattleCalc.CalcSpeed(def, level));
        }

        var titles = GetEquippedList(monsterId, def, level);

        int atkFlat = 0, defFlat = 0, spdFlat = 0;
        float hpMult = 1f, atkMult = 1f, defMult = 1f, spdMult = 1f;
        float hpFlatAdd = 0f;

        void ApplyOne(StatKind stat, OpKind op, float value)
        {
            float FactorFromOp()
            {
                if (op == OpKind.Multiply) return value;
                if (op == OpKind.Divide)   return (Mathf.Approximately(value, 0f) ? 1f : 1f / value);
                return 1f;
            }

            switch (stat)
            {
                case StatKind.HP:
                    if (op == OpKind.Add) hpFlatAdd += value;
                    else if (op == OpKind.Subtract) hpFlatAdd -= value;
                    else hpMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;

                case StatKind.Attack:
                    if (op == OpKind.Add) atkFlat += Mathf.RoundToInt(value);
                    else if (op == OpKind.Subtract) atkFlat -= Mathf.RoundToInt(value);
                    else atkMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;

                case StatKind.Defense:
                    if (op == OpKind.Add) defFlat += Mathf.RoundToInt(value);
                    else if (op == OpKind.Subtract) defFlat -= Mathf.RoundToInt(value);
                    else defMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;

                case StatKind.Speed:
                    if (op == OpKind.Add) spdFlat += Mathf.RoundToInt(value);
                    else if (op == OpKind.Subtract) spdFlat -= Mathf.RoundToInt(value);
                    else spdMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;
            }
        }

        // Static + conditional boosters
        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (t is StatBoosterTitleSO sb)
            {
                ApplyOne(sb.stat, sb.operation, sb.value);
            }
            else if (t is ConditionalStatBoosterTitleSO cb)
            {
                bool ok = TitleUtility.CheckCondition(cb, ctx);
                if (debugEffectiveness)
                    Debug.Log($"TitleManager: Conditional check for {cb.titleId} on {monsterId} -> {ok} (hp01={ctx.selfHp01:F2}, allies={ctx.alliesAlive}, winStreak={ctx.winStreak})");
                if (ok) ApplyOne(cb.stat, cb.operation, cb.value);
            }
            else if (t is DuoStatBoosterTitleSO duo && duo.enabled)
            {
                ApplyOne(duo.statA, duo.opA, duo.valueA);
                ApplyOne(duo.statB, duo.opB, duo.valueB);
            }
            else if (t is DuoConditionalStatBoosterTitleSO dcb)
            {
                bool ok = TitleUtility.CheckCondition(dcb.condition, dcb.threshold01, dcb.countN, in ctx);
                if (debugEffectiveness)
                    Debug.Log($"TitleManager: DuoConditional check for {dcb.titleId} on {monsterId} -> {ok} (hp01={ctx.selfHp01:F2}, allies={ctx.alliesAlive}, winStreak={ctx.winStreak})");
                if (ok)
                {
                    ApplyOne(dcb.statA, dcb.opA, dcb.valueA);
                    ApplyOne(dcb.statB, dcb.opB, dcb.valueB);
                }
            }
        }

        // BattleStartFlat (owner-turn ticking)
        if (_flatStartRemainingTurns.TryGetValue(ctx.ownedId, out int remTurns) && remTurns > 0)
        {
            if (_flatStartAmountAtk.TryGetValue(ctx.ownedId, out int fAtk)) atkFlat += fAtk;
            if (_flatStartAmountDef.TryGetValue(ctx.ownedId, out int fDef)) defFlat += fDef;
            if (_flatStartAmountSpd.TryGetValue(ctx.ownedId, out int fSpd)) spdFlat += fSpd;
            if (_flatStartAmountHp.TryGetValue(ctx.ownedId, out int fHp)) hpFlatAdd += fHp;
        }

        // TurnBooster (percent per stack)
        var tb = GetFirstTitle<TurnBoosterTitleSO>(ctx.ownedId, def, level);
        if (tb != null && _turnStacks.TryGetValue(ctx.ownedId, out int tStacks) && tStacks > 0)
        {
            float pct = Mathf.Max(0f, tb.percentPerTurn) / 100f;
            int stacks = Mathf.Min(tStacks, Mathf.Max(1, tb.maxStacks));
            float factor = 1f + pct * stacks;

            if (MatchesStat(StatKind.HP, tb.stat))  hpMult  *= factor;
            if (MatchesStat(StatKind.Attack, tb.stat)) atkMult *= factor;
            if (MatchesStat(StatKind.Defense, tb.stat)) defMult *= factor;
            if (MatchesStat(StatKind.Speed, tb.stat)) spdMult *= factor;
        }

        // EventStacks (percent per stack)
        var es = GetFirstTitle<EventStacksTitleSO>(monsterId, def, level);
        if (es != null && _eventStacks.TryGetValue(monsterId, out int eStacks) && eStacks > 0)
        {
            float pct = Mathf.Max(0f, es.percentPerStack) / 100f;
            int stacks = Mathf.Min(eStacks, Mathf.Max(1, es.maxStacks));
            float factor = 1f + pct * stacks;

            if (MatchesStat(StatKind.HP, es.stat))  hpMult  *= factor;
            if (MatchesStat(StatKind.Attack, es.stat)) atkMult *= factor;
            if (MatchesStat(StatKind.Defense, es.stat)) defMult *= factor;
            if (MatchesStat(StatKind.Speed, es.stat)) spdMult *= factor;
        }

        // ClutchBooster (hp threshold)
        var clutch = GetFirstTitle<ClutchBoosterTitleSO>(monsterId, def, level);
        float hp01 = ReadHp01(ctx);
        if (clutch != null && hp01 <= Mathf.Clamp01(clutch.hpBelowThreshold01))
        {
            if (clutch.atkPct > 0f) atkMult *= 1f + (clutch.atkPct / 100f);
            if (clutch.defPct > 0f) defMult *= 1f + (clutch.defPct / 100f);
            if (clutch.spdPct > 0f) spdMult *= 1f + (clutch.spdPct / 100f);
        }

        // Convert HP flat into pct factor relative to baseline HP.
        if (!Mathf.Approximately(hpFlatAdd, 0f))
        {
            float hpFactorFromFlat = Mathf.Max(0.01f, (baseHP + hpFlatAdd) / baseHP);
            hpMult *= hpFactorFromFlat;
        }

        TitleStatMods mods = default;
        mods.atkFlat = atkFlat;
        mods.defFlat = defFlat;
        mods.spdFlat = spdFlat;

        mods.hpPct  = hpMult  - 1f;
        mods.atkPct = atkMult - 1f;
        mods.defPct = defMult - 1f;
        mods.spdPct = spdMult - 1f;

        return mods;
    }

    /// <summary>
    /// Adapter hook: returns ONLY conditional boosters as a TitleStatMods block.
    /// </summary>
    public TitleStatMods GetConditionalBattleMods(TitleContext ctx)
    {
        if (string.IsNullOrEmpty(ctx.ownedId))
            return default;

        var def = MonsterLibraryLocator.GetById(ctx.ownedId);
        int lvl = GetLevelOr1(ctx.ownedId);
        var titles = GetEquippedList(ctx.ownedId, def, lvl);

        int atkFlat = 0, defFlat = 0, spdFlat = 0;
        float hpMult = 1f, atkMult = 1f, defMult = 1f, spdMult = 1f;
        float hpFlatAdd = 0f;

        void ApplyOne(StatKind stat, OpKind op, float value)
        {
            float FactorFromOp()
            {
                if (op == OpKind.Multiply) return value;
                if (op == OpKind.Divide)   return (Mathf.Approximately(value, 0f) ? 1f : 1f / value);
                return 1f;
            }

            switch (stat)
            {
                case StatKind.HP:
                    if (op == OpKind.Add) hpFlatAdd += value;
                    else if (op == OpKind.Subtract) hpFlatAdd -= value;
                    else hpMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;
                case StatKind.Attack:
                    if (op == OpKind.Add) atkFlat += Mathf.RoundToInt(value);
                    else if (op == OpKind.Subtract) atkFlat -= Mathf.RoundToInt(value);
                    else atkMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;
                case StatKind.Defense:
                    if (op == OpKind.Add) defFlat += Mathf.RoundToInt(value);
                    else if (op == OpKind.Subtract) defFlat -= Mathf.RoundToInt(value);
                    else defMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;
                case StatKind.Speed:
                    if (op == OpKind.Add) spdFlat += Mathf.RoundToInt(value);
                    else if (op == OpKind.Subtract) spdFlat -= Mathf.RoundToInt(value);
                    else spdMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;
            }
        }

        float baseHP = Mathf.Max(1f, BattleCalc.CalcHP(def, lvl));

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (t is ConditionalStatBoosterTitleSO cb)
            {
                if (TitleUtility.CheckCondition(cb, ctx))
                    ApplyOne(cb.stat, cb.operation, cb.value);
            }
            else if (t is DuoConditionalStatBoosterTitleSO dcb)
            {
                if (TitleUtility.CheckCondition(dcb.condition, dcb.threshold01, dcb.countN, in ctx))
                {
                    ApplyOne(dcb.statA, dcb.opA, dcb.valueA);
                    ApplyOne(dcb.statB, dcb.opB, dcb.valueB);
                }
            }
        }

        // BattleStartFlat (owner-turn ticking)
        if (_flatStartRemainingTurns.TryGetValue(ctx.ownedId, out int remTurns) && remTurns > 0)
        {
            if (_flatStartAmountAtk.TryGetValue(ctx.ownedId, out int fAtk)) atkFlat += fAtk;
            if (_flatStartAmountDef.TryGetValue(ctx.ownedId, out int fDef)) defFlat += fDef;
            if (_flatStartAmountSpd.TryGetValue(ctx.ownedId, out int fSpd)) spdFlat += fSpd;
            if (_flatStartAmountHp.TryGetValue(ctx.ownedId, out int fHp)) hpFlatAdd += fHp;
        }

        if (!Mathf.Approximately(hpFlatAdd, 0f))
        {
            float hpFactorFromFlat = Mathf.Max(0.01f, (baseHP + hpFlatAdd) / baseHP);
            hpMult *= hpFactorFromFlat;
        }

        TitleStatMods mods = default;
        mods.atkFlat = atkFlat;
        mods.defFlat = defFlat;
        mods.spdFlat = spdFlat;
        mods.hpPct  = hpMult  - 1f;
        mods.atkPct = atkMult - 1f;
        mods.defPct = defMult - 1f;
        mods.spdPct = spdMult - 1f;
        return mods;
    }

    public TitleStatMods GetBattleStatMods(string monsterId)
    {
        if (string.IsNullOrEmpty(monsterId))
            return default;

        var def = MonsterLibraryLocator.GetById(monsterId);
        if (!def)
            return default;

        int level = 1;
        var data = SaveManager.Data;
        if (data != null)
        {
            OwnedMonsterData found = null;

            if (data.team != null)
                found = data.team.Find(m => m != null && m.monsterId == monsterId);

            if (found == null && data.owned != null)
                found = data.owned.Find(m => m != null && m.monsterId == monsterId);

            if (found != null)
                level = Mathf.Max(1, found.level);
        }

        var titles = GetEquippedList(monsterId, def, level);

        
    float baseHP;
    float baseATK;
    float baseDEF;
    float baseSPD;

    OwnedMonsterData ownedForBase = null;
    var data2 = SaveManager.Data;
    if (data2 != null)
    {
        if (data2.team != null)
        {
            for (int i = 0; i < data2.team.Count; i++)
            {
                var om = data2.team[i];
                if (om != null && om.monsterId == monsterId) { ownedForBase = om; break; }
            }
        }
        if (ownedForBase == null && data2.owned != null)
        {
            for (int i = 0; i < data2.owned.Count; i++)
            {
                var om = data2.owned[i];
                if (om != null && om.monsterId == monsterId) { ownedForBase = om; break; }
            }
        }
    }

    if (ownedForBase != null)
    {
        var ps = ProgressionStatCalc.Get(ownedForBase);
        baseHP  = Mathf.Max(1f, ps.totalHP);
        baseATK = Mathf.Max(1f, ps.totalATK);
        baseDEF = Mathf.Max(1f, ps.totalDEF);
        baseSPD = Mathf.Max(1f, ps.totalSPD);
    }
    else
    {
        baseHP  = Mathf.Max(1f, BattleCalc.CalcHP(def, level));
        baseATK = Mathf.Max(1f, BattleCalc.CalcBaseAttack(def, level, 0, 0));
        baseDEF = Mathf.Max(1f, BattleCalc.CalcDefense(def, level));
        baseSPD = Mathf.Max(1f, BattleCalc.CalcSpeed(def, level));
    }

        int atkFlat = 0;
        int defFlat = 0;
        int spdFlat = 0;

        float hpMult = 1f;
        float atkMult = 1f;
        float defMult = 1f;
        float spdMult = 1f;

        float hpFlatAdd = 0f;

        void ApplyOne(StatKind stat, OpKind op, float value)
        {
            float FactorFromOp()
            {
                if (op == OpKind.Multiply) return value;
                if (op == OpKind.Divide)   return (Mathf.Approximately(value, 0f) ? 1f : 1f / value);
                return 1f;
            }

            switch (stat)
            {
                case StatKind.HP:
                {
                    if (op == OpKind.Add) hpFlatAdd += value;
                    else if (op == OpKind.Subtract) hpFlatAdd -= value;
                    else hpMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;
                }

                case StatKind.Attack:
                {
                    if (op == OpKind.Add) atkFlat += Mathf.RoundToInt(value);
                    else if (op == OpKind.Subtract) atkFlat -= Mathf.RoundToInt(value);
                    else atkMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;
                }

                case StatKind.Defense:
                {
                    if (op == OpKind.Add) defFlat += Mathf.RoundToInt(value);
                    else if (op == OpKind.Subtract) defFlat -= Mathf.RoundToInt(value);
                    else defMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;
                }

                case StatKind.Speed:
                {
                    if (op == OpKind.Add) spdFlat += Mathf.RoundToInt(value);
                    else if (op == OpKind.Subtract) spdFlat -= Mathf.RoundToInt(value);
                    else spdMult *= Mathf.Max(0.01f, FactorFromOp());
                    break;
                }
            }
        }

        for (int i = 0; i < titles.Count; i++)
        {
            var t = titles[i];
            if (!t) continue;

            if (t is StatBoosterTitleSO sb)
            {
                ApplyOne(sb.stat, sb.operation, sb.value);
            }
            else if (t is DuoStatBoosterTitleSO duo && duo.enabled)
            {
                ApplyOne(duo.statA, duo.opA, duo.valueA);
                ApplyOne(duo.statB, duo.opB, duo.valueB);
            }

            // Intentionally ignored here:
            // - ConditionalStatBoosterTitleSO / DuoConditionalStatBoosterTitleSO (requires ctx)
            // - BattleStart/Turn/Event stack titles (stateful, applied elsewhere)
            // - Damage filters / effectiveness mods (handled via existing adapter calls)
        }

        if (!Mathf.Approximately(hpFlatAdd, 0f))
        {
            float hpFactorFromFlat = Mathf.Max(0.01f, (baseHP + hpFlatAdd) / baseHP);
            hpMult *= hpFactorFromFlat;
        }

        TitleStatMods mods = default;

        mods.atkFlat = atkFlat;
        mods.defFlat = defFlat;
        mods.spdFlat = spdFlat;

        mods.hpPct  = hpMult  - 1f;
        mods.atkPct = atkMult - 1f;
        mods.defPct = defMult - 1f;
        mods.spdPct = spdMult - 1f;

        return mods;
    }

    public void OnMonsterLeveled(string monsterId, int newLevel) { }
    public void OnMonsterCaptured(string monsterId, MonsterType type, int level, bool isShiny) { }
    public void OnMonsterEvolved(string newMonsterId) { }


    // ─────────────────────────────────────────────────────────────────────
    // Debug / UI helpers (used by TitlesAdapter in dev builds)
    // ─────────────────────────────────────────────────────────────────────
    public string ActiveBattleMonsterId => _activeBattleMonsterId;
    public int CurrentTurnIndex => _turnIndex;

    /// <summary>
    /// Returns current TurnBooster stacks for a combatant id (0 if none).
    /// </summary>
    public int Debug_GetTurnBoosterStacks(string combatantId)
    {
        if (string.IsNullOrEmpty(combatantId)) return 0;
        return _turnStacks.TryGetValue(combatantId, out int v) ? v : 0;
    }

}