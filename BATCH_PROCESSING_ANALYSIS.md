# Alfresco Batch Document Processing Analysis

## Executive Summary

The codebase implements a **three-phase, asynchronous batch processing architecture** for migrating documents between Alfresco systems. Documents are processed in **configurable batches with controlled parallelization** using `Parallel.ForEachAsync`. The system includes sophisticated caching, lock striping, and checkpoint recovery mechanisms.

---

## 1. Document Processing Model: Sequential vs Parallel

### Overall Architecture: **HYBRID (Batch Sequential + Intra-Batch Parallel)**

```
┌─────────────────────────────────────────────────────────────────┐
│ SERVICE LOOP (Sequential)                                       │
│ ┌─────────────────────────────────────────────────────────────┐ │
│ │ Batch 1: Acquire documents/folders → Parallel process     │ │
│ │ [1000 items + DOP=5 = parallel processing]                 │ │
│ ├─────────────────────────────────────────────────────────────┤ │
│ │ Delay between batches                                       │ │
│ ├─────────────────────────────────────────────────────────────┤ │
│ │ Batch 2: Acquire documents/folders → Parallel process     │ │
│ │ [1000 items + DOP=5 = parallel processing]                 │ │
│ └─────────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────┘
```

### Key Parallelization Parameters:

| Parameter | Default | Per-Service | Purpose |
|-----------|---------|-------------|---------|
| `MaxDegreeOfParallelism` | 5 | ✓ Configurable per service | Number of parallel workers |
| `BatchSize` | 1000 | ✓ Configurable per service | Items per batch |
| `DelayBetweenBatchesInMs` | 0 | ✓ Configurable per service | Delay between batch iterations |
| `IdleDelayInMs` | 100 | Global | Delay when no items found |
| `StuckItemsTimeoutMinutes` | 10 | Global | Reset timeout for IN PROGRESS items |

**File Location:** `/home/user/Alfresco-app/Alfresco.Contracts/Options/MigrationOptions.cs`

### Execution Pattern:

```csharp
// From DocumentDiscoveryService.RunBatchAsync (Line 93-116)
await Parallel.ForEachAsync(folders, new ParallelOptions
{
    MaxDegreeOfParallelism = dop,      // 5 by default
    CancellationToken = ct
},
async (folder, token) =>
{
    // PARALLEL: Each folder processed independently
    await ProcessSingleFolderAsync(folder, ct);
});
```

**Parallelization Type:** `Parallel.ForEachAsync` - Microsoft's modern async-friendly parallelization primitive

---

## 2. Three-Phase Processing Pipeline

### Phase 1: FOLDER DISCOVERY (FolderDiscoveryService)

**Purpose:** Discover and stage folder metadata from source Alfresco

**Execution Model:**
- **Sequential:** Processes DOSSIER subfolders one at a time
- **Data Flow:** Alfresco → Database (FolderStaging table)
- **No parallelization** within folder discovery

**Key Operations:**
1. Find DOSSIER subfolders (DOSSIER-PI, DOSSIER-LE, DOSSIER-ACC, DOSSIER-D)
2. Query folders using CMIS or AFTS language
3. Enrich with ClientAPI data if properties missing
4. Insert into FolderStaging table

**Query Strategy:**
- **Default (AFTS):** Cursor-based pagination with composite key (createdAt + name)
- **V2 (CMIS):** Skip/Take pagination with optional date filtering
- **Configuration:** `UseV2Reader` flag in ServiceOptions

**Checkpoint Mechanism:**
- Saves `MultiFolderDiscoveryCursor` after each batch
- Tracks current folder type index and skip count
- Recovers on service restart

**File Location:** `/home/user/Alfresco-app/Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs`

---

### Phase 2: DOCUMENT DISCOVERY (DocumentDiscoveryService)

**Purpose:** Discover documents within staged folders and apply mapping rules

**Execution Model:**
- **Parallel:** `MaxDegreeOfParallelism = dop` (default 5)
- **Data Flow:** Alfresco folder → FolderStaging → DocStaging table
- **Batching:** Acquires up to `BatchSize` folders (default 1000)

