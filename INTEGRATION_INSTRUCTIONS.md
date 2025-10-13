# Integration Instructions for External APIs

This document provides step-by-step instructions for integrating the newly implemented services
with your existing migration services.

## Overview

All necessary interfaces, models, and services have been implemented and are ready to use.
The integration points in existing services are **commented out** until you have access to
the external APIs (ClientAPI and DUT API).

---

## Step 1: Update DocumentDiscoveryService

File: `Migration.Infrastructure/Implementation/Services/DocumentDiscoveryService.cs`

### 1.1 Add Dependencies to Constructor

```csharp
// EXISTING constructor (around line 42):
public DocumentDiscoveryService(
    IDocumentIngestor ingestor,
    IDocumentReader reader,
    IDocumentResolver resolver,
    IDocStagingRepository docRepo,
    IFolderStagingRepository folderRepo,
    IOptions<MigrationOptions> options,
    IServiceProvider sp,
    IUnitOfWork unitOfWork,
    ILoggerFactory logger)
{
    _ingestor = ingestor;
    _reader = reader;
    _resolver = resolver;
    _docRepo = docRepo;
    _folderRepo = folderRepo;
    _options = options;
    _sp = sp;
    _dbLogger = logger.CreateLogger("DbLogger");
    _fileLogger = logger.CreateLogger("FileLogger");
}

// ===== ADD THESE NEW SERVICES TO CONSTRUCTOR =====
// TODO: Uncomment when ClientAPI and DUT API are available

// Add these fields at the top of the class (around line 23):
/*
private readonly IClientEnrichmentService _enrichmentService;
private readonly IDocumentTypeTransformationService _transformationService;
*/

// Update constructor to include new services:
/*
public DocumentDiscoveryService(
    IDocumentIngestor ingestor,
    IDocumentReader reader,
    IDocumentResolver resolver,
    IDocStagingRepository docRepo,
    IFolderStagingRepository folderRepo,
    IOptions<MigrationOptions> options,
    IServiceProvider sp,
    IUnitOfWork unitOfWork,
    ILoggerFactory logger,
    IClientEnrichmentService enrichmentService,              // NEW
    IDocumentTypeTransformationService transformationService // NEW
    )
{
    // ... existing assignments ...
    _enrichmentService = enrichmentService;
    _transformationService = transformationService;
}
*/
```

### 1.2 Update ProcessSingleFolderAsync Method

