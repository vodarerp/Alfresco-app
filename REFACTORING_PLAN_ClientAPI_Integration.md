# PLAN REFAKTORISANJA - ClientAPI Integracija

## 📊 TRENUTNO STANJE IMPLEMENTACIJE

### **Postojeća Arhitektura (3-fazni proces)**

```
┌─────────────────────────────────────────────────────────┐
│                  FAZA 1: Folder Discovery                │
│  FolderDiscoveryService.cs (IMPLEMENTIRANO ✅)           │
└─────────────────────────────────────────────────────────┘
                        ↓
        Čita foldere iz starog Alfresco-a
        Skladišti u: FOLDER_STAGING tabelu
        Status: READY → IN PROGRESS → PROCESSED
                        ↓
┌─────────────────────────────────────────────────────────┐
│              FAZA 2: Document Discovery                  │
│  DocumentDiscoveryService.cs (IMPLEMENTIRANO ✅)         │
└─────────────────────────────────────────────────────────┘
                        ↓
        Za svaki folder:
          - Čita dokumente iz foldera
          - Resolve destination folder
          - Skladišti u: DOC_STAGING tabelu
        Status: READY → IN PROGRESS → PROCESSED
                        ↓
┌─────────────────────────────────────────────────────────┐
│                   FAZA 3: Move Service                   │
│     MoveService.cs (IMPLEMENTIRANO ✅)                   │
└─────────────────────────────────────────────────────────┘
                        ↓
        Za svaki dokument:
          - Pomera dokument na novu lokaciju
          - Update statusa
        Status: READY → IN PROGRESS → DONE

┌─────────────────────────────────────────────────────────┐
│          ClientEnrichmentService.cs (READY ⏳)           │
│   **PRIPREMLJENO** ali trenutno NIJE aktivirano!        │
└─────────────────────────────────────────────────────────┘
```

---

## ⚠️ KLJUČNI PROBLEM: ClientAPI Integracija Nedostaje!

### **Šta trenutno NIJE implementirano:**

1. ❌ **Validacija klijenata** (da li klijent postoji u ClientAPI)
2. ❌ **Popunjavanje klijentskih atributa** (Ime, Tip, JMBG, OPU, itd.)
3. ❌ **Određivanje tipa dosijea** (FL vs PL na osnovu clientType)
4. ❌ **Popunjavanje liste računa** za KDP dokumente (00824, 00099)
5. ❌ **Kreiranje novih dosijea** ako ne postoje

### **ClientEnrichmentService JE SPREMLJEN ali...**

Gledajući kod (Migration.Infrastructure/Implementation/ClientEnrichmentService.cs:14-17):
```csharp
/// <summary>
/// Service for enriching folder and document metadata with client data from ClientAPI.
///
/// IMPORTANT: This service is ready but integration with ClientAPI should be enabled
/// by uncommenting the constructor injection and method calls in existing services
/// when ClientAPI becomes available.
/// </summary>
```

**Service postoji ALI:**
- ❌ Nije integrisan u FolderDiscoveryService
- ❌ Nije integrisan u DocumentDiscoveryService
- ❌ Nije integrisan u MoveService
- ❌ IClientApi interfejs verovatno ne postoji ili nije implementiran

---

## 🔧 REFACTORING PLAN - 4 KORAKA

---

### **KORAK 1: Implementacija IClientApi Interfejsa**

#### **1.1. Kreiranje IClientApi Interface**

Lokacija: `Migration.Abstraction/Interfaces/IClientApi.cs`

```csharp
public interface IClientApi
{
    /// <summary>
    /// Validates if client exists in ClientAPI system
    /// Endpoint: GET /api/Client/ClientExists/{identityNumber}
    /// </summary>
    Task<bool> ValidateClientExistsAsync(string coreId, CancellationToken ct = default);

    /// <summary>
    /// Gets detailed client data for folder enrichment
    /// Endpoint: GET /api/Client/GetClientDetailExtended/{coreId}
    /// Maps to FolderStaging fields
    /// </summary>
    Task<ClientDataDto> GetClientDataAsync(string coreId, CancellationToken ct = default);

    /// <summary>
    /// Gets active accounts for KDP document enrichment
    /// Endpoint: GET /api/Client/GetClientAccounts/{clientId}
    /// Filters accounts by date (openDate <= docCreationDate AND (closeDate == null OR closeDate > docCreationDate))
    /// </summary>
    Task<IReadOnlyList<string>> GetActiveAccountsAsync(
        string coreId,
        DateTimeOffset asOfDate,
        CancellationToken ct = default);

    /// <summary>
    /// Quick search to get CoreId from old folder name (e.g., "PI10227858" → "10227858")
    /// Endpoint: GET /api/Client/ClientSearch/{searchValue}
    /// </summary>
    Task<string?> GetCoreIdFromSearchAsync(string searchValue, CancellationToken ct = default);

    /// <summary>
    /// Gets client type (FL/PL) to determine destination dossier
    /// Endpoint: GET /api/Client/GetClientData/{clientId}
    /// Returns: "FIZIČKO LICE" or "PRAVNO LICE"
    /// </summary>
    Task<string> GetClientTypeAsync(string coreId, CancellationToken ct = default);
}

public class ClientDataDto
{
    public string CoreId { get; set; } = string.Empty;
    public string ClientName { get; set; } = string.Empty;
    public string MbrJmbg { get; set; } = string.Empty;
    public string ClientType { get; set; } = string.Empty; // "FL" or "PL"
    public string ClientSubtype { get; set; } = string.Empty;
    public string Residency { get; set; } = string.Empty;
    public string Segment { get; set; } = string.Empty;
    public string Staff { get; set; } = string.Empty;
    public string OpuUser { get; set; } = string.Empty;
    public string OpuRealization { get; set; } = string.Empty;
    public string Barclex { get; set; } = string.Empty;
    public string Collaborator { get; set; } = string.Empty;
}
```

