using UnityEngine;
using System.Collections;

public partial class EncounterManager
{
    IEnumerator AutoLoop()
    {
        while (autoMode)
        {
            if (!inBattle)
            {
                if (!HasHealthyMonsters())
                {
                    EmitStatus("AUTO stopped: no healthy team members.", LogScope.System);
                    StopAuto_NoEnergy();
                    yield break;
                }

                if (!autoRunPaidEnergy)
                {
                    if (!HasEnergy()) { StopAuto_NoEnergy(); yield break; }
                    if (!SpendEnergy()) { StopAuto_NoEnergy(); yield break; }
                    autoRunPaidEnergy = true;
                }

                StartEncounter(false);
            }

            yield return new WaitForSeconds(autoPollSeconds);
        }
    }

    void StopAuto_NoEnergy()
    {
        if (!autoMode) return;
        autoMode = false;
        autoRunPaidEnergy = false;

        IdleBattleManager.I?.DisableAuto();

        if (autoLoopCo != null) { StopCoroutine(autoLoopCo); autoLoopCo = null; }

        PostBattleSummaryManager.I?.NotifyEnergyDepleted();
        PostBattleSummaryManager.I?.SetAutoBattling(false);

        EmitStatus("AUTO stopped: no energy.", LogScope.System);
        OnStateChanged?.Invoke();
    }
}
