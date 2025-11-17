# Plan Refaktorisanja - Zavisnosti Glavnih Servisa

**Verzija:** 1.0
**Datum:** 2025-01-17
**Status:** Draft - Za review

---

## 📋 Sadržaj

1. [Pregled i ciljevi](#1-pregled-i-ciljevi)
2. [Analiza zavisnosti](#2-analiza-zavisnosti)
3. [Prioritizacija problema](#3-prioritizacija-problema)
4. [Detaljni plan refaktorisanja](#4-detaljni-plan-refaktorisanja)
5. [Implementacioni plan](#5-implementacioni-plan)
6. [Testing strategija](#6-testing-strategija)
7. [Metrike uspešnosti](#7-metrike-uspešnosti)

---

## 1. Pregled i ciljevi

### 1.1 Kontekst

Alfresco Migration aplikacija je **one-time** aplikacija za migraciju ~1.5M dokumenata i ~1M foldera. Tri glavna servisa koriste niz zavisnosti koje imaju performansne probleme i potencijalne memory leak-ove.

### 1.2 Glavni servisi

```
FolderDiscoveryService  (783 LOC)
    ↓
DocumentDiscoveryService (1340 LOC)
    ↓
FolderPreparationService (NOVI)
    ↓
MoveService (1435 LOC)
```

### 1.3 Ciljevi refaktorisanja zavisnosti

- ✅ **Stabilnost**: Elimisati memory leak-ove i connection pool exhaustion
- ✅ **Performance**: Optimizovati kritične bottleneck-e (OpisToTipMapperV2)
- ✅ **Skalabilnost**: Pripremiti za 1.5M+ dokumenata
- ✅ **Maintainability**: Čitljiv i testabilan kod
- ✅ **Sequential + Parallel**: Prilagoditi za hybrid pristup

### 1.4 Ograničenja

- **One-time aplikacija**: Ne treba over-engineering
- **Stabilnost > Brzina**: Sigurnost je prioritet
- **Nema Redis**: In-memory caching samo
- **Alfresco API limiti**: Nema batch operacija za folder creation

---

## 2. Analiza zavisnosti

### 2.1 Dependency Tree

#### FolderDiscoveryService Dependencies

```
FolderDiscoveryService
├── IFolderReader ✅ (OK - stateless, thread-safe)
├── IFolderIngestor ✅ (OK - bulk operations)
├── IClientApi ⚠️ (MEDIUM - no caching)
├── IFolderStagingRepository ✅ (OK - scoped)
├── IMigrationCheckpointRepository ✅ (OK - scoped)
├── IUnitOfWork ✅ (OK - scoped per transaction)
└── ILoggerFactory ✅ (OK - thread-safe)
```

#### DocumentDiscoveryService Dependencies

```
DocumentDiscoveryService
├── IDocumentReader ❌ (CRITICAL - no pagination)
├── IDocumentIngestor ✅ (OK - bulk operations)
├── OpisToTipMapperV2 ❌ (CRITICAL - no caching, DB pressure)
├── IDocStagingRepository ✅ (OK - scoped)
├── IFolderStagingRepository ✅ (OK - scoped)
├── IMigrationCheckpointRepository ✅ (OK - scoped)
├── IUnitOfWork ✅ (OK - scoped per transaction)
└── ILoggerFactory ✅ (OK - thread-safe)
```

#### MoveService Dependencies

```
MoveService
├── IMoveReader ✅ (OK - stateless)
├── IMoveExecutor ✅ (OK - stateless)
├── IDocumentResolver ⚠️ (MEDIUM - memory leak in _folderLocks)
├── IAlfrescoWriteApi ✅ (OK - thread-safe HttpClient)
├── IAlfrescoReadApi ✅ (OK - thread-safe HttpClient)
├── IDocStagingRepository ✅ (OK - scoped)
├── IMigrationCheckpointRepository ✅ (OK - scoped)
├── IUnitOfWork ✅ (OK - scoped per transaction)
└── ILoggerFactory ✅ (OK - thread-safe)
```

### 2.2 Shared Dependencies (sve tri službe koriste)

```
Shared Infrastructure
├── IAlfrescoReadApi ✅ (OK - HttpClient, thread-safe)
├── IAlfrescoWriteApi ✅ (OK - HttpClient, thread-safe)
├── IUnitOfWork ✅ (OK - scoped, connection pooling)
├── SqlServerRepository<T> ✅ (OK - bulk operations, Dapper)
└── CheckpointManager ⚠️ (TODO - treba centralizovati)
```

---

## 3. Prioritizacija problema

### 3.1 Kritični problemi (MUST FIX)

| # | Problem | Komponenta | Impact | Prioritet |
|---|---------|------------|--------|-----------|
| 1 | **Nema caching** - svaki dokument udara bazu 3× | `OpisToTipMapperV2` | 4.5M SQL poziva, connection pool exhaustion | 🔴 CRITICAL |
| 2 | **Nema pagination** - čita sve dokumente iz foldera odjednom | `DocumentReader` | Memory spike, OutOfMemory rizik | 🔴 HIGH |

### 3.2 Srednji prioritet (SHOULD FIX)

| # | Problem | Komponenta | Impact | Prioritet |
|---|---------|------------|--------|-----------|
| 3 | **Memory leak** - SemaphoreSlim se ne čiste | `DocumentResolver._folderLocks` | Polako curenje memorije | 🟠 MEDIUM |
| 4 | **Nema caching** - svaki folder enrichment udara eksterni API | `ClientApi` | Spori API pozivi, latency | 🟡 LOW |

### 3.3 Niskoprioritene optimizacije (NICE TO HAVE)

| # | Optimizacija | Komponenta | Benefit | Prioritet |
|---|--------------|------------|---------|-----------|
| 5 | LRU eviction za folder cache | `DocumentResolver` | Bolja cache strategija | ⚪ LOW |
| 6 | Circuit breaker za Alfresco API | `AlfrescoReadApi` | Resilience | ⚪ LOW |
| 7 | Connection pool monitoring | `UnitOfWork` | Observability | ⚪ LOW |

---

## 4. Detaljni plan refaktorisanja

### 4.1 Problem #1: OpisToTipMapperV2 - Kritični caching problem

#### 4.1.1 Trenutno stanje

**Fajl:** `/home/user/Alfresco-app/Migration.Infrastructure/Implementation/OpisToTipMapperV2.cs`

**Problem:**
```csharp
// Trenutni kod - SVAKI dokument udara bazu 3 puta!
public async Task<string> GetTipDokumentaAsync(string opisDokumenta, CancellationToken ct = default)
{
    // 1. Try original name lookup (SELECT + JOIN)
    var result1 = await _documentMappingService.GetByOriginalNameAsync(opisDokumenta, ct);
    if (result1 != null) return result1.NewDocumentCode;

    // 2. Try Serbian name lookup (SELECT + JOIN)
    var result2 = await _documentMappingService.GetBySerbianNameAsync(opisDokumenta, ct);
    if (result2 != null) return result2.NewDocumentCode;

    // 3. Try migrated name lookup (SELECT + JOIN)
    var result3 = await _documentMappingService.GetByMigratedNameAsync(opisDokumenta, ct);
    if (result3 != null) return result3.NewDocumentCode;

    return "UNKNOWN";
}
```

**Impact analiza:**
```
1.5M dokumenata × 3 SQL poziva u najgorem slučaju = 4.5M SQL poziva!
Prosečno vreme po lookup-u: 10-50ms
Ukupno vreme: 1.5M × 30ms = 45,000s = 12.5 SATI samo za mapping! 😱

Connection pool exhaustion rizik: VISOK
```

#### 4.1.2 Predloženo rešenje

**Strategija:** Učitaj SVE mappinge JEDNOM u memoriju pri startu aplikacije.

**Novi kod:**
```csharp
public class OptimizedOpisToTipMapper : IOpisToTipMapper
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<OptimizedOpisToTipMapper> _logger;

    // Lazy initialization - učitava se samo prvi put kad se pozove
    private static readonly Lazy<Task<DocumentMappingCache>> _mappingCache =
        new Lazy<Task<DocumentMappingCache>>(LoadAllMappingsAsync);

    public OptimizedOpisToTipMapper(
        IServiceProvider serviceProvider,
        ILogger<OptimizedOpisToTipMapper> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Gets document type from cache (in-memory lookup - instant!)
    /// </summary>
    public async Task<string> GetTipDokumentaAsync(
        string opisDokumenta,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(opisDokumenta))
            return "UNKNOWN";

        // Get cache (učitava se samo prvi put)
        var cache = await _mappingCache.Value;

        // Memory lookup - O(1) - <1ms
        return cache.GetDocumentType(opisDokumenta);
    }

    /// <summary>
    /// Loads ALL document mappings from database into memory ONCE
    /// </summary>
    private static async Task<DocumentMappingCache> LoadAllMappingsAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var mappingService = scope.ServiceProvider.GetRequiredService<IDocumentMappingService>();

        await uow.BeginAsync();

        try
        {
            // Load ALL mappings from database (one query!)
            var allMappings = await mappingService.GetAllMappingsAsync();

            await uow.CommitAsync();

            _logger.LogInformation(
                "Loaded {Count} document mappings into memory cache",
                allMappings.Count);

            // Build efficient lookup structure
            return new DocumentMappingCache(allMappings);
        }
        catch
        {
            await uow.RollbackAsync();
            throw;
        }
    }
}

/// <summary>
/// In-memory cache for document mappings with multiple lookup strategies
/// </summary>
public class DocumentMappingCache
{
    private readonly Dictionary<string, string> _originalNameLookup;
    private readonly Dictionary<string, string> _serbianNameLookup;
    private readonly Dictionary<string, string> _migratedNameLookup;

    public DocumentMappingCache(List<DocumentMapping> mappings)
    {
        // Build three lookup dictionaries for fast access
        _originalNameLookup = mappings
            .Where(m => !string.IsNullOrEmpty(m.OriginalDocumentName))
            .ToDictionary(
                m => m.OriginalDocumentName!.ToLowerInvariant(),
                m => m.NewDocumentCode,
                StringComparer.OrdinalIgnoreCase);

        _serbianNameLookup = mappings
            .Where(m => !string.IsNullOrEmpty(m.SerbianDocumentName))
            .ToDictionary(
                m => m.SerbianDocumentName!.ToLowerInvariant(),
                m => m.NewDocumentCode,
                StringComparer.OrdinalIgnoreCase);

        _migratedNameLookup = mappings
            .Where(m => !string.IsNullOrEmpty(m.MigratedDocumentName))
            .ToDictionary(
                m => m.MigratedDocumentName!.ToLowerInvariant(),
                m => m.NewDocumentCode,
                StringComparer.OrdinalIgnoreCase);
    }

    public string GetDocumentType(string opisDokumenta)
    {
        var normalizedOpis = opisDokumenta.Trim().ToLowerInvariant();

        // Try three lookup strategies (all O(1) - instant!)
        if (_originalNameLookup.TryGetValue(normalizedOpis, out var type1))
            return type1;

        if (_serbianNameLookup.TryGetValue(normalizedOpis, out var type2))
            return type2;

        if (_migratedNameLookup.TryGetValue(normalizedOpis, out var type3))
            return type3;

        return "UNKNOWN";
    }
}
```

**Novi metod u IDocumentMappingService:**
```csharp
public interface IDocumentMappingService
{
    // Postojeći metodi...
    Task<DocumentMapping?> GetByOriginalNameAsync(string name, CancellationToken ct);
    Task<DocumentMapping?> GetBySerbianNameAsync(string name, CancellationToken ct);
    Task<DocumentMapping?> GetByMigratedNameAsync(string name, CancellationToken ct);

    // NOVI metod za učitavanje svih mappings
    Task<List<DocumentMapping>> GetAllMappingsAsync(CancellationToken ct = default);
}
```

**Implementacija GetAllMappingsAsync:**
```csharp
public class DocumentMappingService : IDocumentMappingService
{
    public async Task<List<DocumentMapping>> GetAllMappingsAsync(
        CancellationToken ct = default)
    {
        // Single query - učitaj SVE mappinge odjednom
        var mappings = await _repository.GetListAsync(
            filters: null, // Bez filtera - sve
            skip: null,
            take: null,
            orderBy: null,
            ct: ct);

        return mappings.ToList();
    }
}
```

#### 4.1.3 Performance projekcija

```
PRE optimizacije:
  - 4.5M SQL poziva
  - 1.5M × 30ms = 45,000s = 12.5 SATI
  - Connection pool exhaustion: VISOK rizik

POSLE optimizacije:
  - 1 SQL poziv (startup)
  - 1.5M × <1ms = 1,500s = 25 MINUTA
  - Connection pool exhaustion: NEMA rizika

POBOLJŠANJE: 30× BRŽE! 🚀
```

#### 4.1.4 Memory footprint

```
Procena veličine cache-a:
  - Pretpostavka: 500 različitih document mappings
  - Po mapping: ~200 bytes (3 stringa + overhead)
  - Ukupno: 500 × 200 = 100 KB

Zaključak: ZANEMARLJIVO mali memory footprint ✅
```

#### 4.1.5 Registracija u DI

```csharp
// Program.cs ili Startup.cs
services.AddSingleton<IOpisToTipMapper, OptimizedOpisToTipMapper>();
// ↑ SINGLETON jer cache se deli između svih instanci
```

---

### 4.2 Problem #2: DocumentReader - Nema pagination

#### 4.2.1 Trenutno stanje

**Fajl:** `/home/user/Alfresco-app/Migration.Infrastructure/Implementation/Document/DocumentReader.cs`

**Problem:**
```csharp
// Trenutni kod - čita SVE dokumente iz foldera odjednom!
public async Task<IReadOnlyList<ListEntry>> ReadBatchAsync(
    string folderNodeId,
    CancellationToken ct = default)
{
    var url = $"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{folderNodeId}/children?" +
              $"include=properties";
    // ↑ NEMA skipCount & maxItems parametara!

    var response = await _httpClient.GetAsync(url, ct);
    var result = await response.Content.ReadFromJsonAsync<ListResponse>(ct);

    return result?.List?.Entries ?? new List<ListEntry>();
}
```

**Impact analiza:**
```
Scenario: Folder sa 10,000 dokumenata
  - Trenutno: Učita 10,000 u memoriju odjednom
  - Memory spike: ~50-100 MB po folderu
  - Rizik: OutOfMemoryException za velike foldere
```

#### 4.2.2 Predloženo rešenje

**Strategija:** Dodati pagination parametre i cursor support.

**Novi kod:**
```csharp
public class DocumentReader : IDocumentReader
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<DocumentReader> _logger;

    /// <summary>
    /// Reads documents from folder with pagination support
    /// </summary>
    public async Task<DocumentReaderResult> ReadBatchAsync(
        string folderNodeId,
        int skipCount = 0,        // NOVO
        int maxItems = 1000,      // NOVO - default 1000
        CancellationToken ct = default)
    {
        var url = $"/alfresco/api/-default-/public/alfresco/versions/1/nodes/{folderNodeId}/children?" +
                  $"skipCount={skipCount}&" +
                  $"maxItems={maxItems}&" +
                  $"include=properties&" +
                  $"orderBy=cm:created ASC"; // Sort za konzistentnost

        _logger.LogDebug(
            "Reading documents from folder {FolderId} (skip: {Skip}, max: {Max})",
            folderNodeId, skipCount, maxItems);

        var response = await _httpClient.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<ListResponse>(ct);

        var entries = result?.List?.Entries ?? new List<ListEntry>();
        var pagination = result?.List?.Pagination;

        var hasMore = pagination?.HasMoreItems ?? false;
        var totalItems = pagination?.TotalItems ?? 0;

        _logger.LogDebug(
            "Read {Count} documents, hasMore: {HasMore}, total: {Total}",
            entries.Count, hasMore, totalItems);

        return new DocumentReaderResult
        {
            Items = entries,
            HasMore = hasMore,
            TotalItems = totalItems,
            NextSkipCount = skipCount + entries.Count
        };
    }
}

/// <summary>
/// Result from DocumentReader with pagination info
/// </summary>
public class DocumentReaderResult
{
    public IReadOnlyList<ListEntry> Items { get; init; } = Array.Empty<ListEntry>();
    public bool HasMore { get; init; }
    public int TotalItems { get; init; }
    public int NextSkipCount { get; init; }
}
```

**Interface update:**
```csharp
public interface IDocumentReader
{
    Task<DocumentReaderResult> ReadBatchAsync(
        string folderNodeId,
        int skipCount = 0,
        int maxItems = 1000,
        CancellationToken ct = default);
}
```

#### 4.2.3 Upotreba u DocumentDiscoveryService

**PRE:**
```csharp
var allDocs = await _documentReader.ReadBatchAsync(folderId, ct);
// ↑ Sve dokumente odjednom
```

**POSLE:**
```csharp
var skipCount = 0;
var maxItems = 5000; // Konfigurisano

while (true)
{
    var result = await _documentReader.ReadBatchAsync(
        folderId, skipCount, maxItems, ct);

    if (result.Items.Count == 0)
        break;

    // Process batch
    await ProcessDocumentBatchAsync(result.Items, ct);

    if (!result.HasMore)
        break;

    skipCount = result.NextSkipCount;
}
```

#### 4.2.4 Performance projekcija

```
PRE pagination:
  - 10,000 dokumenata u memoriji odjednom
  - Memory spike: ~100 MB
  - Rizik: OutOfMemoryException

POSLE pagination (batch 5000):
  - Max 5,000 dokumenata u memoriji
  - Memory spike: ~50 MB
  - Rizik: ELIMINISAN ✅
```

---

### 4.3 Problem #3: DocumentResolver - Memory leak u _folderLocks

#### 4.3.1 Trenutno stanje

**Fajl:** `/home/user/Alfresco-app/Migration.Infrastructure/Implementation/Document/DocumentResolver.cs`

**Problem:**
```csharp
// Trenutni kod - SemaphoreSlim se dodaju ali NIKAD ne čiste!
private readonly ConcurrentDictionary<string, SemaphoreSlim> _folderLocks = new();

public async Task<string> GetOrCreateFolderAsync(string folderPath, ...)
{
    // Kreira SemaphoreSlim za svaki unique folder
    var semaphore = _folderLocks.GetOrAdd(
        folderPath,
        _ => new SemaphoreSlim(1, 1)); // ← Ostaje u memoriji ZAUVEK!

    await semaphore.WaitAsync(ct);
    try
    {
        // Create folder logic...
    }
    finally
    {
        semaphore.Release();
    }
}
```

**Impact analiza:**
```
Scenario: 500,000 unique foldera
  - 500K SemaphoreSlim objekata u memoriji
  - Po SemaphoreSlim: ~200 bytes
  - Ukupno: 500K × 200 = 100 MB memory leak

Nije katastrofalno, ali nije potrebno.
```

#### 4.3.2 Predloženo rešenje

**Strategija 1:** Periodično čišćenje nekorišćenih lock-ova

```csharp
public class DocumentResolver : IDocumentResolver
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _folderLocks = new();
    private readonly Timer _cleanupTimer;

    public DocumentResolver(...)
    {
        // Setup cleanup timer - svakih 5 minuta
        _cleanupTimer = new Timer(
            CleanupUnusedLocks,
            state: null,
            dueTime: TimeSpan.FromMinutes(5),
            period: TimeSpan.FromMinutes(5));
    }

    private void CleanupUnusedLocks(object? state)
    {
        var removedCount = 0;

        // Find locks that are not in use (CurrentCount == 1)
        var unusedKeys = _folderLocks
            .Where(kvp => kvp.Value.CurrentCount == 1)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in unusedKeys)
        {
            // Double-check and remove
            if (_folderLocks.TryRemove(key, out var semaphore))
            {
                if (semaphore.CurrentCount == 1) // Still unused
                {
                    semaphore.Dispose();
                    removedCount++;
                }
                else
                {
                    // Was used in meantime, put it back
                    _folderLocks.TryAdd(key, semaphore);
                }
            }
        }

        if (removedCount > 0)
        {
            _logger.LogDebug(
                "Cleaned up {Count} unused folder locks. Remaining: {Remaining}",
                removedCount, _folderLocks.Count);
        }
    }

    public void Dispose()
    {
        _cleanupTimer?.Dispose();

        // Dispose all remaining semaphores
        foreach (var semaphore in _folderLocks.Values)
        {
            semaphore.Dispose();
        }

        _folderLocks.Clear();
    }
}
```

**Strategija 2:** Ograničen broj lock-ova (lock striping)

```csharp
public class DocumentResolver : IDocumentResolver
{
    // Umesto SemaphoreSlim PER FOLDER, koristi fiksnih N lock-ova
    private readonly SemaphoreSlim[] _lockStripes;
    private const int STRIPE_COUNT = 1024; // 2^10

    public DocumentResolver(...)
    {
        _lockStripes = Enumerable.Range(0, STRIPE_COUNT)
            .Select(_ => new SemaphoreSlim(1, 1))
            .ToArray();
    }

    private SemaphoreSlim GetLockForFolder(string folderPath)
    {
        // Hash folder path to lock index
        var hash = folderPath.GetHashCode();
        var index = Math.Abs(hash % STRIPE_COUNT);
        return _lockStripes[index];
    }

    public async Task<string> GetOrCreateFolderAsync(string folderPath, ...)
    {
        // Use striped lock
        var semaphore = GetLockForFolder(folderPath);

        await semaphore.WaitAsync(ct);
        try
        {
            // Folder creation logic (sa double-check!)
            if (_folderCache.TryGetValue(folderPath, out var cached))
                return cached;

            // Create folder...
        }
        finally
        {
            semaphore.Release();
        }
    }
}
```

**Preporuka:** Koristiti **Strategiju 2** (lock striping) jer je jednostavnija i nema potrebe za cleanup-om.

#### 4.3.3 Performance projekcija

```
PRE optimizacije:
  - 500K SemaphoreSlim objekata
  - 100 MB memorije zauzeto

POSLE optimizacije (lock striping):
  - 1024 SemaphoreSlim objekata
  - ~200 KB memorije

UŠTEDA: 99.8% memorije! 🚀
```

---

### 4.4 Problem #4: ClientApi - Nema caching (LOW priority)

#### 4.4.1 Trenutno stanje

**Fajl:** `/home/user/Alfresco-app/Migration.Infrastructure/Implementation/ClientApi.cs`

**Problem:**
```csharp
// Svaki poziv udara eksterni API
public async Task<ClientData> GetClientDataAsync(string coreId, ...)
{
    var url = $"{_baseUrl}/api/clients/{coreId}";
    var response = await _httpClient.GetAsync(url, ct);
    // ...
}
```

**Impact:** Nizak prioritet jer je ClientAPI **opcioni** dependency.

#### 4.4.2 Predloženo rešenje

**Dodati IMemoryCache:**

```csharp
public class ClientApi : IClientApi
{
    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<ClientApi> _logger;

    private readonly MemoryCacheEntryOptions _cacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1),
        SlidingExpiration = TimeSpan.FromMinutes(20)
    };

    public async Task<ClientData> GetClientDataAsync(
        string coreId,
        CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(coreId))
            return ClientData.Empty;

        // Check cache
        var cacheKey = $"client_{coreId}";

        if (_cache.TryGetValue(cacheKey, out ClientData? cached))
        {
            _logger.LogDebug("Cache HIT for CoreId: {CoreId}", coreId);
            return cached!;
        }

        // Cache MISS - fetch from API
        _logger.LogDebug("Cache MISS for CoreId: {CoreId}, fetching from API", coreId);

        try
        {
            var data = await FetchFromApiAsync(coreId, ct);

            // Cache the result
            _cache.Set(cacheKey, data, _cacheOptions);

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to fetch client data for CoreId: {CoreId}, returning empty",
                coreId);

            return ClientData.Empty;
        }
    }
}
```

**DI registracija:**
```csharp
services.AddMemoryCache(); // Ako već nije registrovan
services.AddHttpClient<IClientApi, ClientApi>();
```

---

## 5. Implementacioni plan

### 5.1 Faza 1: Kritične optimizacije (PRE refaktorisanja)

**Trajanje:** 1 radni dan (8 sati)

#### Task 1.1: OpisToTipMapperV2 Caching ⏱️ 3-4 sata

**Koraci:**
1. ✅ Kreirati `OptimizedOpisToTipMapper` klasu (1 sat)
2. ✅ Kreirati `DocumentMappingCache` klasu (1 sat)
3. ✅ Dodati `GetAllMappingsAsync()` u `IDocumentMappingService` (30 min)
4. ✅ Implementirati `GetAllMappingsAsync()` u repository-ju (30 min)
5. ✅ Zameniti registraciju u DI (5 min)
6. ✅ Unit testovi (1 sat)

**Output fajlovi:**
- `Migration.Infrastructure/Implementation/Mappers/OptimizedOpisToTipMapper.cs`
- `Migration.Infrastructure/Implementation/Mappers/DocumentMappingCache.cs`
- `Migration.Infrastructure/Interfaces/IDocumentMappingService.cs` (update)
- `Migration.Infrastructure.Tests/Mappers/OptimizedOpisToTipMapperTests.cs`

#### Task 1.2: DocumentReader Pagination ⏱️ 2-3 sata

**Koraci:**
1. ✅ Kreirati `DocumentReaderResult` klasu (15 min)
2. ✅ Update `IDocumentReader` interface (5 min)
3. ✅ Implementirati pagination u `DocumentReader` (1 sat)
4. ✅ Update `DocumentDiscoveryService` za pagination loop (30 min)
5. ✅ Unit testovi (1 sat)

**Output fajlovi:**
- `Migration.Abstraction/Models/DocumentReaderResult.cs`
- `Migration.Abstraction/Interfaces/IDocumentReader.cs` (update)
- `Migration.Infrastructure/Implementation/Document/DocumentReader.cs` (update)
- `Migration.Infrastructure.Tests/Document/DocumentReaderTests.cs`

#### Task 1.3: Testing i validacija ⏱️ 2 sata

**Koraci:**
1. ✅ Integration test sa realnim podacima (1 sat)
2. ✅ Performance benchmark (30 min)
3. ✅ Memory profiling (30 min)

**Metrike za merenje:**
- SQL query count (očekivano: <100 umesto 4.5M)
- Execution time za 10K dokumenata (očekivano: <5 min)
- Memory usage (očekivano: <200 MB)

---

### 5.2 Faza 2: Srednji prioritet (TOKOM refaktorisanja)

**Trajanje:** 0.5 radna dana (4 sata)

#### Task 2.1: DocumentResolver Memory Leak Fix ⏱️ 2 sata

**Koraci:**
1. ✅ Implementirati lock striping (1 sat)
2. ✅ Update `DocumentResolver` (30 min)
3. ✅ Unit testovi (30 min)

#### Task 2.2: ClientApi Caching (opciono) ⏱️ 1 sat

**Koraci:**
1. ✅ Dodati IMemoryCache dependency (5 min)
2. ✅ Implementirati caching logic (30 min)
3. ✅ Unit testovi (25 min)

#### Task 2.3: Testing ⏱️ 1 sat

---

### 5.3 Redosled izvršavanja

```
Dan 1 (PRE refaktorisanja):
  08:00-12:00  Task 1.1: OpisToTipMapperV2 Caching
  12:00-13:00  Pauza
  13:00-16:00  Task 1.2: DocumentReader Pagination
  16:00-18:00  Task 1.3: Testing & Validation

Dan 2 (TOKOM refaktorisanja):
  09:00-11:00  Task 2.1: DocumentResolver Fix
  11:00-12:00  Task 2.2: ClientApi Caching
  12:00-13:00  Task 2.3: Testing

  13:00+       Nastavak sa refaktorisanjem servisa
```

---

## 6. Testing strategija

### 6.1 Unit Tests

**Za svaku komponentu:**

```csharp
// OptimizedOpisToTipMapperTests.cs
[Fact]
public async Task GetTipDokumenta_ReturnsCorrectType_ForKnownDocument()
{
    // Arrange
    var mapper = CreateMapper();

    // Act
    var result = await mapper.GetTipDokumentaAsync("KYC Questionnaire", CancellationToken.None);

    // Assert
    Assert.Equal("00100", result);
}

[Fact]
public async Task GetTipDokumenta_ReturnsUnknown_ForUnknownDocument()
{
    // Arrange
    var mapper = CreateMapper();

    // Act
    var result = await mapper.GetTipDokumentaAsync("Unknown Document", CancellationToken.None);

    // Assert
    Assert.Equal("UNKNOWN", result);
}

[Fact]
public async Task GetTipDokumenta_IsFast_WithManyLookups()
{
    // Arrange
    var mapper = CreateMapper();
    var sw = Stopwatch.StartNew();

    // Act - 10,000 lookups
    for (int i = 0; i < 10_000; i++)
    {
        await mapper.GetTipDokumentaAsync("KYC Questionnaire", CancellationToken.None);
    }

    sw.Stop();

    // Assert - should be <100ms for 10K lookups
    Assert.True(sw.ElapsedMilliseconds < 100,
        $"10K lookups took {sw.ElapsedMilliseconds}ms (expected <100ms)");
}
```

### 6.2 Integration Tests

**Test sa realnim podacima:**

```csharp
[Fact]
public async Task DocumentDiscoveryService_ProcessesLargeFolder_WithinMemoryLimit()
{
    // Arrange
    var service = CreateService();
    var maxMemoryMB = 500; // 500 MB limit
    var initialMemory = GC.GetTotalMemory(forceFullCollection: true);

    // Act
    await service.DiscoverAllAsync(CancellationToken.None);

    var finalMemory = GC.GetTotalMemory(forceFullCollection: false);
    var usedMemoryMB = (finalMemory - initialMemory) / (1024 * 1024);

    // Assert
    Assert.True(usedMemoryMB < maxMemoryMB,
        $"Used {usedMemoryMB} MB (limit: {maxMemoryMB} MB)");
}
```

### 6.3 Performance Benchmarks

**Koristiti BenchmarkDotNet:**

```csharp
[MemoryDiagnoser]
public class OpisToTipMapperBenchmarks
{
    private IOpisToTipMapper _oldMapper;
    private IOpisToTipMapper _newMapper;

    [GlobalSetup]
    public void Setup()
    {
        _oldMapper = CreateOldMapper(); // Bez cachinga
        _newMapper = CreateNewMapper(); // Sa cachingom
    }

    [Benchmark(Baseline = true)]
    public async Task OldMapper_10KLookups()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await _oldMapper.GetTipDokumentaAsync("KYC Questionnaire");
        }
    }

    [Benchmark]
    public async Task NewMapper_10KLookups()
    {
        for (int i = 0; i < 10_000; i++)
        {
            await _newMapper.GetTipDokumentaAsync("KYC Questionnaire");
        }
    }
}
```

**Očekivani rezultati:**
```
|              Method |     Mean |   Error |  Allocated |
|-------------------- |---------:|--------:|-----------:|
| OldMapper_10KLookups | 300.0 ms | 15.0 ms |   50.00 MB |
| NewMapper_10KLookups |   5.0 ms |  0.2 ms |    0.05 MB |
```

---

## 7. Metrike uspešnosti

### 7.1 Performance metrike

| Metrika | PRE optimizacije | POSLE optimizacije | Target |
|---------|------------------|-------------------|--------|
| **OpisToTipMapper SQL pozivi** | 4.5M poziva | <100 poziva | <1000 |
| **DocumentDiscovery execution time** | ~12 sati | ~30 minuta | <1 sat |
| **Peak memory usage** | Nepoznato | <500 MB | <1 GB |
| **SQL connection pool usage** | 80-100% | <30% | <50% |
| **Cache hit rate (DocumentResolver)** | N/A | >95% | >90% |

### 7.2 Code quality metrike

| Metrika | PRE | POSLE | Target |
|---------|-----|-------|--------|
| **Unit test coverage** | 0% | >70% | >60% |
| **Cyclomatic complexity** | High | Medium | <10 per method |
| **Code duplication** | Medium | Low | <5% |
| **Memory leaks** | 1 | 0 | 0 |

### 7.3 Stability metrike

| Metrika | Target |
|---------|--------|
| **OutOfMemory errors** | 0 |
| **Connection pool exhaustion** | 0 |
| **Unhandled exceptions** | 0 |
| **Successful completion rate** | 100% |

---

## 8. Rizici i mitigacije

### 8.1 Rizici

| # | Rizik | Verovatnoća | Impact | Mitigacija |
|---|-------|-------------|--------|------------|
| 1 | DocumentMappings tabela je prevelika za in-memory cache | Niska | Visok | Dodati limit + disk cache fallback |
| 2 | Breaking change u DocumentReader interfejsu | Niska | Srednji | Verzionisanje ili backward compatibility |
| 3 | Performance regression | Niska | Visok | Benchmark testovi PRE i POSLE |
| 4 | Memory leak nije potpuno fixovan | Srednja | Srednji | Memory profiling tokom testiranja |

### 8.2 Rollback plan

**Ako optimizacije ne rade:**

1. ✅ Sve promene su u NOVIM klasama (OptimizedOpisToTipMapper)
2. ✅ Stare klase ostaju netaknute
3. ✅ Rollback = promena DI registracije:
   ```csharp
   // Rollback na staru implementaciju
   services.AddScoped<IOpisToTipMapper, OpisToTipMapperV2>();
   ```

---

## 9. Zaključak i preporuke

### 9.1 Rezime

Identifikovano je **4 problema** u zavisnostima koje koriste tri glavna servisa:

1. ✅ **OpisToTipMapperV2 caching** - KRITIČNO (4.5M SQL poziva → 0)
2. ✅ **DocumentReader pagination** - VAŽNO (sprečava OutOfMemory)
3. ✅ **DocumentResolver memory leak** - SREDNJE (100 MB leak → 200 KB)
4. ✅ **ClientApi caching** - NIZAK prioritet (opciono)

### 9.2 Preporuke

1. **MORA se uraditi PRE refaktorisanja:**
   - OpisToTipMapperV2 caching
   - DocumentReader pagination

2. **Može se uraditi TOKOM refaktorisanja:**
   - DocumentResolver memory leak fix
   - ClientApi caching

3. **Testing je OBAVEZAN:**
   - Unit tests za sve nove klase
   - Integration test sa realnim podacima
   - Performance benchmark

### 9.3 Očekivani rezultat

```
POSLE implementacije svih optimizacija:

DocumentDiscoveryService performance:
  - 30× brže (12 sati → 25 minuta)
  - 99.9% manje SQL poziva (4.5M → <100)
  - Stabilan memory usage (<500 MB)
  - Bez connection pool exhaustion

MoveService performance:
  - 99.8% manje memorije za lock-ove
  - >95% cache hit rate

UKUPNO:
  - Migration runtime: ~5 sati (umesto 12+ sati)
  - Stability: Visoka (bez memory leaks, bez connection exhaustion)
  - Ready za refaktorisanje ✅
```

---

## 10. Appendix

### 10.1 Relevantni fajlovi

**Trenutno:**
```
/Migration.Infrastructure/Implementation/
  ├── OpisToTipMapperV2.cs (TREBA zameniti)
  ├── Document/DocumentReader.cs (TREBA update)
  ├── Document/DocumentResolver.cs (TREBA update)
  └── ClientApi.cs (TREBA update)
```

**Novi fajlovi:**
```
/Migration.Infrastructure/Implementation/
  ├── Mappers/
  │   ├── OptimizedOpisToTipMapper.cs (NOVI)
  │   └── DocumentMappingCache.cs (NOVI)
  └── ... (ostali update-ovani)
```

### 10.2 Reference dokumenti

- Originalna analiza: `ANALIZA_I_PREPORUKE.md`
- Dependency analiza: Rezultat Task agenta (gornji output)
- NULL Id error analiza: `SQL/NULL_ID_ERROR_ANALYSIS.md`

### 10.3 Contact

Za pitanja oko implementacije:
- Lead developer: [Ime]
- Code reviewer: [Ime]

---

**Status:** ✅ Ready for implementation
**Sledeći korak:** Kreirati GitHub Issues za svaki Task iz Faze 1
