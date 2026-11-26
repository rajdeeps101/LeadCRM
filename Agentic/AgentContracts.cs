public static class AgentTaskTypes
{
    public const string LeadAssessment = "lead-assessment";
    public const string DocumentReview = "document-review";
}

public sealed class AgentRuntimeOptions
{
    public string Provider { get; set; } = "local-rules";
    public List<AgentWorkflowOptions> Workflows { get; set; } = [];
}

public sealed class AgentWorkflowOptions
{
    public string Task { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Agents { get; set; } = [];
}

public sealed record AgentTaskDefinition(
    string Task,
    string Description,
    IReadOnlyList<string> Agents);

public sealed class AgentTaskRequest
{
    public Guid LeadId { get; set; }
    public string? Task { get; set; }
}

public sealed record AgentFinding(
    string Agent,
    string Summary,
    IReadOnlyList<string> Evidence,
    IReadOnlyList<string> Unknowns,
    string Recommendation,
    string Confidence,
    bool RequiresHumanApproval);

public sealed record AgentRunResult(
    Guid RunId,
    Guid LeadId,
    string Task,
    string Provider,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    bool RequiresHumanApproval,
    IReadOnlyList<AgentFinding> Steps);

public interface ICrmAgent
{
    string Name { get; }
    AgentFinding Run(Lead lead);
}
