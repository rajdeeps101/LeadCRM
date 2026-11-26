using System.Net.Mail;
using System.Text;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.Features;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<LeadStore>();
builder.Services.AddAgenticRuntime(builder.Configuration);
builder.Services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
builder.Services.Configure<FormOptions>(options =>
    options.MultipartBodyLengthLimit = DocumentLimits.MaxFileSizeBytes + 65536);

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAntiforgery();

app.MapGet("/api/leads", ListLeads);
app.MapGet("/api/leads/{id:guid}", GetLead);
app.MapPost("/api/leads", CreateLead);
app.MapPut("/api/leads/{id:guid}/status", UpdateLeadStatus);
app.MapAgenticRuntime();
app.MapGet("/api/antiforgery", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { requestToken = tokens.RequestToken });
});
app.MapPost("/api/leads/{id:guid}/documents", UploadDocument);
app.MapGet("/api/leads/{id:guid}/documents/{documentId:guid}/file", DownloadDocument);
app.MapPut("/api/leads/{id:guid}/documents/{documentId:guid}/review", ReviewDocument);

app.Run();

static async Task<IResult> ListLeads(string? search, string? status, LeadStore store)
{
    var leads = await store.GetAllAsync();

    if (!string.IsNullOrWhiteSpace(search))
    {
        leads = leads
            .Where(lead =>
                lead.FirstName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                lead.LastName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                lead.Email.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (lead.Company?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();
    }

    if (!string.IsNullOrWhiteSpace(status))
    {
        leads = leads
            .Where(lead => lead.Status.Equals(status, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    return Results.Ok(leads);
}

static async Task<IResult> GetLead(Guid id, LeadStore store)
{
    var lead = (await store.GetAllAsync()).FirstOrDefault(candidate => candidate.Id == id);
    return lead is null ? Results.NotFound() : Results.Ok(lead);
}

static async Task<IResult> CreateLead(CreateLeadRequest request, LeadStore store)
{
    var errors = new Dictionary<string, string[]>();

    if (string.IsNullOrWhiteSpace(request.FirstName))
    {
        errors["firstName"] = ["First name is required."];
    }

    if (string.IsNullOrWhiteSpace(request.LastName))
    {
        errors["lastName"] = ["Last name is required."];
    }

    if (!IsValidEmail(request.Email))
    {
        errors["email"] = ["Enter a valid email address."];
    }

    if (errors.Count > 0)
    {
        return Results.ValidationProblem(errors);
    }

    var lead = new Lead
    {
        Id = Guid.NewGuid(),
        FirstName = request.FirstName!.Trim(),
        LastName = request.LastName!.Trim(),
        Email = request.Email!.Trim(),
        Phone = CleanOptionalValue(request.Phone),
        Company = CleanOptionalValue(request.Company),
        Source = CleanOptionalValue(request.Source),
        Status = LeadStatuses.New,
        CreatedAt = DateTimeOffset.UtcNow
    };

    var result = await store.UpsertAsync(lead);
    return result.IsNew
        ? Results.Created($"/api/leads/{result.Lead.Id}", result.Lead)
        : Results.Ok(result.Lead);
}

static async Task<IResult> UpdateLeadStatus(
    Guid id,
    UpdateLeadStatusRequest request,
    LeadStore store)
{
    var status = LeadStatuses.All.FirstOrDefault(
        candidate => candidate.Equals(request.Status?.Trim(), StringComparison.OrdinalIgnoreCase));

    if (status is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["status"] = [$"Choose one of: {string.Join(", ", LeadStatuses.All)}."]
        });
    }

    var lead = await store.UpdateStatusAsync(id, status);
    return lead is null ? Results.NotFound() : Results.Ok(lead);
}

static async Task<IResult> UploadDocument(
    Guid id,
    IFormFile? file,
    LeadStore store,
    AgentOrchestrator orchestrator,
    CancellationToken cancellationToken)
{
    if (file is null || file.Length == 0)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["file"] = ["Choose a non-empty PDF, JPG, or PNG file."]
        });
    }

    if (file.Length > DocumentLimits.MaxFileSizeBytes)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["file"] = ["The maximum file size is 10 MB."]
        });
    }

    var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
    var contentType = extension switch
    {
        ".pdf" => "application/pdf",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".png" => "image/png",
        _ => null
    };

    if (contentType is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["file"] = ["Only PDF, JPG, and PNG documents are allowed."]
        });
    }

    await using var source = file.OpenReadStream();
    var header = new byte[8];
    var headerLength = await source.ReadAsync(header, cancellationToken);
    if (!HasValidSignature(extension, header, headerLength))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["file"] = ["The file contents do not match its PDF, JPG, or PNG extension."]
        });
    }

    await using var contents = new MemoryStream();
    await contents.WriteAsync(header.AsMemory(0, headerLength), cancellationToken);
    await source.CopyToAsync(contents, cancellationToken);
    contents.Position = 0;

    PdfValidationFinding? validation = null;
    if (extension == ".pdf")
    {
        try
        {
            validation = await orchestrator.ValidatePdfAsync(id, contents, cancellationToken);
        }
        catch (PdfValidationUnavailableException exception)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "PDF validation is unavailable",
                detail: exception.Message);
        }

        if (validation is null)
        {
            return Results.NotFound();
        }

        if (!validation.IsStructurallyValid)
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["file"] = [validation.Summary]
            });
        }
    }

    var document = new LeadDocument
    {
        Id = Guid.NewGuid(),
        FileName = SanitizeFileName(file.FileName),
        ContentType = contentType,
        SizeBytes = file.Length,
        Status = validation?.Status == PdfValidationStatuses.Passed
            ? DocumentStatuses.Verified
            : DocumentStatuses.Pending,
        SubmittedAt = DateTimeOffset.UtcNow,
        ValidationStatus = validation?.Status,
        ValidationMessage = validation?.Summary,
        ValidatedAt = validation?.ValidatedAt
    };

    contents.Position = 0;
    var added = await store.AddDocumentAsync(id, document, contents, cancellationToken);
    return added
        ? Results.Created($"/api/leads/{id}/documents/{document.Id}/file", document)
        : Results.NotFound();
}

