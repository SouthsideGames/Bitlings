using UnityEngine;

public static class InfoRouter
{
    public static void Open(string infoId, string fallbackTitle = null, string fallbackSubtitle = null, string fallbackBody = null, Sprite fallbackIcon = null)
    {
        var so = InfoLibrarySO.Find(infoId);

        var title    = so ? (string.IsNullOrWhiteSpace(so.title)    ? fallbackTitle    : so.title)    : fallbackTitle;
        var subtitle = so ? (string.IsNullOrWhiteSpace(so.subtitle) ? fallbackSubtitle : so.subtitle) : fallbackSubtitle;
        var body     = so ? (string.IsNullOrWhiteSpace(so.body)     ? fallbackBody     : so.body)     : fallbackBody;

        if (InfoPanelUI.I == null)
           return;

        InfoPanelUI.I.Set(title, subtitle, body);
        InfoPanelUI.I.Open();
    }
}
