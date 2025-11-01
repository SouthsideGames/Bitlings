// Assets/Scripts/Titles/TitleContext.cs
using System;
using UnityEngine;

/// <summary>
/// Context passed to the title system when evaluating stat changes.
/// The key addition is <see cref="isBattle"/> which lets you gate
/// conditional/battle-only effects so they never leak into menus.
/// </summary>
[Serializable]
public struct TitleContext
{
    /// <summary>Owned monster unique id (if applicable). Optional.</summary>
    public string ownedId;

    /// <summary>Self HP in 0..1 (current / max). Use 0 if unknown.</summary>
    [Range(0f, 1f)] public float selfHp01;

    /// <summary>How many allies are alive (excluding self) for conditional checks.</summary>
    public int alliesAlive;

    /// <summary>Current encounter win streak (0 if none/unknown).</summary>
    public int winStreak;

    /// <summary>
    /// True only during battle turns. TitleManager/Titles should check this
    /// to ensure conditional boosts (like win streak, HP thresholds, allies alive)
    /// apply only in battle and never affect out-of-battle UI/storage.
    /// </summary>
    public bool isBattle;

    // ─────────────────────────────────────────────────────────────────────────────
    // Factories
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Convenient "no battle" context for menus/collection screens.</summary>
    public static TitleContext Empty => new TitleContext
    {
        ownedId     = "",
        selfHp01    = 0f,
        alliesAlive = 0,
        winStreak   = 0,
        isBattle    = false
    };

    /// <summary>
    /// Minimal constructor (kept for backward compatibility with older calls that
    /// passed just (hpPct, allies, streak) without an ownedId). Assumes battle context.
    /// </summary>
    public TitleContext(float hpPct, int alliesAlive, int winStreak)
    {
        this.ownedId     = "";
        this.selfHp01    = Mathf.Clamp01(hpPct);
        this.alliesAlive = Mathf.Max(0, alliesAlive);
        this.winStreak   = Mathf.Max(0, winStreak);
        this.isBattle    = true;
    }

    /// <summary>
    /// Back-compat convenience used in some adapters:
    /// TitleContext ctx = new TitleContext(ownedId, hpPct, alliesAlive, winStreak);
    /// Sets isBattle=true by default (battle evaluation).
    /// </summary>
    public TitleContext(string ownedId, float hpPct, int alliesAlive, int winStreak)
    {
        this.ownedId     = ownedId ?? "";
        this.selfHp01    = Mathf.Clamp01(hpPct);
        this.alliesAlive = Mathf.Max(0, alliesAlive);
        this.winStreak   = Mathf.Max(0, winStreak);
        this.isBattle    = true;
    }

    /// <summary>
    /// Full constructor allowing explicit control of the battle flag.
    /// </summary>
    public TitleContext(string ownedId, float hpPct, int alliesAlive, int winStreak, bool isBattle)
    {
        this.ownedId     = ownedId ?? "";
        this.selfHp01    = Mathf.Clamp01(hpPct);
        this.alliesAlive = Mathf.Max(0, alliesAlive);
        this.winStreak   = Mathf.Max(0, winStreak);
        this.isBattle    = isBattle;
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Returns a copy with isBattle forced to true.</summary>
    public TitleContext AsBattle()
    {
        var c = this;
        c.isBattle = true;
        return c;
    }

    /// <summary>Returns a copy with isBattle forced to false (menu/simulation).</summary>
    public TitleContext AsMenu()
    {
        var c = this;
        c.isBattle = false;
        return c;
    }
}