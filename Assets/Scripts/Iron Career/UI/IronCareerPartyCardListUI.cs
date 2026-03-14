using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages spawning, displaying, and selecting party member cards.
/// Attach to the parent Transform (e.g. ScrollRect Content with VerticalLayoutGroup).
/// </summary>
public sealed class IronCareerPartyCardListUI : MonoBehaviour
{
    [SerializeField] private IronCareerMonsterCardUI cardPrefab;

    /// <summary>Fired when the selected index changes. -1 means nothing selected.</summary>
    public event Action<int> OnSelectionChanged;

    private readonly List<IronCareerMonsterCardUI> _spawnedCards = new List<IronCareerMonsterCardUI>(3);
    private int _selectedIndex = -1;

    public int SelectedIndex => _selectedIndex;

    private void OnDestroy()
    {
        for (int i = 0; i < _spawnedCards.Count; i++)
            if (_spawnedCards[i]) Destroy(_spawnedCards[i].gameObject);
        _spawnedCards.Clear();
    }

    /// <summary>
    /// Build selectable party cards. Each card can be tapped to select it.
    /// </summary>
    public void Bind(IReadOnlyList<IronMonster> party)
    {
        Clear();
        _selectedIndex = -1;

        if (cardPrefab == null)
        {
            Debug.LogWarning("[IronCareerPartyCardListUI] Missing cardPrefab; cannot build party list.");
            return;
        }

        int count = Mathf.Min(3, party != null ? party.Count : 0);
        for (int i = 0; i < count; i++)
        {
            var m = party[i];
            if (m == null || m.def == null) continue;

            var card = Instantiate(cardPrefab, transform);
            card.Bind(m, isLocked: false, isSelectable: true);
            card.SetSelected(false);

            int idx = i;
            card.SetOnClick(() => Select(idx));

            _spawnedCards.Add(card);
        }
    }

    public void Select(int index)
    {
        _selectedIndex = index;
        for (int i = 0; i < _spawnedCards.Count; i++)
        {
            if (_spawnedCards[i]) _spawnedCards[i].SetSelected(i == _selectedIndex);
        }
        OnSelectionChanged?.Invoke(_selectedIndex);
    }

    public void ClearSelection()
    {
        _selectedIndex = -1;
        for (int i = 0; i < _spawnedCards.Count; i++)
        {
            if (_spawnedCards[i]) _spawnedCards[i].SetSelected(false);
        }
    }

    public void Clear()
    {
        for (int i = 0; i < _spawnedCards.Count; i++)
        {
            if (_spawnedCards[i]) Destroy(_spawnedCards[i].gameObject);
        }
        _spawnedCards.Clear();
        _selectedIndex = -1;
    }
}