**Key Operations Per Document:**

1. **Document Extraction (Sequential per folder)**
   - Pagination: 100 documents per page (PAGE_SIZE = 100)
   - Prevents OutOfMemory for large folders

2. **Document Mapping (Complex)**
   ```
   Document Properties Extracted:
   ├── ecm:docDesc → Mapped to ecm:docType via database lookup
   ├── ecm:status → Transformed to new status
   ├── ecm:docDossierType → Copy from folder or document
   ├── ecm:docClientType → Extract from folder/document
   ├── ecm:coreId → Extract from folder or document
   └── Other properties: Contract number, account numbers, product type
   
   Mappings Applied:
   ├── Document Type Mapping: ecm:docDesc → ecm:docType (via OpisToTipMapper)
   ├── Status Determination: DocumentStatusDetector.GetStatusInfoByOpis()
   ├── Destination Type: DestinationRootFolderDeterminator.DetermineAndResolve()
   ├── Dossier ID Conversion: Convert for target type (PI→ACC conversion)
   └── Version Detection: 1.1 (unsigned) or 1.2 (signed)
   ```

3. **Insert into Staging Table (Batch)**
   - Inserts 100 documents per page transaction
   - Then marks folder as PROCESSED

**Parallelization:**
```csharp
await Parallel.ForEachAsync(folders, 
    new ParallelOptions { MaxDegreeOfParallelism = dop },
    async (folder, token) =>
    {
        await ProcessSingleFolderAsync(folder, ct);
    });
```

**File Location:** `/home/user/Alfresco-app/Migration.Infrastructure/Implementation/Services/DocumentDiscoveryService.cs`

---

### Phase 3: MOVE SERVICE (MoveService)

**Purpose:** Move/copy documents to destination folders and update properties

**Execution Model:**
- **Parallel:** `MaxDegreeOfParallelism = dop` (default 5)
- **Data Flow:** DocStaging (Ready) → Alfresco (migrate) → DocStaging (Done/Error)
- **Batching:** Acquires up to `BatchSize` documents (default 1000)

**Per-Document Operations:**

```
MoveSingleDocumentAsync() Process:
│
├─ STEP 1: Create/Get Destination Folder
│  └─ Resolve parent folder (DOSSIERS-PI, DOSSIERS-LE, etc.)
│  └─ Resolve dossier folder (PI102206, LE500342)
│  └─ Uses DocumentResolver with cache + lock striping
│
├─ STEP 2: Move or Copy Document
│  └─ IAlfrescoWriteApi.MoveAsync() or CopyAsync()
│  └─ Configurable via MoveService.UseCopy flag
│
├─ STEP 3: Lookup DocumentMapping
│  └─ Find mapping for ecm:docDesc
│  └─ Get SifraDokumentaMigracija and NazivDokumentaMigracija
│  └─ Fallback to document's original values if not found
│
└─ STEP 4: Update Document Properties
   └─ Set ecm:docType and ecm:naziv
   └─ Update only these two properties
```

**Parallelization:**
```csharp
await Parallel.ForEachAsync(documents,
    new ParallelOptions { MaxDegreeOfParallelism = dop },
    async (doc, token) =>
    {
        var res = await MoveSingleDocumentAsync(doc, token);
        // Track success/failure atomically
    });
```

**Timing Breakdown (from logs):**
```
acquire={AcqMs}ms
move={MoveMs}ms
update={UpdMs}ms
total={TotalMs}ms
Success={Done}, Failed={Failed}
```

**File Location:** `/home/user/Alfresco-app/Migration.Infrastructure/Implementation/Services/MoveService.cs`

---

## 3. Operations Performed on Each Document

### Phase 1: Folder Discovery
| Operation | Component | Details |
|-----------|-----------|---------|
| Search folders | IAlfrescoReadApi.SearchAsync() | CMIS/AFTS query |
| Extract properties | Entry model | Parse Alfresco properties |
| Enrich with ClientAPI | ClientApi.GetClientDataAsync() | Fetch from ClientAPI if needed |
| Batch insert | IFolderStagingRepository.InsertManyAsync() | Insert to staging table |