```csharp
// EXISTING method (around line 374):
private async Task ProcessSingleFolderAsync(FolderStaging folder, CancellationToken ct)
{
    using var logScope = _fileLogger.BeginScope(new Dictionary<string, object>
    {
        ["FolderId"] = folder.Id ,
        ["FolderName"] = folder.Name ?? "unknown",
        ["NodeId"] = folder.NodeId ?? "unknown"
    });

    _fileLogger.LogDebug("Processing folder {FolderId} ({Name}, NodeId: {NodeId})",
        folder.Id, folder.Name, folder.NodeId);

    // ===== ADD CLIENT ENRICHMENT HERE =====
    // TODO: Uncomment when ClientAPI is available
    /*
    try
    {
        // Enrich folder with client data from ClientAPI
        folder = await _enrichmentService.EnrichFolderWithClientDataAsync(folder, ct);
        _fileLogger.LogInformation(
            "Enriched folder {FolderId} with client data for CoreId: {CoreId}",
            folder.Id, folder.CoreId);
    }
    catch (Exception ex)
    {
        _fileLogger.LogWarning(ex,
            "Failed to enrich folder {FolderId} with client data - continuing without enrichment",
            folder.Id);
        // Continue processing even if enrichment fails
    }
    */

    var documents = await _reader.ReadBatchAsync(folder.NodeId!, ct).ConfigureAwait(false);

    if (documents == null || documents.Count == 0)
    {
        _fileLogger.LogInformation(
            "No documents found in folder {FolderId} ({Name}, NodeId: {NodeId}) - marking as PROCESSED",
            folder.Id, folder.Name, folder.NodeId);
        await MarkFolderAsProcessedAsync(folder.Id, ct).ConfigureAwait(false);
        return;
    }

    _fileLogger.LogInformation("Found {Count} documents in folder {FolderId} ({Name})",
        documents.Count, folder.Id, folder.Name);

    var desFolderId = await ResolveDestinationFolder(folder, ct).ConfigureAwait(false);
    _fileLogger.LogDebug("Resolved destination folder: {DestFolderId}", desFolderId);

    var docsToInsert = new List<DocStaging>(documents.Count);

    foreach (var d in documents)
    {
        var item = d.Entry.ToDocStagingInsert();
        item.ToPath = desFolderId;

        // ===== ADD DOCUMENT TYPE DETERMINATION HERE =====
        // TODO: Uncomment when business mapping table is finalized
        /*
        try
        {
            // Determine if document needs migration type suffix
            item = await _transformationService.DetermineDocumentTypesAsync(item, ct);
            _fileLogger.LogDebug(
                "Determined types for document {NodeId}: MigrationType={MigrationType}, FinalType={FinalType}",
                item.NodeId, item.DocumentTypeMigration, item.FinalDocumentType);
        }
        catch (Exception ex)
        {
            _fileLogger.LogWarning(ex,
                "Failed to determine document types for {NodeId} - using default",
                item.NodeId);
            // Continue with default values
        }
        */

        item.Status = MigrationStatus.Ready.ToDbString();
        docsToInsert.Add(item);
    }

    // ... rest of existing code ...
}
```

---

## Step 2: Update FolderDiscoveryService

File: `Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs`

### 2.1 Add Dependencies to Constructor

```csharp
// EXISTING constructor (around line 42):
public FolderDiscoveryService(
    IFolderIngestor ingestor,
    IFolderReader reader,
    IOptions<MigrationOptions> options,
    IServiceProvider sp,
    IUnitOfWork unitOfWork,
    ILoggerFactory logger)
{
    _ingestor = ingestor;
    _reader = reader;
    _options = options;
    _sp = sp;
    _unitOfWork = unitOfWork;
    _dbLogger = logger.CreateLogger("DbLogger");
    _fileLogger = logger.CreateLogger("FileLogger");
}

// ===== ADD UNIQUE FOLDER IDENTIFIER SERVICE =====
// TODO: Uncomment when DUT API is available (for deposit folders)

// Add this field at the top of the class:
/*
private readonly IUniqueFolderIdentifierService _identifierService;
private readonly IDutApi _dutApi;  // If processing deposit folders
*/

// Update constructor:
/*
public FolderDiscoveryService(
    IFolderIngestor ingestor,
    IFolderReader reader,
    IOptions<MigrationOptions> options,
    IServiceProvider sp,
    IUnitOfWork unitOfWork,
    ILoggerFactory logger,
    IUniqueFolderIdentifierService identifierService, // NEW
    IDutApi dutApi                                     // NEW (for deposit folders)
    )
{
    // ... existing assignments ...
    _identifierService = identifierService;
    _dutApi = dutApi;
}
*/
```

### 2.2 Add Method for Deposit Folder Processing

