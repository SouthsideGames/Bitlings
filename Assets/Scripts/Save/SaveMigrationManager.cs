using System;
using System.Collections.Generic;
using UnityEngine;

public static class SaveMigrationManager
{
    public const int CURRENT_SAVE_VERSION = 5;

    [Serializable]
    private sealed class SaveVersionProbe
    {
        public int version;
        public int saveVersion;
    }

    public static bool TryMigrateToCurrent(string rawJson, out SaveData saveData, out string migrationReport)
    {
        saveData = null;
        migrationReport = string.Empty;

        if (string.IsNullOrWhiteSpace(rawJson))
            return false;

        int version = DetectVersion(rawJson);
        string workingJson = rawJson;
        var completedSteps = new List<string>();

        if (version > CURRENT_SAVE_VERSION)
        {
            Debug.LogWarning($"[SaveMigrationManager] Save version {version} is newer than runtime version {CURRENT_SAVE_VERSION}. Attempting best-effort load.");
            saveData = JsonUtility.FromJson<SaveData>(rawJson);
            migrationReport = $"Detected future save version {version}. Loaded without migration.";
            return saveData != null;
        }

        while (version < CURRENT_SAVE_VERSION)
        {
            DevLog.Log($"[SaveMigrationManager] Running migration v{version} -> v{version + 1}.");
            switch (version)
            {
                case 1:
                    workingJson = MigrateV1ToV2(workingJson);
                    version = 2;
                    completedSteps.Add("v1->v2");
                    break;
                case 2:
                    workingJson = MigrateV2ToV3(workingJson);
                    version = 3;
                    completedSteps.Add("v2->v3");
                    break;
                case 3:
                    workingJson = MigrateV3ToV4(workingJson);
                    version = 4;
                    completedSteps.Add("v3->v4");
                    break;
                case 4:
                    workingJson = MigrateV4ToV5(workingJson);
                    version = 5;
                    completedSteps.Add("v4->v5");
                    break;
                default:
                    Debug.LogError($"[SaveMigrationManager] Unsupported save version {version}.");
                    return false;
            }
        }

        saveData = JsonUtility.FromJson<SaveData>(workingJson);
        if (saveData == null)
            return false;

        saveData.saveVersion = CURRENT_SAVE_VERSION;
        migrationReport = completedSteps.Count > 0
            ? $"Completed migrations: {string.Join(", ", completedSteps)}"
            : $"Save already at version {CURRENT_SAVE_VERSION}.";

        if (completedSteps.Count > 0)
            DevLog.Log($"[SaveMigrationManager] {migrationReport}");

        return true;
    }

    public static string MigrateV1ToV2(string rawJson)
    {
        var legacy = DeserializeLegacyRoot(rawJson);
        legacy.player ??= new PlayerManager();
        legacy.player.achievements ??= new List<AchievementProgressData>();
        legacy.version = 2;
        return SerializeLegacyRoot(legacy);
    }

    public static string MigrateV2ToV3(string rawJson)
    {
        var legacy = DeserializeLegacyRoot(rawJson);
        legacy.player ??= new PlayerManager();
        legacy.player.idleTeamOwnedUIDs ??= new List<string>();
        while (legacy.player.idleTeamOwnedUIDs.Count < 3)
            legacy.player.idleTeamOwnedUIDs.Add(null);
        while (legacy.player.idleTeamOwnedUIDs.Count > 3)
            legacy.player.idleTeamOwnedUIDs.RemoveAt(legacy.player.idleTeamOwnedUIDs.Count - 1);
        legacy.player.autoBattleLogArchive ??= new List<AutoBattleLogEntry>();
        legacy.version = 3;
        return SerializeLegacyRoot(legacy);
    }

    public static string MigrateV3ToV4(string rawJson)
    {
        var legacy = DeserializeLegacyRoot(rawJson);
        legacy.worldEvents ??= new WorldEventSaveData();
        legacy.worldEvents.cooldowns ??= new List<WorldEventRollCooldown>();

        legacy.exchange ??= new ExchangeSaveData();
        legacy.exchange.speciesStates ??= new List<MarketSpeciesState>();
        legacy.exchange.monthlyBattleSentiments ??= new List<SpeciesBattleSentimentData>();
        legacy.exchange.activeRequests ??= new List<ActiveRequest>();
        legacy.exchange.demandOverrides ??= new List<DemandOverride>();
        legacy.exchange.bullTokenUsages ??= new List<SpeciesTokenUsage>();
        legacy.exchange.bearTokenUsages ??= new List<SpeciesTokenUsage>();
        legacy.exchange.catchHype ??= new List<CatchHypeEntry>();
        legacy.exchange.brokerScarcity ??= new List<BrokerScarcityEntry>();
        legacy.exchange.surgeAlertSpeciesIds ??= new List<string>();

        legacy.version = 4;
        return SerializeLegacyRoot(legacy);
    }

    public static string MigrateV4ToV5(string rawJson)
    {
        var legacy = DeserializeLegacyRoot(rawJson);
        var saveData = SaveDataMapper.FromLegacyRoot(legacy);
        saveData.saveVersion = CURRENT_SAVE_VERSION;
        return JsonUtility.ToJson(saveData, true);
    }

    private static int DetectVersion(string rawJson)
    {
        try
        {
            var probe = JsonUtility.FromJson<SaveVersionProbe>(rawJson);
            if (probe != null)
            {
                if (probe.saveVersion > 0)
                    return probe.saveVersion;
                if (probe.version > 0)
                    return probe.version;
            }
        }
        catch (Exception e)
        {
            // A save whose version probe throws is suspect; the heuristics below
            // decide its fate, so make sure the failure is diagnosable in the field.
            Debug.LogWarning($"[SaveMigrationManager] Version probe failed ({e.GetType().Name}: {e.Message}); falling back to heuristics.");
        }

        if (rawJson.Contains("\"player\"", StringComparison.Ordinal))
            return 1;

        return CURRENT_SAVE_VERSION;
    }

    private static LegacyCombinedSaveRoot DeserializeLegacyRoot(string rawJson)
    {
        var legacy = JsonUtility.FromJson<LegacyCombinedSaveRoot>(rawJson) ?? new LegacyCombinedSaveRoot();
        if (legacy.version <= 0)
            legacy.version = 1;
        return legacy;
    }

    private static string SerializeLegacyRoot(LegacyCombinedSaveRoot legacy)
    {
        return JsonUtility.ToJson(legacy, true);
    }
}