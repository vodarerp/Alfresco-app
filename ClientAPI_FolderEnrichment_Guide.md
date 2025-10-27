# ClientAPI Folder Enrichment - Integration Guide

## Pregled

ClientAPI je integrisan u `FolderDiscoveryService` za automatsko obogaćivanje folder metapodataka. Sistem prvo pokušava da pročita client propertije iz Alfresca, a ako nisu dostupni, poziva ClientAPI da popuni podatke.

## Arhitektura Rešenja

### Flow Dijagram

```
FolderDiscoveryService.RunBatchAsync
    ↓
Read folders from Alfresco
    ↓
EnrichFoldersWithClientDataAsync
    ↓
┌─────────────────────────────────────┐
│ For each folder:                    │
│  1. Try parse from Alfresco props   │
│  2. If missing → Extract CoreId      │
│  3. Call ClientAPI.GetClientData    │
│  4. Map to ClientProperties          │
└─────────────────────────────────────┘
    ↓
Insert to staging tables
```

### Komponente

#### 1. **Entry Model** (`Alfresco.Contracts/Models/Entry.cs`)
```csharp
public class Entry
{
    // Standard Alfresco properties
    public string Name { get; set; }
    public string Id { get; set; }

    // Raw Alfresco properties (ecm:* custom properties)
    public Dictionary<string, object>? Properties { get; set; }

    // Parsed client properties
    public ClientProperties? ClientProperties { get; set; }
}
```

#### 2. **ClientProperties Model** (`Alfresco.Contracts/Models/ClientProperties.cs`)
```csharp
public class ClientProperties
{
    public string? CoreId { get; set; }
    public string? MbrJmbg { get; set; }
    public string? ClientName { get; set; }
    public string? ClientType { get; set; }  // FL or PL
    public string? ClientSubtype { get; set; }
    public string? Residency { get; set; }
    public string? Segment { get; set; }
    public string? Staff { get; set; }
    public string? OpuUser { get; set; }
    public string? OpuRealization { get; set; }
    public string? Barclex { get; set; }
    public string? Collaborator { get; set; }

    // Helper properties
    public bool HasClientData => !string.IsNullOrWhiteSpace(CoreId);
    public bool IsComplete => /* checks required fields */;
}
```

#### 3. **Extension Methods**

##### ClientPropertiesExtensions (`Alfresco.Contracts/Extensions/ClientPropertiesExtensions.cs`)
```csharp
// Parse Alfresco custom properties (ecm:*) into ClientProperties
public static ClientProperties? ParseFromAlfrescoProperties(
    this Dictionary<string, object>? alfrescoProperties)

// Populate ClientProperties on Entry from Alfresco properties
public static void PopulateClientProperties(this Entry entry)

// Check if Entry has client properties
public static bool HasClientProperties(this Entry entry)

// Extract CoreId from folder name (e.g., "PL-10000123TTT" -> "10000123")
public static string? TryExtractCoreIdFromName(this Entry entry)
```

##### ClientDataExtensions (`Migration.Infrastructure/Extensions/ClientDataExtensions.cs`)
```csharp
// Convert ClientData from ClientAPI to ClientProperties
public static ClientProperties ToClientProperties(this ClientData clientData)

// Enrich Entry with ClientData
public static void EnrichWithClientData(this Entry entry, ClientData clientData)
```

#### 4. **FolderDiscoveryService** (`Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs`)

##### Constructor
```csharp
public FolderDiscoveryService(
    IFolderIngestor ingestor,
    IFolderReader reader,
    IDocumentResolver resolver,
    IOptions<MigrationOptions> options,
    IServiceProvider sp,
    IUnitOfWork unitOfWork,
    ILoggerFactory logger,
    IClientApi? clientApi = null)  // ← ClientAPI is optional
```

##### Enrichment Logic
```csharp
private async Task EnrichFoldersWithClientDataAsync(
    IReadOnlyList<ListEntry> folders,
    CancellationToken ct)
{
    if (_clientApi == null) return;  // Skip if not configured

    foreach (var listEntry in folders)
    {
        var entry = listEntry.Entry;

        // 1. Try to parse from Alfresco properties
        entry.PopulateClientProperties();

        // 2. If already has data, skip ClientAPI call
        if (entry.HasClientProperties())
            continue;

        // 3. Extract CoreId from folder name
        var coreId = entry.TryExtractCoreIdFromName();
        if (string.IsNullOrWhiteSpace(coreId))
            continue;

        // 4. Call ClientAPI
        var clientData = await _clientApi.GetClientDataAsync(coreId, ct);

        // 5. Enrich entry
        entry.EnrichWithClientData(clientData);
    }
}
```