```csharp
// ===== ADD THIS NEW METHOD FOR DEPOSIT FOLDERS =====
// TODO: Uncomment when DUT API is available
/*
private async Task ProcessDepositFolderAsync(FolderStaging folder, CancellationToken ct)
{
    // Per documentation: Generate unique identifier for deposit folders
    // Format: DE-{CoreId}-{ProductType}-{ContractNumber}_{Timestamp}

    if (string.IsNullOrWhiteSpace(folder.CoreId) ||
        string.IsNullOrWhiteSpace(folder.ProductType) ||
        string.IsNullOrWhiteSpace(folder.ContractNumber))
    {
        _fileLogger.LogWarning(
            "Folder {FolderId} missing required fields for deposit identifier - " +
            "CoreId: {CoreId}, ProductType: {ProductType}, ContractNumber: {ContractNumber}",
            folder.Id, folder.CoreId, folder.ProductType, folder.ContractNumber);
        return;
    }

    try
    {
        // Validate with DUT API that offer is "Booked"
        var offers = await _dutApi.GetBookedOffersAsync(folder.CoreId, ct);
        var matchingOffer = offers.FirstOrDefault(o => o.ContractNumber == folder.ContractNumber);

        if (matchingOffer == null)
        {
            _fileLogger.LogWarning(
                "No booked offer found for CoreId: {CoreId}, ContractNumber: {ContractNumber}",
                folder.CoreId, folder.ContractNumber);
            return;
        }

        // Use ProcessedAt date from DUT, not migration date!
        var processDate = matchingOffer.ProcessedAt ?? matchingOffer.CreatedAt;

        // Generate unique identifier
        folder.UniqueIdentifier = _identifierService.GenerateDepositIdentifier(
            folder.CoreId,
            folder.ProductType,
            folder.ContractNumber,
            processDate);

        // Set ProcessDate from DUT (NOT migration date)
        folder.ProcessDate = processDate;

        _fileLogger.LogInformation(
            "Generated deposit identifier for folder {FolderId}: {UniqueIdentifier}",
            folder.Id, folder.UniqueIdentifier);
    }
    catch (Exception ex)
    {
        _fileLogger.LogError(ex,
            "Failed to process deposit folder {FolderId}",
            folder.Id);
        throw;
    }
}
*/
```

---

## Step 3: Register Services in Dependency Injection

File: `Alfresco.App/Program.cs` or `Startup.cs`

```csharp
// ===== ADD SERVICE REGISTRATIONS =====
// TODO: Uncomment when APIs are available

/*
// External API clients
services.AddHttpClient<IClientApi, ClientApi>(client =>
{
    var baseUrl = configuration["ClientApi:BaseUrl"];
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(
        configuration.GetValue<int>("ClientApi:TimeoutSeconds", 30));

    // Add API key header if required
    var apiKey = configuration["ClientApi:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
    {
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }
});

services.AddHttpClient<IDutApi, DutApi>(client =>
{
    var baseUrl = configuration["DutApi:BaseUrl"];
    client.BaseAddress = new Uri(baseUrl);
    client.Timeout = TimeSpan.FromSeconds(
        configuration.GetValue<int>("DutApi:TimeoutSeconds", 30));

    var apiKey = configuration["DutApi:ApiKey"];
    if (!string.IsNullOrEmpty(apiKey))
    {
        client.DefaultRequestHeaders.Add("X-API-Key", apiKey);
    }
});

// Configuration options
services.Configure<ClientApiOptions>(
    configuration.GetSection(ClientApiOptions.SectionName));
services.Configure<DutApiOptions>(
    configuration.GetSection(DutApiOptions.SectionName));

// Migration services
services.AddScoped<IClientEnrichmentService, ClientEnrichmentService>();
services.AddScoped<IDocumentTypeTransformationService, DocumentTypeTransformationService>();
services.AddScoped<IUniqueFolderIdentifierService, UniqueFolderIdentifierService>();
*/
```

---

## Step 4: Update appsettings.json

```json
{
  // ===== ADD THESE CONFIGURATION SECTIONS =====
  // TODO: Update URLs and keys when APIs are available

  /*
  "ClientApi": {
    "BaseUrl": "https://client-api-url.example.com",
    "GetClientDataEndpoint": "/api/clients",
    "GetActiveAccountsEndpoint": "/api/clients",
    "ValidateClientEndpoint": "/api/clients",
    "TimeoutSeconds": 30,
    "ApiKey": "your-client-api-key-here",
    "RetryCount": 3
  },

  "DutApi": {
    "BaseUrl": "https://dut-api-url.example.com",
    "GetOffersEndpoint": "/api/offers",
    "TimeoutSeconds": 30,
    "ApiKey": "your-dut-api-key-here",
    "RetryCount": 3,
    "EnableCaching": true,
    "CacheDurationMinutes": 60
  },
  */

  // Rest of existing configuration...
}
```