### Phase 2: Document Discovery
| Operation | Component | Details |
|-----------|-----------|---------|
| **Read documents** | IDocumentReader.ReadBatchWithPaginationAsync() | 100 docs per page |
| **Extract properties** | Entry → DocStaging mapping | Parse all ecm:* properties |
| **Map document type** | IOpisToTipMapper.GetTipDokumentaAsync() | ecm:docDesc → ecm:docType lookup |
| **Determine status** | DocumentStatusDetector.GetStatusInfoByOpis() | Active/Inactive, status code |
| **Determine destination** | DestinationRootFolderDeterminator.DetermineAndResolve() | PI/LE/ACC/D routing |
| **Convert dossier ID** | DossierIdFormatter.ConvertForTargetType() | PI102206 → ACC102206 if needed |
| **Detect version** | Name pattern matching | Signed (1.2) vs Unsigned (1.1) |
| **Batch insert** | IDocStagingRepository.InsertManyAsync() | Insert to DocStaging table |

### Phase 3: Move Service
| Operation | Component | Details |
|-----------|-----------|---------|
| **Acquire docs** | IDocStagingRepository.TakeReadyForProcessingAsync() | Atomic: acquire + lock |
| **Resolve folders** | IDocumentResolver.ResolveAsync() | Create/get with cache + lock striping |
| **Move/Copy** | IMoveExecutor.MoveAsync() or CopyAsync() | Alfresco API call |
| **Lookup mapping** | IDocumentMappingService.FindByOriginalNameAsync() | Get migrated type/name |
| **Update properties** | IAlfrescoWriteApi.UpdateNodePropertiesAsync() | Set ecm:docType + ecm:naziv |
| **Batch update** | IDocStagingRepository.BatchSetDocumentStatusAsync_v1() | Mark Done/Failed |

---

## 4. Performance Bottlenecks Identified

### Critical Bottlenecks:

#### 1. **Pagination Overhead in Document Discovery**
- **Issue:** Reading documents from Alfresco in pages of 100
- **Impact:** Folders with 10,000 documents = 100 API calls per folder
- **Location:** DocumentDiscoveryService.ProcessSingleFolderAsync() (Line 828-841)
- **Code:**
```csharp
const int PAGE_SIZE = 100; // Process 100 documents at a time
// Loop continues until all pages read
while (hasMore && !ct.IsCancellationRequested)
{
    var result = await _reader.ReadBatchWithPaginationAsync(
        folder.NodeId!, skipCount, PAGE_SIZE, ct);
    // Process each page...
}
```

#### 2. **Sequential Folder Processing in Folder Discovery**
- **Issue:** Processes DOSSIER subfolders one at a time (no parallelization)
- **Impact:** Single thread bottleneck for multi-type processing
- **Location:** FolderDiscoveryService.RunBatchAsync() (Line 162-224)
- **Improvement:** Could parallelize across different DOSSIER types (PI, LE, ACC, D)

#### 3. **Lock Contention in DocumentResolver**
- **Issue:** Multiple parallel moves accessing same parent folders
- **Mitigation Implemented:** Lock striping with 1024 fixed locks
- **Performance:** 99.8% memory reduction (100 MB → 200 KB)
- **File:** LockStriping.cs (Lines 1-97)

#### 4. **Database Transaction Latency**
- **Issue:** Each batch phase requires database roundtrip
  - Acquire items (1 transaction)
  - Mark status IN_PROGRESS (1 transaction)
  - Process items (parallel, no DB)
  - Mark Done/Failed (1 transaction)
  - Save checkpoint (1 transaction)
- **Total per batch:** 4+ transactions
- **Improvement:** Could batch operations in single transaction

#### 5. **Status Detection via Name Pattern Matching**
- **Issue:** Searching document name for "signed"/"potpisano" strings
- **Impact:** String comparison on every document
- **Location:** DocumentDiscoveryService.ApplyDocumentMappingAsync() (Line 596-606)
- **Code:**
```csharp
if (nameLower.Contains("signed") || 
    nameLower.Contains("potpisano") || 
    nameLower.Contains("potpisan"))
{
    doc.Version = 1.2m; // Signed
    doc.IsSigned = true;
}
```

