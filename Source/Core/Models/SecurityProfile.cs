using AgentCommandEnvironment.Core.Enums;
namespace AgentCommandEnvironment.Core.Models;

public sealed class SecurityProfile
{
    public Boolean AllowNetwork { get; set; }
    public Boolean AllowInstall { get; set; }
    public Boolean AllowSystemConfiguration { get; set; }
    public PolicyRiskToleranceOptions PolicyRiskToleranceOptions { get; set; } = PolicyRiskToleranceOptions.LowOnly;
}


