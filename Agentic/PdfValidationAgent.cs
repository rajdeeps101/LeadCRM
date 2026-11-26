using Microsoft.Extensions.Options;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Exceptions;

public static class PdfValidationStatuses
{
    public const string Passed = "Passed";
    public const string NameMismatch = "NameMismatch";
    public const string TextUnavailable = "TextUnavailable";
    public const string StructurallyInvalid = "StructurallyInvalid";
}

public sealed class PdfValidationFinding
{
    public string Status { get; init; } = "";
    public string Summary { get; init; } = "";
    public bool IsStructurallyValid { get; init; }
    public bool? LeadNameMatched { get; init; }
    public int? PageCount { get; init; }
    public DateTimeOffset ValidatedAt { get; init; }
}

public sealed class PdfValidationOptions
{
    public int MaxPages { get; set; } = 10;
}

public sealed class PdfValidationAgent
{
    public const string Name = "pdf-validation";

    private readonly PdfValidationOptions _options;
    private readonly GoogleGeminiFullNameVerificationAgent _fullNameVerificationAgent;

    public PdfValidationAgent(
        IOptions<PdfValidationOptions> options,
        GoogleGeminiFullNameVerificationAgent fullNameVerificationAgent)
    {
        _options = options.Value;
        _fullNameVerificationAgent = fullNameVerificationAgent;
        if (_options.MaxPages <= 0)
        {
            throw new InvalidOperationException("PDF validation limits must be positive.");
        }
    }

    public async Task<PdfValidationFinding> ValidateAsync(
        Lead lead,
        Stream pdfStream,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var buffer = new MemoryStream();
            await pdfStream.CopyToAsync(buffer, cancellationToken);
            var pdfBytes = buffer.ToArray();

            using var pdfData = new MemoryStream(pdfBytes, writable: false);
            using var pdf = PdfDocument.Open(pdfData, ParsingOptions.LenientParsingOff);
            var pageCount = pdf.NumberOfPages;
            if (pageCount <= 0)
            {
                return InvalidPdf("The PDF contains no pages.");
            }

            if (pdf.IsEncrypted)
            {
                return TextUnavailable(
                    pageCount,
                    "The PDF is encrypted; a human reviewer must inspect it.");
            }

            if (pageCount > _options.MaxPages)
            {
                return TextUnavailable(
                    pageCount,
                    $"The PDF exceeds the {_options.MaxPages}-page validation limit; a human reviewer must inspect it.");
            }

            if (string.IsNullOrWhiteSpace(lead.FirstName) ||
                string.IsNullOrWhiteSpace(lead.LastName))
            {
                return TextUnavailable(
                    pageCount,
                    "The lead's full name is unavailable for comparison; a human reviewer must inspect the document.");
            }

            var fullName = $"{lead.FirstName.Trim()} {lead.LastName.Trim()}";
            var isFullNamePresent = await _fullNameVerificationAgent.IsFullNamePresentAsync(
                lead.Id,
                fullName,
                pdfBytes,
                cancellationToken);

            return isFullNamePresent
                ? new PdfValidationFinding
                {
                    Status = PdfValidationStatuses.Passed,
                    Summary = "Google Gemini OCR found the lead's full name in the PDF. The document is automatically marked Verified.",
                    IsStructurallyValid = true,
                    LeadNameMatched = true,
                    PageCount = pageCount,
                    ValidatedAt = DateTimeOffset.UtcNow
                }
                : new PdfValidationFinding
                {
                    Status = PdfValidationStatuses.NameMismatch,
                    Summary = "Google Gemini OCR did not find the lead's full name in the PDF. The document remains Pending for human review.",
                    IsStructurallyValid = true,
                    LeadNameMatched = false,
                    PageCount = pageCount,
                    ValidatedAt = DateTimeOffset.UtcNow
                };
        }
        catch (PdfDocumentFormatException)
        {
            return InvalidPdf("The PDF could not be parsed as a structurally valid document.");
        }
        catch (PdfDocumentEncryptedException)
        {
            return TextUnavailable(null, "The PDF is encrypted; a human reviewer must inspect it.");
        }
    }

    private static PdfValidationFinding InvalidPdf(string summary) =>
        new()
        {
            Status = PdfValidationStatuses.StructurallyInvalid,
            Summary = summary,
            IsStructurallyValid = false,
            ValidatedAt = DateTimeOffset.UtcNow
        };

    private static PdfValidationFinding TextUnavailable(int? pageCount, string summary) =>
        new()
        {
            Status = PdfValidationStatuses.TextUnavailable,
            Summary = summary,
            IsStructurallyValid = true,
            PageCount = pageCount,
            ValidatedAt = DateTimeOffset.UtcNow
        };

}
