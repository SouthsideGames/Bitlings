using UnityEngine;

public class PanelButtonUI : MonoBehaviour
{
    public PanelId target;

    public enum ActionType { Show, Hide, Toggle }
    public ActionType action = ActionType.Show;

    [Header("Optional")]
    [Tooltip("If true, when opening this panel, the currently open panel will close first.")]
    public bool closeOthersFirst = false;

    public void Execute()
    {
        if (!UIManager.I) return;

        if (closeOthersFirst && action == ActionType.Show)
        {
            UIManager.I.CloseAllExcept(target);
        }

        switch (action)
        {
            case ActionType.Show:   UIManager.I.Show(target);   break;
            case ActionType.Hide:   UIManager.I.Hide(target);   break;
            case ActionType.Toggle: UIManager.I.Toggle(target); break;
        }
    }
}