#### 6. **CMIS vs AFTS Query Performance**
- **AFTS (Current):** Cursor-based pagination - stateful
- **CMIS (V2):** Skip/Take pagination - simpler but potentially slower for large datasets
- **Configuration:** `UseV2Reader` flag controls which method used
- **Trade-off:** AFTS = better pagination consistency, CMIS = date filtering support

#### 7. **Memory Pressure from Large Batches**
- **Issue:** Default batch size = 1000 items
- **Impact:** All 1000 items held in memory simultaneously
- **Parallelization:** With DOP=5, 5 items being processed + 995 waiting
- **Improvement:** Could stream/chunk processing

#### 8. **Folder Cache Not Pre-warmed**
- **Issue:** DocumentResolver cache empty at start
- **Impact:** First move of each unique folder = cache miss + lock acquisition
- **Improvement:** Could pre-warm cache during FolderPreparationService

---

## 5. Current Parallelization Strategies

### Strategy 1: Parallel.ForEachAsync with Fixed DOP

**Usage:**
- DocumentDiscoveryService: Processing folders in parallel
- MoveService: Processing documents in parallel

**Configuration:**
```csharp
var dop = _options.Value.MoveService.MaxDegreeOfParallelism 
    ?? _options.Value.MaxDegreeOfParallelism; // Default: 5

await Parallel.ForEachAsync(items, 
    new ParallelOptions
    {
        MaxDegreeOfParallelism = dop,
        CancellationToken = ct
    },
    async (item, token) => { /* process item */ });
```

**Advantages:**
- Simple, built-in .NET mechanism
- Respects CancellationToken
- Thread pool adaptive scheduling

**Disadvantages:**
- Fixed DOP may not be optimal for all workloads
- No adaptive scaling based on I/O latency
- Context switching overhead with DOP > CPU cores

### Strategy 2: Lock Striping for Folder Access

**Purpose:** Prevent unlimited SemaphoreSlim allocation

**Implementation:**
```csharp
private readonly LockStriping _lockStriping = new(1024);

public async Task<string> ResolveAsync(
    string destinationRootId, 
    string newFolderName, 
    CancellationToken ct)
{
    var cacheKey = $"{destinationRootId}_{newFolderName}";
    var folderLock = _lockStriping.GetLock(cacheKey);
    
    await folderLock.WaitAsync(ct);
    try
    {
        // Double-check cache
        // Create/get folder
        // Cache result
    }
    finally
    {
        folderLock.Release();
    }
}
```

**Performance:**
- Fixed 1024 locks for unlimited keys (hash-based)
- Bitwise AND for modulo: `Math.Abs(hash) & (_lockCount - 1)`
- Reduces memory leak from 100 MB → 200 KB (99.8% reduction)

**File:** `/home/user/Alfresco-app/Migration.Infrastructure/Implementation/Document/LockStriping.cs`

### Strategy 3: Atomic Acquire + Status Locking

**Purpose:** Prevent duplicate processing of items

**Implementation:**
```csharp
// AcquireDocumentsForMoveAsync() - Line 429-468
var documents = await docRepo.TakeReadyForProcessingAsync(batch, ct);

// Atomic batch status update
var updates = documents.Select(d => (
    d.Id,
    MigrationStatus.InProgress.ToDbString(),
    (string?)null
));

await docRepo.BatchSetDocumentStatusAsync_v1(
    uow.Connection,
    uow.Transaction,
    updates,
    ct);
```

**Mechanism:**
1. SELECT items with status = READY (locked rows)
2. UPDATE same rows to status = IN_PROGRESS (atomic)
3. Other threads see IN_PROGRESS and skip these items
4. Process items in parallel
5. Batch update final status (Done/Error)

### Strategy 4: Checkpoint-Based Recovery

**Purpose:** Resume from last batch on crash/restart

