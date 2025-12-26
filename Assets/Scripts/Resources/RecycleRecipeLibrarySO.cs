using UnityEngine;

[CreateAssetMenu(menuName = "Data/Recycle/Recipe Library", fileName = "RecycleRecipeLibrary")]
public class RecycleRecipeLibrarySO : ScriptableObject
{
    public RecycleRecipeSO[] recipes;
}