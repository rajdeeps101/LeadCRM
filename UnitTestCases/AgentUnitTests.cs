using Xunit;

public sealed class LeadReadinessAgentTests
{
    private readonly LeadReadinessAgent _agent = new();

    [Fact]
    public void Run_ReportsAllOptionalFieldsMissing()
    {
        var finding = _agent.Run(new Lead
        {
            FirstName = "Taylor",
            LastName = "Jordan",
            Email = "taylor@example.test"
        });

        Assert.Contains("0 of 3", finding.Summary);
        Assert.Contains("no optional profile details", finding.Evidence[0], StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Company, Phone, Lead source", finding.Recommendation);
        Assert.False(finding.RequiresHumanApproval);
    }

    [Fact]
    public void Run_TreatsWhitespaceAsMissingAndReportsPresentFields()
    {
        var finding = _agent.Run(new Lead
        {
            Company = "  ",
            Phone = "555-0100",
            Source = null
        });

        Assert.Contains("1 of 3", finding.Summary);
        Assert.Equal("Optional details recorded: Phone.", finding.Evidence[0]);
        Assert.Contains("Company, Lead source", finding.Recommendation);
    }

    [Fact]
    public void Run_ReportsCompleteOptionalProfile()
    {
        var finding = _agent.Run(new Lead
        {
            Company = "Acme",
            Phone = "555-0100",
            Source = "Referral"
        });

        Assert.Contains("3 of 3", finding.Summary);
        Assert.Equal("Optional details recorded: Company, Phone, Lead source.", finding.Evidence[0]);
        Assert.DoesNotContain("Consider collecting", finding.Recommendation);
    }
}

public sealed class DocumentReviewAssistantTests
{
    private readonly DocumentReviewAssistant _assistant = new();

    [Fact]
    public void Run_WithNoDocuments_ReportsNoReviewAndNoApprovalRequirement()
    {
        var finding = _assistant.Run(new Lead());

        Assert.Equal("No document review is available for this lead.", finding.Summary);
        Assert.Equal("No documents are attached to this lead.", finding.Evidence[0]);
        Assert.False(finding.RequiresHumanApproval);
    }

    [Fact]
    public void Run_WithPendingAndReviewedDocuments_SummarizesStatesAndRequiresApproval()
    {
        var finding = _assistant.Run(new Lead
        {
            Documents =
            [
                new LeadDocument { Status = DocumentStatuses.Pending },
                new LeadDocument { Status = DocumentStatuses.Verified },
                new LeadDocument { Status = DocumentStatuses.Rejected }
            ]
        });

        Assert.Contains("3 document(s)", finding.Summary);
        Assert.Contains("1 pending, 1 verified, 1 rejected", finding.Evidence[1]);
        Assert.True(finding.RequiresHumanApproval);
        Assert.Contains("human reviewer", finding.Recommendation);
        Assert.Contains("contents were not analyzed", finding.Summary);
    }

    [Fact]
    public void Run_WithNoPendingDocuments_DoesNotRequireApproval()
    {
        var finding = _assistant.Run(new Lead
        {
            Documents =
            [
                new LeadDocument { Status = DocumentStatuses.Verified },
                new LeadDocument { Status = DocumentStatuses.Rejected }
            ]
        });

        Assert.False(finding.RequiresHumanApproval);
        Assert.Contains("Keep document decisions under human review", finding.Recommendation);
    }
}
