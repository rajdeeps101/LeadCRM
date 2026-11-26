public sealed class Lead
{
    public Guid Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Source { get; set; }
    public string Status { get; set; } = LeadStatuses.New;
    public DateTimeOffset CreatedAt { get; set; }
    public List<LeadDocument> Documents { get; set; } = [];
}

public sealed class LeadDocument
{
    public Guid Id { get; set; }
    public string FileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Status { get; set; } = DocumentStatuses.Pending;
    public DateTimeOffset SubmittedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewNote { get; set; }
    public string? ValidationStatus { get; set; }
    public string? ValidationMessage { get; set; }
    public DateTimeOffset? ValidatedAt { get; set; }
}

public sealed class CreateLeadRequest
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Company { get; set; }
    public string? Source { get; set; }
}

public sealed class UpdateLeadStatusRequest
{
    public string? Status { get; set; }
}

public sealed class ReviewDocumentRequest
{
    public string? Status { get; set; }
    public string? Note { get; set; }
}

public static class LeadStatuses
{
    public const string New = "New";

    public static readonly string[] All =
    [
        New,
        "Contacted",
        "Qualified",
        "Converted",
        "Lost"
    ];
}

public static class DocumentStatuses
{
    public const string Pending = "Pending";
    public const string Verified = "Verified";
    public const string Rejected = "Rejected";

    public static readonly string[] All = [Pending, Verified, Rejected];
}

public static class DocumentLimits
{
    public const long MaxFileSizeBytes = 10 * 1024 * 1024;
    public const int MaxReviewNoteLength = 1000;
}
