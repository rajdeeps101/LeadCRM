public sealed class LeadReadinessAgent : ICrmAgent
{
    public string Name => "lead-readiness";

    public AgentFinding Run(Lead lead)
    {
        var optionalFields = new (string Name, string? Value)[]
        {
            ("Company", lead.Company),
            ("Phone", lead.Phone),
            ("Lead source", lead.Source)
        };
        var present = optionalFields
            .Where(field => !string.IsNullOrWhiteSpace(field.Value))
            .Select(field => field.Name)
            .ToArray();
        var missing = optionalFields
            .Where(field => string.IsNullOrWhiteSpace(field.Value))
            .Select(field => field.Name)
            .ToArray();

        return new AgentFinding(
            Agent: "lead-readiness",
            Summary: $"{present.Length} of {optionalFields.Length} optional profile details are recorded. This is a completeness check, not a sales prediction.",
            Evidence: present.Length == 0
                ? ["Required name and email are present; no optional profile details are recorded."]
                : [$"Optional details recorded: {string.Join(", ", present)}."],
            Unknowns:
            [
                "Budget, purchase intent, decision authority, and timing are not established by these fields."
            ],
            Recommendation: missing.Length == 0
                ? "The profile has the available optional details; assess business fit using your normal criteria."
                : $"Consider collecting: {string.Join(", ", missing)}.",
            Confidence: "High for field presence; not a prediction of conversion.",
            RequiresHumanApproval: false);
    }
}
