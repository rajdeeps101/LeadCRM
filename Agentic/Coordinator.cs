using Microsoft.Extensions.Options;

public sealed class AgentOrchestrator
{
    private readonly LeadStore _store;
    private readonly string _provider;
    private readonly PdfValidationAgent _pdfValidationAgent;
    private readonly IReadOnlyDictionary<string, AgentTaskDefinition> _tasks;
    private readonly IReadOnlyDictionary<string, ICrmAgent[]> _workflows;

    public AgentOrchestrator(
        LeadStore store,
        IEnumerable<ICrmAgent> registeredAgents,
        PdfValidationAgent pdfValidationAgent,
        IOptions<AgentRuntimeOptions> options)
    {
        _store = store;
        _pdfValidationAgent = pdfValidationAgent;
        var runtime = options.Value;
        if (!string.Equals(runtime.Provider, "local-rules", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"AgentRuntime:Provider '{runtime.Provider}' is not registered. The supported provider is 'local-rules'.");
        }

        _provider = "local-rules";
        var agentRegistry = registeredAgents.ToDictionary(
            agent => agent.Name,
            StringComparer.OrdinalIgnoreCase);
        var tasks = new Dictionary<string, AgentTaskDefinition>(StringComparer.OrdinalIgnoreCase);
        var workflows = new Dictionary<string, ICrmAgent[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var workflow in runtime.Workflows)
        {
            if (string.IsNullOrWhiteSpace(workflow.Task) ||
                string.IsNullOrWhiteSpace(workflow.Description) ||
                workflow.Agents.Count == 0)
            {
                throw new InvalidOperationException(
                    "Each AgentRuntime workflow must define a task, description, and at least one agent.");
            }

            var agents = workflow.Agents.Select(name =>
                agentRegistry.TryGetValue(name, out var agent)
                    ? agent
                    : throw new InvalidOperationException(
                        $"AgentRuntime workflow '{workflow.Task}' references unregistered agent '{name}'."))
                .ToArray();

            var definition = new AgentTaskDefinition(
                workflow.Task,
                workflow.Description,
                workflow.Agents.ToArray());
            if (!tasks.TryAdd(workflow.Task, definition) || !workflows.TryAdd(workflow.Task, agents))
            {
                throw new InvalidOperationException(
                    $"AgentRuntime contains duplicate workflow task '{workflow.Task}'.");
            }
        }

        if (workflows.Count == 0)
        {
            throw new InvalidOperationException("AgentRuntime must define at least one workflow.");
        }

        _tasks = tasks;
        _workflows = workflows;
    }

    public string Provider => _provider;
    public string PdfValidationAgentName => PdfValidationAgent.Name;
    public IReadOnlyList<AgentTaskDefinition> AvailableTasks => _tasks.Values.ToArray();

    public async Task<PdfValidationFinding?> ValidatePdfAsync(
        Guid leadId,
        Stream pdfStream,
        CancellationToken cancellationToken)
    {
        var lead = await _store.GetByIdAsync(leadId);
        return lead is null
            ? null
            : await _pdfValidationAgent.ValidateAsync(lead, pdfStream, cancellationToken);
    }

    public async Task<AgentRunResult?> RunAsync(AgentTaskRequest request)
    {
        if (request.Task is null || !_workflows.TryGetValue(request.Task.Trim(), out var agents))
        {
            return null;
        }

        var lead = await _store.GetByIdAsync(request.LeadId);
        if (lead is null)
        {
            return null;
        }

        var startedAt = DateTimeOffset.UtcNow;
        var findings = agents.Select(agent => agent.Run(lead)).ToArray();
        var completedAt = DateTimeOffset.UtcNow;

        return new AgentRunResult(
            RunId: Guid.NewGuid(),
            LeadId: lead.Id,
            Task: request.Task.Trim(),
            Provider: _provider,
            StartedAt: startedAt,
            CompletedAt: completedAt,
            RequiresHumanApproval: findings.Any(finding => finding.RequiresHumanApproval),
            Steps: findings);
    }
}
