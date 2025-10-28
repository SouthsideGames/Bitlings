using UnityEngine;

public static class InfoRouter
{
    /// <summary>
    /// Opens PanelId.Info and populates it using a string id.
    /// </summary>
    public static void Open(string infoId, string fallbackTitle = null, string fallbackSubtitle = null, string fallbackBody = null, Sprite fallbackIcon = null)
    {
        var so = InfoLibrarySO.Find(infoId);

        var title    = so ? (string.IsNullOrWhiteSpace(so.title)    ? fallbackTitle    : so.title)    : fallbackTitle;
        var subtitle = so ? (string.IsNullOrWhiteSpace(so.subtitle) ? fallbackSubtitle : so.subtitle) : fallbackSubtitle;
        var body     = so ? (string.IsNullOrWhiteSpace(so.body)     ? fallbackBody     : so.body)     : fallbackBody;
        var icon     = so && so.icon ? so.icon : fallbackIcon;

        if (InfoPanelUI.I == null)
        {
            Debug.LogWarning($"[InfoRouter] No InfoPanelUI in scene for id={infoId}");
            return;
        }

        InfoPanelUI.I.Set(icon, title, subtitle, body);
        InfoPanelUI.I.Open();
    }
}
