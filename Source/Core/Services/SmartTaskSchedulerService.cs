using AgentCommandEnvironment.Core.Models;

namespace AgentCommandEnvironment.Core.Services;

public sealed class SmartTaskSchedulerService
{
    private readonly LinkedList<SmartTask> pendingTasks = new LinkedList<SmartTask>();

    public Int32 Count
    {
        get { return pendingTasks.Count; }
    }

    public void Push(SmartTask task)
    {
        if (task == null)
        {
            return;
        }

        pendingTasks.AddLast(task);
    }

    public SmartTask Pop()
    {
        if (pendingTasks.Count == 0)
        {
            throw new InvalidOperationException("No smart tasks are pending.");
        }

        LinkedListNode<SmartTask>? tail = pendingTasks.Last;
        SmartTask value = tail!.Value;
        pendingTasks.RemoveLast();
        return value;
    }
}