## Alfresco Custom Properties Mapping

### Alfresco `ecm:` Properties → ClientProperties

| Alfresco Property | ClientProperties Field |
|-------------------|------------------------|
| `ecm:coreId` | `CoreId` |
| `ecm:mbrJmbg` | `MbrJmbg` |
| `ecm:clientName` | `ClientName` |
| `ecm:clientType` | `ClientType` |
| `ecm:clientSubtype` | `ClientSubtype` |
| `ecm:residency` | `Residency` |
| `ecm:segment` | `Segment` |
| `ecm:staff` | `Staff` |
| `ecm:opuUser` | `OpuUser` |
| `ecm:opuRealization` | `OpuRealization` |
| `ecm:barclex` | `Barclex` |
| `ecm:collaborator` | `Collaborator` |

### Primer Alfresco Response sa Properties

```json
{
  "entry": {
    "name": "PL-10000123TTT",
    "id": "abc-123-def",
    "properties": {
      "ecm:coreId": "10000123",
      "ecm:mbrJmbg": "12345678",
      "ecm:clientName": "Privredno Društvo Test DOO",
      "ecm:clientType": "PL",
      "ecm:clientSubtype": "SME",
      "ecm:residency": "Resident",
      "ecm:segment": "Corporate",
      "ecm:staff": "N",
      "ecm:opuUser": "OPU-100",
      "ecm:opuRealization": "OPU-200/ID-5000",
      "ecm:barclex": "BX12345",
      "ecm:collaborator": "Partner Bank A"
    }
  }
}
```

## Folder Naming Convention

Sistem očekuje da folderi prate naming convention za ekstrakciju CoreId:

**Format:** `{ClientType}-{CoreId}TTT`

**Primeri:**
- `PL-10000123TTT` → CoreId: `10000123`
- `FL-10000456TTT` → CoreId: `10000456`
- `ACC-10000789TTT` → CoreId: `10000789`

Metoda `TryExtractCoreIdFromName()` parsira ovaj format.

## ClientAPI Integration

### Kada se poziva ClientAPI?

ClientAPI se poziva **samo** kada:
1. Folder **NEMA** Alfresco custom properties (`ecm:coreId` nedostaje)
2. CoreId se može ekstraktovati iz imena foldera

### Prednosti ovog pristupa

✅ **Optimizacija performansi** - ClientAPI se poziva samo kada je potrebno
✅ **Fallback mehanizam** - Ako Alfresco ima properties, koristi ih; inače ClientAPI
✅ **Graceful degradation** - Ako ClientAPI nije konfigurisan, proces nastavlja bez greške
✅ **Resiliencija** - Greške u ClientAPI pozivima ne zaustavljaju obradu drugih foldera

## Logging

```csharp
// Debug level
_fileLogger.LogDebug("ClientAPI not configured, skipping client data enrichment");
_fileLogger.LogDebug("Folder {Name} already has client properties from Alfresco", entry.Name);
_fileLogger.LogDebug("Fetching client data from ClientAPI for CoreId: {CoreId}", coreId);

// Info level
_fileLogger.LogInformation(
    "Enriched {EnrichedCount} folders with ClientAPI data (Errors: {ErrorCount})",
    enrichmentCount, errorCount);

// Warning level
_fileLogger.LogWarning("Could not extract CoreId from folder name: {Name}", entry.Name);
_fileLogger.LogWarning(ex,
    "Failed to enrich folder {Name} with ClientAPI data for CoreId: {CoreId}",
    entry.Name, coreId);
```

## Error Handling

```csharp
try
{
    var clientData = await _clientApi.GetClientDataAsync(coreId, ct);
    entry.EnrichWithClientData(clientData);
    enrichmentCount++;
}
catch (Exception ex)
{
    errorCount++;
    // Log warning but continue processing other folders
    _fileLogger.LogWarning(ex, "Failed to enrich folder...");
}
```

Sistem **ne zaustav ja** obradu ako jedan folder ne uspe da se obogati. Nastavlja sa sledećim folderima.

## Testiranje

### 1. Testiranje sa Alfresco Properties

Ako folder ima `ecm:*` properties u Alfresсu:
```bash
# ClientAPI se NEĆE pozvati
curl -k "http://localhost:8080/.../nodes/{folderId}/children?include=properties"
```

Rezultat: Folder će biti obogaćen iz Alfresco properties.

### 2. Testiranje sa ClientAPI

Ako folder NEMA `ecm:*` properties:
```bash
# 1. Pokreni Mock API
cd MockClientAPI
dotnet run

# 2. Pokreni Alfresco.App
# FolderDiscoveryService će pozvati ClientAPI za foldere bez properties
```

