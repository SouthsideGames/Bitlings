using UnityEngine;
using UnityEngine.Serialization;

public class PanelButtonUI : MonoBehaviour
{
    public PanelId target;

    public enum ActionType { Show, Hide, Toggle }
    public ActionType action = ActionType.Show;

    [Header("Optional")]
    [Tooltip("If true, when opening this panel, the currently open panel will close first.")]
    public bool closeOthersFirst = false;

    [Header("Close -> Open Panel")]
    [Tooltip("If enabled, after hiding/toggling OFF this target, UIManager will open the panel below.")]
    [FormerlySerializedAs("openHomeAfterClose")]
    public bool openPanelAfterClose = false;

    [Tooltip("Which panel to open after close (defaults to PanelId.Home).")]
    [FormerlySerializedAs("homePanelId")]
    public PanelId panelAfterCloseId = PanelId.Home;

    public void Execute()
    {
        if (!UIManager.I) return;

        // If we are about to SHOW and we want a single-screen behavior, do it here.
        if (closeOthersFirst && action == ActionType.Show)
        {
            UIManager.I.CloseAllExcept(target);
        }

        switch (action)
        {
            case ActionType.Show:
                UIManager.I.Show(target);
                break;

            case ActionType.Hide:
                UIManager.I.Hide(target);
                if (openPanelAfterClose)
                    UIManager.I.Show(panelAfterCloseId);
                break;

            case ActionType.Toggle:
            {
                bool wasOpen = UIManager.I.IsOpen(target);
                UIManager.I.Toggle(target);

                if (openPanelAfterClose && wasOpen)
                    UIManager.I.Show(panelAfterCloseId);
                break;
            }
        }
    }
}