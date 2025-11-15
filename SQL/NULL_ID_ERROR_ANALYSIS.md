# NULL Id Insertion Error - Analysis and Solution

## Error Messages

### FolderStaging Table
```
Microsoft.Data.SqlClient.SqlException (0x80131904): Cannot insert duplicate key row in object 'dbo.FolderStaging' with unique index 'ix_folderstaging_id'. The duplicate key value is (<NULL>).
```

**Stack Trace:**
- `SqlServerRepository.InsertManyAsync` (line 97)
- `FolderDiscoveryService.InsertFoldersAsync` (line 613)

### DocStaging Table
```
Microsoft.Data.SqlClient.SqlException (0x80131904): Cannot insert duplicate key row in object 'dbo.DocStaging' with unique index 'ix_docstaging_id'. The duplicate key value is (<NULL>).
```

**Stack Trace:**
- `SqlServerRepository.InsertManyAsync` (line 97)
- `DocumentDiscoveryService.InsertDocumentsAsync` (similar location)

---

## Root Cause Analysis

### 1. **Schema Mismatch Detected**

The errors indicate **unique indexes** exist on both staging tables that **DO NOT exist** in any of the SQL scripts:

- `ix_folderstaging_id` on FolderStaging table
- `ix_docstaging_id` on DocStaging table

These indexes are referenced in the errors but missing from:
- `SQL/00_CreateAllTables_SqlServer_FINAL.sql` - Uses index names like `idx_*staging_*`
- `SqlServer.Infrastructure/Scripts/01_CreateTables.sql` - Uses index names like `IX_*Staging_*`

**Conclusion:** The actual database schema differs from the scripts in the codebase.

### 2. **NULL Values in Id Column**

The error message explicitly states: **"The duplicate key value is (<NULL>)"**

This means:
- There's already at least one row with `Id = NULL` in both staging tables
- The application is trying to insert another row with `Id = NULL`
- Unique indexes are preventing the second NULL from being inserted

### 3. **Expected vs. Actual Schema**

