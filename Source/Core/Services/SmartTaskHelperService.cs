using AgentCommandEnvironment.Core.Constants;
using AgentCommandEnvironment.Core.Models;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using AgentCommandEnvironment.Core.Enums;

namespace AgentCommandEnvironment.Core.Services;

public static class SmartTaskHelperService
{
    public static String HashIntent(String? intent)
    {
        if (String.IsNullOrWhiteSpace(intent))
        {
            return String.Empty;
        }

        using SHA256 sha = SHA256.Create();
        Byte[] bytes = Encoding.UTF8.GetBytes(intent);
        Byte[] hash = sha.ComputeHash(bytes);
        StringBuilder builder = new StringBuilder(hash.Length * 2);
        for (Int32 index = 0; index < hash.Length; index++)
        {
            builder.Append(hash[index].ToString("x2", CultureInfo.InvariantCulture));
        }

        return builder.ToString();
    }

    public static Boolean ShouldForceDecomposition(SmartTask smartTask)
    {
        if (smartTask == null)
        {
            return false;
        }

        return WorkBudgetSettings.HasMeaningfulDelegation(smartTask.DelegationFraction);
    }

    public static SmartTaskStrategyOptions NormalizeDelegatorStrategy(String? strategyText, Boolean allowDecomposition)
    {
        if (String.IsNullOrWhiteSpace(strategyText))
        {
            return SmartTaskStrategyOptions.Execute;
        }

        String normalized = strategyText.Trim().ToLowerInvariant();
        if (normalized == "skip")
        {
            return SmartTaskStrategyOptions.Skip;
        }

        if (normalized == "research")
        {
            return SmartTaskStrategyOptions.Research;
        }

        if (normalized == "decompose" && allowDecomposition)
        {
            return SmartTaskStrategyOptions.Decompose;
        }

        return SmartTaskStrategyOptions.Execute;
    }
}


