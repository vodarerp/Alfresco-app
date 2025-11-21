-- =============================================
-- Migration Script: Add DestinationFolderId to DocStaging
-- Purpose: Store actual Alfresco folder UUID for direct move operations
-- Author: Refactoring - MoveService Simplification
-- Date: 2025-01-21
-- =============================================

USE [AlfrescoMigration];
GO

-- Check if column already exists
IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'DocStaging'
      AND COLUMN_NAME = 'DestinationFolderId'
)
BEGIN
    PRINT 'Adding DestinationFolderId column to DocStaging table...';

    ALTER TABLE dbo.DocStaging
    ADD DestinationFolderId NVARCHAR(100) NULL;

    PRINT 'Column DestinationFolderId added successfully.';
END
ELSE
BEGIN
    PRINT 'Column DestinationFolderId already exists. Skipping.';
END
GO

-- Create index for performance
IF NOT EXISTS (
    SELECT 1
    FROM sys.indexes
    WHERE name = 'IX_DocStaging_DestinationFolderId'
      AND object_id = OBJECT_ID('dbo.DocStaging')
)
BEGIN
    PRINT 'Creating index IX_DocStaging_DestinationFolderId...';

    CREATE NONCLUSTERED INDEX IX_DocStaging_DestinationFolderId
    ON dbo.DocStaging(DestinationFolderId)
    INCLUDE (Id, NodeId, Status)
    WHERE DestinationFolderId IS NOT NULL;

    PRINT 'Index created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_DocStaging_DestinationFolderId already exists. Skipping.';
END
GO

-- Verify the changes
PRINT '';
PRINT 'Verification:';
SELECT
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'DocStaging'
  AND COLUMN_NAME = 'DestinationFolderId';

PRINT '';
PRINT 'Index information:';
SELECT
    i.name AS IndexName,
    i.type_desc AS IndexType,
    i.is_unique AS IsUnique,
    STRING_AGG(c.name, ', ') AS Columns
FROM sys.indexes i
INNER JOIN sys.index_columns ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
INNER JOIN sys.columns c ON ic.object_id = c.object_id AND ic.column_id = c.column_id
WHERE i.object_id = OBJECT_ID('dbo.DocStaging')
  AND i.name = 'IX_DocStaging_DestinationFolderId'
GROUP BY i.name, i.type_desc, i.is_unique;

PRINT '';
PRINT 'Migration completed successfully!';
GO

-- Sample verification query
PRINT '';
PRINT 'Sample data check (should show NULL for existing records):';
SELECT TOP 5
    Id,
    NodeId,
    DossierDestFolderId,
    DestinationFolderId,  -- New column (will be NULL initially)
    Status
FROM dbo.DocStaging
ORDER BY Id DESC;
GO
