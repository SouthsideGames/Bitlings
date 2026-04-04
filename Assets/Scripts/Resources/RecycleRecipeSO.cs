using UnityEngine;

[CreateAssetMenu(menuName = "Data/Recycle/Recipe", fileName = "RecycleRecipe_")]
public class RecycleRecipeSO : ScriptableObject
{
    [Header("Identity")]
    public string recipeId;     
    public string displayName;   

    [Header("Conversion")]
    public ResourceType fromType;
    public int fromAmount = 10;

    public ResourceType toType;
    public int toAmount = 1;
}


