-- =============================================
-- Migration Script: Add DossierDestFolderId Column
-- =============================================
-- Version: 1.0
-- Date: 2025-10-24
-- Description: Adds DossierDestFolderId column to FolderStaging table
--              to store the NodeId of the DOSSIER-{folderType} destination folder
-- =============================================

USE [AlfrescoMigration]
GO

-- Check if column already exists
IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[FolderStaging]')
    AND name = 'DossierDestFolderId'
)
BEGIN
    PRINT 'Adding DossierDestFolderId column to FolderStaging table...';

    -- Add the new column
    ALTER TABLE [dbo].[FolderStaging]
    ADD [DossierDestFolderId] NVARCHAR(255) NULL;

    -- Add index for performance
    CREATE NONCLUSTERED INDEX [IX_FolderStaging_DossierDestFolderId]
        ON [dbo].[FolderStaging] ([DossierDestFolderId])
        WHERE [DossierDestFolderId] IS NOT NULL;

    -- Add comment/description
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description',
        @value = N'NodeId of the DOSSIER-{folderType} destination folder (e.g., DOSSIER-PL, DOSSIER-FL). This is the parent folder under RootDestinationFolderId where target folders will be created. Populated by FolderDiscoveryService during initial discovery.',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE', @level1name = N'FolderStaging',
        @level2type = N'COLUMN', @level2name = N'DossierDestFolderId';

    PRINT 'DossierDestFolderId column added successfully!';
    PRINT 'Index [IX_FolderStaging_DossierDestFolderId] created successfully!';
END
ELSE
BEGIN
    PRINT 'Column DossierDestFolderId already exists in FolderStaging table.';
END
GO

-- =============================================
-- Verification Query
-- =============================================
PRINT '';
PRINT '========================================';
PRINT 'Verification:';
PRINT '========================================';

-- Show column details
SELECT
    c.name AS ColumnName,
    t.name AS DataType,
    c.max_length AS MaxLength,
    c.is_nullable AS IsNullable,
    CAST(ep.value AS NVARCHAR(500)) AS Description
FROM sys.columns c
INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
LEFT JOIN sys.extended_properties ep
    ON ep.major_id = c.object_id
    AND ep.minor_id = c.column_id
    AND ep.name = 'MS_Description'
WHERE c.object_id = OBJECT_ID(N'[dbo].[FolderStaging]')
AND c.name = 'DossierDestFolderId';

-- Show index details
SELECT
    i.name AS IndexName,
    i.type_desc AS IndexType,
    i.is_unique AS IsUnique
FROM sys.indexes i
WHERE i.object_id = OBJECT_ID(N'[dbo].[FolderStaging]')
AND i.name = 'IX_FolderStaging_DossierDestFolderId';

PRINT '';
PRINT '========================================';
PRINT 'Migration completed successfully!';
PRINT '========================================';
GO
