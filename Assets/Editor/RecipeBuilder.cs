#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class RecipeBuilder
{
    private const string RecipesFolder = "Assets/Data/Recycle Recipes";
    private const string LibraryPath = "Assets/Data/Recycle Recipes/RecycleRecipeLibrary.asset";

    [MenuItem("Bitlings/Recycle/Build From CSV...")]
    public static void BuildFromCsvMenu()
    {
        string csvPath = EditorUtility.OpenFilePanel("Recycle Recipes CSV", Application.dataPath, "csv");
        if (string.IsNullOrEmpty(csvPath))
            return;

        try
        {
            BuildFromCsvPath(csvPath);
            EditorUtility.DisplayDialog("Recipe Builder", "Recycle recipes imported successfully.", "OK");
        }
        catch (Exception ex)
        {
            Debug.LogError($"[RecipeBuilder] Failed: {ex}");
            EditorUtility.DisplayDialog("Recipe Builder Failed", ex.Message, "OK");
        }
    }

    public static void BuildFromCsvPath(string csvPath)
    {
        if (!File.Exists(csvPath))
            throw new FileNotFoundException("CSV file not found.", csvPath);

        EnsureFolder(RecipesFolder);

        string[] lines = File.ReadAllLines(csvPath, Encoding.UTF8);
        if (lines.Length < 2)
            throw new InvalidDataException("CSV must contain a header row and at least one data row.");

        var header = SplitCsvLine(lines[0]);
        var col = BuildColumnMap(header);

        RequireColumn(col, "id");
        RequireColumn(col, "fromtype");
        RequireColumn(col, "fromamount");
        RequireColumn(col, "totype");
        RequireColumn(col, "toamount");

        var createdOrUpdated = new List<RecycleRecipeSO>();
        var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int created = 0;
        int updated = 0;
        int skipped = 0;

        for (int i = 1; i < lines.Length; i++)
        {
            string raw = lines[i];
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            if (raw.TrimStart().StartsWith("#", StringComparison.Ordinal))
                continue;

            var cells = SplitCsvLine(raw);
            string recipeId = Get(col, cells, "id");
            if (string.IsNullOrWhiteSpace(recipeId))
            {
                skipped++;
                Debug.LogWarning($"[RecipeBuilder] Row {i + 1}: missing id. Skipped.");
                continue;
            }

            if (!seenIds.Add(recipeId))
            {
                skipped++;
                Debug.LogWarning($"[RecipeBuilder] Row {i + 1}: duplicate id '{recipeId}'. Skipped.");
                continue;
            }

            if (!TryParseResourceType(col, cells, "fromtype", out ResourceType fromType))
            {
                skipped++;
                Debug.LogWarning($"[RecipeBuilder] Row {i + 1} ({recipeId}): invalid fromType. Skipped.");
                continue;
            }

            if (!TryParseInt(col, cells, "fromamount", out int fromAmount))
            {
                skipped++;
                Debug.LogWarning($"[RecipeBuilder] Row {i + 1} ({recipeId}): invalid fromAmount. Skipped.");
                continue;
            }

            if (!TryParseResourceType(col, cells, "totype", out ResourceType toType))
            {
                skipped++;
                Debug.LogWarning($"[RecipeBuilder] Row {i + 1} ({recipeId}): invalid toType. Skipped.");
                continue;
            }

            if (!TryParseInt(col, cells, "toamount", out int toAmount))
            {
                skipped++;
                Debug.LogWarning($"[RecipeBuilder] Row {i + 1} ({recipeId}): invalid toAmount. Skipped.");
                continue;
            }

            string displayName = GetFirst(col, cells, "displayname", "name", "title");
            if (string.IsNullOrWhiteSpace(displayName))
                displayName = recipeId;

            string assetPath = $"{RecipesFolder}/RecycleRecipe_{SanitizeFileName(recipeId)}.asset";
            var recipe = AssetDatabase.LoadAssetAtPath<RecycleRecipeSO>(assetPath);
            bool isNew = false;

            if (!recipe)
            {
                recipe = ScriptableObject.CreateInstance<RecycleRecipeSO>();
                AssetDatabase.CreateAsset(recipe, assetPath);
                isNew = true;
                created++;
            }
            else
            {
                updated++;
            }

            recipe.recipeId = recipeId;
            recipe.displayName = displayName;
            recipe.fromType = fromType;
            recipe.fromAmount = Mathf.Max(1, fromAmount);
            recipe.toType = toType;
            recipe.toAmount = Mathf.Max(1, toAmount);

            if (isNew)
                recipe.name = $"RecycleRecipe_{SanitizeFileName(recipeId)}";

            EditorUtility.SetDirty(recipe);
            createdOrUpdated.Add(recipe);
        }

        createdOrUpdated.Sort((a, b) => string.Compare(a.recipeId, b.recipeId, StringComparison.OrdinalIgnoreCase));

        var library = AssetDatabase.LoadAssetAtPath<RecycleRecipeLibrarySO>(LibraryPath);
        if (!library)
        {
            library = ScriptableObject.CreateInstance<RecycleRecipeLibrarySO>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }

        library.recipes = createdOrUpdated.ToArray();
        EditorUtility.SetDirty(library);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[RecipeBuilder] Import complete. Created={created}, Updated={updated}, Skipped={skipped}, LibraryCount={createdOrUpdated.Count}");
    }

    private static bool TryParseResourceType(Dictionary<string, int> col, List<string> cells, string key, out ResourceType value)
    {
        value = ResourceType.None;
        string raw = Get(col, cells, key);
        return !string.IsNullOrWhiteSpace(raw) && Enum.TryParse(raw, true, out value);
    }

    private static bool TryParseInt(Dictionary<string, int> col, List<string> cells, string key, out int value)
    {
        value = 0;
        string raw = Get(col, cells, key);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static void EnsureFolder(string folderAssetPath)
    {
        if (AssetDatabase.IsValidFolder(folderAssetPath))
            return;

        string[] parts = folderAssetPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }

    private static Dictionary<string, int> BuildColumnMap(List<string> header)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < header.Count; i++)
        {
            string normalized = NormalizeColumnName(header[i]);
            if (string.IsNullOrEmpty(normalized) || map.ContainsKey(normalized))
                continue;

            map.Add(normalized, i);
        }

        return map;
    }

    private static string NormalizeColumnName(string value)
    {
        return (value ?? string.Empty)
            .Trim()
            .Replace(" ", string.Empty)
            .Replace("_", string.Empty)
            .ToLowerInvariant();
    }

    private static void RequireColumn(Dictionary<string, int> col, string key)
    {
        if (!col.ContainsKey(key))
            throw new InvalidDataException($"Missing required column: {key}");
    }

    private static string Get(Dictionary<string, int> col, List<string> cells, string key)
    {
        if (!col.TryGetValue(key, out int index))
            return string.Empty;

        if (index < 0 || index >= cells.Count)
            return string.Empty;

        return (cells[index] ?? string.Empty).Trim();
    }

    private static string GetFirst(Dictionary<string, int> col, List<string> cells, params string[] keys)
    {
        for (int i = 0; i < keys.Length; i++)
        {
            string value = Get(col, cells, keys[i]);
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        return string.Empty;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var result = new List<string>();
        if (line == null)
            return result;

        var builder = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char current = line[i];

            if (current == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    builder.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }

                continue;
            }

            if (current == ',' && !inQuotes)
            {
                result.Add(builder.ToString());
                builder.Length = 0;
                continue;
            }

            builder.Append(current);
        }

        result.Add(builder.ToString());
        return result;
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "Recipe";

        char[] invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);

        for (int i = 0; i < value.Length; i++)
        {
            char current = value[i];
            if (Array.IndexOf(invalid, current) >= 0)
                continue;

            if (char.IsWhiteSpace(current) || current == '-')
                continue;

            builder.Append(current);
        }

        return builder.Length > 0 ? builder.ToString() : "Recipe";
    }
}
#endif