using System.Text.Json;

public sealed class LeadStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly string _filePath;
    private readonly string _documentsDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public LeadStore(IWebHostEnvironment environment)
    {
        var dataDirectory = Path.Combine(environment.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "leads.json");
        _documentsDirectory = Path.Combine(dataDirectory, "documents");
        Directory.CreateDirectory(_documentsDirectory);
    }

    public async Task<List<Lead>> GetAllAsync()
    {
        await _gate.WaitAsync();
        try
        {
            return await ReadAsync();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Lead?> GetByIdAsync(Guid id)
    {
        await _gate.WaitAsync();
        try
        {
            var leads = await ReadAsync();
            return leads.FirstOrDefault(lead => lead.Id == id);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(Lead Lead, bool IsNew)> UpsertAsync(Lead lead)
    {
        await _gate.WaitAsync();
        try
        {
            var leads = await ReadAsync();
            var existingLead = leads.FirstOrDefault(
                candidate => candidate.Email.Equals(lead.Email, StringComparison.OrdinalIgnoreCase));

            if (existingLead is not null)
            {
                existingLead.FirstName = lead.FirstName;
                existingLead.LastName = lead.LastName;
                existingLead.Email = lead.Email;
                existingLead.Phone = lead.Phone;
                existingLead.Company = lead.Company;
                existingLead.Source = lead.Source;
                await WriteAsync(leads);
                return (existingLead, false);
            }

            leads.Add(lead);
            await WriteAsync(leads);
            return (lead, true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Lead?> UpdateStatusAsync(Guid id, string status)
    {
        await _gate.WaitAsync();
        try
        {
            var leads = await ReadAsync();
            var lead = leads.FirstOrDefault(candidate => candidate.Id == id);
            if (lead is null)
            {
                return null;
            }

            lead.Status = status;
            await WriteAsync(leads);
            return lead;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> AddDocumentAsync(
        Guid leadId,
        LeadDocument document,
        Stream contents,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var leads = await ReadAsync();
            var lead = leads.FirstOrDefault(candidate => candidate.Id == leadId);
            if (lead is null)
            {
                return false;
            }

            var leadDirectory = Path.Combine(_documentsDirectory, leadId.ToString("N"));
            Directory.CreateDirectory(leadDirectory);
            var filePath = Path.Combine(leadDirectory, GetStorageFileName(document));

            try
            {
                await using (var destination = new FileStream(
                    filePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    useAsync: true))
                {
                    await contents.CopyToAsync(destination, cancellationToken);
                }

                lead.Documents.Add(document);
                await WriteAsync(leads);
            }
            catch
            {
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                throw;
            }

            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<(LeadDocument Document, string FilePath)?> GetDocumentAsync(
        Guid leadId,
        Guid documentId)
    {
        await _gate.WaitAsync();
        try
        {
            var leads = await ReadAsync();
            var document = leads
                .FirstOrDefault(candidate => candidate.Id == leadId)?
                .Documents.FirstOrDefault(candidate => candidate.Id == documentId);
            if (document is null)
            {
                return null;
            }

            var filePath = Path.Combine(
                _documentsDirectory,
                leadId.ToString("N"),
                GetStorageFileName(document));
            return File.Exists(filePath) ? (document, filePath) : null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<LeadDocument?> ReviewDocumentAsync(
        Guid leadId,
        Guid documentId,
        string status,
        string? note)
    {
        await _gate.WaitAsync();
        try
        {
            var leads = await ReadAsync();
            var document = leads
                .FirstOrDefault(candidate => candidate.Id == leadId)?
                .Documents.FirstOrDefault(candidate => candidate.Id == documentId);
            if (document is null)
            {
                return null;
            }

            document.Status = status;
            document.ReviewedAt = status == DocumentStatuses.Pending ? null : DateTimeOffset.UtcNow;
            document.ReviewNote = note;
            await WriteAsync(leads);
            return document;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<Lead>> ReadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return [];
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<List<Lead>>(stream, JsonOptions)
            ?? throw new InvalidDataException($"The lead data file '{_filePath}' contains invalid data.");
    }

    private static string GetStorageFileName(LeadDocument document)
    {
        var extension = document.ContentType switch
        {
            "application/pdf" => ".pdf",
            "image/jpeg" => ".jpg",
            "image/png" => ".png",
            _ => throw new InvalidDataException("The stored document has an unsupported content type.")
        };

        return $"{document.Id:N}{extension}";
    }

    private async Task WriteAsync(List<Lead> leads)
    {
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, leads, JsonOptions);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
