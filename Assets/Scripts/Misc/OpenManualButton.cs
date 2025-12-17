using UnityEngine;

public class OpenManualButton : MonoBehaviour
{
    private const string MANUAL_URL =
        "https://github.com/SouthsideGames/Bitlings-Manual/wiki";

    public void OpenManual()
    {
        Application.OpenURL(MANUAL_URL);
    }
}