**Implementation:**
```csharp
// Before: Load checkpoint
await LoadCheckpointAsync(ct);
var batchCounter = _batchCounter + 1;

// After: Save checkpoint
Interlocked.Increment(ref _batchCounter);
await SaveCheckpointAsync(ct);
```

**Data Stored:**
- For DocumentDiscovery: Total processed, total failed, batch counter
- For FolderDiscovery: MultiFolderDiscoveryCursor (JSON serialized)
- For MoveService: Total moved, total failed, batch counter

---

## 6. Caching Mechanisms

### Document Resolver Cache

**Type:** ConcurrentDictionary<string, string>

**Key:** `"{destinationRootId}_{newFolderName}"`

**Value:** Folder ID returned from Alfresco

**Strategy:**
1. Fast path: Check cache (no locking)
2. Cache miss: Acquire lock, double-check cache
3. If still missing: Check Alfresco, create if needed
4. Add to cache for future use

**Memory Impact:** One entry per unique folder, minimal overhead

**File:** `/home/user/Alfresco-app/Migration.Infrastructure/Implementation/Document/DocumentResolver.cs`

---

## 7. Error Handling & Recovery

### Per-Batch Error Handling

**Folder/Document Level Failure:**
```csharp
try
{
    await ProcessSingleFolderAsync(folder, ct);
    Interlocked.Increment(ref processedCount);
}
catch (Exception ex)
{
    errors.Add((folder.Id, ex));
    _fileLogger.LogError("Failed to process folder {FolderId}: {Error}",
        folder.Id, ex.Message);
}
```

**Batch Recovery:**
```csharp
// After Parallel.ForEachAsync completes:

// 1. Mark failed items
if (!ct.IsCancellationRequested && !errors.IsEmpty)
{
    await MarkFoldersAsFailedAsync(errors, ct);
    Interlocked.Add(ref _totalFailed, errors.Count);
}

// 2. Save checkpoint (if not cancelled)
if (!ct.IsCancellationRequested)
{
    Interlocked.Increment(ref _batchCounter);
    await SaveCheckpointAsync(ct);
}
```

### Stuck Items Detection

**Mechanism:**
```csharp
// MoveService.ResetStuckItemsAsync() - Line 290-338
var timeout = TimeSpan.FromMinutes(_options.Value.StuckItemsTimeoutMinutes);
var resetCount = await docRepo.ResetStuckDocumentsAsync(
    uow.Connection,
    uow.Transaction,
    timeout,
    ct);
```

**Process:**
1. On service startup: Find items in IN_PROGRESS status
2. Check LastUpdatedAt timestamp
3. If older than timeout (default 10 min): Reset to READY
4. Resume processing

---

## 8. Database Integration

### Batch Operations

**Folder/Document Acquisition:**
```csharp
var documents = await docRepo.TakeReadyForProcessingAsync(batch, ct);
```

**Status Update (Batch):**
```csharp
await docRepo.BatchSetDocumentStatusAsync_v1(
    uow.Connection,
    uow.Transaction,
    updates,  // Enumerable<(id, status, errorMsg)>
    ct);
```

**Insert Operations:**
```csharp
// Folder Discovery inserts folders
var inserted = await folderRepo.InsertManyAsync(folders, ct);

// Document Discovery inserts documents (per page)
int inserted = await docRepo.InsertManyAsync(docsToInsert, ct);
```

### Transaction Isolation

**Used:** IsolationLevel.ReadCommitted (default)

**In FolderDiscoveryService.InsertFoldersAsync():**
```csharp
await uow.BeginAsync(IsolationLevel.ReadCommitted, ct);
var inserted = await folderRepo.InsertManyAsync(folders, ct);
await uow.CommitAsync(ct);
```

---

## 9. Logging Strategy

### Three-Logger Pattern

```csharp
private readonly ILogger _dbLogger;      // Database operations
private readonly ILogger _fileLogger;    // Detailed file logs
private readonly ILogger _uiLogger;      // User-facing UI logs
```

**Usage:**
- **FileLogger:** DEBUG/TRACE level for development
- **DbLogger:** INFO/ERROR level for database audit trail
- **UiLogger:** INFO level for UI progress display

