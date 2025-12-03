using System.Globalization;

namespace AgentCommandEnvironment.Core.Models;

public sealed class PolicyDecisionItem
{
    public DateTime TimestampUtc { get; set; }
    public String Command { get; set; } = String.Empty;
    public Boolean Allowed { get; set; }
    public String Reason { get; set; } = String.Empty;
    public String Risk { get; set; } = String.Empty;

    public String TimestampDisplay
    {
        get { return TimestampUtc.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture); }
    }

    public String DecisionDisplay
    {
        get { return Allowed ? "Allowed" : "Blocked"; }
    }
}

