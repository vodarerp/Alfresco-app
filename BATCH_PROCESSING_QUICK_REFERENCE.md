# Batch Document Processing - Quick Reference Guide

## File Locations Quick Index

### Core Services
- **DocumentDiscoveryService** → `/Migration.Infrastructure/Implementation/Services/DocumentDiscoveryService.cs`
  - Phase 2: Discover documents and apply mapping rules
  - Parallel processing: 5 workers (default), batch size 1000 (default)
  - Key method: `RunBatchAsync()` (Line 64-140)

- **FolderDiscoveryService** → `/Migration.Infrastructure/Implementation/Services/FolderDiscoveryService.cs`
  - Phase 1: Discover folder metadata from source Alfresco
  - Sequential processing (per DOSSIER type)
  - Key method: `RunBatchAsync()` (Line 66-320)

- **MoveService** → `/Migration.Infrastructure/Implementation/Services/MoveService.cs`
  - Phase 3: Migrate documents to destination Alfresco
  - Parallel processing: 5 workers (default), batch size 1000 (default)
  - Key method: `RunBatchAsync()` (Line 77-188)

### Support Components
- **DocumentResolver** → `/Migration.Infrastructure/Implementation/Document/DocumentResolver.cs`
  - Folder cache + lock striping (1024 locks)
  - Prevents memory leaks from folder access synchronization

- **LockStriping** → `/Migration.Infrastructure/Implementation/Document/LockStriping.cs`
  - Fixed 1024 locks for unlimited folder keys
  - Memory optimization: 100 MB → 200 KB (99.8% reduction)

- **MoveExecutor** → `/Migration.Infrastructure/Implementation/Move/MoveExecutor.cs`
  - Wraps Alfresco API calls for move/copy operations
  - Configurable: move or copy strategy

- **FolderReader** → `/Migration.Infrastructure/Implementation/Folder/FolderReader.cs`
  - AFTS (cursor-based) vs CMIS (skip/take) pagination
  - Supports date filtering and CoreId filtering

### Configuration
- **MigrationOptions** → `/Alfresco.Contracts/Options/MigrationOptions.cs`
  - Global defaults: MaxDOP=5, BatchSize=1000
  - Per-service overrides for DocumentDiscovery, FolderDiscovery, MoveService

---

## Processing Flow Diagram

```
START (Service Loop)
│
├─ PHASE 1: FOLDER DISCOVERY
│  ├─ Load checkpoint (resume from last position)
│  ├─ Find DOSSIER subfolders (DOSSIER-PI, DOSSIER-LE, etc.)
│  ├─ Query folders using CMIS or AFTS
│  ├─ Enrich with ClientAPI if needed
│  └─ Insert into FolderStaging table
│     └─ Save checkpoint after batch
│
├─ PHASE 2: DOCUMENT DISCOVERY (PARALLEL - DOP=5)
│  ├─ Load checkpoint
│  ├─ Acquire batch of folders (READY status)
│  ├─ For each folder (parallel):
│  │  ├─ Read documents in pages (100 per page)
│  │  ├─ Extract properties
│  │  ├─ Map ecm:docDesc → ecm:docType (database lookup)
│  │  ├─ Determine status (active/inactive)
│  │  ├─ Determine destination (PI/LE/ACC/D)
│  │  ├─ Convert dossier ID if needed
│  │  ├─ Detect version (signed/unsigned)
│  │  └─ Insert documents into DocStaging table
│  └─ Mark folder PROCESSED
│     └─ Save checkpoint after batch
│
└─ PHASE 3: MOVE SERVICE (PARALLEL - DOP=5)
   ├─ Load checkpoint
   ├─ Acquire batch of documents (READY status)
   ├─ For each document (parallel):
   │  ├─ Resolve destination folder (cached + lock striped)
   │  ├─ Move/Copy to destination
   │  ├─ Lookup DocumentMapping for migrated type/name
   │  ├─ Read current document properties
   │  └─ Update ecm:docType and ecm:naziv
   ├─ Mark documents DONE or ERROR (batch update)
   └─ Save checkpoint after batch
      └─ If no more items → Complete
```

---

## Parallelization Pattern

### For Each Service

```
Batch 1 (1000 items)          Batch 2 (1000 items)        Batch N
├─ Acquire (1 thread)         ├─ Acquire                   ├─ Acquire
│                              │                            │
├─ Parallel Processing:        ├─ Parallel Processing:      ├─ Parallel Processing:
│  Worker 1: Doc 1             │  Worker 1: Doc 1001        │  ...
│  Worker 2: Doc 2             │  Worker 2: Doc 1002        │  ...
│  Worker 3: Doc 3             │  Worker 3: Doc 1003        │  ...
│  Worker 4: Doc 4             │  Worker 4: Doc 1004        │  ...
│  Worker 5: Doc 5             │  Worker 5: Doc 1005        │  ...
│  (Workers 1-5 cycle)         │  (Workers 1-5 cycle)       │  (cycle)
│
├─ Update Status (1 thread)    ├─ Update Status             ├─ Update Status
├─ Save Checkpoint             ├─ Save Checkpoint           ├─ Save Checkpoint
└─ Delay (configurable)        └─ Delay                     └─ Done
```