**Scoping:**
```csharp
using var batchScope = _fileLogger.BeginScope(new Dictionary<string, object>
{
    ["Service"] = nameof(DocumentDiscoveryService),
    ["Operation"] = "RunBatch",
    ["BatchCounter"] = batchCounter
});
```

---

## 10. Recommendations for Optimization

### High Priority

1. **Implement Adaptive DOP**
   - Monitor I/O latency
   - Adjust parallelism dynamically
   - Target: CPU + I/O balance

2. **Pre-warm Folder Cache**
   - Run FolderPreparationService first
   - Pre-populate DocumentResolver cache
   - Reduce cache misses during move phase

3. **Implement Streaming Document Processing**
   - Don't hold 1000 items in memory
   - Process as chunks with backpressure
   - Reduce memory pressure

4. **Optimize Document Type Mapping**
   - Cache mapper results
   - Use database index on ecm:docDesc
   - Batch lookup multiple documents at once

### Medium Priority

5. **Parallelize FolderDiscovery**
   - Process multiple DOSSIER types in parallel
   - Respects current sequential design

6. **Consolidate Database Transactions**
   - Combine acquire + status + checkpoint into single transaction
   - Reduce roundtrips to database

7. **Implement CMIS Query Optimization**
   - Test CMIS vs AFTS performance on actual dataset
   - Consider hybrid approach (small batches = CMIS, large = AFTS)

8. **Add Telemetry/Metrics**
   - Track p50/p95/p99 latencies per operation
   - Monitor lock contention
   - Export to monitoring system

### Low Priority

9. **Implement Exponential Backoff on Rate Limiting**
   - Currently: Linear backoff (delay * 2)
   - Consider: More sophisticated retry logic

10. **Document Configuration Best Practices**
    - Current defaults may not suit all deployments
    - Create tuning guide based on dataset size

---

## 11. Configuration Best Practices

### For Small Datasets (< 100K documents)

```json
{
  "MaxDegreeOfParallelism": 8,
  "BatchSize": 500,
  "DocumentDiscovery": {
    "BatchSize": 200,
    "MaxDegreeOfParallelism": 8
  },
  "MoveService": {
    "BatchSize": 100,
    "MaxDegreeOfParallelism": 4
  }
}
```

### For Large Datasets (> 1M documents)

```json
{
  "MaxDegreeOfParallelism": 3,
  "BatchSize": 2000,
  "DelayBetweenBatchesInMs": 500,
  "DocumentDiscovery": {
    "BatchSize": 1000,
    "MaxDegreeOfParallelism": 3,
    "DelayBetweenBatchesInMs": 1000
  },
  "MoveService": {
    "BatchSize": 500,
    "MaxDegreeOfParallelism": 2,
    "DelayBetweenBatchesInMs": 500
  }
}
```

### For I/O Constrained Environments

```json
{
  "MaxDegreeOfParallelism": 2,
  "BatchSize": 500,
  "DelayBetweenBatchesInMs": 2000,
  "MoveService": {
    "UseCopy": true,  // Less I/O intensive than move
    "BatchSize": 200,
    "MaxDegreeOfParallelism": 1
  }
}
```

---

## Summary Table

| Aspect | Details |
|--------|---------|
| **Processing Model** | 3-phase pipeline: Folder Discovery → Document Discovery → Move Service |
| **Parallelization** | Parallel.ForEachAsync with configurable DOP (default 5) |
| **Batch Mechanism** | Fixed batch size (default 1000), process sequentially |
| **Intra-Batch Parallelism** | All items in batch processed in parallel up to DOP |
| **Synchronization** | Status-based locking (READY → IN_PROGRESS → Done/Error) |
| **Concurrency Control** | Lock striping (1024 fixed locks) for folder access |
| **Recovery** | Checkpoint-based resumption from last successful batch |
| **Bottlenecks** | Pagination overhead, sequential folder discovery, DB transaction latency |
| **Caching** | DocumentResolver cache + lock striping for folders |
| **Error Handling** | Per-item try-catch, batch status tracking, stuck item detection |
| **Monitoring** | Three-logger pattern (db/file/ui) with scoped context |

