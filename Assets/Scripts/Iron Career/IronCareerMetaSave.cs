using System;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class IronCareerMetaData
{
    public string lastRerollDate;
    public int rerollsRemaining;

    // Persist the daily offer so players can't spam open/close
    public string[] starterOfferIds;
}

public static class IronCareerMetaSave
{
    private const string FileName = "IronCareerMetaSave.json";

    private static IronCareerMetaData CreateDefault()
    {
        return new IronCareerMetaData
        {
            lastRerollDate = string.Empty,
            rerollsRemaining = 0,
            starterOfferIds = null
        };
    }

    private static string FilePath =>
        Path.Combine(Application.persistentDataPath, FileName);

    public static IronCareerMetaData Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return CreateDefault();

            string json = File.ReadAllText(FilePath);
            if (string.IsNullOrEmpty(json))
                return CreateDefault();

            var parsed = JsonUtility.FromJson<IronCareerMetaData>(json);
            return parsed ?? CreateDefault();
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IronCareerMetaSave] Failed to load meta save. Resetting. Error: {ex.Message}");
            return CreateDefault();
        }
    }

    public static void Save(IronCareerMetaData data)
    {
        try
        {
            string json = JsonUtility.ToJson(data, true);
            File.WriteAllText(FilePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[IronCareerMetaSave] Failed to save meta. Error: {ex.Message}");
        }
    }
}