using UnityEngine;

public static class InfoRouter
{
    public static void Open(
        string infoId,
        string fallbackTitle = null,
        string fallbackSubtitle = null,
        string fallbackBody = null,
        Sprite fallbackIcon = null)
    {
        var so = InfoLibrarySO.Find(infoId);

        var title    = so ? (string.IsNullOrWhiteSpace(so.title)    ? fallbackTitle    : so.title)    : fallbackTitle;
        var subtitle = so ? (string.IsNullOrWhiteSpace(so.subtitle) ? fallbackSubtitle : so.subtitle) : fallbackSubtitle;
        var body     = so ? (string.IsNullOrWhiteSpace(so.body)     ? fallbackBody     : so.body)     : fallbackBody;

        // If the Info panel starts inactive, InfoPanelUI.I will be null until it's activated.
        if (InfoPanelUI.I == null)
        {
            if (UIManager.I != null)
            {
                // This will SetActive(true) and run Awake/OnEnable on InfoPanelUI immediately.
                UIManager.I.Show(PanelId.Info);
            }
        }

        // Still null? Then the scene does not have an InfoPanelUI on the PanelId.Info root (or not in UIManager list).
        if (InfoPanelUI.I == null)
        {
            #if UNITY_EDITOR || DEVELOPMENT_BUILD
            Debug.LogWarning(
            #endif
                $"[InfoRouter] Cannot open info '{infoId}' because InfoPanelUI.I is null. " +
                $"Ensure the Info panel root is registered in UIManager as PanelId.Info and contains an InfoPanelUI component.");
            return;
        }

        InfoPanelUI.I.Set(title, subtitle, body);
        InfoPanelUI.I.Open();
    }
}