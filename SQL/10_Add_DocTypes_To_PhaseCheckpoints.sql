-- ============================================================================
-- Add DocTypes Column to PhaseCheckpoints Table - SQL SERVER
-- ============================================================================
-- Purpose: Adds DocTypes column to track document types used in
--          MigrationByDocument mode. Used to detect configuration changes
--          and reset checkpoint if DocTypes have changed.
-- ============================================================================
-- Verzija: 1.0
-- Datum: 2025-12-07
-- ============================================================================

USE [AlfrescoMigration]
GO

PRINT '============================================================================';
PRINT 'Adding DocTypes column to PhaseCheckpoints table...';
PRINT '============================================================================';

-- ============================================================================
-- 1. ADD COLUMN IF NOT EXISTS
-- ============================================================================

IF NOT EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.PhaseCheckpoints')
    AND name = 'DocTypes'
)
BEGIN
    PRINT 'Adding DocTypes column...';

    ALTER TABLE dbo.PhaseCheckpoints
    ADD DocTypes NVARCHAR(MAX) NULL;

    PRINT 'DocTypes column added successfully!';
END
ELSE
BEGIN
    PRINT 'DocTypes column already exists, skipping...';
END
GO

-- ============================================================================
-- 2. ADD COLUMN DESCRIPTION
-- ============================================================================

IF EXISTS (
    SELECT * FROM sys.columns
    WHERE object_id = OBJECT_ID(N'dbo.PhaseCheckpoints')
    AND name = 'DocTypes'
)
BEGIN
    PRINT 'Adding column description...';

    EXEC sp_addextendedproperty
        @name = N'MS_Description',
        @value = N'Document types used for DocumentSearch (MigrationByDocument mode). Stored as comma-separated string (e.g., "00756,00824,00125"). Used to detect configuration changes and reset checkpoint if needed.',
        @level0type = N'SCHEMA', @level0name = 'dbo',
        @level1type = N'TABLE',  @level1name = 'PhaseCheckpoints',
        @level2type = N'COLUMN', @level2name = 'DocTypes';

    PRINT 'Column description added successfully!';
END
GO

-- ============================================================================
-- 3. FINALNI REPORT
-- ============================================================================

PRINT '';
PRINT '============================================================================';
PRINT 'DocTypes COLUMN SUCCESSFULLY ADDED!';
PRINT '============================================================================';
PRINT '';
PRINT 'Added:';
PRINT '  - DocTypes NVARCHAR(MAX) NULL column';
PRINT '  - Column description for documentation';
PRINT '';
PRINT 'Usage:';
PRINT '  - Stores comma-separated DocTypes (e.g., "00756,00824")';
PRINT '  - Used in MigrationByDocument mode to detect config changes';
PRINT '  - If DocTypes change, checkpoint is reset automatically';
PRINT '';
PRINT 'Verification query:';
PRINT '  SELECT Phase, Status, DocTypes FROM PhaseCheckpoints;';
PRINT '';
PRINT '============================================================================';
GO

-- ============================================================================
-- 4. VERIFICATION QUERY
-- ============================================================================

SELECT
    Phase,
    CASE Phase
        WHEN 1 THEN 'FolderDiscovery/DocumentSearch'
        WHEN 2 THEN 'DocumentDiscovery'
        WHEN 3 THEN 'FolderPreparation'
        WHEN 4 THEN 'Move'
    END AS PhaseName,
    CASE Status
        WHEN 0 THEN 'NotStarted'
        WHEN 1 THEN 'InProgress'
        WHEN 2 THEN 'Completed'
        WHEN 3 THEN 'Failed'
    END AS StatusName,
    DocTypes,
    StartedAt,
    CompletedAt,
    TotalProcessed
FROM PhaseCheckpoints
ORDER BY Phase;
GO
