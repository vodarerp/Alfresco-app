-- =============================================================================
-- DIAGNOSTIC: Check FolderStaging Table Schema
-- =============================================================================
-- Purpose: Diagnose the NULL Id insertion error
-- Error: "Cannot insert duplicate key row with unique index 'ix_folderstaging_id'.
--         The duplicate key value is (<NULL>)"
-- =============================================================================

PRINT '=============================================================================';
PRINT 'STEP 1: Check if Id column is IDENTITY';
PRINT '=============================================================================';

SELECT
    c.name AS ColumnName,
    t.name AS DataType,
    c.max_length AS MaxLength,
    c.is_nullable AS IsNullable,
    c.is_identity AS IsIdentity,
    ic.seed_value AS IdentitySeed,
    ic.increment_value AS IdentityIncrement
FROM sys.columns c
INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
LEFT JOIN sys.identity_columns ic ON c.object_id = ic.object_id AND c.column_id = ic.column_id
WHERE c.object_id = OBJECT_ID('dbo.FolderStaging')
  AND c.name = 'Id';

PRINT '';
PRINT '=============================================================================';
PRINT 'STEP 2: Check all indexes on FolderStaging table';
PRINT '=============================================================================';

SELECT
    i.name AS IndexName,
    i.type_desc AS IndexType,
    i.is_unique AS IsUnique,
    i.is_primary_key AS IsPrimaryKey,
    COL_NAME(ic.object_id, ic.column_id) AS ColumnName
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE i.object_id = OBJECT_ID('dbo.FolderStaging')
ORDER BY i.name, ic.key_ordinal;

PRINT '';
PRINT '=============================================================================';
PRINT 'STEP 3: Check for NULL values in Id column';
PRINT '=============================================================================';

SELECT COUNT(*) AS NullIdCount
FROM dbo.FolderStaging
WHERE Id IS NULL;

PRINT '';
PRINT '=============================================================================';
PRINT 'STEP 4: Check for duplicate NULL values (if any)';
PRINT '=============================================================================';

SELECT TOP 10
    Id, NodeId, Name, Status, CreatedAt
FROM dbo.FolderStaging
WHERE Id IS NULL
ORDER BY CreatedAt DESC;

PRINT '';
PRINT '=============================================================================';
PRINT 'STEP 5: Check current max Id value';
PRINT '=============================================================================';

SELECT
    MAX(Id) AS MaxId,
    MIN(Id) AS MinId,
    COUNT(*) AS TotalRows
FROM dbo.FolderStaging;

PRINT '';
PRINT '=============================================================================';
PRINT 'STEP 6: Check table constraints';
PRINT '=============================================================================';

SELECT
    CONSTRAINT_NAME,
    CONSTRAINT_TYPE
FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
WHERE TABLE_NAME = 'FolderStaging';

PRINT '';
PRINT '=============================================================================';
PRINT 'DIAGNOSTIC COMPLETE';
PRINT '=============================================================================';
PRINT '';
PRINT 'Expected Results:';
PRINT '  - Id column should have IsIdentity = 1';
PRINT '  - Id column should have IsNullable = 0';
PRINT '  - NullIdCount should be 0';
PRINT '  - There should be a PRIMARY KEY constraint on Id';
PRINT '';
PRINT 'If any of these are different, the database schema needs to be fixed!';
PRINT '=============================================================================';
