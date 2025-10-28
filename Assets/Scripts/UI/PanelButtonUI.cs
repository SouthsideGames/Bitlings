using UnityEngine;

public class PanelButton : MonoBehaviour
{
    public PanelId target;
    public enum ActionType { Show, Hide, Toggle }
    public ActionType action = ActionType.Show;

    public void Execute()
    {
        if (!UIManager.I) return;
        switch (action)
        {
            case ActionType.Show:   UIManager.I.Show(target);   break;
            case ActionType.Hide:   UIManager.I.Hide(target);   break;
            case ActionType.Toggle: UIManager.I.Toggle(target); break;
        }
    }
}