**Key Points:**
- Each batch acquires items sequentially
- Parallel processing happens WITHIN batch (5 workers default)
- Status updates happen after all workers complete
- Checkpoint saves per batch (not per item)

---

## Key Methods Reference

### DocumentDiscoveryService

```csharp
// Main batch loop - called repeatedly
public async Task<bool> RunLoopAsync(CancellationToken ct)

// Single batch execution
public async Task<DocumentBatchResult> RunBatchAsync(CancellationToken ct)
  ├─ AcquireFoldersForProcessingAsync()     // Get folders with READY status
  ├─ ProcessSingleFolderAsync()             // Read and map documents
  │   ├─ ReadBatchWithPaginationAsync()     // Read 100 docs per page
  │   ├─ ApplyDocumentMappingAsync()        // Apply mapping rules
  │   └─ InsertDocsAsync()                  // Insert to DocStaging
  ├─ MarkFoldersAsFailedAsync()             // Batch update errors
  └─ SaveCheckpointAsync()                  // Persist progress

// Mapping logic for single document
private async Task ApplyDocumentMappingAsync(
    DocStaging doc, 
    FolderStaging folder, 
    Entry alfrescoEntry, 
    CancellationToken ct)
  ├─ Extract properties from alfrescoEntry
  ├─ OpisToTipMapper.GetTipDokumentaAsync() // Map ecm:docDesc
  ├─ DocumentStatusDetector.GetStatusInfoByOpis()
  ├─ DestinationRootFolderDeterminator.DetermineAndResolve()
  ├─ DossierIdFormatter.ConvertForTargetType()
  └─ Detect version from filename
```

### MoveService

```csharp
// Single batch execution
public async Task<MoveBatchResult> RunBatchAsync(CancellationToken ct)
  ├─ AcquireDocumentsForMoveAsync()         // Get docs with READY status
  ├─ Parallel.ForEachAsync(documents)       // Process with DOP=5
  │   └─ MoveSingleDocumentAsync()
  │       ├─ CreateOrGetDestinationFolder() // Uses DocumentResolver cache
  │       ├─ MoveExecutor.MoveAsync() or CopyAsync()
  │       ├─ DocumentMappingService.FindByOriginalNameAsync()
  │       ├─ AlfrescoReadApi.GetNodeByIdAsync()
  │       └─ AlfrescoWriteApi.UpdateNodePropertiesAsync()
  ├─ MarkDocumentsAsDoneAsync()             // Batch update successful
  ├─ MarkDocumentsAsFailedAsync()           // Batch update errors
  └─ SaveCheckpointAsync()                  // Persist progress
```

---

## Configuration Parameter Reference

### Global Settings (apply to all services if not overridden)

| Parameter | Default | Type | Purpose |
|-----------|---------|------|---------|
| `MaxDegreeOfParallelism` | 5 | int | Parallel worker count |
| `BatchSize` | 1000 | int | Items per batch |
| `DelayBetweenBatchesInMs` | 0 | int | Delay after each batch |
| `IdleDelayInMs` | 100 | int | Delay when no items found |
| `BreakEmptyResults` | ? | int | Empty batches before stopping |
| `StuckItemsTimeoutMinutes` | 10 | int | Reset IN_PROGRESS after this |
| `RootDestinationFolderId` | - | string | Target Alfresco root |
| `RootDiscoveryFolderId` | - | string | Source Alfresco root |

### Service-Specific Overrides

**DocumentDiscovery**
```csharp
public ServiceOptions DocumentDiscovery { get; set; }
  ├─ BatchSize?              // Override global batch size
  ├─ MaxDegreeOfParallelism? // Override global DOP
  ├─ DelayBetweenBatchesInMs? // Override inter-batch delay
  └─ NameFilter              // Filter folder names
```

**MoveService**
```csharp
public ServiceOptions MoveService { get; set; }
  ├─ BatchSize?              // Override global batch size
  ├─ MaxDegreeOfParallelism? // Override global DOP
  ├─ DelayBetweenBatchesInMs? // Override inter-batch delay
  └─ UseCopy                 // true=copy, false=move (default)
```

**FolderDiscovery**
```csharp
public ServiceOptions FolderDiscovery { get; set; }
  ├─ BatchSize?              // Override global batch size
  ├─ DelayBetweenBatchesInMs? // Override inter-batch delay
  ├─ NameFilter              // Filter folder names
  ├─ FolderTypes             // List of types: ["PI", "LE", "ACC", "D"]
  ├─ TargetCoreIds           // Filter by CoreId list
  ├─ UseV2Reader             // Use CMIS (v2) instead of AFTS
  ├─ UseDateFilter           // Enable date filtering in CMIS
  ├─ DateFrom                // CMIS date filter start
  └─ DateTo                  // CMIS date filter end
```

---

## Common Performance Tuning Scenarios