#### **1.2. Implementacija ClientApiHttpService**

Lokacija: `Migration.Infrastructure/Implementation/Clients/ClientApiHttpService.cs`

```csharp
public class ClientApiHttpService : IClientApi
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ClientApiHttpService> _logger;
    private readonly IMemoryCache _cache; // Za caching klijentskih podataka

    public ClientApiHttpService(
        HttpClient httpClient,
        ILogger<ClientApiHttpService> logger,
        IMemoryCache cache)
    {
        _httpClient = httpClient;
        _logger = logger;
        _cache = cache;
    }

    public async Task<bool> ValidateClientExistsAsync(string coreId, CancellationToken ct = default)
    {
        // Check cache first
        if (_cache.TryGetValue($"client_exists_{coreId}", out bool cachedResult))
        {
            return cachedResult;
        }

        try
        {
            // GET /api/Client/ClientExists/{coreId}
            var response = await _httpClient.GetAsync(
                $"api/Client/ClientExists/{coreId}",
                ct).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            // API vraća string (verovatno coreId ili "true"/"false")
            var exists = !string.IsNullOrEmpty(content) && content != "null";

            // Cache za 1 sat
            _cache.Set($"client_exists_{coreId}", exists, TimeSpan.FromHours(1));

            return exists;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to validate client {CoreId}", coreId);
            throw new InvalidOperationException($"ClientAPI validation failed for {coreId}", ex);
        }
    }

    public async Task<ClientDataDto> GetClientDataAsync(string coreId, CancellationToken ct = default)
    {
        // Check cache first
        if (_cache.TryGetValue($"client_data_{coreId}", out ClientDataDto cachedData))
        {
            return cachedData;
        }

        try
        {
            // GET /api/Client/GetClientDetailExtended/{coreId}
            var response = await _httpClient.GetAsync(
                $"api/Client/GetClientDetailExtended/{coreId}",
                ct).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadFromJsonAsync<ClientDetailExtendedResponse>(
                cancellationToken: ct).ConfigureAwait(false);

            if (content == null)
            {
                throw new InvalidOperationException($"No data returned for client {coreId}");
            }

            // Map response to ClientDataDto
            var dto = new ClientDataDto
            {
                CoreId = coreId,
                ClientName = content.ClientGeneralInfo?.ShortName ?? "",
                MbrJmbg = content.ClientGeneralInfo?.ClientNumber ?? "",
                ClientType = DetermineClientType(content.ClientGeneralInfo?.ClientType),
                ClientSubtype = content.ClientGeneralInfo?.Status ?? "",
                Residency = content.ClientGeneralInfo?.ResidentIndicator ?? "",
                Segment = content.ClientOtherInfo?.MarketSegment ?? "",
                Staff = content.ClientGeneralInfo?.Status ?? "", // TODO: Verify mapping
                OpuUser = content.ClientGeneralInfo?.Opu ?? "",
                OpuRealization = content.ClientGeneralInfo?.Opu ?? "",
                Barclex = content.ClientContactData?.Email ?? "",
                Collaborator = "" // TODO: Verify source field
            };

            // Cache za 30 minuta
            _cache.Set($"client_data_{coreId}", dto, TimeSpan.FromMinutes(30));

            return dto;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get client data for {CoreId}", coreId);
            throw new InvalidOperationException($"ClientAPI data fetch failed for {coreId}", ex);
        }
    }

    public async Task<IReadOnlyList<string>> GetActiveAccountsAsync(
        string coreId,
        DateTimeOffset asOfDate,
        CancellationToken ct = default)
    {
        try
        {
            // GET /api/Client/GetClientAccounts/{coreId}
            var response = await _httpClient.GetAsync(
                $"api/Client/GetClientAccounts/{coreId}",
                ct).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var accountsData = await response.Content.ReadFromJsonAsync<ClientAccountsDataResponse>(
                cancellationToken: ct).ConfigureAwait(false);

            if (accountsData?.Accounts == null)
            {
                return Array.Empty<string>();
            }

            // Filter accounts based on asOfDate
            var activeAccounts = accountsData.Accounts
                .Where(a =>
                    a.OpenDate <= asOfDate &&
                    (a.CloseDate == null || a.CloseDate > asOfDate))
                .Select(a => a.AccountNumber)
                .ToList();

            return activeAccounts;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get accounts for {CoreId}", coreId);
            throw new InvalidOperationException($"ClientAPI accounts fetch failed for {coreId}", ex);
        }
    }

    public async Task<string?> GetCoreIdFromSearchAsync(string searchValue, CancellationToken ct = default)
    {
        try
        {
            // GET /api/Client/ClientSearch/{searchValue}
            var response = await _httpClient.GetAsync(
                $"api/Client/ClientSearch/{searchValue}",
                ct).ConfigureAwait(false);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();

            var searchResult = await response.Content.ReadFromJsonAsync<ClientSearchResultResponse>(
                cancellationToken: ct).ConfigureAwait(false);

            return searchResult?.CoreId;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to search client {SearchValue}", searchValue);
            return null;
        }
    }

    public async Task<string> GetClientTypeAsync(string coreId, CancellationToken ct = default)
    {
        try
        {
            // GET /api/Client/GetClientData/{coreId}
            var response = await _httpClient.GetAsync(
                $"api/Client/GetClientData/{coreId}",
                ct).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            var clientData = await response.Content.ReadFromJsonAsync<ClientDataResponse>(
                cancellationToken: ct).ConfigureAwait(false);

            return DetermineClientType(clientData?.ClientType);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to get client type for {CoreId}", coreId);
            throw new InvalidOperationException($"ClientAPI type fetch failed for {coreId}", ex);
        }
    }

    private string DetermineClientType(string? apiClientType)
    {
        if (string.IsNullOrEmpty(apiClientType))
        {
            return "UNKNOWN";
        }

        // Mapiranje različitih naziva iz API-ja na standardizovane tipove
        return apiClientType.ToUpperInvariant() switch
        {
            "FIZIČKO LICE" => "FL",
            "PRAVNO LICE" => "PL",
            "INDIVIDUAL" => "FL",
            "LEGAL ENTITY" => "PL",
            "RETAIL" => "FL",
            "CORPORATE" => "PL",
            _ => apiClientType
        };
    }
}

// Response DTOs (mapiraju se na swagger.json strukturu)
public class ClientDetailExtendedResponse
{
    public ClientGeneralInfo? ClientGeneralInfo { get; set; }
    public ClientContactData? ClientContactData { get; set; }
    public ClientOtherInfo? ClientOtherInfo { get; set; }
}

public class ClientAccountsDataResponse
{
    public List<AccountInfo>? Accounts { get; set; }
}

public class AccountInfo
{
    public string AccountNumber { get; set; } = string.Empty;
    public DateTimeOffset OpenDate { get; set; }
    public DateTimeOffset? CloseDate { get; set; }
    public string Status { get; set; } = string.Empty;
}

// ... ostale response DTOs prema swagger.json
```

