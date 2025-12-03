using System.Globalization;

namespace AgentCommandEnvironment.Core.Results;

public static class UsageFormattingResult
{
    public static String FormatCompactNumber(Int64 value)
    {
        if (value >= 1_000_000_000)
        {
            return (value / 1_000_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "B";
        }

        if (value >= 1_000_000)
        {
            return (value / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
        }

        if (value >= 1_000)
        {
            return (value / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "k";
        }

        return value.ToString(CultureInfo.InvariantCulture);
    }
}

