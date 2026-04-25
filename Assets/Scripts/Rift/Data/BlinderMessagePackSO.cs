using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(menuName = "Data/Localization/Blinder Message Pack", fileName = "BlinderMessages_en")]
public sealed class BlinderMessagePackSO : ScriptableObject
{
    [Tooltip("ISO-ish code (e.g., en, es, fr, pt-BR). Keep lowercase for consistency.")]
    public string languageCode = "en";

    [Tooltip("Weighted lines for this language.")]
    public List<BlinderMessageEntry> entries = new();

    public string GetAnyNonEmptyFallback(string hardFallback = "I WONDER WHAT WE WILL RIFT")
    {
        for (int i = 0; i < entries.Count; i++)
            if (!string.IsNullOrWhiteSpace(entries[i].line))
                return entries[i].line;
        return hardFallback;
    }
}