**Expected Schema (from C# models):**

Both `FolderStaging.cs` (line 12-13) and `DocStaging.cs` (line 14-15):
```csharp
[DatabaseGenerated(DatabaseGeneratedOption.Identity)]
public long Id { get; set; }
```

**Expected SQL:**
```sql
Id BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY
```

**Actual Schema:** Unknown - needs verification

If `Id` is NOT configured as IDENTITY in the actual database:
- Default value for `long` in C# is `0`
- When not explicitly set, `Id` might be inserted as `0` or `NULL`
- This would cause duplicate key violations

---

## How This Happened

### Possible Scenarios:

1. **Old Schema Still in Database**
   - Database was created with an early version of the schema
   - Schema where `Id` was NULLABLE and had a manual unique index
   - Code was updated but database wasn't migrated

2. **Manual Database Modifications**
   - Someone manually altered the tables using SSMS or SQL scripts
   - Added unique indexes `ix_*staging_id` that weren't in the scripts
   - Possibly modified `Id` columns to be nullable

3. **IDENTITY Column Not Working**
   - `Id` column exists as IDENTITY in schema
   - But something is explicitly setting it to NULL during insert
   - Could be a Dapper/reflection issue detecting the `[DatabaseGenerated]` attribute

---

## Investigation Steps

### Step 1: Run Diagnostic Scripts
```bash
# Execute the diagnostic SQL scripts to check actual database schema
```

**For FolderStaging:** Run `SQL/DIAGNOSTIC_FolderStaging_Schema.sql`
**For DocStaging:** Run `SQL/DIAGNOSTIC_DocStaging_Schema.sql`

These will show:
- Whether `Id` is configured as IDENTITY
- All indexes on the tables (including the mysterious `ix_*staging_id` indexes)
- How many rows have `Id = NULL`
- Current min/max Id values

### Step 2: Check SqlServerHelpers Detection

The `SqlServerHelpers<T>.TableProps` uses reflection to detect IDENTITY columns:

```csharp
var identity = p.GetCustomAttribute<DatabaseGeneratedAttribute>()?.DatabaseGeneratedOption == DatabaseGeneratedOption.Identity;
```

Then `InsertManyAsync` filters them out:
```csharp
var columns = SqlServerHelpers<T>.TableProps.Where(o => !o.IsIdentity).ToArray();
```

**Verification needed:** Ensure `FolderStaging.Id` and `DocStaging.Id` have the attribute at runtime.

### Step 3: Check Insert SQL Statement

Add logging to see the actual SQL being generated:

```csharp
// In SqlServerRepository.InsertManyAsync line 69
string sql = $"INSERT INTO {TableName} ({colNames}) VALUES ({paramNames})";
Console.WriteLine($"DEBUG SQL: {sql}");  // Add this line
```

Expected output (Id should NOT be in the column list):
```sql
INSERT INTO FolderStaging (NodeId, ParentId, Name, Status, CreatedAt, UpdatedAt, ...) VALUES (@NodeId, @ParentId, ...)
INSERT INTO DocStaging (NodeId, Name, IsFolder, IsFile, NodeType, ParentId, ...) VALUES (@NodeId, @Name, ...)
```

---

## Solutions

### Solution 1: Quick Fix - Truncate and Reset (Development Only)

**WARNING:** This deletes all data!

```sql
USE [AlfrescoMigration]
GO

-- Truncate both tables
TRUNCATE TABLE dbo.FolderStaging;
TRUNCATE TABLE dbo.DocStaging;

-- Reset identity seeds
DBCC CHECKIDENT ('dbo.FolderStaging', RESEED, 0);
DBCC CHECKIDENT ('dbo.DocStaging', RESEED, 0);
```

### Solution 2: Recreate Tables with Proper Schema (Recommended)

**For FolderStaging:** Run `SQL/FIX_FolderStaging_Identity.sql`
**For DocStaging:** Run `SQL/FIX_DocStaging_Identity.sql`

These scripts will:
1. Backup existing data to `*Staging_BACKUP` tables
2. Drop and recreate the tables with proper IDENTITY configuration
3. Recreate all indexes with correct names
4. Optionally restore data (excluding rows with NULL Id)

### Solution 3: Manual Schema Fix (If You Want to Keep Data)

If you have important data and want to fix the schema:

**For FolderStaging:**
```sql
-- 1. Add temporary column
ALTER TABLE dbo.FolderStaging ADD Id_New BIGINT IDENTITY(1,1);

-- 2. Drop old Id column and unique index
DROP INDEX ix_folderstaging_id ON dbo.FolderStaging;  -- If exists
ALTER TABLE dbo.FolderStaging DROP COLUMN Id;

-- 3. Rename new column
EXEC sp_rename 'dbo.FolderStaging.Id_New', 'Id', 'COLUMN';

-- 4. Add primary key constraint
ALTER TABLE dbo.FolderStaging ADD CONSTRAINT PK_FolderStaging PRIMARY KEY CLUSTERED (Id);
```

**For DocStaging:**
```sql
-- 1. Add temporary column
ALTER TABLE dbo.DocStaging ADD Id_New BIGINT IDENTITY(1,1);

-- 2. Drop old Id column and unique index
DROP INDEX ix_docstaging_id ON dbo.DocStaging;  -- If exists
ALTER TABLE dbo.DocStaging DROP COLUMN Id;

-- 3. Rename new column
EXEC sp_rename 'dbo.DocStaging.Id_New', 'Id', 'COLUMN';

-- 4. Add primary key constraint
ALTER TABLE dbo.DocStaging ADD CONSTRAINT PK_DocStaging PRIMARY KEY CLUSTERED (Id);
```

---

## After AFTS Refactoring

The error appeared **after** the AFTS refactoring was committed. The refactoring changed:

1. **Query Language:** CMIS → AFTS (for FolderReader)
2. **Cursor Model:** Added `LastObjectName` to `FolderSeekCursor`
3. **Query Building:** Different field references (e.g., `cm:created`, `cm:name`)

**Potential Impact:**
- AFTS query response structure might differ from CMIS
- `Entry.Id` field might be missing or NULL in AFTS responses
- Need to verify that `page.Items` (ListEntry objects) have valid `Entry.Id` values

### Verification:

Add logging in FolderDiscoveryService.cs before insertion:

```csharp
// Line 188-192
await EnrichFoldersWithClientDataAsync(page.Items, ct).ConfigureAwait(false);

// ADD THIS:
foreach (var item in page.Items)
{
    _fileLogger.LogDebug("Folder Entry: Id={Id}, Name={Name}", item.Entry?.Id, item.Entry?.Name);
    if (string.IsNullOrEmpty(item.Entry?.Id))
    {
        _fileLogger.LogError("ERROR: Entry.Id is NULL or empty for folder: {Name}", item.Entry?.Name);
    }
}

var foldersToInsert = page.Items.ToList().ToFolderStagingListInsert();
```

Similar logging should be added for DocumentDiscoveryService.

---

## Next Steps

1. **Run Diagnostics:** Execute both `DIAGNOSTIC_*Staging_Schema.sql` scripts and share results
2. **Check Logs:** Look for any entries with NULL or empty `Entry.Id` values
3. **Decide on Fix:**
   - If testing/dev: Use Solution 1 (truncate)
   - If production with data: Use Solution 3 (manual fix)
   - If clean slate needed: Use Solution 2 (recreate)

4. **Test AFTS Changes:** Verify that AFTS queries return proper `id` fields in responses

---

## Prevention

To prevent this in the future:

1. **Add Validation** before insertion:
   ```csharp
   // In FolderDiscoveryService before InsertFoldersAsync
   var invalidFolders = foldersToInsert.Where(f => string.IsNullOrEmpty(f.NodeId)).ToList();
   if (invalidFolders.Any())
   {
       _fileLogger.LogError("Found {Count} folders with NULL NodeId", invalidFolders.Count);
       // Skip or handle them
   }

   // Similar for DocumentDiscoveryService
   var invalidDocs = docsToInsert.Where(d => string.IsNullOrEmpty(d.NodeId)).ToList();
   if (invalidDocs.Any())
   {
       _fileLogger.LogError("Found {Count} documents with NULL NodeId", invalidDocs.Count);
       // Skip or handle them
   }
   ```

2. **Add Database Constraints:**
   ```sql
   ALTER TABLE dbo.FolderStaging
   ADD CONSTRAINT CHK_FolderStaging_NodeId_NotNull CHECK (NodeId IS NOT NULL);

   ALTER TABLE dbo.DocStaging
   ADD CONSTRAINT CHK_DocStaging_NodeId_NotNull CHECK (NodeId IS NOT NULL AND NodeId != '');
   ```

3. **Add Unit Tests** for:
   - `ToFolderStagingInsert()` mapping
   - `ToDocStagingInsert()` mapping
   - `InsertManyAsync()` with IDENTITY columns
   - AFTS query response parsing

---

## Files Created

### Diagnostic Scripts
1. `SQL/DIAGNOSTIC_FolderStaging_Schema.sql` - FolderStaging schema verification queries
2. `SQL/DIAGNOSTIC_DocStaging_Schema.sql` - DocStaging schema verification queries

### Fix Scripts
3. `SQL/FIX_FolderStaging_Identity.sql` - FolderStaging table recreation script
4. `SQL/FIX_DocStaging_Identity.sql` - DocStaging table recreation script

### Analysis
5. `SQL/NULL_ID_ERROR_ANALYSIS.md` - This comprehensive analysis document (updated for both tables)
