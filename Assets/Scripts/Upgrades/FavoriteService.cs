using UnityEngine;
using System.Collections.Generic;

public static class FavoriteService
{
    private static List<string> GetList()
    {
        var data = SaveManager.Data;
        if (data == null)
            return null;

        // Use the LIST as the authoritative, serialized source
        if (data.favoriteMonsterIdsList == null)
            data.favoriteMonsterIdsList = new List<string>();

        return data.favoriteMonsterIdsList;
    }

    public static bool IsFavorite(string monsterDefId)
    {
        if (SaveManager.Data == null || string.IsNullOrEmpty(monsterDefId))
            return false;

        var list = GetList();
        return list != null && list.Contains(monsterDefId);
    }

    public static void ToggleFavorite(string monsterDefId)
    {
        if (SaveManager.Data == null || string.IsNullOrEmpty(monsterDefId))
            return;

        var list = GetList();
        if (list == null)
            return;

        if (list.Contains(monsterDefId))
            list.Remove(monsterDefId);
        else
            list.Add(monsterDefId);

        SaveManager.Save();
        GameEvents.FavoritesChanged?.Invoke();
    }
}
