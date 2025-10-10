# Missing Documents Analysis - Troubleshooting Guide

## 🔍 Problem Description

You have ~10,000 folders in `FolderStaging` table, all marked as `PROCESSED`, but only ~3,500 folders have corresponding documents in `DocStaging` table. This means approximately 6,500 folders were marked as PROCESSED without inserting their documents.

## 🐛 Root Causes Identified

### 1. Empty Folders (Expected Behavior)
Some folders may legitimately have no documents. This is normal and expected.

### 2. Silent Failures (BUG - FIXED)
Documents discovery might have failed silently without proper error logging. Added comprehensive logging to track every step.

### 3. Case Sensitivity Issue (BUG - FIXED)
**File**: `DocumentDiscoveryService.cs` line 427

**Before**:
```csharp
await folderRepo.SetStatusAsync(folderId, MigrationStatus.Processed.ToString(), null, ct);
```

**After**:
```csharp
await folderRepo.SetStatusAsync(folderId, MigrationStatus.Processed.ToDbString(), null, ct);
```

`ToString()` returns "Processed" while `ToDbString()` returns "PROCESSED". While Oracle VARCHAR2 comparisons are usually case-insensitive, it's better to be consistent.

### 4. Transaction Rollback Without Re-throw (Potential Issue)
If `InsertManyAsync` fails and rolls back, but the folder was already marked as PROCESSED in memory before the rollback, there could be a mismatch.

## 🔧 Fixes Applied

### Enhanced Logging
Added comprehensive logging at every critical step:

1. **Folder Processing Start**
```csharp
_logger.LogDebug("Processing folder {FolderId} ({Name}, NodeId: {NodeId})",
    folder.Id, folder.Name, folder.NodeId);
```

2. **Empty Folder Detection**
```csharp
_logger.LogInformation(
    "No documents found in folder {FolderId} ({Name}, NodeId: {NodeId}) - marking as PROCESSED",
    folder.Id, folder.Name, folder.NodeId);
```

3. **Document Discovery Success**
```csharp
_logger.LogInformation("Found {Count} documents in folder {FolderId} ({Name})",
    documents.Count, folder.Id, folder.Name);
```

4. **Destination Folder Resolution**
```csharp
_logger.LogDebug("Resolved destination folder: {DestFolderId}", desFolderId);
```

5. **Documents Prepared for Insert**
```csharp
_logger.LogInformation(
    "Prepared {Count} documents for insertion (folder {FolderId})",
    docsToInsert.Count, folder.Id);
```

6. **Insert Operation**
```csharp
_logger.LogInformation(
    "Successfully inserted {Inserted}/{Total} documents for folder {FolderId}",
    inserted, docsToInsert.Count, folderId);
```

7. **Transaction Commit**
```csharp
_logger.LogDebug("Transaction committed for folder {FolderId}", folderId);
```

8. **Error Handling**
```csharp
_logger.LogError(ex,
    "Failed to insert documents and mark folder {FolderId} as PROCESSED. " +
    "Attempted to insert {Count} documents. Rolling back transaction.",
    folderId, docsToInsert.Count);
```

### Warning for Empty docsToInsert
Added warning if `docsToInsert` list is empty when it shouldn't be:
```csharp
_logger.LogWarning(
    "docsToInsert is empty for folder {FolderId} - this should have been caught earlier!",
    folderId);
```

## 📊 Diagnostic Queries

Run the diagnostic SQL script to analyze the issue:

**File**: `SQL/DEBUG_FolderStatus.sql`

### Key Queries:

#### 1. Find Folders Without Documents
```sql
SELECT
    fs.Id,
    fs.NodeId,
    fs.Name,
    fs.Status,
    fs.UpdatedAt AS ProcessedAt,
    (SELECT COUNT(*)
     FROM DocStaging ds
     WHERE ds.ParentId = fs.NodeId) AS DocumentCount
FROM FolderStaging fs
WHERE fs.Status = 'PROCESSED'
  AND NOT EXISTS (
      SELECT 1
      FROM DocStaging ds
      WHERE ds.ParentId = fs.NodeId
  )
ORDER BY fs.UpdatedAt DESC;
```

#### 2. Count Problem Folders
```sql
SELECT COUNT(*) AS FoldersWithNoDocuments
FROM FolderStaging fs
WHERE fs.Status = 'PROCESSED'
  AND NOT EXISTS (
      SELECT 1
      FROM DocStaging ds
      WHERE ds.ParentId = fs.NodeId
  );
```

#### 3. Timeline Analysis
```sql
SELECT
    TO_CHAR(UpdatedAt, 'YYYY-MM-DD HH24') AS ProcessedHour,
    COUNT(*) AS FoldersProcessed,
    SUM(CASE WHEN EXISTS (
        SELECT 1 FROM DocStaging ds WHERE ds.ParentId = FolderStaging.NodeId
    ) THEN 1 ELSE 0 END) AS FoldersWithDocs,
    SUM(CASE WHEN NOT EXISTS (
        SELECT 1 FROM DocStaging ds WHERE ds.ParentId = FolderStaging.NodeId
    ) THEN 1 ELSE 0 END) AS FoldersWithoutDocs
FROM FolderStaging
WHERE Status = 'PROCESSED'
GROUP BY TO_CHAR(UpdatedAt, 'YYYY-MM-DD HH24')
ORDER BY ProcessedHour DESC;
```

## 🔄 Recovery Steps

### Option 1: Reset Problem Folders (Recommended)
Reset folders that have no documents back to READY status for reprocessing:

