using System;

namespace AgentCommandEnvironment.Core.Constants;

public static class WorkBudgetSettings
{
        public const Double MinimumDelegationBudgetFraction = 0.05;

    public static Boolean HasMeaningfulDelegation(Double delegationFraction)
    {
        return delegationFraction > MinimumDelegationBudgetFraction;
    }
}