---

### **KORAK 2: Refaktoring FolderDiscoveryService**

#### **2.1. Dodavanje ClientAPI Poziva**

**Trenutno stanje** (FolderDiscoveryService.cs:93-95):
```csharp
var foldersToInsert = page.Items.ToList().ToFolderStagingListInsert();
var inserted = await InsertFoldersAsync(foldersToInsert, ct).ConfigureAwait(false);
```

**NOVO stanje** - sa validacijom i enrichment-om:

```csharp
// 1. Parsiranje CoreId iz folder imena
var foldersToValidate = page.Items.ToList();
var validatedFolders = new List<FolderStaging>();

foreach (var folder in foldersToValidate)
{
    // Extract CoreId from folder name (e.g., "PI10227858" → "10227858")
    var coreId = ExtractCoreIdFromFolderName(folder.Name);

    if (string.IsNullOrEmpty(coreId))
    {
        _fileLogger.LogWarning(
            "Cannot extract CoreId from folder name: {FolderName}",
            folder.Name);
        continue; // Skip invalid folder
    }

    // 2. Validate client exists in ClientAPI
    try
    {
        var clientExists = await _clientEnrichment.ValidateClientAsync(coreId, ct)
            .ConfigureAwait(false);

        if (!clientExists)
        {
            _fileLogger.LogWarning(
                "Client {CoreId} does not exist in ClientAPI, skipping folder {FolderName}",
                coreId, folder.Name);
            continue; // Skip non-existent client
        }

        // 3. Enrich folder with ClientAPI data
        folder.CoreId = coreId; // Set CoreId first
        var enrichedFolder = await _clientEnrichment.EnrichFolderWithClientDataAsync(folder, ct)
            .ConfigureAwait(false);

        validatedFolders.Add(enrichedFolder);

        _fileLogger.LogInformation(
            "Folder {FolderName} validated and enriched: CoreId={CoreId}, ClientType={ClientType}",
            folder.Name, coreId, enrichedFolder.ClientType);
    }
    catch (Exception ex)
    {
        _fileLogger.LogError(ex,
            "Failed to validate/enrich folder {FolderName} for CoreId {CoreId}",
            folder.Name, coreId);
        // Odluka: Skip ili nastavi sa praznim podacima?
        // Za sigurnost, SKIP-ujemo foldere gde ClientAPI ne radi
        continue;
    }
}

// 4. Insert only validated folders
if (validatedFolders.Count > 0)
{
    var inserted = await InsertFoldersAsync(validatedFolders, ct).ConfigureAwait(false);
    _fileLogger.LogInformation(
        "Inserted {Inserted}/{Total} validated folders",
        inserted, foldersToValidate.Count);
}
else
{
    _fileLogger.LogWarning("No valid folders to insert after ClientAPI validation");
}
```

