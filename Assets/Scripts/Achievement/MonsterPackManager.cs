using UnityEngine;

public class MonsterPackManager : MonoBehaviour
{
    public static MonsterPackManager I { get; private set; }
    private MonsterPackLibrarySO library;

    void Awake()
    {
        if (I != null && I != this) { Destroy(gameObject); return; }
        I = this;
        if (library == null) library = Resources.Load<MonsterPackLibrarySO>("MonsterPackLibrary");
    }

    public bool IsUnlocked(string packId) => AchievementsSaveStore.Data.unlockedPacks.Contains(packId);

    public bool CanPurchase(string packId, out string reason)
    {
        reason = "";
        var p = library?.Get(packId);
        if (p == null) { reason = "Not found"; return false; }
        if (IsUnlocked(packId)) { reason = "Already unlocked"; return false; }

        int gems = ResourceManager.I.Get(ResourceType.Gems);
        if (gems < p.tokenCost) { reason = "Not enough Gems"; return false; }
        return true;
    }

    public bool Purchase(string packId)
    {
        if (!CanPurchase(packId, out _)) return false;
        var p = library.Get(packId);

        if (!ResourceManager.I.TrySpend(ResourceType.Gems, p.tokenCost)) return false; // ← spend Gems

        if (!AchievementsSaveStore.Data.unlockedPacks.Contains(packId))
            AchievementsSaveStore.Data.unlockedPacks.Add(packId);
        AchievementsSaveStore.Save();

        AchievementService.I?.ForceRecheck();
        GameEvents.ShowRewardPopup?.Invoke(p.displayName, "Pack Unlocked", 0, 0);
        return true;
    }
}
