using UnityEngine;

[CreateAssetMenu(menuName = "Data/Recycle/Recipe", fileName = "RecycleRecipe_")]
public class RecycleRecipeSO : ScriptableObject
{
    [Header("Identity")]
    public string recipeId;       // optional, for tracking/logs
    public string displayName;    // shown in the prefab (e.g., "Junk to Juice")

    [Header("Conversion")]
    public ResourceType fromType;
    public int fromAmount = 10;

    public ResourceType toType;
    public int toAmount = 1;
}


