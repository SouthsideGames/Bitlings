using System;
using System.Collections.Generic;
using UnityEngine;


public sealed class IronRoster
{
    private readonly IronCareerRunState _state;

    private static bool IsForcedEvolutionEligible(IronMonster monster)
    {
        if (monster == null || monster.def == null) return false;
        if (monster.IsDead) return false;

        var nextForm = monster.def.evolutionForm;
        if (nextForm == null) return false;
        if (ReferenceEquals(nextForm, monster.def)) return false;

        if (monster.def.evolutionLevel > 0 && monster.level < monster.def.evolutionLevel) return false;
        return true;
    }

    public IronRoster(IronCareerRunState state)
    {
        _state = state;
    }

    public IReadOnlyList<IronMonster> Party => _state.party;
    public int ActiveIndex => Mathf.Clamp(_state.activeIndex, 0, Mathf.Max(0, _state.party.Count - 1));

    public bool IsFull => _state.party.Count >= 3;

    public void AddMember(IronMonster m)
    {
        if (m == null || m.def == null) return;
        if (_state.party.Count >= 3) return;
        EnsureHpInitialized(m);
        _state.party.Add(m);
        ClampActiveIndex();
    }

    public void ReplaceMember(int indexToReplace, IronMonster newMember)
    {
        if (newMember == null || newMember.def == null) return;
        if (_state.party.Count <= 0) return;

        indexToReplace = Mathf.Clamp(indexToReplace, 0, _state.party.Count - 1);
        EnsureHpInitialized(newMember);
        _state.party[indexToReplace] = newMember;

        ClampActiveIndex();
    }

    public void RemoveDead()
    {
        for (int i = _state.party.Count - 1; i >= 0; i--)
        {
            var m = _state.party[i];
            if (m == null || m.def == null || m.IsDead)
                _state.party.RemoveAt(i);
        }
        ClampActiveIndex();
    }

    public bool IsPartyEmpty => _state.party.Count <= 0;

    public void RotateActiveAfterWin()
    {
        if (_state.party.Count <= 1) { _state.activeIndex = 0; return; }

        // Rotate forward, skipping any dead (should be removed already, but be safe).
        int start = Mathf.Clamp(_state.activeIndex, 0, _state.party.Count - 1);
        for (int step = 1; step <= _state.party.Count; step++)
        {
            int idx = (start + step) % _state.party.Count;
            var m = _state.party[idx];
            if (m != null && m.def != null && !m.IsDead)
            {
                _state.activeIndex = idx;
                return;
            }
        }

        _state.activeIndex = 0;
    }

    public bool TryForceEvolveActive()
    {
        if (_state.party.Count <= 0) return false;
        int idx = ActiveIndex;
        if (idx < 0 || idx >= _state.party.Count) return false;

        var m = _state.party[idx];
        if (!IsForcedEvolutionEligible(m)) return false;

        var evolved = m.def.evolutionForm;

        float hp01 = m.Hp01;

        m.def = evolved;
        // Keep level
        m.maxHp = Mathf.Max(1f, BattleCalc.CalcHP(m.def, Mathf.Max(1, m.level)));
        m.hp = Mathf.Clamp(m.maxHp * hp01, 0f, m.maxHp);
        // Keep lockedTitle

        _state.party[idx] = m;
        return true;
    }

    public bool CanEvolveAny()
    {
        for (int i = 0; i < _state.party.Count; i++)
        {
            var m = _state.party[i];
            if (!IsForcedEvolutionEligible(m)) continue;
            return true;
        }
        return false;
    }

    public bool TryForceEvolveAtIndex(int idx)
    {
        if (_state.party.Count <= 0) return false;
        idx = Mathf.Clamp(idx, 0, _state.party.Count - 1);

        var m = _state.party[idx];
        if (!IsForcedEvolutionEligible(m)) return false;

        var evolved = m.def.evolutionForm;
        float hp01 = m.Hp01;

        m.def = evolved;
        m.maxHp = Mathf.Max(1f, BattleCalc.CalcHP(m.def, Mathf.Max(1, m.level)));
        m.hp = Mathf.Clamp(m.maxHp * hp01, 0f, m.maxHp);

        _state.party[idx] = m;
        return true;
    }

    public void EnsureHpInitialized(IronMonster m)
    {
        if (m == null) return;
        if (m.def == null) { m.maxHp = 1f; m.hp = 0f; return; }
        m.level = Mathf.Max(1, m.level);
        m.maxHp = Mathf.Max(1f, (m.maxHp > 0.01f) ? m.maxHp : BattleCalc.CalcHP(m.def, m.level));
        m.hp = Mathf.Clamp(m.hp, 0f, m.maxHp);
    }

    private void ClampActiveIndex()
    {
        if (_state.party.Count <= 0) { _state.activeIndex = 0; return; }
        _state.activeIndex = Mathf.Clamp(_state.activeIndex, 0, _state.party.Count - 1);
    }
}
