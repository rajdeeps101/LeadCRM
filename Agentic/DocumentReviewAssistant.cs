public sealed class DocumentReviewAssistant : ICrmAgent
{
    public string Name => "document-review-assistant";

    public AgentFinding Run(Lead lead)
    {
        var pending = lead.Documents.Count(document => document.Status == DocumentStatuses.Pending);
        var verified = lead.Documents.Count(document => document.Status == DocumentStatuses.Verified);
        var rejected = lead.Documents.Count(document => document.Status == DocumentStatuses.Rejected);

        var evidence = lead.Documents.Count == 0
            ? new[] { "No documents are attached to this lead." }
            : new[]
            {
                $"Documents attached: {lead.Documents.Count}.",
                $"Current states: {pending} pending, {verified} verified, {rejected} rejected."
            };

        return new AgentFinding(
            Agent: "document-review-assistant",
            Summary: lead.Documents.Count == 0
                ? "No document review is available for this lead."
                : $"Summarized review metadata for {lead.Documents.Count} document(s); file contents were not analyzed.",
            Evidence: evidence,
            Unknowns: ["Document contents and authenticity have not been independently checked by this agent."],
            Recommendation: pending > 0
                ? "A human reviewer should inspect pending documents and choose the review status."
                : "Keep document decisions under human review if a new submission or re-review is needed.",
            Confidence: "High for stored metadata only; no content verification was performed.",
            RequiresHumanApproval: pending > 0);
    }
}
