#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class RenameBitlingsUtility
{
    // ---------------- Types ----------------

    public readonly struct MonsterKey
    {
        public readonly MonsterType type;
        public readonly Rarity rarity;
        public readonly string displayName;

        public MonsterKey(MonsterType type, Rarity rarity, string displayName)
        {
            this.type = type;
            this.rarity = rarity;
            this.displayName = displayName;
        }

        public override string ToString() => $"{type}|{rarity}|{displayName}";
    }

    // ---------------- Naming Rules ----------------

    public static readonly string[] RequiredSuffixes =
    {
        "_front",
        "_back",
        "_frontshiny",
        "_backshiny"
    };

    private static readonly Dictionary<string, string> LegacyBaseToSuffix =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "front_nobg",       "_front" },
            { "back_nobg",        "_back" },
            { "front_shiny_nobg", "_frontshiny" },
            { "back_shiny_nobg",  "_backshiny" },
        };

    // ---------------- Report ----------------

    public sealed class Report
    {
        public readonly List<string> Missing = new();
        public readonly List<string> Collisions = new();
        public readonly List<string> Skipped = new();
        public readonly List<string> Info = new();

        public bool HasIssues => Missing.Count > 0 || Collisions.Count > 0;

        public string ToSummaryString(int max = 60)
        {
            var sb = new StringBuilder();

            void Dump(string title, List<string> list)
            {
                if (list.Count == 0) return;
                sb.AppendLine($"{title} ({list.Count})");
                foreach (var l in list.Take(max))
                    sb.AppendLine("  - " + l);
                if (list.Count > max)
                    sb.AppendLine($"  ... +{list.Count - max} more");
                sb.AppendLine();
            }

            Dump("Missing", Missing);
            Dump("Collisions", Collisions);
            Dump("Skipped", Skipped);
            Dump("Info", Info);

            return sb.ToString();
        }
    }

    // ---------------- Public Pipeline ----------------
    // NOTE: This pipeline ONLY renames files IN PLACE (no moving).
    // The "fixFolderNamesAndMigrateToFlat" parameter is accepted for compatibility but intentionally ignored.

    public static Report RunPreImportPipeline(
        string targetRootArtMonsters,
        IEnumerable<(MonsterType type, Rarity rarity, string displayName)> monsters,
        bool fixFolderNamesAndMigrateToFlat, // intentionally ignored (no moves)
        bool renameFilesToConvention,
        bool validateRequiredSprites,
        bool dryRun,
        bool logVerbose
    )
    {
        var rep = new Report();

        if (!AssetDatabase.IsValidFolder(targetRootArtMonsters))
        {
            rep.Missing.Add($"Invalid art root: {targetRootArtMonsters}");
            return rep;
        }

        foreach (var m in monsters)
        {
            string token = NormalizeMonsterToken(m.displayName);
            if (string.IsNullOrWhiteSpace(token))
            {
                rep.Skipped.Add($"Invalid token: '{m.displayName}'");
                continue;
            }

            string typeFolder = m.type == MonsterType.None ? "Unsorted" : m.type.ToString();
            string rarityFolder = m.rarity.ToString();
            string folder = $"{targetRootArtMonsters}/{typeFolder}/{rarityFolder}";

            if (!AssetDatabase.IsValidFolder(folder))
            {
                rep.Missing.Add($"Missing folder: {folder}");
                continue;
            }

            // ---- Rename legacy files IN PLACE ONLY ----
            if (renameFilesToConvention)
            {
                foreach (var kv in LegacyBaseToSuffix)
                {
                    string legacyBase = kv.Key;
                    string suffix = kv.Value;
                    string desiredBase = token + suffix;

                    // If strict target already exists, do not overwrite.
                    if (FindTextureByExactBaseName(folder, desiredBase) != null ||
                        LoadSpriteByExactName(folder, desiredBase) != null)
                        continue;

                    // Find the legacy file inside THIS SAME folder.
                    string legacyPath = FindTextureByExactBaseName(folder, legacyBase);
                    if (string.IsNullOrWhiteSpace(legacyPath))
                        continue;

                    if (dryRun)
                    {
                        rep.Info.Add($"[DryRun] Rename in place: {legacyBase} -> {desiredBase} ({folder})");
                        continue;
                    }

                    string err = AssetDatabase.RenameAsset(legacyPath, desiredBase);
                    if (!string.IsNullOrWhiteSpace(err))
                        rep.Collisions.Add($"Rename failed: {legacyPath} -> {desiredBase}. Error: {err}");
                    else if (logVerbose)
                        Debug.Log($"[RenameBitlings] Renamed in place: {legacyBase} -> {desiredBase} ({folder})");
                }

                if (!dryRun)
                {
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                }
            }

            // ---- Validation ----
            if (validateRequiredSprites)
            {
                foreach (var suffix in RequiredSuffixes)
                {
                    string expected = token + suffix;
                    if (LoadSpriteByExactName(folder, expected) == null)
                        rep.Missing.Add($"Missing '{expected}' in {folder} (Monster='{m.displayName}')");
                }
            }
        }

        return rep;
    }

    // ---------------- Scanner (restored) ----------------
    // If no CSV is provided, this tries to infer monsters by scanning:
    // Assets/Art/Monsters/<Type>/<Rarity>/
    // It detects any Sprite ending with "_front" and uses the prefix as the token/displayName.

    public static List<(MonsterType type, Rarity rarity, string displayName)> ScanMonstersFromArtRoot(string artMonstersRoot)
    {
        var list = new List<(MonsterType type, Rarity rarity, string displayName)>();

        if (string.IsNullOrWhiteSpace(artMonstersRoot) || !AssetDatabase.IsValidFolder(artMonstersRoot))
            return list;

        var typeFolders = AssetDatabase.GetSubFolders(artMonstersRoot);
        foreach (var tf in typeFolders)
        {
            string typeName = Path.GetFileName(tf);
            if (!Enum.TryParse(typeName, true, out MonsterType type))
                type = MonsterType.None;

            var rarityFolders = AssetDatabase.GetSubFolders(tf);
            foreach (var rf in rarityFolders)
            {
                string rarityName = Path.GetFileName(rf);
                if (!Enum.TryParse(rarityName, true, out Rarity rarity))
                    rarity = Rarity.Common;

                // Primary: strict sprites
                string[] spriteGuids = AssetDatabase.FindAssets("t:Sprite", new[] { rf });
                foreach (var g in spriteGuids)
                {
                    string p = AssetDatabase.GUIDToAssetPath(g);
                    var s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
                    if (s == null) continue;

                    if (!s.name.EndsWith("_front", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string token = s.name.Substring(0, s.name.Length - "_front".Length).Trim();
                    if (string.IsNullOrWhiteSpace(token)) continue;

                    list.Add((type, rarity, token));
                }

                // Fallback: legacy textures (in case sprites haven't been created yet)
                // If front_nobg exists, treat folder as having a monster; but we need a token.
                // In this situation we cannot infer the token reliably, so we do NOT add entries.
                // (This keeps the tool safe and avoids creating nonsense tokens.)
            }
        }

        // De-dupe
        return list
            .GroupBy(x => $"{x.type}|{x.rarity}|{x.displayName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    // ---------------- Asset Helpers ----------------

    private static string FindTextureByExactBaseName(string folder, string baseName)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return null;

        string[] guids = AssetDatabase.FindAssets($"t:Texture2D {baseName}", new[] { folder });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrWhiteSpace(p)) continue;

            if (Path.GetFileNameWithoutExtension(p)
                .Equals(baseName, StringComparison.OrdinalIgnoreCase))
                return p;
        }
        return null;
    }

    private static Sprite LoadSpriteByExactName(string folder, string name)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return null;

        string[] guids = AssetDatabase.FindAssets($"t:Sprite {name}", new[] { folder });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            var s = AssetDatabase.LoadAssetAtPath<Sprite>(p);
            if (s != null && s.name.Equals(name, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return null;
    }

    // ---------------- Naming ----------------

    public static string NormalizeMonsterToken(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "";

        foreach (char c in Path.GetInvalidFileNameChars())
            displayName = displayName.Replace(c.ToString(), "");

        string token = string.Concat(
            displayName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        if (token.Length == 0) return "";

        return char.ToUpperInvariant(token[0]) + token.Substring(1);
    }

    // ---------------- CSV Support ----------------
    // NOTE: This is a minimal reader intended only for Name/Type/Rarity.
    // It does not support quoted commas fully; use your MonsterBuilder CSV parsing for complex cases.

    public static bool TryReadCsvMonsters(
        UnityEngine.Object csvAsset,
        out List<(MonsterType type, Rarity rarity, string displayName)> monsters,
        out string error
    )
    {
        monsters = new();
        error = null;

        if (csvAsset == null)
        {
            error = "CSV asset is null.";
            return false;
        }

        string text;

        if (csvAsset is TextAsset ta)
        {
            text = ta.text;
        }
        else
        {
            string assetPath = AssetDatabase.GetAssetPath(csvAsset);
            if (string.IsNullOrWhiteSpace(assetPath))
            {
                error = "Could not resolve CSV asset path.";
                return false;
            }

            string abs = Path.Combine(
                Directory.GetParent(Application.dataPath)!.FullName,
                assetPath);

            if (!File.Exists(abs))
            {
                error = $"CSV file not found: {abs}";
                return false;
            }

            text = File.ReadAllText(abs, Encoding.UTF8);
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            error = "CSV content is empty.";
            return false;
        }

        var lines = text.Split('\n');
        if (lines.Length < 2)
        {
            error = "CSV contains no data rows.";
            return false;
        }

        string[] headers = lines[0].TrimEnd('\r').Split(',');
        int nameCol = Array.FindIndex(headers, h =>
            h.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("Display Name", StringComparison.OrdinalIgnoreCase));
        int typeCol = Array.FindIndex(headers, h =>
            h.Equals("Type", StringComparison.OrdinalIgnoreCase));
        int rarityCol = Array.FindIndex(headers, h =>
            h.Equals("Rarity", StringComparison.OrdinalIgnoreCase));

        if (nameCol < 0 || typeCol < 0 || rarityCol < 0)
        {
            error = "CSV must include Name (or Display Name), Type, and Rarity columns.";
            return false;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            var cells = line.Split(',');
            if (cells.Length <= Math.Max(nameCol, Math.Max(typeCol, rarityCol)))
                continue;

            string name = cells[nameCol].Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            MonsterType t = MonsterType.None;
            Enum.TryParse(cells[typeCol].Trim(), true, out t);

            Rarity r = Rarity.Common;
            Enum.TryParse(cells[rarityCol].Trim(), true, out r);

            monsters.Add((t, r, name));
        }

        return true;
    }
}
#endif
