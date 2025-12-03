using AgentCommandEnvironment.Core.Enums;
namespace AgentCommandEnvironment.Core.Models;

public sealed class PlannerRequestContext
{
    public PlannerInvocationOptions InvocationKind { get; private set; }
    public String? AssignmentTitle { get; private set; }
    public String AssignmentPrompt { get; private set; }
    public String? WorkspaceContext { get; private set; }
    public AgentPlannedTask? ParentTask { get; private set; }
    public Int32 Depth { get; private set; }
    public Double WorkRetentionFraction { get; private set; }
    public Double DelegationFraction { get; private set; }
    public Boolean AllowDecomposition { get; set; }
    public IReadOnlyList<String> AncestorSummaries
    {
        get { return ancestorSummaries; }
    }

    private readonly List<String> ancestorSummaries;

    private PlannerRequestContext(String? assignmentTitle, String assignmentPrompt, String? workspaceContext, Double workRetentionFraction, Double delegationFraction)
    {
        AssignmentTitle = assignmentTitle;
        AssignmentPrompt = assignmentPrompt ?? String.Empty;
        WorkspaceContext = workspaceContext;
        ancestorSummaries = new List<String>();
        WorkRetentionFraction = Math.Clamp(workRetentionFraction, 0.0, 1.0);
        DelegationFraction = Math.Clamp(delegationFraction, 0.0, 1.0);
        AllowDecomposition = true;
    }

    private PlannerRequestContext(PlannerRequestContext template)
    {
        AssignmentTitle = template.AssignmentTitle;
        AssignmentPrompt = template.AssignmentPrompt;
        WorkspaceContext = template.WorkspaceContext;
        ancestorSummaries = new List<String>(template.ancestorSummaries);
        WorkRetentionFraction = template.WorkRetentionFraction;
        DelegationFraction = template.DelegationFraction;
        AllowDecomposition = template.AllowDecomposition;
    }

    public static PlannerRequestContext CreateRoot(String? assignmentTitle, String assignmentPrompt, String? workspaceContext, Double workRetentionFraction, Double delegationFraction)
    {
        PlannerRequestContext context = new PlannerRequestContext(assignmentTitle, assignmentPrompt, workspaceContext, workRetentionFraction, delegationFraction);
        context.InvocationKind = PlannerInvocationOptions.AssignmentRoot;
        context.ParentTask = null;
        context.Depth = 0;

        if (!String.IsNullOrWhiteSpace(assignmentTitle))
        {
            context.ancestorSummaries.Add("Assignment: " + assignmentTitle.Trim());
        }

        return context;
    }

    public PlannerRequestContext CreateChild(AgentPlannedTask taskToExpand, Double workRetentionFraction, Double delegationFraction)
    {
        PlannerRequestContext child = new PlannerRequestContext(this);
        child.InvocationKind = PlannerInvocationOptions.SubtaskExpansion;
        child.ParentTask = taskToExpand;
        child.Depth = Depth + 1;
        child.WorkRetentionFraction = Math.Clamp(workRetentionFraction, 0.0, 1.0);
        child.DelegationFraction = Math.Clamp(delegationFraction, 0.0, 1.0);

        if (ParentTask != null)
        {
            child.ancestorSummaries.Add(BuildTaskSummary(ParentTask));
        }

        return child;
    }

    private static String BuildTaskSummary(AgentPlannedTask task)
    {
        String label = String.IsNullOrWhiteSpace(task.Label) ? "(no label)" : task.Label!.Trim();
        String type = String.IsNullOrWhiteSpace(task.Type) ? "Task" : task.Type!.Trim();
        String id = String.IsNullOrWhiteSpace(task.Id) ? "(no id)" : task.Id!.Trim();
        return id + " - " + label + " [" + type + "]";
    }
}


