using System.Collections.Specialized;
using System.ComponentModel;

namespace AgentCommandEnvironment.Presentation.Views;

internal sealed class SupervisorTaskSubscription
{
    public PropertyChangedEventHandler? PropertyHandler { get; init; }
    public NotifyCollectionChangedEventHandler? SubtasksHandler { get; init; }
}