static async Task<IResult> DownloadDocument(Guid id, Guid documentId, LeadStore store)
{
    var result = await store.GetDocumentAsync(id, documentId);
    return result is null
        ? Results.NotFound()
        : Results.File(
            result.Value.FilePath,
            result.Value.Document.ContentType,
            result.Value.Document.FileName,
            enableRangeProcessing: false);
}

static async Task<IResult> ReviewDocument(
    Guid id,
    Guid documentId,
    ReviewDocumentRequest request,
    LeadStore store)
{
    var status = DocumentStatuses.All.FirstOrDefault(
        candidate => candidate.Equals(request.Status?.Trim(), StringComparison.OrdinalIgnoreCase));
    if (status is null)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["status"] = [$"Choose one of: {string.Join(", ", DocumentStatuses.All)}."]
        });
    }

    if (request.Note?.Length > DocumentLimits.MaxReviewNoteLength)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["note"] = [$"Review notes cannot exceed {DocumentLimits.MaxReviewNoteLength} characters."]
        });
    }

    var document = await store.ReviewDocumentAsync(id, documentId, status, CleanOptionalValue(request.Note));
    return document is null ? Results.NotFound() : Results.Ok(document);
}

static bool HasValidSignature(string extension, byte[] header, int headerLength)
{
    return extension switch
    {
        ".pdf" => headerLength >= 5 && Encoding.ASCII.GetString(header, 0, 5) == "%PDF-",
        ".jpg" or ".jpeg" => headerLength >= 3 &&
            header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF,
        ".png" => headerLength >= 8 &&
            header.AsSpan(0, 8).SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }),
        _ => false
    };
}

static string SanitizeFileName(string fileName)
{
    var name = Path.GetFileName(fileName);
    var sanitized = new string(name.Where(character => !char.IsControl(character)).ToArray());
    return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized[..Math.Min(sanitized.Length, 200)];
}

static bool IsValidEmail(string? email)
{
    return !string.IsNullOrWhiteSpace(email) &&
        MailAddress.TryCreate(email.Trim(), out var address) &&
        address.Address.Equals(email.Trim(), StringComparison.OrdinalIgnoreCase);
}

static string? CleanOptionalValue(string? value) =>
    string.IsNullOrWhiteSpace(value) ? null : value.Trim();

public partial class Program { }
