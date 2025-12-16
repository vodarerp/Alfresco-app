-- =============================================
-- Migration Script: Add DossierDestFolderIsCreated column to DocStaging
-- Purpose: Track whether DossierDestFolder was created during migration or already existed
-- Date: 2025-12-16
-- =============================================

USE [AlfrescoMigration]
GO

-- Check if column already exists
IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'DocStaging'
    AND COLUMN_NAME = 'DossierDestFolderIsCreated'
)
BEGIN
    PRINT 'Adding DossierDestFolderIsCreated column to DocStaging table...'

    ALTER TABLE DocStaging
    ADD DossierDestFolderIsCreated BIT NOT NULL DEFAULT 0;

    PRINT 'Column DossierDestFolderIsCreated added successfully.'
END
ELSE
BEGIN
    PRINT 'Column DossierDestFolderIsCreated already exists. Skipping...'
END
GO

-- Verify the column was added
IF EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'DocStaging'
    AND COLUMN_NAME = 'DossierDestFolderIsCreated'
)
BEGIN
    PRINT 'Verification: DossierDestFolderIsCreated column exists in DocStaging table.'

    -- Display column info
    SELECT
        COLUMN_NAME,
        DATA_TYPE,
        IS_NULLABLE,
        COLUMN_DEFAULT
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'DocStaging'
    AND COLUMN_NAME = 'DossierDestFolderIsCreated';
END
ELSE
BEGIN
    PRINT 'ERROR: DossierDestFolderIsCreated column was not added!'
END
GO

-- Optional: Add comment/description to column (Extended Properties)
IF NOT EXISTS (
    SELECT 1
    FROM sys.extended_properties
    WHERE major_id = OBJECT_ID('DocStaging')
    AND minor_id = (SELECT column_id FROM sys.columns WHERE object_id = OBJECT_ID('DocStaging') AND name = 'DossierDestFolderIsCreated')
    AND name = 'MS_Description'
)
BEGIN
    EXEC sys.sp_addextendedproperty
        @name = N'MS_Description',
        @value = N'Flag indicating whether the DossierDestFolder was created during this migration run. TRUE = folder was created (did not exist on Alfresco before), FALSE = folder already existed on Alfresco. Used for testing/verification purposes to identify newly created folders.',
        @level0type = N'SCHEMA', @level0name = 'dbo',
        @level1type = N'TABLE',  @level1name = 'DocStaging',
        @level2type = N'COLUMN', @level2name = 'DossierDestFolderIsCreated';

    PRINT 'Extended property (description) added to DossierDestFolderIsCreated column.'
END
GO

PRINT '========================================='
PRINT 'Migration completed successfully!'
PRINT '========================================='
GO
