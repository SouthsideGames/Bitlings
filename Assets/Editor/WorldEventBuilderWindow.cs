#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

public class WorldEventBuilderWindow : EditorWindow
{
    private static readonly ResourceType[] VoucherResourceTypes =
    {
        ResourceType.TrainingVoucher,
        ResourceType.WellnessVoucher,
        ResourceType.EfficiencyVoucher,
        ResourceType.PackVoucher
    };

    private static readonly ResourceType[] AllResourceTypes = Enum.GetValues(typeof(ResourceType))
        .Cast<ResourceType>()
        .Where(t => t != ResourceType.None)
        .ToArray();

    private const string PrefCsvPath = "WorldEventBuilderWindow.CsvPath";
    private const string PrefOutputFolder = "WorldEventBuilderWindow.OutputFolder";
    private const string PrefLibraryPath = "WorldEventBuilderWindow.LibraryPath";

    private const string DefaultOutputFolder = "Assets/Resources/WorldEvents/Generated";
    private const string DefaultLibraryPath = "Assets/Resources/WorldEvents/WorldEventLibrary.asset";

    private static readonly string[] RequiredColumns =
    {
        "sortOrder","id","assetFileName","displayName","category","isHoliday","description","tickerMessage","scheduledOnly","startUnix","endUnix","canRotate","weight","minDaysBetween","idleRewardMultiplier","battleRewardMultiplier","exchangeValueMultiplier","boostedMonsterType","typeDamageMultiplier","effectCount","effect1_kind","effect1_job","effect1_resource","effect1_monsterType","effect1_value","effect1_flag","effect2_kind","effect2_job","effect2_resource","effect2_monsterType","effect2_value","effect2_flag","effect3_kind","effect3_job","effect3_resource","effect3_monsterType","effect3_value","effect3_flag","effect4_kind","effect4_job","effect4_resource","effect4_monsterType","effect4_value","effect4_flag","effect5_kind","effect5_job","effect5_resource","effect5_monsterType","effect5_value","effect5_flag"
    };

    private static readonly HashSet<WorldEventEffectKind> JobEffects = new HashSet<WorldEventEffectKind>
    {
        WorldEventEffectKind.DisableJobSite,
        WorldEventEffectKind.JobRateMultiplier,
        WorldEventEffectKind.JobStorageCapMultiplier,
        WorldEventEffectKind.JobCollectDisabled,
        WorldEventEffectKind.JobFatigueRateMultiplier
    };

    private static readonly HashSet<WorldEventEffectKind> ValueRequiredEffects = new HashSet<WorldEventEffectKind>
    {
        WorldEventEffectKind.JobRateMultiplier,
        WorldEventEffectKind.JobStorageCapMultiplier,
        WorldEventEffectKind.JobFatigueRateMultiplier,
        WorldEventEffectKind.RiftEnergyCostMultiplier,
        WorldEventEffectKind.WildPremiumChanceMultiplier,
        WorldEventEffectKind.BossCadenceMultiplier,
        WorldEventEffectKind.ShopPriceMultiplier,
        WorldEventEffectKind.ResourceGainMultiplier,
        WorldEventEffectKind.ExchangeDemandMultiplier,
        WorldEventEffectKind.ExchangeValueMultiplier,
        WorldEventEffectKind.IdleRewardMultiplier,
        WorldEventEffectKind.BattleRewardMultiplier,
        WorldEventEffectKind.TypeDamageMultiplier
    };

    private string csvFilePath;
    private string outputFolder;
    private string libraryAssetPath;

    private Vector2 logScroll;
    private readonly List<LogEntry> logEntries = new List<LogEntry>();
    private int infoCount;
    private int warningCount;
    private int errorCount;

    [MenuItem("Bitlings/World Events/Builder")]
    public static void Open()
    {
        GetWindow<WorldEventBuilderWindow>("World Event Builder");
    }

