using UnityEngine;

public static class InfoServices
{
    /// <summary>
    /// Open the Info panel by a string id (e.g., "res.coins", "job.forge", "tag.hunter").
    /// Fallbacks are optional and only used if the SO is missing or has empty fields.
    /// </summary>
    public static void OpenById(
        string infoId,
        string fallbackTitle = null,
        string fallbackSubtitle = null,
        string fallbackBody = null,
        Sprite fallbackIcon = null)
    {
        InfoRouter.Open(infoId, fallbackTitle, fallbackSubtitle, fallbackBody, fallbackIcon);
    }

    /// <summary>
    /// Convenience for resources: builds id "res.{enum}" (e.g., res.Coins, res.Energy).
    /// </summary>
    public static void OpenResource(
        ResourceType type,
        string fallbackTitle,
        string fallbackSubtitle,
        string fallbackBody,
        Sprite fallbackIcon = null)
    {
        var id = $"res.{type}";
        InfoRouter.Open(id, fallbackTitle, fallbackSubtitle, fallbackBody, fallbackIcon);
    }

    /// <summary>
    /// Convenience for job sites: builds id "job.{key}" (you decide the key, e.g., "forge").
    /// </summary>
    public static void OpenJob(
        string jobKey,
        string fallbackTitle,
        string fallbackSubtitle,
        string fallbackBody,
        Sprite fallbackIcon = null)
    {
        var id = $"job.{jobKey}";
        InfoRouter.Open(id, fallbackTitle, fallbackSubtitle, fallbackBody, fallbackIcon);
    }

    /// <summary>
    /// If you still have older call sites that try to resolve by (category, key),
    /// this adapter converts them to your string id convention and opens the panel.
    /// </summary>
    public static void Open(InfoCategory category, string key,
        string fallbackTitle = null, string fallbackSubtitle = null, string fallbackBody = null, Sprite fallbackIcon = null)
    {
        var id = BuildId(category, key);
        InfoRouter.Open(id, fallbackTitle, fallbackSubtitle, fallbackBody, fallbackIcon);
    }

    // ---------- helpers ----------

    private static string BuildId(InfoCategory category, string key)
    {
        if (string.IsNullOrWhiteSpace(key)) key = "unknown";
        var prefix = category switch
        {
            InfoCategory.Resource => "res",
            InfoCategory.JobSite  => "job",
            InfoCategory.Tag      => "tag",
            InfoCategory.Monster  => "mon",
            _                     => "misc"
        };
        // keep case as-is, or normalize if you prefer:
        // key = key.Trim().ToLowerInvariant().Replace(" ", "");
        return $"{prefix}.{key}";
    }
}