#### **2.2. Helper Metoda za Ekstrakciju CoreId**

```csharp
/// <summary>
/// Extracts CoreId from old folder name format
/// Examples:
/// - "PI10227858" → "10227858"
/// - "PL12345678" → "12345678"
/// - "10227858" → "10227858" (already numeric)
/// </summary>
private string? ExtractCoreIdFromFolderName(string? folderName)
{
    if (string.IsNullOrWhiteSpace(folderName))
    {
        return null;
    }

    // Remove non-numeric prefix (e.g., "PI", "PL")
    var numericPart = new string(folderName.Where(char.IsDigit).ToArray());

    return string.IsNullOrEmpty(numericPart) ? null : numericPart;
}
```

#### **2.3. Constructor Injection Update**

```csharp
private readonly IClientEnrichmentService _clientEnrichment;

public FolderDiscoveryService(
    IFolderIngestor ingestor,
    IFolderReader reader,
    IOptions<MigrationOptions> options,
    IServiceProvider sp,
    IUnitOfWork unitOfWork,
    ILoggerFactory logger,
    IClientEnrichmentService clientEnrichment) // ⬅️ NOVO!
{
    _ingestor = ingestor;
    _reader = reader;
    _options = options;
    _sp = sp;
    _unitOfWork = unitOfWork;
    _dbLogger = logger.CreateLogger("DbLogger");
    _fileLogger = logger.CreateLogger("FileLogger");
    _uiLogger = logger.CreateLogger("UiLogger");
    _clientEnrichment = clientEnrichment; // ⬅️ NOVO!
}
```

---

### **KORAK 3: Refaktoring DocumentDiscoveryService**

#### **3.1. Dodavanje Tip Dosijea Logic-a**

**Trenutno stanje** (DocumentDiscoveryService.cs:556-557):
```csharp
var desFolderId = await ResolveDestinationFolder(folder, ct).ConfigureAwait(false);
_fileLogger.LogDebug("Resolved destination folder: {DestFolderId}", desFolderId);
```

**Problem**: Resolve se radi samo na osnovu imena, **NEMA validacije tipa klijenta** (FL vs PL)!

**NOVO stanje** - sa ClientAPI tipom:

```csharp
// 1. Determine client type from ClientAPI
string clientType = "UNKNOWN";
try
{
    if (!string.IsNullOrEmpty(folder.CoreId))
    {
        clientType = await _clientApi.GetClientTypeAsync(folder.CoreId, ct)
            .ConfigureAwait(false);

        _fileLogger.LogInformation(
            "Client type for folder {FolderId} (CoreId: {CoreId}): {ClientType}",
            folder.Id, folder.CoreId, clientType);
    }
    else
    {
        _fileLogger.LogWarning(
            "Folder {FolderId} has no CoreId, cannot determine client type",
            folder.Id);
    }
}
catch (Exception ex)
{
    _fileLogger.LogError(ex,
        "Failed to get client type for folder {FolderId} (CoreId: {CoreId})",
        folder.Id, folder.CoreId);
    // Fallback: pokušaj iz folder imena ili prebaci kao "OSTALO"
}

// 2. Resolve destination based on client type + document type
var desFolderId = await ResolveDestinationFolderWithClientType(
    folder,
    clientType,
    ct).ConfigureAwait(false);

_fileLogger.LogDebug(
    "Resolved destination folder for {ClientType}: {DestFolderId}",
    clientType, desFolderId);
```

#### **3.2. Nova Metoda: ResolveDestinationFolderWithClientType**

```csharp
private async Task<string> ResolveDestinationFolderWithClientType(
    FolderStaging folder,
    string clientType,
    CancellationToken ct)
{
    // Mapiranje prema dokumentaciji:
    // - Dosije klijenta FL (za fizička lica)
    // - Dosije klijenta PL (za pravna lica)
    // - Dosije paket računa
    // - Dosije ostalo
    // - Dosije depozita

    string targetDossierType = clientType switch
    {
        "FL" => "Dosije klijenta FL",
        "PL" => "Dosije klijenta PL",
        _ => "Dosije ostalo" // Fallback
    };

    var normalizedName = folder.Name?.NormalizeName()
        ?? throw new InvalidOperationException($"Folder {folder.Id} has null name");

    // Check cache
    var cacheKey = $"{targetDossierType}_{normalizedName}";
    if (_resolvedFoldersCache.TryGetValue(cacheKey, out var cachedId))
    {
        return cachedId;
    }

    // Resolve actual destination folder
    var destFolderId = await _resolver.ResolveAsync(
        _options.Value.RootDestinationFolderId,
        $"{targetDossierType}/{normalizedName}",
        ct).ConfigureAwait(false);

    _resolvedFoldersCache.TryAdd(cacheKey, destFolderId);

    return destFolderId;
}
```

