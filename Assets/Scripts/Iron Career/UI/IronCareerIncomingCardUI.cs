using UnityEngine;

/// <summary>
/// Manages spawning and displaying the incoming recruit card.
/// Attach to the parent Transform where the card should be instantiated.
/// </summary>
public sealed class IronCareerIncomingCardUI : MonoBehaviour
{
    [SerializeField] private IronCareerMonsterCardUI cardPrefab;

    private IronCareerMonsterCardUI _spawnedCard;

    private void OnDestroy()
    {
        if (_spawnedCard) Destroy(_spawnedCard.gameObject);
    }

    public void Bind(IronMonster offer)
    {
        if (_spawnedCard)
        {
            Destroy(_spawnedCard.gameObject);
            _spawnedCard = null;
        }

        if (cardPrefab == null)
        {
            Debug.LogWarning("[IronCareerIncomingCardUI] Missing cardPrefab; incoming recruit card cannot be shown.");
            return;
        }

        _spawnedCard = Instantiate(cardPrefab, transform);
        _spawnedCard.Bind(offer, isLocked: true, isSelectable: false);
    }

    public void Clear()
    {
        if (_spawnedCard)
        {
            Destroy(_spawnedCard.gameObject);
            _spawnedCard = null;
        }
    }
}