    private void OnEnable()
    {
        csvFilePath = EditorPrefs.GetString(PrefCsvPath, string.Empty);
        outputFolder = EditorPrefs.GetString(PrefOutputFolder, DefaultOutputFolder);
        libraryAssetPath = EditorPrefs.GetString(PrefLibraryPath, DefaultLibraryPath);
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("World Event Builder", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        DrawCsvPathField();
        DrawOutputFolderField();
        DrawLibraryPathField();

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Validate CSV", GUILayout.Height(30f)))
            {
                RunValidation();
            }

            if (GUILayout.Button("Build / Update Assets", GUILayout.Height(30f)))
            {
                RunBuild();
            }

            if (GUILayout.Button("Ping Library", GUILayout.Height(30f)))
            {
                PingLibrary();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField($"Info: {infoCount}    Warnings: {warningCount}    Errors: {errorCount}", EditorStyles.helpBox);

        logScroll = EditorGUILayout.BeginScrollView(logScroll);
        for (int i = 0; i < logEntries.Count; i++)
        {
            var entry = logEntries[i];
            var style = entry.Level == LogLevel.Error ? EditorStyles.boldLabel : EditorStyles.label;
            EditorGUILayout.LabelField($"[{entry.Level}] {entry.Message}", style);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawCsvPathField()
    {
        EditorGUILayout.LabelField("CSV File Path");
        using (new EditorGUILayout.HorizontalScope())
        {
            csvFilePath = EditorGUILayout.TextField(csvFilePath ?? string.Empty);
            if (GUILayout.Button("Browse", GUILayout.Width(70f)))
            {
                string selected = EditorUtility.OpenFilePanel("Select World Events CSV", Application.dataPath, "csv");
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    csvFilePath = selected;
                    EditorPrefs.SetString(PrefCsvPath, csvFilePath);
                }
            }
        }
    }

    private void DrawOutputFolderField()
    {
        EditorGUILayout.LabelField("Output Folder (WorldEventSO assets)");
        using (new EditorGUILayout.HorizontalScope())
        {
            outputFolder = EditorGUILayout.TextField(outputFolder ?? DefaultOutputFolder);
            if (GUILayout.Button("Browse", GUILayout.Width(70f)))
            {
                string basePath = Application.dataPath;
                string selected = EditorUtility.OpenFolderPanel("Select Output Folder", basePath, string.Empty);
                if (!string.IsNullOrWhiteSpace(selected))
                {
                    string projectPath = Directory.GetParent(Application.dataPath)?.FullName ?? string.Empty;
                    string relative = ToAssetPath(selected, projectPath);
                    if (!string.IsNullOrWhiteSpace(relative))
                    {
                        outputFolder = relative;
                        EditorPrefs.SetString(PrefOutputFolder, outputFolder);
                    }
                    else
                    {
                        AddError("Selected output folder must be inside this Unity project.");
                    }
                }
            }
        }
    }

    private void DrawLibraryPathField()
    {
        EditorGUILayout.LabelField("Library Asset Path");
        libraryAssetPath = EditorGUILayout.TextField(libraryAssetPath ?? DefaultLibraryPath);
    }

    private void RunValidation()
    {
        SavePrefs();
        ClearLogs();

        if (!TryLoadAndValidate(out CsvParseResult csv, out List<ValidatedRow> rows))
        {
            AddSummary(CountDataRows(csv), 0, 0, 0);
            return;
        }

        AddInfo($"Validation passed for {rows.Count} rows.");
        AddSummary(CountDataRows(csv), 0, 0, 0);
    }

    private void RunBuild()
    {
        SavePrefs();
        ClearLogs();

        if (!TryLoadAndValidate(out CsvParseResult csv, out List<ValidatedRow> rows))
        {
            AddSummary(CountDataRows(csv), 0, 0, 0);
            return;
        }

        int created = 0;
        int updated = 0;

        EnsureFolderPath(outputFolder);
        EnsureFolderPath(Path.GetDirectoryName(libraryAssetPath)?.Replace("\\", "/") ?? "Assets");

        var byId = LoadExistingEventsById(outputFolder);

        bool startedAssetEditing = false;
        try
        {
            AssetDatabase.StartAssetEditing();
            startedAssetEditing = true;

            var builtEvents = new List<(int sortOrder, WorldEventSO evt)>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                EditorUtility.DisplayProgressBar(
                    "Building World Events",
                    $"Processing {row.id} ({i + 1}/{rows.Count})",
                    rows.Count == 0 ? 1f : (i + 1f) / rows.Count);

                WorldEventSO evt;
                bool isNew;

                if (byId.TryGetValue(row.id, out var existingById) && existingById != null)
                {
                    evt = existingById;
                    isNew = false;
                }
                else
                {
                    string desiredAssetPath = BuildAssetPath(outputFolder, BuildAssetFileName(row));
                    evt = AssetDatabase.LoadAssetAtPath<WorldEventSO>(desiredAssetPath);
                    if (evt == null)
                    {
                        string legacyAssetPath = BuildAssetPath(outputFolder, row.assetFileName);
                        evt = AssetDatabase.LoadAssetAtPath<WorldEventSO>(legacyAssetPath);
                    }
                    if (evt != null)
                    {
                        isNew = false;
                    }
                    else
                    {
                        evt = ScriptableObject.CreateInstance<WorldEventSO>();
                        AssetDatabase.CreateAsset(evt, desiredAssetPath);
                        isNew = true;
                        created++;
                        AddInfo($"Created asset: {desiredAssetPath}");
                    }
                }

                if (!isNew)
                {
                    Undo.RecordObject(evt, "Update World Event");
                    updated++;
                }

                ApplyRowToAsset(evt, row);

                string currentAssetPath = AssetDatabase.GetAssetPath(evt);
                string desiredAssetPathForRow = BuildAssetPath(outputFolder, BuildAssetFileName(row));
                if (!string.Equals(currentAssetPath, desiredAssetPathForRow, StringComparison.Ordinal))
                {
                    var occupied = AssetDatabase.LoadAssetAtPath<WorldEventSO>(desiredAssetPathForRow);
                    if (occupied == null || occupied == evt)
                    {
                        string newNameNoExt = Path.GetFileNameWithoutExtension(desiredAssetPathForRow);
                        string renameError = AssetDatabase.RenameAsset(currentAssetPath, newNameNoExt);
                        if (!string.IsNullOrEmpty(renameError))
                        {
                            AddWarning($"Could not rename asset '{currentAssetPath}' to '{newNameNoExt}': {renameError}");
                        }
                    }
                    else
                    {
                        AddWarning($"Cannot rename '{currentAssetPath}' to '{desiredAssetPathForRow}' because another asset already exists there.");
                    }
                }

                EditorUtility.SetDirty(evt);
                byId[row.id] = evt;
                builtEvents.Add((row.sortOrder, evt));
            }

            var library = AssetDatabase.LoadAssetAtPath<WorldEventLibrarySO>(libraryAssetPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<WorldEventLibrarySO>();
                AssetDatabase.CreateAsset(library, libraryAssetPath);
                AddInfo($"Created library: {libraryAssetPath}");
            }
            else
            {
                Undo.RecordObject(library, "Update World Event Library");
            }

            library.events ??= new List<WorldEventSO>();
            library.events.Clear();

            var ordered = builtEvents
                .Where(x => x.evt != null)
                .OrderBy(x => x.sortOrder)
                .ThenBy(x => x.evt.id, StringComparer.Ordinal)
                .Select(x => x.evt)
                .Where(x => x != null)
                .ToList();

            library.events.AddRange(ordered);
            library.events.RemoveAll(e => e == null);
            EditorUtility.SetDirty(library);

            AssetDatabase.SaveAssets();
            AddInfo("Assets saved.");
            AddSummary(CountDataRows(csv), created, updated, library.events.Count);
        }
        catch (Exception ex)
        {
            AddError($"Build failed: {ex.Message}");
            AddError(ex.StackTrace ?? string.Empty);
            AddSummary(CountDataRows(csv), created, updated, 0);
        }
        finally
        {
            if (startedAssetEditing)
            {
                AssetDatabase.StopAssetEditing();
            }

            EditorUtility.ClearProgressBar();
            AssetDatabase.Refresh();
        }
    }

    private void PingLibrary()
    {
        SavePrefs();
        var library = AssetDatabase.LoadAssetAtPath<WorldEventLibrarySO>(libraryAssetPath);
        if (library == null)
        {
            AddWarning($"Library not found at: {libraryAssetPath}");
            return;
        }

        EditorGUIUtility.PingObject(library);
        Selection.activeObject = library;
        AddInfo($"Pinged library: {libraryAssetPath}");
    }

    private bool TryLoadAndValidate(out CsvParseResult parseResult, out List<ValidatedRow> validatedRows)
    {
        parseResult = null;
        validatedRows = new List<ValidatedRow>();

        if (string.IsNullOrWhiteSpace(csvFilePath))
        {
            AddError("CSV file path is required.");
            return false;
        }

        if (!File.Exists(csvFilePath))
        {
            AddError($"CSV file not found: {csvFilePath}");
            return false;
        }

        if (string.IsNullOrWhiteSpace(outputFolder) || !outputFolder.StartsWith("Assets", StringComparison.Ordinal))
        {
            AddError("Output folder must be a valid project-relative path under Assets.");
            return false;
        }

        if (string.IsNullOrWhiteSpace(libraryAssetPath) || !libraryAssetPath.StartsWith("Assets", StringComparison.Ordinal) || !libraryAssetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
        {
            AddError("Library asset path must be an .asset path under Assets.");
            return false;
        }

        try
        {
            parseResult = ParseCsvFile(csvFilePath);
        }
        catch (Exception ex)
        {
            AddError($"Failed to parse CSV: {ex.Message}");
            return false;
        }

        if (parseResult.Rows.Count == 0)
        {
            AddError("CSV must contain a header row.");
            return false;
        }

        if (!ValidateHeader(parseResult.Rows[0]))
        {
            return false;
        }

        for (int i = 1; i < parseResult.Rows.Count; i++)
        {
            var rawRow = parseResult.Rows[i];
            if (IsRowCompletelyEmpty(rawRow))
            {
                continue;
            }

            if (rawRow.Count != RequiredColumns.Length)
            {
                AddError($"Row {i + 1}: expected {RequiredColumns.Length} columns, got {rawRow.Count}.");
                continue;
            }

            var row = new CsvRow(rawRow, i + 1);
            if (TryValidateRow(row, out ValidatedRow validatedRow))
            {
                validatedRows.Add(validatedRow);
            }
        }

        ValidateUniqueness(validatedRows);

        if (errorCount > 0)
        {
            AddError("Validation failed. Fix errors before building.");
            return false;
        }

        return true;
    }

    private bool ValidateHeader(List<string> header)
    {
        if (header.Count != RequiredColumns.Length)
        {
            AddError($"Header column count mismatch. Expected {RequiredColumns.Length}, got {header.Count}.");
            return false;
        }

        bool ok = true;
        for (int i = 0; i < RequiredColumns.Length; i++)
        {
            string got = header[i]?.Trim() ?? string.Empty;
            string expected = RequiredColumns[i];
            if (!string.Equals(got, expected, StringComparison.Ordinal))
            {
                AddError($"Header mismatch at column {i + 1}: expected '{expected}', got '{got}'.");
                ok = false;
            }
        }

        return ok;
    }

    private bool TryValidateRow(CsvRow row, out ValidatedRow validated)
    {
        validated = new ValidatedRow();
        validated.sourceRow = row.RowNumber;

        bool ok = true;

        ok &= TryParseInt(row, "sortOrder", out validated.sortOrder);

        validated.id = GetTrimmed(row, "id");
        if (string.IsNullOrWhiteSpace(validated.id))
        {
            AddError(RowCol(row.RowNumber, "id", "id is required."));
            ok = false;
        }

        validated.assetFileName = GetTrimmed(row, "assetFileName");
        if (string.IsNullOrWhiteSpace(validated.assetFileName))
        {
            AddError(RowCol(row.RowNumber, "assetFileName", "assetFileName is required."));
            ok = false;
        }

        validated.displayName = GetRaw(row, "displayName");
        if (string.IsNullOrWhiteSpace(validated.displayName))
        {
            AddError(RowCol(row.RowNumber, "displayName", "displayName is required."));
            ok = false;
        }

        ok &= TryParseEnum(row, "category", out validated.category);
        ok &= TryParseBool(row, "isHoliday", out validated.isHoliday);

        validated.description = GetRaw(row, "description");
        validated.tickerMessage = GetRaw(row, "tickerMessage");

        ok &= TryParseBool(row, "scheduledOnly", out validated.scheduledOnly);
        ok &= TryParseLong(row, "startUnix", out validated.startUnix);
        ok &= TryParseLong(row, "endUnix", out validated.endUnix);
        ok &= TryParseBool(row, "canRotate", out validated.canRotate);
        ok &= TryParseInt(row, "weight", out validated.weight);
        ok &= TryParseFloat(row, "minDaysBetween", out validated.minDaysBetween);
        ok &= TryParseFloat(row, "idleRewardMultiplier", out validated.idleRewardMultiplier);
        ok &= TryParseFloat(row, "battleRewardMultiplier", out validated.battleRewardMultiplier);
        ok &= TryParseFloat(row, "exchangeValueMultiplier", out validated.exchangeValueMultiplier);
        ok &= TryParseEnum(row, "boostedMonsterType", out validated.boostedMonsterType);
        ok &= TryParseFloat(row, "typeDamageMultiplier", out validated.typeDamageMultiplier);
        ok &= TryParseInt(row, "effectCount", out validated.effectCount);

        if (validated.weight < 0)
        {
            AddError(RowCol(row.RowNumber, "weight", "weight must be >= 0."));
            ok = false;
        }

        if (validated.minDaysBetween < 0f)
        {
            AddError(RowCol(row.RowNumber, "minDaysBetween", "minDaysBetween must be >= 0."));
            ok = false;
        }

        if (validated.scheduledOnly && validated.startUnix <= 0 && validated.endUnix <= 0)
        {
            AddError(RowCol(row.RowNumber, "scheduledOnly", "scheduledOnly=true requires startUnix > 0 or endUnix > 0."));
            ok = false;
        }

        if (validated.boostedMonsterType == MonsterType.None)
        {
            if (!Mathf.Approximately(validated.typeDamageMultiplier, 1f))
            {
                AddWarning(RowCol(row.RowNumber, "typeDamageMultiplier", "boostedMonsterType is None; typeDamageMultiplier will be forced to 1."));
            }
            validated.typeDamageMultiplier = 1f;
        }
        else
        {
            if (!(validated.typeDamageMultiplier > 0f))
            {
                AddError(RowCol(row.RowNumber, "typeDamageMultiplier", "typeDamageMultiplier must be > 0 when boostedMonsterType != None."));
                ok = false;
            }
        }

        if (validated.effectCount < 0 || validated.effectCount > 5)
        {
            AddError(RowCol(row.RowNumber, "effectCount", "effectCount must be between 0 and 5."));
            ok = false;
        }

        validated.effects = new List<WorldEventEffect>(5);
        validated.effectResourceTokens = new List<string>(5);

        for (int slot = 1; slot <= 5; slot++)
        {
            string prefix = $"effect{slot}_";
            bool slotOk = true;

            slotOk &= TryParseEnum(row, prefix + "kind", out WorldEventEffectKind kind);
            slotOk &= TryParseEnum(row, prefix + "job", out JobType job);
            slotOk &= TryParseEnum(row, prefix + "resource", out ResourceType resource);
            slotOk &= TryParseEnum(row, prefix + "monsterType", out MonsterType monsterType);
            slotOk &= TryParseFloat(row, prefix + "value", out float value);
            slotOk &= TryParseBool(row, prefix + "flag", out bool flag);

            if (!slotOk)
            {
                ok = false;
                validated.effects.Add(default);
                validated.effectResourceTokens.Add(string.Empty);
                continue;
            }

            var effect = new WorldEventEffect
            {
                kind = kind,
                job = job,
                resource = resource,
                monsterType = monsterType,
                value = value,
                flag = flag
            };

            string resourceToken = GetTrimmed(row, prefix + "resource");

            if (slot <= validated.effectCount)
            {
                ValidateUsedEffectSlot(row, slot, effect, resourceToken, ref ok);
            }
            else
            {
                ValidateUnusedEffectSlot(row, slot, effect, ref ok);
            }

            validated.effects.Add(effect);
            validated.effectResourceTokens.Add(resourceToken);
        }

        return ok;
    }

    private static int CountDataRows(CsvParseResult csv)
    {
        if (csv == null || csv.Rows == null || csv.Rows.Count <= 1)
        {
            return 0;
        }

        int count = 0;
        for (int i = 1; i < csv.Rows.Count; i++)
        {
            if (!IsRowCompletelyEmpty(csv.Rows[i]))
            {
                count++;
            }
        }

        return count;
    }

    private void ValidateUsedEffectSlot(CsvRow row, int slot, WorldEventEffect effect, string resourceToken, ref bool ok)
    {
        string col = $"effect{slot}_kind";

        if (effect.kind == WorldEventEffectKind.None)
        {
            AddError(RowCol(row.RowNumber, col, "Used effect slot cannot have kind=None."));
            ok = false;
            return;
        }

        if (JobEffects.Contains(effect.kind) && effect.job == JobType.None)
        {
            AddError(RowCol(row.RowNumber, $"effect{slot}_job", "Job effect requires job != None."));
            ok = false;
        }

        if (effect.kind == WorldEventEffectKind.ResourceGainMultiplier && effect.resource == ResourceType.None)
        {
            if (!IsAllResourceAlias(resourceToken) && !IsVoucherResourceAlias(resourceToken))
            {
                AddError(RowCol(row.RowNumber, $"effect{slot}_resource", "ResourceGainMultiplier requires resource != None."));
                ok = false;
            }
        }

        if (effect.kind == WorldEventEffectKind.BoostedMonsterType && effect.monsterType == MonsterType.None)
        {
            AddError(RowCol(row.RowNumber, $"effect{slot}_monsterType", "BoostedMonsterType requires monsterType != None."));
            ok = false;
        }

        if (ValueRequiredEffects.Contains(effect.kind) && float.IsNaN(effect.value))
        {
            AddError(RowCol(row.RowNumber, $"effect{slot}_value", "Effect requires numeric value."));
            ok = false;
        }

        if (effect.kind == WorldEventEffectKind.JobCollectDisabled)
        {
            string rawFlag = GetTrimmed(row, $"effect{slot}_flag");
            string rawValue = GetTrimmed(row, $"effect{slot}_value");

            bool hasFlag = string.Equals(rawFlag, "true", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(rawFlag, "false", StringComparison.OrdinalIgnoreCase);

            bool hasTruthyNumeric = TryParseFloatLoose(rawValue, out float numeric) && numeric > 0f;

            if (!hasFlag && !hasTruthyNumeric)
            {
                AddWarning(RowCol(row.RowNumber, $"effect{slot}_flag", "JobCollectDisabled should provide flag=true/false and/or numeric truthy value."));
            }
        }
    }

    private void ValidateUnusedEffectSlot(CsvRow row, int slot, WorldEventEffect effect, ref bool ok)
    {
        bool neutral =
            effect.kind == WorldEventEffectKind.None &&
            effect.job == JobType.None &&
            effect.resource == ResourceType.None &&
            effect.monsterType == MonsterType.None &&
            Mathf.Approximately(effect.value, 0f) &&
            !effect.flag;

        if (!neutral)
        {
            AddError(RowCol(row.RowNumber, $"effect{slot}_kind", "Unused effect slots must be neutral (kind=None, job=None, resource=None, monsterType=None, value=0, flag=false)."));
            ok = false;
        }
    }

    private void ValidateUniqueness(List<ValidatedRow> rows)
    {
        var idSet = new Dictionary<string, int>(StringComparer.Ordinal);
        var fileSet = new Dictionary<string, int>(StringComparer.Ordinal);

        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];

            if (!string.IsNullOrWhiteSpace(row.id))
            {
                if (idSet.TryGetValue(row.id, out int firstRow))
                {
                    AddError($"Row {row.sourceRow}: [id] duplicate id '{row.id}' (first seen at row {firstRow}).");
                }
                else
                {
                    idSet[row.id] = row.sourceRow;
                }
            }

            if (!string.IsNullOrWhiteSpace(row.assetFileName))
            {
                if (fileSet.TryGetValue(row.assetFileName, out int firstFileRow))
                {
                    AddError($"Row {row.sourceRow}: [assetFileName] duplicate assetFileName '{row.assetFileName}' (first seen at row {firstFileRow}).");
                }
                else
                {
                    fileSet[row.assetFileName] = row.sourceRow;
                }
            }
        }
    }

    private static Dictionary<string, WorldEventSO> LoadExistingEventsById(string folder)
    {
        var map = new Dictionary<string, WorldEventSO>(StringComparer.Ordinal);
        string[] guids = AssetDatabase.FindAssets("t:WorldEventSO", new[] { folder });
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            var evt = AssetDatabase.LoadAssetAtPath<WorldEventSO>(path);
            if (evt == null || string.IsNullOrWhiteSpace(evt.id))
            {
                continue;
            }

            if (!map.ContainsKey(evt.id))
            {
                map.Add(evt.id, evt);
            }
        }

        return map;
    }

    private static void ApplyRowToAsset(WorldEventSO evt, ValidatedRow row)
    {
        evt.name = BuildObjectName(row);

        var serializedObject = new SerializedObject(evt);
        serializedObject.Update();

        serializedObject.FindProperty("id").stringValue = row.id;
        serializedObject.FindProperty("displayName").stringValue = row.displayName;
        serializedObject.FindProperty("category").intValue = (int)row.category;
        serializedObject.FindProperty("isHoliday").boolValue = row.isHoliday;
        serializedObject.FindProperty("description").stringValue = row.description;
        serializedObject.FindProperty("tickerMessage").stringValue = row.tickerMessage;
        serializedObject.FindProperty("scheduledOnly").boolValue = row.scheduledOnly;
        serializedObject.FindProperty("startUnix").longValue = row.startUnix;
        serializedObject.FindProperty("endUnix").longValue = row.endUnix;
        serializedObject.FindProperty("canRotate").boolValue = row.canRotate;
        serializedObject.FindProperty("weight").intValue = row.weight;
        serializedObject.FindProperty("minDaysBetween").floatValue = row.minDaysBetween;
        serializedObject.FindProperty("idleRewardMultiplier").floatValue = row.idleRewardMultiplier;
        serializedObject.FindProperty("battleRewardMultiplier").floatValue = row.battleRewardMultiplier;
        serializedObject.FindProperty("exchangeValueMultiplier").floatValue = row.exchangeValueMultiplier;
        serializedObject.FindProperty("boostedMonsterType").intValue = (int)row.boostedMonsterType;
        serializedObject.FindProperty("typeDamageMultiplier").floatValue = row.typeDamageMultiplier;

        SerializedProperty effectsProperty = serializedObject.FindProperty("effects");
        effectsProperty.ClearArray();

        List<WorldEventEffect> writeEffects = ExpandEffectsForWrite(row);
        for (int i = 0; i < writeEffects.Count; i++)
        {
            effectsProperty.InsertArrayElementAtIndex(i);
            SerializedProperty effectProperty = effectsProperty.GetArrayElementAtIndex(i);
            WorldEventEffect effect = writeEffects[i];

            effectProperty.FindPropertyRelative("kind").intValue = (int)effect.kind;
            effectProperty.FindPropertyRelative("job").intValue = (int)effect.job;
            effectProperty.FindPropertyRelative("resource").intValue = (int)effect.resource;
            effectProperty.FindPropertyRelative("monsterType").intValue = (int)effect.monsterType;
            effectProperty.FindPropertyRelative("value").floatValue = effect.value;
            effectProperty.FindPropertyRelative("flag").boolValue = effect.flag;
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    private static string BuildObjectName(ValidatedRow row)
    {
        string normalizedId = (row.id ?? string.Empty).Trim();
        if (!string.IsNullOrEmpty(normalizedId))
        {
            int dash = normalizedId.LastIndexOf('-');
            if (dash >= 0 && dash < normalizedId.Length - 1)
            {
                string suffix = normalizedId.Substring(dash + 1).Trim();
                if (int.TryParse(suffix, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idNumber) && idNumber >= 0)
                {
                    return $"WE_{idNumber:000}";
                }
            }
        }

        int number = Mathf.Max(0, row.sortOrder);
        return $"WE_{number:000}";
    }

    private static List<WorldEventEffect> ExpandEffectsForWrite(ValidatedRow row)
    {
        var result = new List<WorldEventEffect>();
        int count = Mathf.Clamp(row.effectCount, 0, 5);

        for (int i = 0; i < count; i++)
        {
            WorldEventEffect effect = row.effects[i];
            string token = i < row.effectResourceTokens.Count ? row.effectResourceTokens[i] : string.Empty;

            if (effect.kind == WorldEventEffectKind.ResourceGainMultiplier && effect.resource == ResourceType.None)
            {
                if (IsAllResourceAlias(token))
                {
                    for (int r = 0; r < AllResourceTypes.Length; r++)
                    {
                        var expanded = effect;
                        expanded.resource = AllResourceTypes[r];
                        result.Add(expanded);
                    }
                    continue;
                }

                if (IsVoucherResourceAlias(token))
                {
                    for (int r = 0; r < VoucherResourceTypes.Length; r++)
                    {
                        var expanded = effect;
                        expanded.resource = VoucherResourceTypes[r];
                        result.Add(expanded);
                    }
                    continue;
                }
            }

            result.Add(effect);
        }

        return result;
    }

    private static CsvParseResult ParseCsvFile(string path)
    {
        string text = File.ReadAllText(path);
        var rows = ParseCsvText(text);
        return new CsvParseResult { Rows = rows };
    }

    private static List<List<string>> ParseCsvText(string text)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var cell = new StringBuilder();

        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    cell.Append(c);
                }

                continue;
            }

            if (c == '"')
            {
                inQuotes = true;
                continue;
            }

            if (c == ',')
            {
                row.Add(cell.ToString());
                cell.Length = 0;
                continue;
            }

            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                row.Add(cell.ToString());
                cell.Length = 0;
                rows.Add(row);
                row = new List<string>();
                continue;
            }