#### **3.3. Constructor Injection Update**

```csharp
private readonly IClientApi _clientApi;

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
    IClientApi clientApi) // ⬅️ NOVO!
{
    // ... existing initialization
    _clientApi = clientApi; // ⬅️ NOVO!
}
```

---

### **KORAK 4: Post-Migracija - KDP Enrichment**

Ova faza se izvršava **NAKON** što sva tri servisa završe migraciju.

#### **4.1. Novi Servis: KdpEnrichmentService**

Lokacija: `Migration.Infrastructure/Implementation/Services/KdpEnrichmentService.cs`

```csharp
public interface IKdpEnrichmentService
{
    Task RunEnrichmentAsync(CancellationToken ct = default);
}

public class KdpEnrichmentService : IKdpEnrichmentService
{
    private readonly IDocStagingRepository _docRepo;
    private readonly IClientEnrichmentService _clientEnrichment;
    private readonly IOptions<MigrationOptions> _options;
    private readonly ILogger<KdpEnrichmentService> _logger;
    private readonly IServiceProvider _sp;

    public KdpEnrichmentService(
        IDocStagingRepository docRepo,
        IClientEnrichmentService clientEnrichment,
        IOptions<MigrationOptions> options,
        ILogger<KdpEnrichmentService> logger,
        IServiceProvider sp)
    {
        _docRepo = docRepo;
        _clientEnrichment = clientEnrichment;
        _options = options;
        _logger = logger;
        _sp = sp;
    }

    public async Task RunEnrichmentAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Starting KDP enrichment process...");

        // 1. Export lista aktivnih KDP dokumenata bez računa
        var kdpDocuments = await GetActiveKdpDocumentsWithoutAccountsAsync(ct)
            .ConfigureAwait(false);

        if (kdpDocuments.Count == 0)
        {
            _logger.LogInformation("No KDP documents found for enrichment");
            return;
        }

        _logger.LogInformation(
            "Found {Count} active KDP documents without account numbers",
            kdpDocuments.Count);

        // 2. Parallel enrichment
        var dop = _options.Value.MaxDegreeOfParallelism;
        var successCount = 0;
        var failedCount = 0;

        await Parallel.ForEachAsync(
            kdpDocuments,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = dop,
                CancellationToken = ct
            },
            async (doc, token) =>
            {
                try
                {
                    var enrichedDoc = await _clientEnrichment.EnrichDocumentWithAccountsAsync(
                        doc,
                        token).ConfigureAwait(false);

                    await UpdateDocumentAccountsAsync(enrichedDoc, token)
                        .ConfigureAwait(false);

                    Interlocked.Increment(ref successCount);

                    _logger.LogInformation(
                        "Enriched document {DocId} with {Count} accounts",
                        doc.Id, enrichedDoc.AccountNumbers?.Split(',').Length ?? 0);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Failed to enrich document {DocId}",
                        doc.Id);
                    Interlocked.Increment(ref failedCount);
                }
            });

        _logger.LogInformation(
            "KDP enrichment completed: {Success} succeeded, {Failed} failed",
            successCount, failedCount);
    }

    private async Task<IReadOnlyList<DocStaging>> GetActiveKdpDocumentsWithoutAccountsAsync(
        CancellationToken ct)
    {
        await using var scope = _sp.CreateAsyncScope();
        var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

        // SQL Query (dodati u repository):
        // SELECT * FROM DOC_STAGING
        // WHERE DOCUMENT_TYPE IN ('00824', '00099')
        //   AND STATUS = 'DONE'
        //   AND IS_ACTIVE = 1
        //   AND (ACCOUNT_NUMBERS IS NULL OR ACCOUNT_NUMBERS = '')

        return await docRepo.GetActiveKdpDocumentsWithoutAccountsAsync(ct)
            .ConfigureAwait(false);
    }

    private async Task UpdateDocumentAccountsAsync(
        DocStaging document,
        CancellationToken ct)
    {
        await using var scope = _sp.CreateAsyncScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

        await uow.BeginAsync(ct: ct).ConfigureAwait(false);
        try
        {
            await docRepo.UpdateAccountNumbersAsync(
                document.Id,
                document.AccountNumbers,
                ct).ConfigureAwait(false);

            await uow.CommitAsync(ct: ct).ConfigureAwait(false);
        }
        catch
        {
            await uow.RollbackAsync(ct: ct).ConfigureAwait(false);
            throw;
        }
    }
}
```

#### **4.2. Dodavanje Repository Metoda**

U `IDocStagingRepository.cs`:
```csharp
Task<IReadOnlyList<DocStaging>> GetActiveKdpDocumentsWithoutAccountsAsync(
    CancellationToken ct = default);

Task UpdateAccountNumbersAsync(
    long documentId,
    string? accountNumbers,
    CancellationToken ct = default);
```

---

