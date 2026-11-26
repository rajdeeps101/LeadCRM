using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.FileProviders;
using Xunit;

[Trait("TestType", "Functional")]
public sealed class CrmIntegrationTests : IDisposable
{
    private readonly CrmApplicationFactory _factory;
    private readonly HttpClient _client;

    public CrmIntegrationTests()
    {
        _factory = new CrmApplicationFactory(includeGeminiKey: true);
        _client = _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    [Trait("Category", "Quick")]
    public async Task HomePageIsServed()
    {
        using var response = await _client.GetAsync("/");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("<title>Lead CRM application</title>", html);
    }

    [Fact]
    [Trait("Category", "Sanity")]
    public async Task AgentsEndpointListsConfiguredTasks()
    {
        using var response = await _client.GetAsync("/api/agents");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(json.RootElement.GetProperty("tasks").EnumerateArray(),
            task => task.GetProperty("task").GetString() == "lead-assessment");
        Assert.Equal("pdf-validation",
            json.RootElement.GetProperty("documentUploadAgents")[0].GetString());
    }

    [Fact]
    [Trait("Category", "Quick")]
    public async Task CreateLeadReturnsCreatedAndCanBeRetrieved()
    {
        var lead = await CreateLeadAsync("Taylor", "Jordan", $"taylor.jordan.{Guid.NewGuid():N}@example.test");

        Assert.Equal("New", lead.GetProperty("status").GetString());
        Assert.Equal("Taylor", lead.GetProperty("firstName").GetString());

        using var response = await _client.GetAsync($"/api/leads/{lead.GetProperty("id").GetString()}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task InvalidLeadIsRejected()
    {
        using var response = await _client.PostAsJsonAsync("/api/leads", new
        {
            firstName = "",
            lastName = "Jordan",
            email = "not-an-email"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Sanity")]
    public async Task DuplicateEmailUpdatesLeadAndPreservesIdentityAndStatus()
    {
        var email = $"taylor.jordan.{Guid.NewGuid():N}@example.test";
        var original = await CreateLeadAsync("Taylor", "Jordan", email);
        var id = original.GetProperty("id").GetString();
        var createdAt = original.GetProperty("createdAt").GetString();
        using var statusResponse = await _client.PutAsJsonAsync($"/api/leads/{id}/status", new { status = "Qualified" });
        Assert.Equal(HttpStatusCode.OK, statusResponse.StatusCode);

        using var updateResponse = await _client.PostAsJsonAsync("/api/leads", new
        {
            firstName = "Tay",
            lastName = "Jordan",
            email = email.ToUpperInvariant()
        });
        using var updated = JsonDocument.Parse(await updateResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, updateResponse.StatusCode);
        Assert.Equal(id, updated.RootElement.GetProperty("id").GetString());
        Assert.Equal(createdAt, updated.RootElement.GetProperty("createdAt").GetString());
        Assert.Equal("Qualified", updated.RootElement.GetProperty("status").GetString());
        Assert.Equal("Tay", updated.RootElement.GetProperty("firstName").GetString());
    }

    [Theory]
    [Trait("Category", "Sanity")]
    [InlineData("New")]
    [InlineData("Contacted")]
    [InlineData("Qualified")]
    [InlineData("Converted")]
    [InlineData("Lost")]
    public async Task SupportedLeadStatusesAreSaved(string status)
    {
        var lead = await CreateLeadAsync("Avery", "Lane", $"avery.{Guid.NewGuid():N}@example.test");
        using var response = await _client.PutAsJsonAsync(
            $"/api/leads/{lead.GetProperty("id").GetString()}/status",
            new { status });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var updated = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(status, updated.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnsupportedLeadStatusIsRejected()
    {
        var lead = await CreateLeadAsync("Avery", "Lane", "avery.lane@example.test");
        using var response = await _client.PutAsJsonAsync(
            $"/api/leads/{lead.GetProperty("id").GetString()}/status",
            new { status = "Archived" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Sanity")]
    public async Task LeadSearchAndStatusFilterReturnMatchingRecords()
    {
        var searchableLead = await CreateLeadAsync(
            "Morgan", "Searchable", $"morgan.{Guid.NewGuid():N}@example.test", company: "Example Search Co");
        var id = searchableLead.GetProperty("id").GetString();
        using var update = await _client.PutAsJsonAsync($"/api/leads/{id}/status", new { status = "Qualified" });

        using var response = await _client.GetAsync("/api/leads?search=Example%20Search%20Co&status=Qualified");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var leads = json.RootElement.EnumerateArray().ToArray();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Single(leads);
        Assert.Equal(id, leads[0].GetProperty("id").GetString());
    }

    [Fact]
    [Trait("Category", "Sanity")]
    public async Task LeadAssessmentReturnsReadOnlyFindings()
    {
        var lead = await CreateLeadAsync("Casey", "River", "casey.river@example.test");
        using var response = await _client.PostAsJsonAsync("/api/agents/run", new
        {
            leadId = lead.GetProperty("id").GetString(),
            task = "lead-assessment"
        });
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("lead-assessment", result.RootElement.GetProperty("task").GetString());
        Assert.NotEmpty(result.RootElement.GetProperty("steps").EnumerateArray());
    }

    [Fact]
    public async Task UnsupportedAgentTaskIsRejected()
    {
        using var response = await _client.PostAsJsonAsync("/api/agents/run", new
        {
            leadId = Guid.NewGuid(),
            task = "unknown-task"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task AgentTaskForUnknownLeadReturnsNotFound()
    {
        using var response = await _client.PostAsJsonAsync("/api/agents/run", new
        {
            leadId = Guid.NewGuid(),
            task = "lead-assessment"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    [Trait("Category", "Sanity")]
    public async Task ImageUploadRemainsPendingAndCanBeDownloadedAndReviewed()
    {
        var lead = await CreateLeadAsync("Riley", "Stone", "riley.stone@example.test");
        var leadId = lead.GetProperty("id").GetString()!;
        var image = await UploadAsync(leadId, "sample.png", "image/png",
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var documentId = image.GetProperty("id").GetString()!;

        Assert.Equal("Pending", image.GetProperty("status").GetString());

        using var download = await _client.GetAsync($"/api/leads/{leadId}/documents/{documentId}/file");
        Assert.Equal(HttpStatusCode.OK, download.StatusCode);

        using var review = await _client.PutAsJsonAsync(
            $"/api/leads/{leadId}/documents/{documentId}/review",
            new { status = "Verified", note = "Reviewed by automated test" });
        Assert.Equal(HttpStatusCode.OK, review.StatusCode);
        using var reviewed = JsonDocument.Parse(await review.Content.ReadAsStringAsync());
        Assert.Equal("Verified", reviewed.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task UnsupportedUploadIsRejected()
    {
        var lead = await CreateLeadAsync("Jamie", "West", "jamie.west@example.test");
        using var response = await UploadResponseAsync(
            lead.GetProperty("id").GetString()!, "notes.txt", "text/plain", Encoding.UTF8.GetBytes("hello"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MismatchedFileSignatureIsRejected()
    {
        var lead = await CreateLeadAsync("Jamie", "West", "jamie.signature@example.test");
        using var response = await UploadResponseAsync(
            lead.GetProperty("id").GetString()!, "fake.pdf", "application/pdf", Encoding.UTF8.GetBytes("not a PDF"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task OversizedUploadIsRejected()
    {
        var lead = await CreateLeadAsync("Jamie", "West", "jamie.large@example.test");
        var bytes = new byte[10 * 1024 * 1024 + 1];
        bytes[0] = 0x89;
        bytes[1] = 0x50;
        bytes[2] = 0x4E;
        bytes[3] = 0x47;
        bytes[4] = 0x0D;
        bytes[5] = 0x0A;
        bytes[6] = 0x1A;
        bytes[7] = 0x0A;

        using var response = await UploadResponseAsync(
            lead.GetProperty("id").GetString()!, "large.png", "image/png", bytes);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MatchingFullNamePdfIsVerifiedByGemini()
    {
        _factory.GeminiHandler.ResponseStatusCode = HttpStatusCode.OK;
        _factory.GeminiHandler.FullNameFound = true;
        var lead = await CreateLeadAsync("Taylor", "Jordan", "taylor.pdfmatch@example.test");
        var pdf = CreatePdf("Taylor Jordan");

        var document = await UploadAsync(
            lead.GetProperty("id").GetString()!, "matching.pdf", "application/pdf", pdf);

        Assert.Equal("Verified", document.GetProperty("status").GetString());
        Assert.Equal("Passed", document.GetProperty("validationStatus").GetString());
        Assert.Contains("full name", document.GetProperty("validationMessage").GetString(),
            StringComparison.OrdinalIgnoreCase);
        using var requestJson = JsonDocument.Parse(_factory.GeminiHandler.LastRequestBody);
        var documentData = requestJson.RootElement.GetProperty("input")
            .EnumerateArray()
            .Single(item => item.GetProperty("type").GetString() == "document")
            .GetProperty("data")
            .GetString();
        Assert.Equal(Convert.ToBase64String(pdf), documentData);
    }

    [Fact]
    public async Task FullNameMismatchPdfRemainsPending()
    {
        _factory.GeminiHandler.ResponseStatusCode = HttpStatusCode.OK;
        _factory.GeminiHandler.FullNameFound = false;
        var lead = await CreateLeadAsync("Taylor", "Jordan", "taylor.pdfmismatch@example.test");

        var document = await UploadAsync(
            lead.GetProperty("id").GetString()!, "mismatch.pdf", "application/pdf", CreatePdf("Unrelated Person"));

        Assert.Equal("Pending", document.GetProperty("status").GetString());
        Assert.Equal("NameMismatch", document.GetProperty("validationStatus").GetString());
    }

    [Fact]
    public async Task PdfOverPageLimitStaysPendingWithoutCallingGemini()
    {
        _factory.GeminiHandler.RequestCount = 0;
        var lead = await CreateLeadAsync("Taylor", "Jordan", "taylor.multipage@example.test");

        var document = await UploadAsync(
            lead.GetProperty("id").GetString()!,
            "over-limit.pdf",
            "application/pdf",
            CreatePdf("Taylor Jordan", pageCount: 11));

        Assert.Equal("Pending", document.GetProperty("status").GetString());
        Assert.Equal("TextUnavailable", document.GetProperty("validationStatus").GetString());
        Assert.Equal(0, _factory.GeminiHandler.RequestCount);
    }

    [Fact]
    public async Task MalformedPdfIsRejectedBeforeCallingGemini()
    {
        _factory.GeminiHandler.RequestCount = 0;
        var lead = await CreateLeadAsync("Taylor", "Jordan", $"taylor.malformed.{Guid.NewGuid():N}@example.test");

        using var response = await UploadResponseAsync(
            lead.GetProperty("id").GetString()!,
            "malformed.pdf",
            "application/pdf",
            Encoding.ASCII.GetBytes("%PDF-not-valid"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, _factory.GeminiHandler.RequestCount);
    }

    [Fact]
    public async Task MissingGeminiKeyReturnsUnavailableAndDoesNotSavePdf()
    {
        using var factory = new CrmApplicationFactory(includeGeminiKey: false);
        using var client = factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            HandleCookies = true
        });
        using var createResponse = await client.PostAsJsonAsync("/api/leads", new
        {
            firstName = "Taylor",
            lastName = "Jordan",
            email = $"taylor.no-key.{Guid.NewGuid():N}@example.test"
        });
        using var leadJson = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        using var uploadResponse = await UploadResponseUsingClientAsync(
            client,
            leadJson.RootElement.GetProperty("id").GetString()!,
            "no-key.pdf",
            "application/pdf",
            CreatePdf("Taylor Jordan"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, uploadResponse.StatusCode);
        Assert.Equal(0, factory.GeminiHandler.RequestCount);
    }

    [Fact]
    public async Task Gemini503IsRetriedAtMostTwiceAndUploadFailsClosed()
    {
        _factory.GeminiHandler.ResponseStatusCode = HttpStatusCode.ServiceUnavailable;
        _factory.GeminiHandler.RequestCount = 0;
        var lead = await CreateLeadAsync("Taylor", "Jordan", "taylor.gemini503@example.test");

        using var response = await UploadResponseAsync(
            lead.GetProperty("id").GetString()!,
            "busy.pdf",
            "application/pdf",
            CreatePdf("Taylor Jordan"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(3, _factory.GeminiHandler.RequestCount);
    }

    private async Task<JsonElement> CreateLeadAsync(
        string firstName,
        string lastName,
        string email,
        string? company = null)
    {
        using var response = await _client.PostAsJsonAsync("/api/leads", new
        {
            firstName,
            lastName,
            email,
            company
        });
        Assert.True(response.StatusCode is HttpStatusCode.Created or HttpStatusCode.OK,
            await response.Content.ReadAsStringAsync());
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task<JsonElement> UploadAsync(
        string leadId,
        string fileName,
        string contentType,
        byte[] fileBytes)
    {
        using var response = await UploadResponseAsync(leadId, fileName, contentType, fileBytes);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }

    private async Task<HttpResponseMessage> UploadResponseAsync(
        string leadId,
        string fileName,
        string contentType,
        byte[] fileBytes) =>
        await UploadResponseUsingClientAsync(_client, leadId, fileName, contentType, fileBytes);

    private static async Task<HttpResponseMessage> UploadResponseUsingClientAsync(
        HttpClient client,
        string leadId,
        string fileName,
        string contentType,
        byte[] fileBytes)
    {
        using var tokenResponse = await client.GetAsync("/api/antiforgery");
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var tokenJson = JsonDocument.Parse(await tokenResponse.Content.ReadAsStringAsync());
        var token = tokenJson.RootElement.GetProperty("requestToken").GetString();

        var body = new MultipartFormDataContent();
        var file = new ByteArrayContent(fileBytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        body.Add(file, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/leads/{leadId}/documents")
        {
            Content = body
        };
        request.Headers.Add("X-CSRF-TOKEN", token);
        return await client.SendAsync(request);
    }

    private static byte[] CreatePdf(string text, int pageCount = 1)
    {
        var objects = new List<byte[]>();
        var pageObjectIds = Enumerable.Range(0, pageCount)
            .Select(index => 3 + index * 2)
            .ToArray();
        var kids = string.Join(" ", pageObjectIds.Select(id => $"{id} 0 R"));

        objects.Add(Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"));
        objects.Add(Encoding.ASCII.GetBytes(
            $"<< /Type /Pages /Kids [{kids}] /Count {pageCount} >>"));

        for (var index = 0; index < pageCount; index++)
        {
            var pageId = pageObjectIds[index];
            var contentId = pageId + 1;
            objects.Add(Encoding.ASCII.GetBytes(
                $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 << /Type /Font /Subtype /Type1 /BaseFont /Helvetica >> >> >> /Contents {contentId} 0 R >>"));

            var pageText = index == 0 ? text : $"Additional page {index + 1}";
            var content = Encoding.ASCII.GetBytes($"BT /F1 18 Tf 72 720 Td ({EscapePdfText(pageText)}) Tj ET");
            using var stream = new MemoryStream();
            stream.Write(Encoding.ASCII.GetBytes($"<< /Length {content.Length} >>\nstream\n"));
            stream.Write(content);
            stream.Write(Encoding.ASCII.GetBytes("\nendstream"));
            objects.Add(stream.ToArray());
        }

        using var pdf = new MemoryStream();
        pdf.Write(Encoding.ASCII.GetBytes("%PDF-1.4\n"));
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(pdf.Position);
            pdf.Write(Encoding.ASCII.GetBytes($"{index + 1} 0 obj\n"));
            pdf.Write(objects[index]);
            pdf.Write(Encoding.ASCII.GetBytes("\nendobj\n"));
        }

        var crossReferenceOffset = pdf.Position;
        pdf.Write(Encoding.ASCII.GetBytes($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n"));
        foreach (var offset in offsets.Skip(1))
        {
            pdf.Write(Encoding.ASCII.GetBytes($"{offset:0000000000} 00000 n \n"));
        }

        pdf.Write(Encoding.ASCII.GetBytes(
            $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{crossReferenceOffset}\n%%EOF\n"));
        return pdf.ToArray();
    }

    private static string EscapePdfText(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("(", "\\(", StringComparison.Ordinal)
            .Replace(")", "\\)", StringComparison.Ordinal);
}

public sealed class CrmApplicationFactory : WebApplicationFactory<Program>
{
    private readonly bool _includeGeminiKey;
    private readonly string _dataPath = Path.Combine(
        Path.GetTempPath(),
        $"lead-crm-tests-{Guid.NewGuid():N}");

    public CrmApplicationFactory(bool includeGeminiKey)
    {
        _includeGeminiKey = includeGeminiKey;
    }

    public FakeGeminiHandler GeminiHandler { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            var settings = new Dictionary<string, string?>();
            if (_includeGeminiKey)
            {
                settings["GOOGLE_GEMINI_API_KEY"] = "test-api-key";
            }

            configuration.AddInMemoryCollection(settings);
        });
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<LeadStore>();
            services.AddSingleton(serviceProvider =>
            {
                var environment = serviceProvider.GetRequiredService<IWebHostEnvironment>();
                return new LeadStore(new TestWebHostEnvironment(environment, _dataPath));
            });
            services.AddHttpClient(GoogleGeminiFullNameVerificationAgent.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => GeminiHandler);
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_dataPath))
        {
            Directory.Delete(_dataPath, recursive: true);
        }
    }

    private sealed class TestWebHostEnvironment : IWebHostEnvironment
    {
        private readonly IWebHostEnvironment _inner;

        public TestWebHostEnvironment(IWebHostEnvironment inner, string contentRootPath)
        {
            _inner = inner;
            ContentRootPath = contentRootPath;
            Directory.CreateDirectory(contentRootPath);
        }

        public string ApplicationName
        {
            get => _inner.ApplicationName;
            set => _inner.ApplicationName = value;
        }

        public IFileProvider WebRootFileProvider
        {
            get => _inner.WebRootFileProvider;
            set => _inner.WebRootFileProvider = value;
        }

        public string WebRootPath
        {
            get => _inner.WebRootPath;
            set => _inner.WebRootPath = value;
        }

        public string EnvironmentName
        {
            get => _inner.EnvironmentName;
            set => _inner.EnvironmentName = value;
        }

        public string ContentRootPath { get; set; }

        public IFileProvider ContentRootFileProvider
        {
            get => _inner.ContentRootFileProvider;
            set => _inner.ContentRootFileProvider = value;
        }
    }
}

public sealed class FakeGeminiHandler : HttpMessageHandler
{
    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;
    public bool FullNameFound { get; set; } = true;
    public int RequestCount { get; set; }
    public string LastRequestBody { get; private set; } = "";

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        LastRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);

        var body = ResponseStatusCode == HttpStatusCode.OK
            ? JsonSerializer.Serialize(new
            {
                status = "completed",
                output_text = JsonSerializer.Serialize(new { full_name_found = FullNameFound })
            })
            : JsonSerializer.Serialize(new
            {
                error = new
                {
                    status = "UNAVAILABLE",
                    message = "Synthetic Gemini test failure"
                }
            });

        return new HttpResponseMessage(ResponseStatusCode)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
    }
}
