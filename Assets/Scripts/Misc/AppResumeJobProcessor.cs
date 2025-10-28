using UnityEngine;

public class AppResumeJobProcessor : MonoBehaviour
{
    [Tooltip("Save on pause so offline accrual has a precise starting timestamp.")]
    public bool saveOnPause = true;

    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            SaveManager.OnResume();
 
        }
    }

    void OnApplicationPause(bool pause)
    {
        if (pause)
        {
            if (saveOnPause) SaveManager.Save();
        }
        else
        {
            SaveManager.OnResume();
      
        }
    }
}