## 🔄 CEL PROCES MIGRACIJE (SA ClientAPI)

### **Finalni Workflow:**

```
┌─────────────────────────────────────────────────────────┐
│          FAZA 0: Pre-Validacija (NOVO ✨)                │
└─────────────────────────────────────────────────────────┘
                        ↓
            Export liste starih dosijea
            Za svaki dosije:
              - Extract CoreId (PI10227858 → 10227858)
              - ClientAPI.ClientExists(coreId)
              - IF NOT EXISTS → SKIP
            Rezultat: Whitelist validnih klijenata
                        ↓
┌─────────────────────────────────────────────────────────┐
│       FAZA 1: Folder Discovery (REFAKTORING ✨)          │
└─────────────────────────────────────────────────────────┘
                        ↓
            Za svaki folder:
              1. Extract CoreId iz imena
              2. ✅ ClientAPI.ValidateClientExistsAsync(coreId)
              3. ✅ ClientAPI.GetClientDataAsync(coreId)
              4. ✅ Enrich FolderStaging sa:
                   - ClientName
                   - MbrJmbg
                   - ClientType (FL/PL)
                   - Residency, Segment, OPU, itd.
              5. Insert u FOLDER_STAGING
            Status: READY → IN PROGRESS → PROCESSED
                        ↓
┌─────────────────────────────────────────────────────────┐
│      FAZA 2: Document Discovery (REFAKTORING ✨)         │
└─────────────────────────────────────────────────────────┘
                        ↓
            Za svaki folder:
              1. Čita dokumente
              2. ✅ ClientAPI.GetClientTypeAsync(folder.CoreId)
              3. ✅ Determine target dosije:
                   - FL → "Dosije klijenta FL"
                   - PL → "Dosije klijenta PL"
              4. ✅ Resolve destination folder sa tipom
              5. Insert u DOC_STAGING
            Status: READY → IN PROGRESS → PROCESSED
                        ↓
┌─────────────────────────────────────────────────────────┐
│            FAZA 3: Move Service (OSTAJE ISTO)            │
└─────────────────────────────────────────────────────────┘
                        ↓
            Za svaki dokument:
              - Pomera na novu lokaciju
              - Update status
            Status: READY → IN PROGRESS → DONE
                        ↓
┌─────────────────────────────────────────────────────────┐
│          FAZA 4: KDP Enrichment (NOVO ✨)                │
└─────────────────────────────────────────────────────────┘
                        ↓
            Za svaki KDP dokument (00824, 00099):
              1. ✅ ClientAPI.GetClientAccounts(coreId)
              2. ✅ Filter račune po datumu:
                   - openDate <= doc.CreationDate
                   - status == ACTIVE
              3. ✅ Format: "R1,R2,R3"
              4. ✅ Update DOC_STAGING.ACCOUNT_NUMBERS
            Rezultat: Svi KDP imaju popunjene račune
                        ↓
┌─────────────────────────────────────────────────────────┐
│        FAZA 5: Finalizacija KDP Statusa (NOVO ✨)        │
└─────────────────────────────────────────────────────────┘
                        ↓
            Implementacija KDP pravila iz dokumentacije:
              1. IF EXISTS aktivan 00824 iz "Depo kartoni_Validan" THEN
                   → Ostali KDP → NEAKTIVNI
              2. ELSE IF NOT EXISTS iz depo kartona THEN
                   → Najnoviji KDP (max creationDate) → AKTIVAN
                   → Ostali → NEAKTIVNI
              3. IF KDP postane AKTIVAN AND tip == 00824 THEN
                   → Promeni tip u 00099
                   → Politika čuvanja: "nova verzija"
                        ↓
┌─────────────────────────────────────────────────────────┐
│      FAZA 6: Cleanup - Brisanje Starih Foldera (NOVO ✨) │
│         OldFolderCleanupService.cs                       │
└─────────────────────────────────────────────────────────┘
                        ↓
            Nakon uspešne migracije:
              1. Identifikuj sve "stare" foldere (format: PI*, PL*)
              2. Proveri da su svi dokumenti migrirani (folder prazan)
              3. ✅ Obriši ili sakrij foldere od korisnika
              4. ✅ Logovanje obrisanih foldera za audit
            Rezultat: Stari dosijei više nisu dostupni korisnicima
```

---

## 📝 DEPENDENCY INJECTION SETUP

### **Startup.cs / Program.cs**

```csharp
// 1. Registracija HttpClient za ClientAPI
services.AddHttpClient<IClientApi, ClientApiHttpService>(client =>
{
    client.BaseAddress = new Uri(configuration["ClientAPI:BaseUrl"]!);
    client.Timeout = TimeSpan.FromSeconds(30);
    client.DefaultRequestHeaders.Add("Accept", "application/json");
})
.AddPolicyHandler(GetRetryPolicy()) // Retry policy za network failures
.AddPolicyHandler(GetCircuitBreakerPolicy()); // Circuit breaker

// 2. Registracija Memory Cache (za caching klijentskih podataka)
services.AddMemoryCache();

// 3. Registracija servisa
services.AddScoped<IClientEnrichmentService, ClientEnrichmentService>();
services.AddScoped<IKdpEnrichmentService, KdpEnrichmentService>();

// Retry Policy (3 retries sa exponential backoff)
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .WaitAndRetryAsync(
            3,
            retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
            onRetry: (outcome, timespan, retryCount, context) =>
            {
                // Log retry attempt
            });
}

// Circuit Breaker (otvaranje nakon 5 uzastopnih grešaka)
static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .CircuitBreakerAsync(
            handledEventsAllowedBeforeBreaking: 5,
            durationOfBreak: TimeSpan.FromSeconds(30));
}
```