            cell.Append(c);
        }

        if (inQuotes)
        {
            throw new InvalidDataException("Unterminated quoted field in CSV.");
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row);
        }

        return rows;
    }

    private static bool IsRowCompletelyEmpty(List<string> row)
    {
        for (int i = 0; i < row.Count; i++)
        {
            if (!string.IsNullOrWhiteSpace(row[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static string BuildAssetPath(string folder, string assetFileName)
    {
        string name = assetFileName.Trim();
        if (!name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
        {
            name += ".asset";
        }

        return $"{folder.TrimEnd('/')}/{name}";
    }

    private static string BuildAssetFileName(ValidatedRow row)
    {
        return BuildObjectName(row) + ".asset";
    }

    private static string ToAssetPath(string absolutePath, string projectPath)
    {
        string fullAbs = absolutePath.Replace("\\", "/");
        string fullProject = projectPath.Replace("\\", "/");

        if (!fullAbs.StartsWith(fullProject, StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        string relative = fullAbs.Substring(fullProject.Length).TrimStart('/');
        return relative;
    }

    private static void EnsureFolderPath(string folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return;
        }

        string normalized = folder.Replace("\\", "/").Trim('/');
        string[] parts = normalized.Split('/');

        if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Invalid folder path: {folder}");
        }

        string current = "Assets";
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }

    private static string GetRaw(CsvRow row, string column)
    {
        int idx = Array.IndexOf(RequiredColumns, column);
        if (idx < 0 || idx >= row.Cells.Count)
        {
            return string.Empty;
        }

        return row.Cells[idx] ?? string.Empty;
    }

    private static string GetTrimmed(CsvRow row, string column)
    {
        return GetRaw(row, column).Trim();
    }

    private bool TryParseBool(CsvRow row, string column, out bool value)
    {
        string raw = GetTrimmed(row, column);

        if (string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }

        if (string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }

        value = false;
        AddError(RowCol(row.RowNumber, column, $"Expected true/false, got '{raw}'."));
        return false;
    }

    private bool TryParseInt(CsvRow row, string column, out int value)
    {
        string raw = GetTrimmed(row, column);
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        AddError(RowCol(row.RowNumber, column, $"Expected int, got '{raw}'."));
        return false;
    }

    private bool TryParseLong(CsvRow row, string column, out long value)
    {
        string raw = GetTrimmed(row, column);
        if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        AddError(RowCol(row.RowNumber, column, $"Expected long, got '{raw}'."));
        return false;
    }

    private bool TryParseFloat(CsvRow row, string column, out float value)
    {
        string raw = GetTrimmed(row, column);
        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
        {
            return true;
        }

        AddError(RowCol(row.RowNumber, column, $"Expected float, got '{raw}'."));
        return false;
    }

    private static bool TryParseFloatLoose(string raw, out float value)
    {
        return float.TryParse(raw?.Trim() ?? string.Empty, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private bool TryParseEnum<TEnum>(CsvRow row, string column, out TEnum value) where TEnum : struct, Enum
    {
        string raw = GetTrimmed(row, column);
        if (TryParseEnumFlexible(raw, out value))
        {
            return true;
        }

        AddError(RowCol(row.RowNumber, column, $"Expected {typeof(TEnum).Name}, got '{raw}'."));
        return false;
    }

    private static bool TryParseEnumFlexible<TEnum>(string raw, out TEnum value) where TEnum : struct, Enum
    {
        if (Enum.TryParse(raw, true, out value))
        {
            return true;
        }

        string token = (raw ?? string.Empty).Trim();
        if (token.Length == 0)
        {
            value = default;
            return false;
        }

        // Support common CSV aliases such as WyrmDen -> Wyrm_Den.
        string normalizedInput = NormalizeEnumToken(token);
        string[] names = Enum.GetNames(typeof(TEnum));
        for (int i = 0; i < names.Length; i++)
        {
            string name = names[i];
            if (string.Equals(NormalizeEnumToken(name), normalizedInput, StringComparison.OrdinalIgnoreCase))
            {
                if (Enum.TryParse(name, out value))
                {
                    return true;
                }
            }
        }

        if (TryResolveEnumAlias<TEnum>(token, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryResolveEnumAlias<TEnum>(string token, out TEnum value) where TEnum : struct, Enum
    {
        string normalized = NormalizeEnumToken(token);

        if (typeof(TEnum) == typeof(ResourceType))
        {
            // CSV may use aggregate labels for irrelevant resource fields; treat as neutral None.
            if (normalized == "all" || normalized == "any" || normalized == "voucher" || normalized == "vouchers" || normalized == "na" || normalized == "n/a" || normalized == "-")
            {
                object boxed = ResourceType.None;
                value = (TEnum)boxed;
                return true;
            }
        }

        if (typeof(TEnum) == typeof(JobType))
        {
            if (normalized == "any" || normalized == "all" || normalized == "na" || normalized == "n/a" || normalized == "-")
            {
                object boxed = JobType.None;
                value = (TEnum)boxed;
                return true;
            }
        }

        if (typeof(TEnum) == typeof(MonsterType))
        {
            if (normalized == "any" || normalized == "all" || normalized == "na" || normalized == "n/a" || normalized == "-")
            {
                object boxed = MonsterType.None;
                value = (TEnum)boxed;
                return true;
            }
        }

        if (typeof(TEnum) == typeof(WorldEventEffectKind))
        {
            if (normalized == "na" || normalized == "n/a" || normalized == "-")
            {
                object boxed = WorldEventEffectKind.None;
                value = (TEnum)boxed;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string NormalizeEnumToken(string input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];
            if (char.IsLetterOrDigit(c) || c == '/')
            {
                sb.Append(char.ToLowerInvariant(c));
            }
        }

        return sb.ToString();
    }

    private static bool IsAllResourceAlias(string token)
    {
        return string.Equals(NormalizeEnumToken(token), "all", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVoucherResourceAlias(string token)
    {
        string normalized = NormalizeEnumToken(token);
        return normalized == "voucher" || normalized == "vouchers";
    }

    private static string RowCol(int row, string col, string message)
    {
        return $"Row {row}: [{col}] {message}";
    }

    private void SavePrefs()
    {
        EditorPrefs.SetString(PrefCsvPath, csvFilePath ?? string.Empty);
        EditorPrefs.SetString(PrefOutputFolder, outputFolder ?? DefaultOutputFolder);
        EditorPrefs.SetString(PrefLibraryPath, libraryAssetPath ?? DefaultLibraryPath);
    }

    private void ClearLogs()
    {
        logEntries.Clear();
        infoCount = 0;
        warningCount = 0;
        errorCount = 0;
    }

    private void AddSummary(int rowsRead, int created, int updated, int libraryEntries)
    {
        AddInfo($"Summary: rows read={rowsRead}, assets created={created}, assets updated={updated}, library entries={libraryEntries}, warnings={warningCount}, errors={errorCount}");
    }

    private void AddInfo(string message)
    {
        infoCount++;
        logEntries.Add(new LogEntry(LogLevel.Info, message));
        Debug.Log("[WorldEventBuilderWindow] " + message);
    }

    private void AddWarning(string message)
    {
        warningCount++;
        logEntries.Add(new LogEntry(LogLevel.Warning, message));
        Debug.LogWarning("[WorldEventBuilderWindow] " + message);
    }

    private void AddError(string message)
    {
        errorCount++;
        logEntries.Add(new LogEntry(LogLevel.Error, message));
        Debug.LogError("[WorldEventBuilderWindow] " + message);
    }

    private enum LogLevel
    {
        Info,
        Warning,
        Error
    }

    private readonly struct LogEntry
    {
        public readonly LogLevel Level;
        public readonly string Message;

        public LogEntry(LogLevel level, string message)
        {
            Level = level;
            Message = message;
        }
    }

    private sealed class CsvParseResult
    {
        public List<List<string>> Rows;
    }

    private readonly struct CsvRow
    {
        public readonly List<string> Cells;
        public readonly int RowNumber;

        public CsvRow(List<string> cells, int rowNumber)
        {
            Cells = cells;
            RowNumber = rowNumber;
        }
    }

    private sealed class ValidatedRow
    {
        public int sourceRow;
        public int sortOrder;
        public string id;
        public string assetFileName;
        public string displayName;
        public WorldEventCategory category;
        public bool isHoliday;
        public string description;
        public string tickerMessage;
        public bool scheduledOnly;
        public long startUnix;
        public long endUnix;
        public bool canRotate;
        public int weight;
        public float minDaysBetween;
        public float idleRewardMultiplier;
        public float battleRewardMultiplier;
        public float exchangeValueMultiplier;
        public MonsterType boostedMonsterType;
        public float typeDamageMultiplier;
        public int effectCount;
        public List<WorldEventEffect> effects;
        public List<string> effectResourceTokens;
    }
}
#endif
