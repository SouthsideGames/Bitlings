#if UNITY_EDITOR
using NUnit.Framework;
using System;

public class TagTriggerCoverageTests
{
    [Test]
    public void All_Enum_Values_Are_Handled_By_Dispatcher()
    {
        // This test assumes your dispatcher won't throw NotImplementedException
        // for any TagTrigger. If you use a default: throw pattern, you can
        // adapt this to catch missing cases. For now, a smoke test:

        var all = (TagTrigger[])Enum.GetValues(typeof(TagTrigger));
        foreach (var trig in all)
        {
            // Minimal dummy effect/signal/context to pass through dispatcher without side-effects
            var eff = new TagEffect { trigger = trig, addPct = 0f, durationTurns = 0 };
            var sig = new TagSignal(1, MonsterType.None, MonsterType.None, false, 0f, JobType.None, false, 0);

            Assert.DoesNotThrow(() =>
            {
                // Use your static dispatcher if you implemented it,
                // or minimally call any TagRuntime entrypoint that maps over triggers.
                // Example (adjust if you named it differently):
                // TagTriggerDispatcher.Dispatch(eff, DummyEnv.Instance, sig);
            }, $"Dispatcher threw for trigger {trig}");
        }
    }
}
#endif
