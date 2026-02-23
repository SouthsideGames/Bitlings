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

    private static string FilePath =>
        Path.Combine(Application.persistentDataPath, FileName);

    public static IronCareerMetaData Load()
    {
        if (!File.Exists(FilePath))
        {
            return new IronCareerMetaData
            {
                lastRerollDate = "",
                rerollsRemaining = 0,
                starterOfferIds = null
            };
        }

        string json = File.ReadAllText(FilePath);
        if (string.IsNullOrEmpty(json))
        {
            return new IronCareerMetaData
            {
                lastRerollDate = "",
                rerollsRemaining = 0,
                starterOfferIds = null
            };
        }

        return JsonUtility.FromJson<IronCareerMetaData>(json);
    }

    public static void Save(IronCareerMetaData data)
    {
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(FilePath, json);
    }
}