```sql
UPDATE FolderStaging
SET Status = 'READY',
    Error = 'Reset - missing documents issue',
    RetryCount = NVL(RetryCount, 0) + 1,
    UpdatedAt = SYSTIMESTAMP
WHERE Status = 'PROCESSED'
  AND NOT EXISTS (
      SELECT 1
      FROM DocStaging ds
      WHERE ds.ParentId = FolderStaging.NodeId
  );

COMMIT;
```

**Expected Result**: ~6,500 folders will be reset to READY and reprocessed.

### Option 2: Analyze First (Conservative)
Before resetting, check if these are legitimately empty folders by querying Alfresco API directly for a sample of folders:

1. Pick 10 folders from the diagnostic query
2. Check Alfresco API manually: `GET /alfresco/api/-default-/public/alfresco/versions/1/nodes/{nodeId}/children`
3. If folders have documents in Alfresco but not in DB → Reset them
4. If folders are empty in Alfresco → Leave as PROCESSED

### Option 3: Selective Reset Based on Timestamp
If the issue started at a specific time, reset only folders processed during that period:

```sql
UPDATE FolderStaging
SET Status = 'READY',
    Error = 'Reset - processed during problem window',
    RetryCount = NVL(RetryCount, 0) + 1,
    UpdatedAt = SYSTIMESTAMP
WHERE Status = 'PROCESSED'
  AND UpdatedAt BETWEEN TIMESTAMP '2025-01-08 10:00:00' AND TIMESTAMP '2025-01-08 14:00:00'
  AND NOT EXISTS (
      SELECT 1
      FROM DocStaging ds
      WHERE ds.ParentId = FolderStaging.NodeId
  );

COMMIT;
```

## 📝 Verification After Reset

After resetting and reprocessing, verify the fix:

### 1. Check Logs
Look for these patterns in `logs/app.log`:

**Good (Expected for empty folders)**:
```
No documents found in folder 12345 (MyFolder, NodeId: abc-123) - marking as PROCESSED
```

**Bad (Unexpected)**:
```
Found 25 documents in folder 12345 (MyFolder)
Prepared 25 documents for insertion (folder 12345)
Successfully inserted 25/25 documents for folder 12345
```
Followed by:
```
No documents in DocStaging for ParentId=abc-123
```

**Error (Needs investigation)**:
```
Failed to insert documents and mark folder 12345 as PROCESSED. Attempted to insert 25 documents. Rolling back transaction.
```

### 2. Query Database
Run the diagnostic queries again and verify:

```sql
-- Should be 0 or close to 0 (only legitimately empty folders)
SELECT COUNT(*) AS FoldersWithNoDocuments
FROM FolderStaging fs
WHERE fs.Status = 'PROCESSED'
  AND NOT EXISTS (
      SELECT 1 FROM DocStaging ds WHERE ds.ParentId = fs.NodeId
  );
```

### 3. Check Application Logs in Database
Query log table for errors:

```sql
SELECT LOG_DATE, LOGGER, MESSAGE, EXCEPTION
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL = 'ERROR'
  AND LOGGER LIKE '%DocumentDiscovery%'
  AND LOG_DATE >= SYSTIMESTAMP - INTERVAL '1' HOUR
ORDER BY LOG_DATE DESC;
```

## 🎯 Prevention

To prevent this issue in the future:

### 1. Monitor Folder Processing
Add a scheduled job to detect folders marked as PROCESSED without documents:

```sql
-- Run every hour
SELECT COUNT(*) AS SuspiciousFolders
FROM FolderStaging fs
WHERE fs.Status = 'PROCESSED'
  AND fs.UpdatedAt >= SYSTIMESTAMP - INTERVAL '1' HOUR
  AND NOT EXISTS (
      SELECT 1 FROM DocStaging ds WHERE ds.ParentId = fs.NodeId
  );
```

If count > 0, investigate immediately.

### 2. Enable Database Logging
Configure log4net to log INFO level for DocumentDiscoveryService to database:

**log4net.config**:
```xml
<logger name="Migration.Infrastructure.Implementation.Services.DocumentDiscoveryService" additivity="false">
  <level value="Info" />
  <appender-ref ref="OracleAdoAppender" />
  <appender-ref ref="RollingFileAppender" />
</logger>
```

### 3. Add Monitoring View
Create a view to detect the issue in real-time:

```sql
CREATE OR REPLACE VIEW vw_FoldersWithoutDocuments AS
SELECT
    fs.Id,
    fs.NodeId,
    fs.Name,
    fs.Status,
    fs.UpdatedAt,
    ROUND((SYSTIMESTAMP - fs.UpdatedAt) * 24) AS HoursSinceProcessed
FROM FolderStaging fs
WHERE fs.Status = 'PROCESSED'
  AND NOT EXISTS (
      SELECT 1 FROM DocStaging ds WHERE ds.ParentId = fs.NodeId
  );

-- Query it
SELECT * FROM vw_FoldersWithoutDocuments
WHERE HoursSinceProcessed < 24;
```

## 📞 Next Steps

1. **Run diagnostic queries** from `SQL/DEBUG_FolderStatus.sql`
2. **Analyze results** to understand the scope
3. **Check application logs** for errors during the problem period
4. **Reset problem folders** using Option 1 or Option 3
5. **Monitor reprocessing** with enhanced logging
6. **Verify fix** using verification queries

If issues persist after reset and reprocessing with enhanced logging, check:
- Alfresco API health (timeout issues?)
- Network connectivity
- Database constraints (unique violations?)
- Memory issues (OutOfMemoryException?)
