# NULL Id Insertion Error - Analysis and Solution

## Error Message
```
Microsoft.Data.SqlClient.SqlException (0x80131904): Cannot insert duplicate key row in object 'dbo.FolderStaging' with unique index 'ix_folderstaging_id'. The duplicate key value is (<NULL>).
```

**Stack Trace:**
- `SqlServerRepository.InsertManyAsync` (line 97)
- `FolderDiscoveryService.InsertFoldersAsync` (line 613)

---

## Root Cause Analysis

### 1. **Schema Mismatch Detected**

The error indicates a **unique index named `ix_folderstaging_id`** exists on the FolderStaging table, but this index **does NOT exist** in any of the SQL scripts:

- `SQL/00_CreateAllTables_SqlServer_FINAL.sql` - Uses index names like `idx_folderstaging_*`
- `SqlServer.Infrastructure/Scripts/01_CreateTables.sql` - Uses index names like `IX_FolderStaging_*`

**Conclusion:** The actual database schema differs from the scripts in the codebase.

### 2. **NULL Values in Id Column**

The error message explicitly states: **"The duplicate key value is (<NULL>)"**

This means:
- There's already at least one row with `Id = NULL` in the FolderStaging table
- The application is trying to insert another row with `Id = NULL`
- A unique index is preventing the second NULL from being inserted

### 3. **Expected vs. Actual Schema**

**Expected Schema (from C# model):**
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
   - Someone manually altered the table using SSMS or SQL scripts
   - Added a unique index `ix_folderstaging_id` that wasn't in the scripts
   - Possibly modified `Id` column to be nullable

3. **IDENTITY Column Not Working**
   - `Id` column exists as IDENTITY in schema
   - But something is explicitly setting it to NULL during insert
   - Could be a Dapper/reflection issue detecting the `[DatabaseGenerated]` attribute

---

## Investigation Steps

### Step 1: Run Diagnostic Script
```bash
# Execute the diagnostic SQL script to check actual database schema
```

Run: `SQL/DIAGNOSTIC_FolderStaging_Schema.sql`

This will show:
- Whether `Id` is configured as IDENTITY
- All indexes on the table (including the mysterious `ix_folderstaging_id`)
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

**Verification needed:** Ensure `FolderStaging.Id` has the attribute at runtime.

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
```

---

## Solutions

### Solution 1: Quick Fix - Truncate and Reset (Development Only)

**WARNING:** This deletes all data!

```sql
USE [AlfrescoMigration]
GO

TRUNCATE TABLE dbo.FolderStaging;

-- Reset identity seed
DBCC CHECKIDENT ('dbo.FolderStaging', RESEED, 0);
```

### Solution 2: Recreate Table with Proper Schema (Recommended)

Run: `SQL/FIX_FolderStaging_Identity.sql`

This script will:
1. Backup existing data to `FolderStaging_BACKUP`
2. Drop and recreate the table with proper IDENTITY configuration
3. Optionally restore data (excluding rows with NULL Id)

### Solution 3: Manual Schema Fix (If You Want to Keep Data)

If you have important data and want to fix the schema:

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

---

## After AFTS Refactoring

The error appeared **after** the AFTS refactoring was committed. The refactoring changed:

1. **Query Language:** CMIS → AFTS
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

---

## Next Steps

1. **Run Diagnostic:** Execute `DIAGNOSTIC_FolderStaging_Schema.sql` and share results
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
   ```

2. **Add Database Constraint:**
   ```sql
   ALTER TABLE dbo.FolderStaging
   ADD CONSTRAINT CHK_FolderStaging_NodeId_NotNull CHECK (NodeId IS NOT NULL);
   ```

3. **Add Unit Tests** for:
   - `ToFolderStagingInsert()` mapping
   - `InsertManyAsync()` with IDENTITY columns
   - AFTS query response parsing

---

## Files Created

1. `SQL/DIAGNOSTIC_FolderStaging_Schema.sql` - Schema verification queries
2. `SQL/FIX_FolderStaging_Identity.sql` - Table recreation script
3. `SQL/NULL_ID_ERROR_ANALYSIS.md` - This analysis document
