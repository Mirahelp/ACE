using AgentCommandEnvironment.Core.Models;
using System;

namespace AgentCommandEnvironment.Core.Results;

public sealed class RepairOrchestrationResult
{
    public Action<SmartTaskExecutionContext, String>? AppendTaskLog { get; init; }
}
