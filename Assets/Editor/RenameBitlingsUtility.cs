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
    // ---------------- Naming Rules ----------------

    // REQUIRED in the sense that the validator expects all 4 to exist.
    // If you later decide shiny should be optional, we can make that a toggle.
    public static readonly string[] RequiredSuffixes =
    {
        "_front",
        "_back",
        "_frontshiny",
        "_backshiny"
    };

    // Legacy file base name (no token) -> strict suffix (token + suffix).
    // Example: "front_nobg.png" becomes "<Token>_front.png"
    private static readonly Dictionary<string, string> LegacyBaseToSuffix =
        new(StringComparer.OrdinalIgnoreCase)
        {
            { "front_nobg",       "_front" },
            { "back_nobg",        "_back" },
            { "front_shiny_nobg", "_frontshiny" },
            { "back_shiny_nobg",  "_backshiny" },

            // Additional legacy variants (no underscore)
            { "frontshiny",        "_frontshiny" },
            { "backshiny",         "_backshiny" },
            { "front_shiny",       "_frontshiny" },
            { "back_shiny",        "_backshiny" },
        };

    // ---------------- Report ----------------

    public sealed class Report
    {
        public readonly List<string> Missing = new();
        public readonly List<string> Collisions = new();
        public readonly List<string> Skipped = new();
        public readonly List<string> Info = new();

        public bool HasIssues => Missing.Count > 0 || Collisions.Count > 0;

        public void Absorb(Report other)
        {
            if (other == null) return;
            Missing.AddRange(other.Missing);
            Collisions.AddRange(other.Collisions);
            Skipped.AddRange(other.Skipped);
            Info.AddRange(other.Info);
        }

        public string ToSummaryString(int max = 80)
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

    // ---------------- Public Pipeline (Main Art Root) ----------------
    // Assets/Art/Monsters/<Type>/<Rarity>/
    // NOTE: This pipeline renames files IN PLACE only (no moving).

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

            RenameAndValidateInFolder(rep, folder, token, m.displayName, renameFilesToConvention, validateRequiredSprites, dryRun, logVerbose);
        }

        return rep;
    }

    // ---------------- Public Pipeline (Pack Root) ----------------
    // Expected structure:
    // Assets/Monsters/Packs/<Pack Name>/<Monster Name>/
    public static Report RunPackPipeline(
        string packsRoot,
        IEnumerable<(string packName, string displayName)> monsters,
        bool renameFilesToConvention,
        bool validateRequiredSprites,
        bool dryRun,
        bool logVerbose
    )
    {
        var rep = new Report();

        if (!AssetDatabase.IsValidFolder(packsRoot))
        {
            rep.Missing.Add($"Invalid packs root: {packsRoot}");
            return rep;
        }

        foreach (var m in monsters)
        {
            string packName = (m.packName ?? "").Trim();
            string monsterName = (m.displayName ?? "").Trim();

            if (IsMainPackValue(packName))
            {
                rep.Skipped.Add($"Pack entry treated as Main (skipped by pack pipeline): Pack='{packName}', Monster='{monsterName}'");
                continue;
            }

            if (string.IsNullOrWhiteSpace(monsterName))
            {
                rep.Skipped.Add($"Invalid monster folder name (blank) in pack '{packName}'");
                continue;
            }

            // Use actual folder names on disk.
            string folder = $"{packsRoot}/{packName}/{monsterName}";
            if (!AssetDatabase.IsValidFolder(folder))
            {
                rep.Missing.Add($"Missing pack monster folder: {folder}");
                continue;
            }

            string token = NormalizeMonsterToken(monsterName);
            if (string.IsNullOrWhiteSpace(token))
            {
                rep.Skipped.Add($"Invalid token: '{monsterName}' (Pack='{packName}')");
                continue;
            }

            RenameAndValidateInFolder(rep, folder, token, monsterName, renameFilesToConvention, validateRequiredSprites, dryRun, logVerbose);
        }

        return rep;
    }

    // ---------------- Core Work ----------------

    private static void RenameAndValidateInFolder(
        Report rep,
        string folder,
        string token,
        string displayNameForLogs,
        bool renameFilesToConvention,
        bool validateRequiredSprites,
        bool dryRun,
        bool logVerbose
    )
    {
        // ---- Rename legacy files IN PLACE ONLY ----
        if (renameFilesToConvention)
        {
            foreach (var kv in LegacyBaseToSuffix)
            {
                string legacyBase = kv.Key;
                string suffix = kv.Value;
                string desiredBase = token + suffix;

                // If strict target already exists, do not overwrite.
                if (HasAnyAssetWithExactBaseName(folder, desiredBase))
                    continue;

                // Find the legacy file inside THIS SAME folder by exact base name.
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
        // IMPORTANT: Validation should not false-warn if the file exists but is imported as Texture2D (not Sprite).
        // So we accept either an exact-named Sprite OR an exact-named Texture2D file.
        if (validateRequiredSprites)
        {
            foreach (var suffix in RequiredSuffixes)
            {
                string expected = token + suffix;
                if (!HasAnyAssetWithExactBaseName(folder, expected))
                    rep.Missing.Add($"Missing '{expected}' in {folder} (Monster='{displayNameForLogs}')");
            }
        }
    }

    // ---------------- Scanner (Main) ----------------
    // Assets/Art/Monsters/<Type>/<Rarity>/
    // Detect any Sprite ending with "_front" and uses the prefix as the token/displayName.

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
            }
        }

        return list
            .GroupBy(x => $"{x.type}|{x.rarity}|{x.displayName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    // ---------------- Scanner (Packs) ----------------
    // Assets/Monsters/Packs/<Pack Name>/<Monster Name>/
    // Uses folder names for pack + monster; does not attempt to infer type/rarity.
    public static List<(string packName, string displayName)> ScanMonstersFromPackRoot(string packsRoot)
    {
        var list = new List<(string packName, string displayName)>();

        if (string.IsNullOrWhiteSpace(packsRoot) || !AssetDatabase.IsValidFolder(packsRoot))
            return list;

        var packFolders = AssetDatabase.GetSubFolders(packsRoot);
        foreach (var pf in packFolders)
        {
            string packName = Path.GetFileName(pf);

            // Explicitly skip "Main" pack folder if it exists for any reason.
            if (IsMainPackValue(packName))
                continue;

            var monsterFolders = AssetDatabase.GetSubFolders(pf);
            foreach (var mf in monsterFolders)
            {
                string monsterName = Path.GetFileName(mf);
                if (string.IsNullOrWhiteSpace(monsterName)) continue;

                list.Add((packName, monsterName));
            }
        }

        return list
            .GroupBy(x => $"{x.packName}|{x.displayName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    // ---------------- Asset Helpers ----------------

    private static bool HasAnyAssetWithExactBaseName(string folder, string baseName)
    {
        // Accept exact-named Sprite OR exact-named Texture2D (pre-sliced / not sprite-imported).
        if (LoadSpriteByExactName(folder, baseName) != null)
            return true;

        if (FindTextureByExactBaseName(folder, baseName) != null)
            return true;

        return false;
    }

    private static string FindTextureByExactBaseName(string folder, string baseName)
    {
        if (!AssetDatabase.IsValidFolder(folder)) return null;

        // FindAssets tokenizes; we still confirm exact filename base match.
        string[] guids = AssetDatabase.FindAssets($"t:Texture2D {baseName}", new[] { folder });
        foreach (var g in guids)
        {
            string p = AssetDatabase.GUIDToAssetPath(g);
            if (string.IsNullOrWhiteSpace(p)) continue;

            if (Path.GetFileNameWithoutExtension(p).Equals(baseName, StringComparison.OrdinalIgnoreCase))
                return p;
        }

        // Also allow "Default" textures that may not import as Texture2D search hits reliably in some cases.
        // Fall back to direct file scan as a last resort.
        try
        {
            string absFolder = ToAbsolutePath(folder);
            if (!string.IsNullOrWhiteSpace(absFolder) && Directory.Exists(absFolder))
            {
                // Typical image extensions
                string[] exts = { ".png", ".jpg", ".jpeg", ".tga", ".psd", ".webp" };
                foreach (var ext in exts)
                {
                    string candidate = Path.Combine(absFolder, baseName + ext);
                    if (File.Exists(candidate))
                        return folder + "/" + Path.GetFileName(candidate);
                }
            }
        }
        catch { /* no-op */ }

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

    private static string ToAbsolutePath(string assetPath)
    {
        // assetPath like "Assets/Art/Monsters/Fire/Epic"
        if (string.IsNullOrWhiteSpace(assetPath)) return null;
        if (!assetPath.StartsWith("Assets", StringComparison.OrdinalIgnoreCase)) return null;

        // Application.dataPath => .../<Project>/Assets
        string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
        return Path.Combine(projectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));
    }

    // ---------------- Naming ----------------

    public static string NormalizeMonsterToken(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
            return "";

        foreach (char c in Path.GetInvalidFileNameChars())
            displayName = displayName.Replace(c.ToString(), "");

        // Remove whitespace and collapse to a single token.
        string token = string.Concat(
            displayName.Split((char[])null, StringSplitOptions.RemoveEmptyEntries));

        if (token.Length == 0) return "";

        return char.ToUpperInvariant(token[0]) + token.Substring(1);
    }

    private static bool IsMainPackValue(string packName)
    {
        if (string.IsNullOrWhiteSpace(packName)) return true;
        return packName.IndexOf("Main", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    // ---------------- CSV Support (V2) ----------------
    // Reads Name, Type, Rarity, Pack Name.
    // Pack Name containing "Main" => main monster (uses type/rarity folders under Assets/Art/Monsters).
    // Any other Pack Name => pack monster (uses Assets/Monsters/Packs/<Pack Name>/<Monster Name>/).

    public static bool TryReadCsvMonstersV2(
        UnityEngine.Object csvAsset,
        out List<(MonsterType type, Rarity rarity, string displayName)> mainMonsters,
        out List<(string packName, string displayName)> packMonsters,
        out string error
    )
    {
        mainMonsters = new();
        packMonsters = new();
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

        int packCol = Array.FindIndex(headers, h =>
            h.Equals("Pack Name", StringComparison.OrdinalIgnoreCase) ||
            h.Equals("PackName", StringComparison.OrdinalIgnoreCase));

        if (nameCol < 0 || rarityCol < 0 || typeCol < 0 || packCol < 0)
        {
            error = "CSV must include Name (or Display Name), Type, Rarity, and Pack Name columns.";
            return false;
        }

        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i].TrimEnd('\r');
            if (string.IsNullOrWhiteSpace(line)) continue;

            // Minimal CSV parsing: comma-split.
            // If you expect commas inside quoted cells, we should swap in a proper CSV parser.
            var cells = line.Split(',');
            int need = Math.Max(Math.Max(nameCol, typeCol), Math.Max(rarityCol, packCol));
            if (cells.Length <= need)
                continue;

            string name = cells[nameCol].Trim();
            if (string.IsNullOrWhiteSpace(name)) continue;

            string packName = cells[packCol].Trim();

            if (IsMainPackValue(packName))
            {
                MonsterType t = MonsterType.None;
                Enum.TryParse(cells[typeCol].Trim(), true, out t);

                Rarity r = Rarity.Common;
                Enum.TryParse(cells[rarityCol].Trim(), true, out r);

                mainMonsters.Add((t, r, name));
            }
            else
            {
                packMonsters.Add((packName, name));
            }
        }

        // De-dupe
        mainMonsters = mainMonsters
            .GroupBy(x => $"{x.type}|{x.rarity}|{x.displayName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        packMonsters = packMonsters
            .GroupBy(x => $"{x.packName}|{x.displayName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();

        return true;
    }
}
#endif