### Scenario 1: Fast Small Dataset (< 100K documents)
```json
{
  "MaxDegreeOfParallelism": 8,
  "BatchSize": 500,
  "DelayBetweenBatchesInMs": 0,
  "DocumentDiscovery": { "BatchSize": 200, "MaxDegreeOfParallelism": 8 },
  "MoveService": { "BatchSize": 100, "MaxDegreeOfParallelism": 4 }
}
```
**Rationale:** Higher parallelism, smaller batches, no delays

### Scenario 2: Stable Large Dataset (> 1M documents)
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
**Rationale:** Lower parallelism to reduce memory/CPU, larger batches, inter-batch delays for stability

### Scenario 3: I/O Constrained
```json
{
  "MaxDegreeOfParallelism": 2,
  "BatchSize": 500,
  "DelayBetweenBatchesInMs": 2000,
  "MoveService": { 
    "UseCopy": true,
    "BatchSize": 200,
    "MaxDegreeOfParallelism": 1
  }
}
```
**Rationale:** Minimal parallelism, copy instead of move (less I/O), longer delays

---

## Bottleneck Checklist

When experiencing slow migration:

- [ ] Check **batch size** - too large = memory pressure, too small = overhead
- [ ] Check **DOP (MaxDegreeOfParallelism)** - too low = underutilized, too high = context switch
- [ ] Check **page size** (100 in DocumentDiscoveryService) - folders with 10K docs = 100 API calls
- [ ] Check **database transaction time** - each phase has 4+ transactions
- [ ] Check **folder cache hit rate** - first document of each folder = cache miss
- [ ] Check **document type mapping latency** - database lookup per document
- [ ] Check **Alfresco API response time** - may be network-bound
- [ ] Check **CMIS vs AFTS** - which is faster for your dataset?

---

## Status State Machine

```
READY → IN_PROGRESS → DONE
        (processing)
    
    └─→ ERROR
        (on failure)

READY: Item available for processing
IN_PROGRESS: Item currently being processed (prevents duplicate)
DONE: Item successfully migrated
ERROR: Item failed (error message stored)

Timeout Rule:
If IN_PROGRESS for > StuckItemsTimeoutMinutes (default 10):
  Reset back to READY on service restart
```

---

## Logging Output Examples

### DocumentDiscovery Batch
```
info: DocumentDiscovery batch started - BatchSize: 1000, DOP: 5
debug: Acquiring 1000 folders for processing
info: Acquired 992 folders in 234ms
info: Starting parallel processing of 992 folders with DOP=5
info: Parallel move completed: 992 succeeded, 0 failed in 45230ms (avg 45.7ms/doc)
info: Move batch TOTAL: acquire=234ms, move=45230ms, update=123ms, total=45587ms
```

### MoveService Batch
```
info: Move batch started - BatchSize: 1000, DOP: 5
info: Acquired 850 documents in 156ms
info: Starting parallel move with DOP=5
info: Parallel move completed: 847 succeeded, 3 failed in 32456ms (avg 38.3ms/doc)
info: Move batch TOTAL: acquire=156ms, move=32456ms, update=234ms, total=32846ms | Success=847, Failed=3
```

---

## Troubleshooting Guide

### Issue: Migration very slow
**Check:**
1. Is DOP limited to 1? Increase it.
2. Is batch size too large (memory swapping)? Reduce it.
3. Is Alfresco API slow? Monitor response times.
4. Are database transactions slow? Check database performance.
5. Is page size (100) causing too many API calls? Increase it.

### Issue: High memory usage
**Check:**
1. Batch size too large? Reduce BatchSize.
2. Folder cache growing unbounded? Clear and restart.
3. SemaphoreSlim instances? Should be fixed 1024 via LockStriping.
4. Document list in memory? Consider streaming approach.

### Issue: Parallelism not working
**Check:**
1. Is `MaxDegreeOfParallelism` set to 1? Increase it.
2. Are documents acquiring locks on same folder? Expected, uses lock striping.
3. Is I/O bound? High DOP won't help I/O-bound workloads.

### Issue: Migration stops or hangs
**Check:**
1. Look for stuck items (IN_PROGRESS for > timeout).
2. Check cancellation token status.
3. Monitor exception logs - exception in one worker shouldn't stop batch.
4. Check database connectivity.

---

## Key Files Summary

| File | Purpose | Key Classes |
|------|---------|-------------|
| DocumentDiscoveryService.cs | Phase 2: Map documents | DocumentDiscoveryService |
| FolderDiscoveryService.cs | Phase 1: Discover folders | FolderDiscoveryService |
| MoveService.cs | Phase 3: Migrate documents | MoveService |
| DocumentResolver.cs | Folder cache + locking | DocumentResolver, LockStriping |
| LockStriping.cs | Memory-efficient locks | LockStriping |
| FolderReader.cs | CMIS/AFTS queries | FolderReader |
| MoveExecutor.cs | Alfresco move/copy | MoveExecutor |
| MigrationOptions.cs | Configuration | MigrationOptions, ServiceOptions |

