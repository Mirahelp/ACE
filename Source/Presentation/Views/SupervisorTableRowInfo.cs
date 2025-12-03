using AgentCommandEnvironment.Core.Models;
using System;
using System.Collections.Generic;

namespace AgentCommandEnvironment.Presentation.Views;

internal readonly struct SupervisorTableRowInfo
{
    public SupervisorTableRowInfo(SmartTask task, IReadOnlyList<SmartTask>? ancestors)
    {
        Task = task ?? throw new ArgumentNullException(nameof(task));
        Ancestors = ancestors ?? Array.Empty<SmartTask>();
    }

    public SmartTask Task { get; }
    public IReadOnlyList<SmartTask> Ancestors { get; }
    public Int32 Depth => Ancestors.Count;
}
