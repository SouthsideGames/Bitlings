using UnityEngine;
using System.Collections.Generic;


[CreateAssetMenu(menuName = "Data/Localization/Blinder Message Library", fileName = "BlinderMessageLibrary")]
public class BlinderMessageLibrarySO : ScriptableObject
{
    [Tooltip("If no pack matches the requested language, this will be used.")]
    public BlinderMessagePackSO defaultPack;

    [Tooltip("Add one pack per language you support.")]
    public List<BlinderMessagePackSO> packs = new();

    // Optional: small lookup cache (rebuilt on validate)
    [SerializeField, HideInInspector] private Dictionary<string, BlinderMessagePackSO> _byCode;

    void OnValidate()
    {
        RebuildCache();
    }

    void OnEnable()
    {
        if (_byCode == null || _byCode.Count == 0) RebuildCache();
    }

    void RebuildCache()
    {
        if (_byCode == null) _byCode = new Dictionary<string, BlinderMessagePackSO>();
        _byCode.Clear();

        if (packs != null)
        {
            for (int i = 0; i < packs.Count; i++)
            {
                var p = packs[i];
                if (!p) continue;
                var code = NormalizeLangCode(p.languageCode);
                if (string.IsNullOrEmpty(code)) continue;
                _byCode[code] = p;
            }
        }

        // Ensure defaultPack is also reachable via its code
        if (defaultPack)
        {
            var code = NormalizeLangCode(defaultPack.languageCode);
            if (!string.IsNullOrEmpty(code))
                _byCode[code] = defaultPack;
        }
    }

    /// <summary>
    /// Resolves a pack by language code. Returns defaultPack if no direct match.
    /// </summary>
    public BlinderMessagePackSO ResolvePack(string langCodeLower)
    {
        var code = NormalizeLangCode(langCodeLower);
        if (!string.IsNullOrEmpty(code) && _byCode != null && _byCode.TryGetValue(code, out var hit) && hit)
            return hit;

        if (defaultPack) return defaultPack;
        // Fallback to first available pack if default not assigned
        if (packs != null && packs.Count > 0) return packs[0];
        return null;
    }

    /// <summary>
    /// Resolves a pack using Application.systemLanguage (mapped to simple ISO-ish code).
    /// </summary>
    public BlinderMessagePackSO ResolveForSystemLanguage()
    {
        var sys = Application.systemLanguage.ToString();
        return ResolvePack(sys);
    }

    /// <summary>
    /// Picks a weighted random line from the resolved pack.
    /// Avoids repeating lastLine when possible (one reroll).
    /// Returns hardFallback if no entry available.
    /// </summary>
    public string GetWeightedRandomLine(string langCode, string hardFallback = "I WONDER WHAT WE WILL ENCOUNTER", string lastLine = null)
    {
        var pack = ResolvePack(langCode);
        return GetWeightedRandomLine(pack, hardFallback, lastLine);
    }

    /// <summary>
    /// Picks a weighted random line from a specific pack.
    /// Avoids repeating lastLine when possible (one reroll).
    /// Returns hardFallback if no entry available.
    /// </summary>
    public string GetWeightedRandomLine(BlinderMessagePackSO pack, string hardFallback = "I WONDER WHAT WE WILL ENCOUNTER", string lastLine = null)
    {
        if (!pack || pack.entries == null || pack.entries.Count == 0)
            return hardFallback;

        // total weight and validity
        float total = 0f;
        int validCount = 0;
        for (int i = 0; i < pack.entries.Count; i++)
        {
            var e = pack.entries[i];
            if (string.IsNullOrWhiteSpace(e.line)) continue;
            if (e.weight <= 0f) continue;
            total += e.weight;
            validCount++;
        }

        if (validCount == 0 || total <= 0f)
            return pack.GetAnyNonEmptyFallback(hardFallback);

        string PickOnce()
        {
            float r = Random.value * total;
            float acc = 0f;
            for (int i = 0; i < pack.entries.Count; i++)
            {
                var e = pack.entries[i];
                if (string.IsNullOrWhiteSpace(e.line) || e.weight <= 0f) continue;

                acc += e.weight;
                if (r <= acc) return e.line;
            }
            // Edge case: float precision → fallback to any non-empty
            return pack.GetAnyNonEmptyFallback(hardFallback);
        }

        var chosen = PickOnce();

        // Avoid immediate repeat if we have more than one valid option
        if (validCount > 1 && !string.IsNullOrEmpty(lastLine) && chosen == lastLine)
        {
            var reroll = PickOnce();
            if (!string.IsNullOrEmpty(reroll)) chosen = reroll;
        }

        return string.IsNullOrWhiteSpace(chosen) ? pack.GetAnyNonEmptyFallback(hardFallback) : chosen;
    }

    /// <summary>
    /// Maps various strings (including "English", "en-US") to a simple lowercase code (e.g., "en").
    /// </summary>
    public static string NormalizeLangCode(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;

        string s = raw.Trim().ToLowerInvariant();

        // Already looks like a short code with region → strip region (en-us -> en)
        int dash = s.IndexOf('-');
        if (dash > 0) s = s.Substring(0, dash);

        // Common names coming from SystemLanguage.ToString()
        if (s.StartsWith("english"))    return "en";
        if (s.StartsWith("spanish"))    return "es";
        if (s.StartsWith("french"))     return "fr";
        if (s.StartsWith("german"))     return "de";
        if (s.StartsWith("italian"))    return "it";
        if (s.StartsWith("portuguese")) return "pt";
        if (s.StartsWith("russian"))    return "ru";
        if (s.StartsWith("japanese"))   return "ja";
        if (s.StartsWith("korean"))     return "ko";
        if (s.StartsWith("chinese"))    return "zh";
        if (s.StartsWith("turkish"))    return "tr";
        if (s.StartsWith("polish"))     return "pl";
        if (s.StartsWith("dutch"))      return "nl";
        if (s.StartsWith("arabic"))     return "ar";
        if (s.StartsWith("hindi"))      return "hi";

        // If it's already a 2-letter code, keep it
        if (s.Length == 2) return s;

        // Fallback: return first 2 letters to try a best-effort match
        if (s.Length > 2) return s.Substring(0, 2);

        return s;
    }
}
