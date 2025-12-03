using AgentCommandEnvironment.Core.Models;
using System;
using System.Collections.Generic;

namespace AgentCommandEnvironment.Core.Results;

public sealed class FailureResolutionResult
{
    public Action<SmartTaskExecutionContext, String>? AppendTaskLog { get; init; }
    public Action<SmartTaskExecutionContext, List<AgentPlannedTask>>? InsertTasksAfter { get; init; }
}