### **appsettings.json**

```json
{
  "ClientAPI": {
    "BaseUrl": "https://client-api.example.com",
    "Timeout": 30,
    "Cache": {
      "ClientExistsTTL": 3600,
      "ClientDataTTL": 1800,
      "AccountsTTL": 600
    },
    "Retry": {
      "MaxRetries": 3,
      "InitialDelay": 1000
    }
  },
  "Migration": {
    "FolderDiscovery": {
      "ValidateClientsBeforeInsert": true,
      "SkipInvalidClients": true
    },
    "DocumentDiscovery": {
      "UseDynamicDossierRouting": true
    },
    "KdpEnrichment": {
      "BatchSize": 100,
      "MaxDegreeOfParallelism": 10
    }
  }
}
```

---

## ⚠️ KRITIČNE IZMENE I RIZICI

### **Šta se MORA promeniti:**

1. ✅ **FolderDiscoveryService** - Dodati validaciju i enrichment
2. ✅ **DocumentDiscoveryService** - Dodati tip klijenta za routing
3. ❌ **MoveService** - **OSTAJE NEPROMENJEN** (nije mu potreban ClientAPI)
4. ✅ **ClientEnrichmentService** - Aktivirati (već postoji!)
5. ✅ **IClientApi + implementacija** - Kreirati od nule

### **Breaking Changes:**

- **FOLDER_STAGING tabela** mora imati **CORE_ID** kolonu (proveri!)
- **DOC_STAGING tabela** mora imati **ACCOUNT_NUMBERS** kolonu (proveri!)
- Svi postojeći podaci u staging tabelama **mogu biti nekompletni** (nemaju ClientAPI podatke)

### **Migracija Postojećih Podataka:**

```sql
-- 1. Popuni CoreId za foldere koji ga nemaju
UPDATE FOLDER_STAGING
SET CORE_ID = REGEXP_REPLACE(NAME, '[^0-9]', '')
WHERE CORE_ID IS NULL AND NAME IS NOT NULL;

-- 2. Označi foldere bez CoreId kao "ERROR"
UPDATE FOLDER_STAGING
SET STATUS = 'ERROR',
    ERROR_MESSAGE = 'Cannot extract CoreId from folder name'
WHERE CORE_ID IS NULL AND STATUS = 'READY';
```

---

## 🧪 TESTIRANJE

### **Unit Tests**

```csharp
[Fact]
public async Task FolderDiscovery_ShouldSkipInvalidClient()
{
    // Arrange
    var mockClientApi = new Mock<IClientApi>();
    mockClientApi
        .Setup(x => x.ValidateClientExistsAsync("10227858", It.IsAny<CancellationToken>()))
        .ReturnsAsync(false);

    var service = CreateFolderDiscoveryService(mockClientApi.Object);

    // Act
    var result = await service.RunBatchAsync(CancellationToken.None);

    // Assert
    Assert.Equal(0, result.InsertedCount); // Folder skipped
}

[Fact]
public async Task DocumentDiscovery_ShouldRouteTo_DosijeFLForFizickoLice()
{
    // Arrange
    var mockClientApi = new Mock<IClientApi>();
    mockClientApi
        .Setup(x => x.GetClientTypeAsync("10227858", It.IsAny<CancellationToken>()))
        .ReturnsAsync("FL");

    var service = CreateDocumentDiscoveryService(mockClientApi.Object);

    // Act
    var destinationFolder = await service.ResolveDestinationFolderWithClientType(...);

    // Assert
    Assert.Contains("Dosije klijenta FL", destinationFolder);
}

[Fact]
public async Task KdpEnrichment_ShouldPopulateAccountNumbers()
{
    // Arrange
    var mockClientApi = new Mock<IClientApi>();
    mockClientApi
        .Setup(x => x.GetActiveAccountsAsync(
            "10227858",
            It.IsAny<DateTimeOffset>(),
            It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<string> { "123456", "234567" });

    var service = CreateKdpEnrichmentService(mockClientApi.Object);

    // Act
    await service.RunEnrichmentAsync(CancellationToken.None);

    // Assert
    // Verify database updated with "123456,234567"
}
```

### **Integration Tests**

```csharp
[Fact]
public async Task EndToEnd_Migration_WithClientAPI()
{
    // 1. Setup test database sa starim folderima
    // 2. Pokreni FolderDiscovery (sa pravim ClientAPI-jem)
    // 3. Proveri da su folderi validirani i enriched
    // 4. Pokreni DocumentDiscovery
    // 5. Proveri da su dokumenti routovani u pravi dosije (FL/PL)
    // 6. Pokreni MoveService
    // 7. Pokreni KdpEnrichment
    // 8. Proveri da KDP dokumenti imaju račune
}
```