Rezultat: Folder će biti obogaćen iz ClientAPI.

### 3. Provera Logova

```
[DEBUG] Folder PL-10000123TTT already has client properties from Alfresco
[DEBUG] Fetching client data from ClientAPI for CoreId: 10000456
[INFO] Successfully enriched folder FL-10000456TTT with ClientAPI data
[INFO] Enriched 25 folders with ClientAPI data (Errors: 0)
```

## Konfiguracija

### appsettings.json

```json
{
  "ClientApi": {
    "BaseUrl": "https://localhost:5101",
    "GetClientDataEndpoint": "/api/Client/GetClientDetailExtended",
    "TimeoutSeconds": 30,
    "RetryCount": 3
  }
}
```

### DI Registration (App.xaml.cs)

ClientAPI je već registrovan u `App.xaml.cs` (linije 183-214):
```csharp
services.AddHttpClient<IClientApi, Migration.Infrastructure.Implementation.ClientApi>(...)
    .AddPolicyHandler(GetRetryPlicy())
    .AddPolicyHandler(GetCircuitBreakerPolicy());
```

`FolderDiscoveryService` automatski prima `IClientApi` kroz Dependency Injection.

## Alfresco Content Model

Za kreiranje custom `ecm:*` properties u Alfresсu, pogledajte dokumentaciju za bankContentModel.xml deployment.

Ako je content model deployovan, Alfresco će vraćati `ecm:*` properties automatski.

## Troubleshooting

### Problem: ClientAPI se ne poziva

**Provera:**
1. Da li je ClientAPI registrovan u DI? (App.xaml.cs linija 188)
2. Da li je Mock API pokrenut? (`dotnet run` u MockClientAPI folderu)
3. Da li je BaseUrl tačan u appsettings.json?

**Debug:**
```csharp
// Dodaj breakpoint u FolderDiscoveryService.cs:650
if (_clientApi == null)
{
    _fileLogger.LogDebug("ClientAPI not configured...");
}
```

### Problem: Cannot extract CoreId

**Uzrok:** Folder name ne prati format `{Type}-{CoreId}TTT`

**Rešenje:** Osigurajte da folderi imaju pravilno ime ili dodajte custom parsing logiku u `TryExtractCoreIdFromName()`.

### Problem: ClientAPI timeout

**Rešenje:** Povećajte timeout u appsettings.json:
```json
{
  "ClientApi": {
    "TimeoutSeconds": 60
  }
}
```

## Database Persistence

### Automatic Mapping to FolderStaging Table

ClientProperties se automatski mapiraju u `FolderStaging` tabelu preko `MyMapper.ToFolderStagingInsert()` extension metode.

**Mapping Flow:**
```
Entry.ClientProperties → FolderStaging → SQL Server Database
```

**Mapped Fields:**
- CoreId → FolderStaging.CoreId
- MbrJmbg → FolderStaging.MbrJmbg
- ClientName → FolderStaging.ClientName
- ClientType → FolderStaging.ClientType
- ClientSubtype → FolderStaging.ClientSubtype
- Residency → FolderStaging.Residency
- Segment → FolderStaging.Segment
- Staff → FolderStaging.Staff
- OpuUser → FolderStaging.OpuUser
- OpuRealization → FolderStaging.OpuRealization
- Barclex → FolderStaging.Barclex
- Collaborator → FolderStaging.Collaborator

**Verification Query:**
```sql
SELECT CoreId, ClientName, ClientType, Residency, Segment
FROM FolderStaging
WHERE CoreId IS NOT NULL
ORDER BY CreatedAt DESC;
```

Za detaljnije informacije o database mapping-u, pogledajte `ClientAPI_Database_Mapping.md`.

## Sledeći Koraci

- Implementirati sličnu logiku u `DocumentDiscoveryService` za obogaćivanje dokumenata
- Dodati caching za ClientAPI responses da se smanje pozivi
- Dodati metrics za praćenje koliko foldera se obogaćuje iz Alfresca vs ClientAPI

## Dodatna Dokumentacija

- **ClientAPI Integration Guide**: `ClientAPI_Integration_Guide.md`
- **ClientAPI Database Mapping**: `ClientAPI_Database_Mapping.md`
- **Mock API Documentation**: `Mock_ClientAPI_DOKUMENTACIJA.md`
- **ClientApi Implementation**: `Migration.Infrastructure/Implementation/ClientApi.cs`
- **FolderDiscoveryService**: `Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs`
- **Mapper Implementation**: `Mapper/MyMapper.cs` (linija 26-57)