---

## Step 5: Post-Migration Tasks

After all documents are migrated from all sources (Heimdall, DUT, old DMS):

### 5.1 Run Document Type Transformation

Create a console command or scheduled job:

```csharp
// Example console command for running post-migration transformation
public class PostMigrationCommand
{
    private readonly IDocumentTypeTransformationService _transformationService;
    private readonly ILogger<PostMigrationCommand> _logger;

    public PostMigrationCommand(
        IDocumentTypeTransformationService transformationService,
        ILogger<PostMigrationCommand> logger)
    {
        _transformationService = transformationService;
        _logger = logger;
    }

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting post-migration document type transformation");

        try
        {
            var count = await _transformationService.TransformActiveDocumentsAsync(ct);

            _logger.LogInformation(
                "Post-migration transformation completed: {Count} documents transformed",
                count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Post-migration transformation failed");
            throw;
        }
    }
}
```

### 5.2 Run Account Number Enrichment for KDP Documents

```csharp
// Example: Enrich all KDP documents that are active and missing account numbers
public class EnrichAccountNumbersCommand
{
    private readonly IDocStagingRepository _docRepo;
    private readonly IClientEnrichmentService _enrichmentService;
    private readonly ILogger<EnrichAccountNumbersCommand> _logger;

    public async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("Starting account number enrichment for KDP documents");

        // Get all KDP documents (00099, 00824) that are active and don't have account numbers
        var kdpDocuments = await _docRepo.GetKdpDocumentsWithoutAccountsAsync(ct);

        _logger.LogInformation(
            "Found {Count} KDP documents needing account enrichment",
            kdpDocuments.Count);

        var enrichedCount = 0;
        foreach (var doc in kdpDocuments)
        {
            try
            {
                await _enrichmentService.EnrichDocumentWithAccountsAsync(doc, ct);
                enrichedCount++;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to enrich document {DocId} - skipping",
                    doc.Id);
            }
        }

        _logger.LogInformation(
            "Account enrichment completed: {Enriched}/{Total} documents enriched",
            enrichedCount, kdpDocuments.Count);
    }
}
```

---

## Testing Checklist

Before enabling in production:

### Unit Tests
- [ ] Test ClientApi with mock HttpClient
- [ ] Test DutApi with mock HttpClient
- [ ] Test ClientEnrichmentService with mock ClientApi
- [ ] Test DocumentTypeTransformationService type mappings
- [ ] Test UniqueFolderIdentifierService format generation

### Integration Tests
- [ ] Test ClientAPI connection and authentication
- [ ] Test DUT API connection and authentication
- [ ] Test full folder enrichment workflow
- [ ] Test full document enrichment workflow
- [ ] Test document type transformation
- [ ] Test unique identifier generation

### Performance Tests
- [ ] Measure ClientAPI response times
- [ ] Measure DUT API response times
- [ ] Test batch enrichment performance
- [ ] Test caching effectiveness (if enabled)

---

## Troubleshooting

### Issue: ClientAPI returns 401 Unauthorized
**Solution**: Check API key in appsettings.json and ensure it's being sent in request headers

### Issue: DUT API returns no booked offers
**Solution**: Verify status filter in query parameters. Check that offers exist in OfferBO table with status="Booked"

### Issue: Document type transformation doesn't work
**Solution**: Verify TypeMappings dictionary in DocumentTypeTransformationService matches business mapping table

### Issue: Unique identifier format invalid
**Solution**: Check that CoreId, ProductType (5 digits), and ContractNumber are all populated correctly

---

## Contact

For questions about implementation, contact the development team.
For questions about business rules and mapping tables, contact business analysts.