---

## 📊 PERFORMANCE OPTIMIZACIJE

### **1. Caching Strategy**

```
ClientAPI Pozivi        Cache TTL    Razlog
───────────────────────────────────────────────────
ClientExists            1 hour       Rarely changes
GetClientData           30 min       May change
GetActiveAccounts       10 min       Dynamic data
GetClientType           1 hour       Rarely changes
```

### **2. Batch Processing**

```csharp
// Umesto pojedinačnih poziva:
foreach (var folder in folders)
{
    await clientApi.GetClientDataAsync(folder.CoreId); // ❌ N poziva
}

// Koristi batch endpoint (ako postoji):
var coreIds = folders.Select(f => f.CoreId).ToList();
var clientDataBatch = await clientApi.GetClientDataBatchAsync(coreIds); // ✅ 1 poziv
```

**PROVERI** da li ClientAPI podržava batch operacije!

### **3. Circuit Breaker**

```
Ako ClientAPI padne:
- Circuit breaker otvara krug nakon 5 grešaka
- Pause od 30 sekundi
- Svi pozivi u tom periodu odmah vraćaju grešku
- Nakon 30s, probaj jedan test poziv
- Ako uspe → zatvori krug, nastavi normalno
```

---

## ✅ CHECKLIST ZA IMPLEMENTACIJU

### **Pre Kodiranja:**
- [ ] Testiraj sve ClientAPI endpointe ručno (Postman/Swagger)
- [ ] Proveri da li postoje batch endpointi
- [ ] Proveri rate limiting (koliko poziva/sec dozvoljen)
- [ ] Testiraj sa pravim CoreId-jevima iz produkcije
- [ ] Proveri da FOLDER_STAGING ima CORE_ID kolonu
- [ ] Proveri da DOC_STAGING ima ACCOUNT_NUMBERS kolonu

### **Tokom Kodiranja:**
- [ ] Implementiraj IClientApi interfejs
- [ ] Implementiraj ClientApiHttpService sa retry + circuit breaker
- [ ] Refaktoriši FolderDiscoveryService
- [ ] Refaktoriši DocumentDiscoveryService
- [ ] Implementiraj KdpEnrichmentService
- [ ] Dodaj repository metode (GetActiveKdpDocumentsWithoutAccountsAsync, itd.)
- [ ] Dodaj unit tests
- [ ] Dodaj integration tests

### **Post Kodiranje:**
- [ ] Testiraj na TEST okruženju sa malim setom podataka (10-100 foldera)
- [ ] Proveri logove - da li ClientAPI odgovara očekivano
- [ ] Proveri da li caching radi
- [ ] Testiraj error handling (prekini ClientAPI server i vidi šta se dešava)
- [ ] Testiraj performance (vreme po folderu/dokumentu)
- [ ] Kreiraj monitoring dashboard (Grafana) za ClientAPI pozive

### **Pre Produkcije:**
- [ ] Kreiraj backup trenutne baze
- [ ] Pokreni migraciju na staging okruženju (ceo proces)
- [ ] Validacija rezultata (spot check 100 nasumičnih foldera)
- [ ] Performance test sa pravim volumenom (koliko traje za 100k foldera?)
- [ ] Plan za rollback ako nešto pođe po zlu
- [ ] Dokumentuj novi proces (update runbook-a)

---

## 🚀 ZAKLJČAK

### **Da li treba refaktorizacija?**
**DA** ✅ - Ali **ClientEnrichmentService je već spreman**, samo treba:

1. **Implementirati IClientApi** (HTTP client za ClientAPI)
2. **Aktivirati ClientEnrichmentService** u FolderDiscovery i DocumentDiscovery
3. **Dodati routing logiku** za FL/PL dosijee
4. **Kreirati KdpEnrichmentService** za post-processing

### **Koliko posla?**
- **IClientApi + HTTP implementacija**: ~2-3 dana
- **FolderDiscoveryService refaktoring**: ~1 dan
- **DocumentDiscoveryService refaktoring**: ~1 dan
- **KdpEnrichmentService**: ~1-2 dana
- **Testiranje**: ~2-3 dana
- **Dokumentacija**: ~1 dan

**UKUPNO: 8-11 radnih dana** (za jednog developera)

### **Rizici:**
- ⚠️ ClientAPI može biti spor (timeout-ovi)
- ⚠️ Rate limiting može zaustaviti proces
- ⚠️ Nepotpuni podaci iz ClientAPI mogu zahtevati fallback logiku
- ⚠️ Stari podaci u FOLDER_STAGING/DOC_STAGING mogu biti nepotpuni

### **Benefit:**
- ✅ Puna validacija klijenata
- ✅ Popunjeni atributi dosijea
- ✅ Automatski routing FL/PL
- ✅ KDP dokumenti sa računima
- ✅ Compliance sa dokumentacijom

