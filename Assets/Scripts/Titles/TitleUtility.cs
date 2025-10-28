using UnityEngine;

public static class TitleUtility
{
    public static float ApplyOp(float current, OpKind op, float value)
    {
        switch (op)
        {
            case OpKind.Add:      return current + value;
            case OpKind.Subtract: return current - value;
            case OpKind.Multiply: return current * value;
            case OpKind.Divide:   return value == 0f ? current : current / value;
            default:              return current;
        }
    }

    public static bool CheckCondition(ConditionalBoosterTitleSO t, in TitleContext ctx)
    {
        switch (t.condition)
        {
            case ConditionKind.HealthBelowPercent:
                return ctx.selfHp01 <= Mathf.Clamp01(t.threshold01);

            case ConditionKind.HealthAbovePercent:
                return ctx.selfHp01 >= Mathf.Clamp01(t.threshold01);

            case ConditionKind.AllyCountBelow:
                return ctx.allyCount < Mathf.Max(0, t.countN);

            case ConditionKind.WinStreakAbove:
                return ctx.winStreak > Mathf.Max(0, t.countN);

            default:
                return false;
        }
    }
}
