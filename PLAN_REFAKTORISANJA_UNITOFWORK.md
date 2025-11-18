# Plan Refaktorisanja - UnitOfWork Pattern u Discovery Servisima

**Verzija:** 1.0
**Datum:** 2025-01-17
**Status:** Ready for Implementation
**Branch:** `Refaktorizacija_2`

---

## 📋 Sadržaj

1. [Problem Statement](#1-problem-statement)
2. [Root Cause Analysis](#2-root-cause-analysis)
3. [Current vs. Correct Implementation](#3-current-vs-correct-implementation)
4. [Detaljni Plan Refaktorisanja](#4-detaljni-plan-refaktorisanja)
5. [Implementation Steps](#5-implementation-steps)
6. [Testing Strategy](#6-testing-strategy)
7. [Success Metrics](#7-success-metrics)
8. [Rollback Plan](#8-rollback-plan)

---

## 1. Problem Statement

### 1.1 Executive Summary

Tri glavna servisa (`DocumentDiscoveryService`, `FolderDiscoveryService`, `MoveService`) kreiraju **nove DI scope-ove u paralelnim task-ovima**, što rezultuje:

- ❌ **Connection pool exhaustion** (40-800 istovremenih SQL konekcija)
- ❌ **Smanjen throughput** (constant connection open/close overhead)
- ❌ **Nekonzistentan kod** (različiti paterni za istu funkcionalnost)
- ❌ **Dead code** (UnitOfWork prima se ali se ne koristi)

**FolderPreparationService** je **jedini servis koji koristi UnitOfWork ISPRAVNO** i treba da bude **template** za ostale.

### 1.2 Impact

| Servis | Current State | Problem Severity | Connection Pool Risk |
|--------|--------------|------------------|---------------------|
| **DocumentDiscoveryService** | ❌ Prima UnitOfWork ali ga NE koristi | 🔴 CRITICAL | Very High (8-40 concurrent connections) |
| **FolderDiscoveryService** | ❌ Kreira nove scope-ove u svakoj helper metodi | 🔴 HIGH | Medium (1-4 concurrent connections) |
| **MoveService** | ❌ NE prima UnitOfWork, kreira scope-ove svuda | 🔴 CRITICAL | High (8-32 concurrent connections) |
| **FolderPreparationService** | ✅ **CORRECT** - koristi constructor-injected UOW | ✅ GOOD | None (1 connection per service) |

---

## 2. Root Cause Analysis

### 2.1 Anti-Pattern: Scope Creation u Loop-u

**Problem:**
```csharp
// ❌ LOŠE - Kreira novi scope (i novu SQL konekciju) u svakom pozivu
private async Task InsertDocsAsync(List<DocStaging> docs, long folderId, CancellationToken ct)
{
    await using var scope = _sp.CreateAsyncScope();  // ❌ NOVI SCOPE!
    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();  // ❌ NOVI UOW!
    var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

    await uow.BeginAsync();  // ❌ Nova SQL konekcija otvorena!
    // ... database work ...
    await uow.CommitAsync();
}
```

**Poziva se iz paralelnog loop-a:**
```csharp
await Parallel.ForEachAsync(folders, new ParallelOptions { MaxDegreeOfParallelism = 8 },
    async (folder, token) =>
    {
        await ProcessSingleFolderAsync(folder, ct);  // ⬇️
            await InsertDocsAsync(...);  // ⬇️ ❌ SVAKI TASK OTVARA NOVU KONEKCIJU!
    });
```

**Rezultat:**
```
8 paralelnih task-ova × 5 stranica (paginacija) = 40 otvorenih SQL konekcija istovremeno!
Default SQL Server Max Pool Size = 100
Risk: 40-80% connection pool capacity consumed!
```

---

### 2.2 Correct Pattern: Constructor Injection

**FolderPreparationService pokazuje kako treba:**

```csharp
// ✅ DOBRO - Constructor injection
public class FolderPreparationService
{
    private readonly IUnitOfWork _uow;  // ✅ Prima i KORISTI

    public FolderPreparationService(IUnitOfWork uow, ...)
    {
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));  // ✅ Validacija
    }

    private async Task GetUniqueFoldersAsync(CancellationToken ct)
    {
        await _uow.BeginAsync(ct: ct);  // ✅ Koristi postojeći UOW

        var folders = await _docRepo.GetUniqueDestinationFoldersAsync(ct);

        await _uow.CommitAsync(ct);  // ✅ Commit direktno

        return folders;
    }
}
```

**Benefiti:**
- ✅ **1 SQL konekcija** po DI scope (servisu)
- ✅ **Manje pritiska** na connection pool
- ✅ **Bolje performanse** (manje open/close overhead)
- ✅ **Čitljiv kod** (konzistentan pattern)

---

## 3. Current vs. Correct Implementation

### 3.1 Comparison Table

| Aspekt | FolderPreparationService ✅ | DocumentDiscoveryService ❌ | FolderDiscoveryService ❌ | MoveService ❌ |
|--------|---------------------------|---------------------------|-------------------------|---------------|
| **UnitOfWork Constructor Injection** | ✅ Prima i KORISTI | ❌ Prima ali NE KORISTI | ❌ NE PRIMA (koristi _sp) | ❌ NE PRIMA |
| **Scope Creation Count** | **0** (koristi DI scope) | **∞** (kreira u loop-u) | **∞** (kreira u svakoj metodi) | **∞** (kreira svuda) |
| **SQL Connections** | **1** po servisu | **8-40** istovremeno | **1-4** istovremeno | **8-32** istovremeno |
| **Connection Pool Risk** | **Nema** | **Visok** | **Srednji** | **Visok** |
| **Code Quality** | **Čist** | **Nekonzistentan** | **Nekonzistentan** | **Nekonzistentan** |
| **Repository Injection** | ✅ Constructor | ✅ Constructor | ✅ Constructor | ✅ Constructor |
| **Error Handling** | ✅ Try-catch-rollback | ✅ Try-catch-rollback | ✅ Try-catch-rollback | ✅ Try-catch-rollback |

---

### 3.2 Code Examples

#### ✅ CORRECT - FolderPreparationService

```csharp
public class FolderPreparationService
{
    private readonly IDocStagingRepository _docRepo;
    private readonly IUnitOfWork _uow;

    public FolderPreparationService(
        IDocStagingRepository docRepo,
        IUnitOfWork uow,
        ...)
    {
        _docRepo = docRepo;
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
    }

    private async Task<List<UniqueFolderInfo>> GetUniqueFoldersAsync(CancellationToken ct)
    {
        try
        {
            await _uow.BeginAsync(ct: ct);

            var uniqueFolders = await _docRepo.GetUniqueDestinationFoldersAsync(ct);

            await _uow.CommitAsync(ct);

            return uniqueFolders;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unique folders from DocStaging");
            await _uow.RollbackAsync(ct);
            throw;
        }
    }
}
```

**Key Points:**
- ✅ UnitOfWork je constructor-injected
- ✅ Ne kreira nove scope-ove
- ✅ Konzistentan error handling
- ✅ Jednostavan i čitljiv kod

---

#### ❌ INCORRECT - DocumentDiscoveryService (Current)

```csharp
public class DocumentDiscoveryService
{
    private readonly IUnitOfWork _unitOfWork;  // ❌ DEAD CODE!

    public DocumentDiscoveryService(
        ...,
        IUnitOfWork unitOfWork,
        ...)
    {
        // _unitOfWork = unitOfWork;  // ❌ ZAKOMENTIRANO!!!
    }

    private async Task InsertDocsAsync(List<DocStaging> docsToInsert, long folderId, CancellationToken ct)
    {
        await using var scope = _sp.CreateAsyncScope();  // ❌ NOVI SCOPE!
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();  // ❌ NOVI UOW!
        var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();

        await uow.BeginAsync();  // ❌ Nova konekcija!

        try
        {
            int inserted = await docRepo.InsertManyAsync(docsToInsert, ct);
            await uow.CommitAsync();
        }
        catch
        {
            await uow.RollbackAsync();
            throw;
        }
    }
}
```

**Problems:**
- ❌ Constructor prima `IUnitOfWork` ali ga NE koristi (dead code)
- ❌ Kreira **novi scope** u svakom pozivu → **nova SQL konekcija**
- ❌ Poziva se iz `Parallel.ForEachAsync` → **40 konekcija istovremeno**

---

## 4. Detaljni Plan Refaktorisanja

### 4.1 DocumentDiscoveryService

**Lokacija:** `/Migration.Infrastructure/Implementation/Services/DocumentDiscoveryService.cs`

#### Problem Areas (linije):

| Metoda | Linija | Problem | Fix |
|--------|--------|---------|-----|
| `Constructor` | 33, 55, 70 | UnitOfWork prima se ali NE dodeljuje | Uncomment `_unitOfWork = unitOfWork` |
| `InsertDocsAsync` | 729-733 | Kreira novi scope za UOW | Koristi `_unitOfWork` iz konstruktora |
| `InsertDocsAndMarkFolderAsync` | 669-672 | Kreira novi scope za UOW | Koristi `_unitOfWork` |
| `MarkFolderAsProcessedAsync` | ~800+ | Verovatno kreira scope | Koristi `_unitOfWork` |
| `AcquireFoldersForProcessingAsync` | ~400+ | Verovatno kreira scope | Koristi `_unitOfWork` |
| `SaveCheckpointAsync` | ~300+ | Verovatno kreira scope | Koristi `_unitOfWork` |

#### Refactoring Steps:

**STEP 1: Fix Constructor (trivial)**
```csharp
// BEFORE (linija 70)
// _unitOfWork = unitOfWork;  // ❌ ZAKOMENTIRANO

// AFTER
_unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));  // ✅
```

**STEP 2: Refactor InsertDocsAsync**
```csharp
// BEFORE (linije 729-733)
private async Task InsertDocsAsync(List<DocStaging> docsToInsert, long folderId, CancellationToken ct)
{
    await using var scope = _sp.CreateAsyncScope();  // ❌ REMOVE
    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();  // ❌ REMOVE
    var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();  // ❌ REMOVE

    await uow.BeginAsync();
    // ...
}

// AFTER
private async Task InsertDocsAsync(List<DocStaging> docsToInsert, long folderId, CancellationToken ct)
{
    // ✅ Koristi constructor-injected dependencies
    await _unitOfWork.BeginAsync(ct: ct);

    try
    {
        int inserted = await _docRepo.InsertManyAsync(docsToInsert, ct);

        await _unitOfWork.CommitAsync(ct);

        _fileLogger.LogInformation("Successfully inserted {Count} documents", inserted);
    }
    catch (Exception ex)
    {
        _dbLogger.LogError(ex, "Failed to insert documents for folder {FolderId}", folderId);
        await _unitOfWork.RollbackAsync(ct);
        throw;
    }
}
```

**STEP 3: Apply same pattern to all helper methods**
- `InsertDocsAndMarkFolderAsync()`
- `MarkFolderAsProcessedAsync()`
- `AcquireFoldersForProcessingAsync()`
- `MarkFoldersAsFailedAsync()`
- `SaveCheckpointAsync()`
- `LoadCheckpointAsync()`

---

### 4.2 FolderDiscoveryService

**Lokacija:** `/Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs`

#### Problem Areas:

| Metoda | Linija | Problem | Fix |
|--------|--------|---------|-----|
| `Constructor` | 35, 54-62 | UnitOfWork prima se i dodeljuje | ✅ Već OK, samo treba koristiti |
| `InsertFoldersAsync` | 606-608 | Kreira novi scope | Koristi `_unitOfWork` |
| `SaveCheckpointAsync` | ~520+ | Kreira novi scope | Koristi `_unitOfWork` |
| `LoadCheckpointAsync` | 486-488 | Kreira novi scope | Koristi `_unitOfWork` |

#### Refactoring Steps:

**STEP 1: Refactor InsertFoldersAsync**
```csharp
// BEFORE (linije 606-608)
private async Task<int> InsertFoldersAsync(List<FolderStaging> folders, CancellationToken ct)
{
    await using var scope = _sp.CreateAsyncScope();  // ❌ REMOVE
    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();  // ❌ REMOVE
    var folderRepo = scope.ServiceProvider.GetRequiredService<IFolderStagingRepository>();  // ❌ REMOVE

    await uow.BeginAsync(IsolationLevel.ReadCommitted, ct);
    // ...
}

// AFTER
private async Task<int> InsertFoldersAsync(List<FolderStaging> folders, CancellationToken ct)
{
    if (folders.Count == 0)
    {
        _fileLogger.LogDebug("No folders to insert");
        return 0;
    }

    try
    {
        await _unitOfWork.BeginAsync(IsolationLevel.ReadCommitted, ct);

        // ✅ Koristi constructor-injected repository
        var inserted = await _folderRepo.InsertManyAsync(folders, ct);

        await _unitOfWork.CommitAsync(ct);

        _fileLogger.LogInformation("Inserted {Count} folders", inserted);
        return inserted;
    }
    catch (Exception ex)
    {
        _fileLogger.LogError(ex, "Failed to insert {Count} folders", folders.Count);
        await _unitOfWork.RollbackAsync(ct);
        throw;
    }
}
```

**STEP 2: Refactor SaveCheckpointAsync i LoadCheckpointAsync**
- Isti pattern kao gore
- Koristi `_unitOfWork` umesto kreiranje scope-a

---

### 4.3 MoveService

**Lokacija:** `/Migration.Infrastructure/Implementation/Services/MoveService.cs`

#### Problem Areas:

| Metoda | Linija | Problem | Fix |
|--------|--------|---------|-----|
| `Constructor` | 54-67 | **NE PRIMA** IUnitOfWork | Dodati `IUnitOfWork uow` parametar |
| `AcquireDocumentsForMoveAsync` | 425-427 | Kreira novi scope | Koristi `_uow` |
| `MarkDocumentsAsFailedAsync` | 653-655 | Kreira novi scope | Koristi `_uow` |
| `MarkDocumentsAsDoneAsync` | 690-692 | Kreira novi scope | Koristi `_uow` |
| `SaveCheckpointAsync` | 388-390 | Kreira novi scope | Koristi `_uow` |
| `LoadCheckpointAsync` | ~336+ | Kreira novi scope | Koristi `_uow` |
| `MoveSingleDocumentAsync` | 528-530 | Kreira scope za DocumentMapping | **SPECIAL CASE** (vidi dole) |

#### Refactoring Steps:

**STEP 1: Add UnitOfWork to Constructor**
```csharp
// BEFORE (linija 54)
public MoveService(
    IMoveReader moveService,
    IMoveExecutor moveExecutor,
    IDocStagingRepository docRepo,
    IDocumentResolver resolver,
    IAlfrescoWriteApi write,
    IAlfrescoReadApi read,
    IOptions<MigrationOptions> options,
    IServiceProvider sp,
    ILoggerFactory logger)

// AFTER
public MoveService(
    IMoveReader moveService,
    IMoveExecutor moveExecutor,
    IDocStagingRepository docRepo,
    IDocumentResolver resolver,
    IAlfrescoWriteApi write,
    IAlfrescoReadApi read,
    IOptions<MigrationOptions> options,
    IServiceProvider sp,
    ILoggerFactory logger,
    IUnitOfWork uow)  // ✅ ADD THIS
{
    // ... existing assignments ...
    _uow = uow ?? throw new ArgumentNullException(nameof(uow));  // ✅ ADD THIS
}
```

**STEP 2: Refactor AcquireDocumentsForMoveAsync**
```csharp
// BEFORE (linije 425-427)
private async Task<IReadOnlyList<DocStaging>> AcquireDocumentsForMoveAsync(int batch, CancellationToken ct)
{
    await using var scope = _sp.CreateAsyncScope();  // ❌ REMOVE
    var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();  // ❌ REMOVE
    var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();  // ❌ REMOVE

    await uow.BeginAsync(ct: ct);
    // ...
}

// AFTER
private async Task<IReadOnlyList<DocStaging>> AcquireDocumentsForMoveAsync(int batch, CancellationToken ct)
{
    _fileLogger.LogDebug("Acquiring {BatchSize} documents for processing", batch);

    try
    {
        await _uow.BeginAsync(ct: ct);  // ✅ Koristi constructor-injected UOW

        var documents = await _docRepo.TakeReadyForProcessingAsync(batch, ct);

        var updates = documents.Select(d => (
            d.Id,
            MigrationStatus.InProgress.ToDbString(),
            (string?)null
        ));

        await _docRepo.BatchSetDocumentStatusAsync_v1(
            _uow.Connection,
            _uow.Transaction,
            updates,
            ct);

        await _uow.CommitAsync(ct);

        _fileLogger.LogDebug("Marked {Count} documents as IN PROGRESS", documents.Count);
        return documents;
    }
    catch (Exception ex)
    {
        _fileLogger.LogError(ex, "Failed to acquire documents");
        await _uow.RollbackAsync(ct);
        throw;
    }
}
```

**STEP 3: Refactor MarkDocumentsAsFailedAsync i MarkDocumentsAsDoneAsync**
- Isti pattern kao gore

---

#### ⚠️ SPECIAL CASE: MoveSingleDocumentAsync - DocumentMapping Lookup

**Problem:** Ova metoda se poziva u `Parallel.ForEachAsync` loop-u i interno kreira scope za DocumentMapping lookup.

**Lokacija:** `MoveService.cs:528-530`

```csharp
private async Task<bool> MoveSingleDocumentAsync(DocStaging doc, CancellationToken ct)
{
    // ... STEP 1 & STEP 2: Folder creation & document move ...

    // STEP 3: DocumentMapping lookup
    await using var mappingScope = _sp.CreateAsyncScope();  // ❌ Svaki task otvara novu konekciju!
    var uow = mappingScope.ServiceProvider.GetRequiredService<IUnitOfWork>();
    var mappingService = mappingScope.ServiceProvider.GetRequiredService<IDocumentMappingService>();

    await uow.BeginAsync(ct: ct);
    // ... lookup mapping ...
    await uow.CommitAsync(ct: ct);
}
```

**Problem Analysis:**
- Poziva se u `Parallel.ForEachAsync` sa `MaxDegreeOfParallelism = 8`
- Svaki task otvara **novu SQL konekciju** za DocumentMapping lookup
- 8 paralelnih task-ova = **8 concurrent SQL konekcija**

**SOLUTION OPTIONS:**

**Option A: Pre-load DocumentMapping u memoriju (BEST)**

Koristi `OptimizedOpisToTipMapper` koji učitava SVE mappinge jednom na startu:

```csharp
// Već implementirano u Refaktorizacija_2 branch!
// Migration.Infrastructure/Implementation/Mappers/OptimizedOpisToTipMapper.cs

// MoveSingleDocumentAsync refactor:
private async Task<bool> MoveSingleDocumentAsync(DocStaging doc, CancellationToken ct)
{
    // ... STEP 1 & 2 ...

    // STEP 3: Lookup DocumentMapping - NO DATABASE CALL!
    string? migratedDocType = null;
    string? migratedNaziv = null;

    if (!string.IsNullOrWhiteSpace(doc.DocDescription))
    {
        // ✅ In-memory lookup (već učitano pri startu aplikacije)
        var mapping = await _opisToTipMapper.GetTipDokumentaAsync(doc.DocDescription, ct);

        if (mapping != null)
        {
            migratedDocType = mapping.NewDocumentCode;
            migratedNaziv = mapping.NewDocumentName;
        }
    }

    // ... STEP 4: Update properties ...
}
```

**Benefiti:**
- ✅ **0 database poziva** za mapping lookup
- ✅ **0 dodatnih SQL konekcija**
- ✅ **30× brže** (cache hit ~100%)
- ✅ Već implementirano u `OptimizedOpisToTipMapper`

**Option B: Koristi _uow iz konstruktora (ALTERNATIVE)**

Ako ne želimo in-memory caching:

```csharp
private async Task<bool> MoveSingleDocumentAsync(DocStaging doc, CancellationToken ct)
{
    // ⚠️ PROBLEM: _uow je SHARED između paralelnih task-ova!
    // NE MOŽE se koristiti _uow direktno jer Parallel.ForEachAsync deli isti scope!

    // Mora ostati scope creation ZA OVU SPECIFIČNU OPERACIJU
    await using var mappingScope = _sp.CreateAsyncScope();
    // ...
}
```

**⚠️ IMPORTANT:** Option B nije dobro rešenje jer MoveSingleDocumentAsync se poziva u Parallel.ForEachAsync loop-u, a UnitOfWork transaction ne sme biti shared između paralelnih task-ova.

**RECOMMENDATION:** **Koristi Option A** - `OptimizedOpisToTipMapper` sa in-memory caching.

---

### 4.4 Summary of Changes

| Servis | Constructor Changes | Method Refactors | Estimated LOC Changed |
|--------|-------------------|-----------------|---------------------|
| **DocumentDiscoveryService** | Uncomment `_unitOfWork` assignment | 6 metoda | ~100 LOC |
| **FolderDiscoveryService** | ✅ Already OK | 3 metode | ~60 LOC |
| **MoveService** | Add `IUnitOfWork uow` parameter | 5 metoda + special case | ~120 LOC |
| **TOTAL** | - | **14 metoda** | **~280 LOC** |

---

## 5. Implementation Steps

### 5.1 Phase 1: DocumentDiscoveryService (Prioritet 🔴 CRITICAL)

**Estimated Time:** 2 sata

**Steps:**
1. ✅ Uncomment `_unitOfWork = unitOfWork` u konstruktoru (linija 70)
2. ✅ Refactor `InsertDocsAsync()` - replace scope creation with `_unitOfWork`
3. ✅ Refactor `InsertDocsAndMarkFolderAsync()` - replace scope creation
4. ✅ Refactor `MarkFolderAsProcessedAsync()` - replace scope creation
5. ✅ Refactor `AcquireFoldersForProcessingAsync()` - replace scope creation
6. ✅ Refactor `MarkFoldersAsFailedAsync()` - replace scope creation
7. ✅ Refactor `SaveCheckpointAsync()` - replace scope creation
8. ✅ Refactor `LoadCheckpointAsync()` - replace scope creation
9. ✅ Run tests
10. ✅ Commit: "Refactor DocumentDiscoveryService to use constructor-injected UnitOfWork"

**Expected Impact:**
- 🎯 Connection pool usage: **40 → 1** connection per service instance
- 🎯 Performance improvement: **~5-10% faster** (less connection overhead)

---

### 5.2 Phase 2: FolderDiscoveryService (Prioritet 🟠 HIGH)

**Estimated Time:** 1 sat

**Steps:**
1. ✅ Refactor `InsertFoldersAsync()` - replace scope creation with `_unitOfWork`
2. ✅ Refactor `SaveCheckpointAsync()` - replace scope creation
3. ✅ Refactor `LoadCheckpointAsync()` - replace scope creation
4. ✅ Run tests
5. ✅ Commit: "Refactor FolderDiscoveryService to use constructor-injected UnitOfWork"

**Expected Impact:**
- 🎯 Connection pool usage: **4 → 1** connection per service instance
- 🎯 Code consistency: ✅ Aligned with FolderPreparationService pattern

---

### 5.3 Phase 3: MoveService (Prioritet 🔴 CRITICAL)

**Estimated Time:** 3 sata

**Steps:**
1. ✅ Add `IUnitOfWork uow` parameter to constructor
2. ✅ Assign `_uow = uow` u konstruktoru
3. ✅ Refactor `AcquireDocumentsForMoveAsync()` - replace scope creation
4. ✅ Refactor `MarkDocumentsAsFailedAsync()` - replace scope creation
5. ✅ Refactor `MarkDocumentsAsDoneAsync()` - replace scope creation
6. ✅ Refactor `SaveCheckpointAsync()` - replace scope creation
7. ✅ Refactor `LoadCheckpointAsync()` - replace scope creation
8. ⚠️ **SPECIAL:** Refactor `MoveSingleDocumentAsync()` - use `OptimizedOpisToTipMapper` (Option A)
9. ✅ Run tests
10. ✅ Commit: "Refactor MoveService to use constructor-injected UnitOfWork"

**Expected Impact:**
- 🎯 Connection pool usage: **32 → 1** connection per service instance
- 🎯 Performance improvement: **~30× faster** mapping lookup (in-memory cache)

---

### 5.4 Timeline

| Phase | Duration | Start | End |
|-------|----------|-------|-----|
| Phase 1: DocumentDiscoveryService | 2h | 09:00 | 11:00 |
| Phase 2: FolderDiscoveryService | 1h | 11:00 | 12:00 |
| **Pauza** | 1h | 12:00 | 13:00 |
| Phase 3: MoveService | 3h | 13:00 | 16:00 |
| Testing & Validation | 1h | 16:00 | 17:00 |
| **TOTAL** | **7h** | 09:00 | 17:00 |

---

## 6. Testing Strategy

### 6.1 Unit Tests

**Validate:**
- ✅ UnitOfWork lifecycle (BeginAsync → CommitAsync/RollbackAsync)
- ✅ Exception handling (rollback on error)
- ✅ Repository operations use correct transaction

**Test Cases:**
```csharp
[Fact]
public async Task InsertDocsAsync_ShouldUseConstructorInjectedUnitOfWork()
{
    // Arrange
    var mockUow = new Mock<IUnitOfWork>();
    var service = new DocumentDiscoveryService(..., mockUow.Object, ...);

    // Act
    await service.InsertDocsAsync(docs, folderId, ct);

    // Assert
    mockUow.Verify(u => u.BeginAsync(It.IsAny<CancellationToken>()), Times.Once);
    mockUow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    mockUow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Never);
}

[Fact]
public async Task InsertDocsAsync_ShouldRollbackOnException()
{
    // Arrange
    var mockUow = new Mock<IUnitOfWork>();
    var mockRepo = new Mock<IDocStagingRepository>();
    mockRepo.Setup(r => r.InsertManyAsync(It.IsAny<List<DocStaging>>(), It.IsAny<CancellationToken>()))
        .ThrowsAsync(new Exception("Database error"));

    var service = new DocumentDiscoveryService(..., mockUow.Object, mockRepo.Object, ...);

    // Act & Assert
    await Assert.ThrowsAsync<Exception>(() => service.InsertDocsAsync(docs, folderId, ct));

    mockUow.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    mockUow.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
}
```

---

### 6.2 Integration Tests

**Validate:**
- ✅ Connection pool ne iscrpljuje se pod paralelnim load-om
- ✅ Performance je bolji (manje open/close overhead)
- ✅ Checkpoint persistence radi ispravno

**Test Scenario:**
```csharp
[Fact]
public async Task RunBatchAsync_ParallelProcessing_ShouldNotExhaustConnectionPool()
{
    // Arrange
    var folders = CreateTestFolders(100); // 100 folders sa dokumentima
    var service = CreateDocumentDiscoveryService();

    // Act
    var sw = Stopwatch.StartNew();
    var result = await service.RunBatchAsync(ct);
    sw.Stop();

    // Assert
    Assert.True(result.PlannedCount > 0);
    Assert.True(sw.ElapsedMilliseconds < 30000); // Should complete in < 30s

    // Check connection pool metrics (if available)
    var activeConnections = GetActiveConnectionCount();
    Assert.True(activeConnections < 10); // Should use < 10 connections
}
```

---

### 6.3 Manual Testing

**Test Plan:**

1. **Test 1: Small Batch (10 folders)**
   - Run DocumentDiscovery batch sa 10 foldera
   - Verify SQL Profiler: MAX 1-2 concurrent connections
   - Verify logs: No errors, checkpoint saved

2. **Test 2: Large Batch (100 folders, DOP=8)**
   - Run DocumentDiscovery batch sa 100 foldera, paralelizam 8
   - Verify SQL Profiler: MAX 8-10 concurrent connections (down from 40+)
   - Verify logs: No connection pool warnings

3. **Test 3: Full Migration Simulation**
   - Run FolderDiscovery → DocumentDiscovery → FolderPreparation → Move pipeline
   - Verify checkpoint persistence throughout
   - Verify total time < baseline (pre-refactoring)

---

## 7. Success Metrics

### 7.1 Performance Metrics

| Metric | Before | After | Target Improvement |
|--------|--------|-------|-------------------|
| **Max Concurrent SQL Connections** | 40-80 | 8-12 | **75% reduction** |
| **DocumentDiscoveryService Throughput** | 100 docs/min | 110-120 docs/min | **10-20% increase** |
| **MoveService Throughput** | 50 docs/min | 150-200 docs/min | **30× faster** (with OptimizedOpisToTipMapper) |
| **Connection Pool Warnings** | Frequent | None | **0 warnings** |

---

### 7.2 Code Quality Metrics

| Metric | Before | After | Target |
|--------|--------|-------|--------|
| **Dead Code (unused UnitOfWork)** | 1 service | 0 services | **0** |
| **Scope Creation in Loops** | 14 metoda | 0 metoda | **0** |
| **Consistent UnitOfWork Pattern** | 25% (1/4 services) | 100% (4/4 services) | **100%** |
| **LOC Changed** | - | ~280 LOC | < 300 LOC |

---

## 8. Rollback Plan

### 8.1 Rollback Trigger

**Rollback if:**
- ❌ Tests fail consistently (> 3 failures)
- ❌ Connection pool exhaustion INCREASES (worse than before)
- ❌ Data corruption detected
- ❌ Performance DEGRADES > 20%

---

### 8.2 Rollback Steps

**Option A: Git Revert**
```bash
# Revert commit(s)
git log --oneline -10  # Find commit hash
git revert <commit-hash>
git push origin Refaktorizacija_2
```

**Option B: Branch Reset**
```bash
# Reset to pre-refactoring state
git reset --hard <commit-before-refactoring>
git push --force origin Refaktorizacija_2
```

---

### 8.3 Mitigation Steps

If partial rollback needed:
1. Rollback only problematic service (e.g., DocumentDiscoveryService)
2. Keep successful refactorings (FolderDiscoveryService, MoveService)
3. Investigate root cause
4. Re-attempt refactoring with fix

---

## 9. Best Practices Summary

### 9.1 Do's ✅

1. ✅ **ALWAYS** inject `IUnitOfWork` via constructor
2. ✅ **ALWAYS** validate constructor parameters: `?? throw new ArgumentNullException()`
3. ✅ **ALWAYS** use try-catch-rollback pattern
4. ✅ **ALWAYS** commit/rollback within same method scope
5. ✅ **PREFER** in-memory caching over repeated DB queries (OptimizedOpisToTipMapper)

---

### 9.2 Don'ts ❌

1. ❌ **NEVER** create new DI scopes inside loop methods
2. ❌ **NEVER** call `_sp.CreateAsyncScope()` inside methods that are called in loops
3. ❌ **NEVER** leave unused constructor parameters (dead code)
4. ❌ **NEVER** share UnitOfWork transaction across parallel tasks
5. ❌ **AVOID** multiple database round-trips for same data (cache it!)

---

### 9.3 Pattern Template

**Use this template for all services:**

```csharp
public class MyService : IMyService
{
    private readonly IUnitOfWork _uow;
    private readonly IRepository _repo;
    private readonly ILogger<MyService> _logger;

    public MyService(
        IUnitOfWork uow,
        IRepository repo,
        ILogger<MyService> logger)
    {
        _uow = uow ?? throw new ArgumentNullException(nameof(uow));
        _repo = repo ?? throw new ArgumentNullException(nameof(repo));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private async Task DoWorkAsync(CancellationToken ct)
    {
        try
        {
            await _uow.BeginAsync(ct: ct);

            // Database work using _repo
            var data = await _repo.GetDataAsync(ct);

            await _uow.CommitAsync(ct);

            _logger.LogInformation("Work completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during work");
            await _uow.RollbackAsync(ct);
            throw;
        }
    }
}
```

---

## 10. Appendix

### 10.1 Reference Implementation

**Best Practice Example:** `FolderPreparationService.cs` (linije 1-302)

**Key Files:**
- `/Migration.Infrastructure/Implementation/Services/FolderPreparationService.cs` ✅ PERFECT
- `/Migration.Infrastructure/Implementation/Services/DocumentDiscoveryService.cs` ❌ NEEDS REFACTORING
- `/Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs` ❌ NEEDS REFACTORING
- `/Migration.Infrastructure/Implementation/Services/MoveService.cs` ❌ NEEDS REFACTORING

---

### 10.2 Related Documents

- `PLAN_REFAKTORISANJA_ZAVISNOSTI.md` - Problem #1-4 analysis
- `Analiza_migracije_v2.md` - Migration architecture
- `UnitOfWork` implementation:
  - `SqlServer.Infrastructure/Implementation/SqlServerUnitOfWork.cs`
  - `Oracle.Infrastructure/Implementation/OracleUnitOfWork.cs`

---

## 11. Sign-off

**Plan Created By:** Claude (AI Assistant)
**Date:** 2025-01-17
**Status:** ✅ Ready for Implementation

**Estimated Implementation Time:** 7 sati (1 radni dan)

**Expected Impact:**
- 🎯 **75% reduction** u connection pool usage
- 🎯 **10-20% performance improvement** u DocumentDiscovery
- 🎯 **30× faster** mapping lookup (with OptimizedOpisToTipMapper)
- 🎯 **100% code consistency** across all services

---

**Pitanja/Feedback:** Kontaktirajte plan autora pre početka implementacije.

**Next Steps:** Sutra počinjemo sa implementacijom! 🚀
