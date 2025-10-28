using UnityEngine;

public class TagLibraryBootstrap : MonoBehaviour
{
    private TagLibrarySO library;
    
    void Awake()
    {
        if (library == null)
            library = Resources.Load<TagLibrarySO>("TagLibrary");

    }
}
