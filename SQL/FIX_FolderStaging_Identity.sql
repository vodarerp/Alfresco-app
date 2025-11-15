-- =============================================================================
-- FIX: Recreate FolderStaging table with proper IDENTITY column
-- =============================================================================
-- WARNING: This script will TRUNCATE and recreate the FolderStaging table!
-- IMPORTANT: Backup your data before running this script!
-- =============================================================================
-- Problem: Id column is not IDENTITY or has NULL values
-- Solution: Drop and recreate table with proper schema
-- =============================================================================

USE [AlfrescoMigration]
GO

PRINT '=============================================================================';
PRINT 'WARNING: This will TRUNCATE and recreate FolderStaging table!';
PRINT 'Press Ctrl+C to cancel, or wait 10 seconds to continue...';
PRINT '=============================================================================';
WAITFOR DELAY '00:00:10';

-- =============================================================================
-- STEP 1: Backup existing data (optional - comment out if not needed)
-- =============================================================================
PRINT 'STEP 1: Creating backup table...';

IF OBJECT_ID('dbo.FolderStaging_BACKUP', 'U') IS NOT NULL
    DROP TABLE dbo.FolderStaging_BACKUP;

SELECT * INTO dbo.FolderStaging_BACKUP
FROM dbo.FolderStaging;

PRINT 'Backup created: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows backed up';

-- =============================================================================
-- STEP 2: Drop existing table
-- =============================================================================
PRINT 'STEP 2: Dropping existing FolderStaging table...';

DROP TABLE IF EXISTS dbo.FolderStaging;

PRINT 'Table dropped successfully';

-- =============================================================================
-- STEP 3: Recreate table with proper IDENTITY column
-- =============================================================================
PRINT 'STEP 3: Creating FolderStaging table with proper schema...';

CREATE TABLE dbo.FolderStaging (
    -- Primary key - IDENTITY to auto-generate values
    Id                              BIGINT IDENTITY(1,1) NOT NULL,

    -- Basic folder info
    NodeId                          NVARCHAR(100) NULL,
    ParentId                        NVARCHAR(100) NULL,
    Name                            NVARCHAR(500) NULL,

    -- Migration status
    Status                          NVARCHAR(20) NOT NULL,  -- NEW, READY, IN_PROGRESS, DONE, ERROR

    -- Destination folder info
    DestFolderId                    NVARCHAR(100) NULL,
    DossierDestFolderId             NVARCHAR(100) NULL,

    -- Audit timestamps
    CreatedAt                       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt                       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    -- ========== EXTENDED FIELDS FOR MIGRATION ==========

    -- Client info
    ClientType                      NVARCHAR(50) NULL,
    CoreId                          NVARCHAR(50) NULL,
    ClientName                      NVARCHAR(500) NULL,
    MbrJmbg                         NVARCHAR(50) NULL,

    -- Product and contract info
    ProductType                     NVARCHAR(50) NULL,
    ContractNumber                  NVARCHAR(100) NULL,
    Batch                           NVARCHAR(100) NULL,
    Source                          NVARCHAR(100) NULL,
    UniqueIdentifier                NVARCHAR(200) NULL,

    -- Process date
    ProcessDate                     DATETIME2 NULL,

    -- Client classification (from ClientAPI)
    Residency                       NVARCHAR(50) NULL,
    Segment                         NVARCHAR(50) NULL,
    ClientSubtype                   NVARCHAR(50) NULL,
    Staff                           NVARCHAR(50) NULL,

    -- Organizational info (from ClientAPI)
    OpuUser                         NVARCHAR(100) NULL,
    OpuRealization                  NVARCHAR(100) NULL,
    Barclex                         NVARCHAR(100) NULL,
    Collaborator                    NVARCHAR(200) NULL,

    -- BarCLEX properties
    BarCLEXName                     NVARCHAR(255) NULL,
    BarCLEXOpu                      NVARCHAR(100) NULL,
    BarCLEXGroupName                NVARCHAR(255) NULL,
    BarCLEXGroupCode                NVARCHAR(100) NULL,
    BarCLEXCode                     NVARCHAR(100) NULL,

    -- Audit fields
    Creator                         NVARCHAR(200) NULL,
    ArchivedAt                      DATETIME2 NULL,

    -- Dossier type detection
    TipDosijea                      NVARCHAR(200) NULL,
    TargetDossierType               NVARCHAR(50) NULL,

    -- Client segment
    ClientSegment                   NVARCHAR(50) NULL,

    -- Error tracking
    RetryCount                      INT NOT NULL DEFAULT 0,
    Error                           NVARCHAR(MAX) NULL,

    -- Primary Key Constraint
    CONSTRAINT [PK_FolderStaging] PRIMARY KEY CLUSTERED ([Id] ASC)
);

PRINT 'Table created successfully';

-- =============================================================================
-- STEP 4: Create indexes
-- =============================================================================
PRINT 'STEP 4: Creating indexes...';

-- Index for Status filtering (most common query)
CREATE NONCLUSTERED INDEX [IX_FolderStaging_Status]
    ON dbo.FolderStaging([Status])
    INCLUDE ([Id], [RetryCount]);

-- Index for NodeId lookups
CREATE NONCLUSTERED INDEX [IX_FolderStaging_NodeId]
    ON dbo.FolderStaging([NodeId]);

-- Index for ParentId
CREATE NONCLUSTERED INDEX [IX_FolderStaging_ParentId]
    ON dbo.FolderStaging([ParentId])
    WHERE ParentId IS NOT NULL;

-- Index for UniqueIdentifier
CREATE NONCLUSTERED INDEX [IX_FolderStaging_UniqueIdentifier]
    ON dbo.FolderStaging([UniqueIdentifier])
    WHERE UniqueIdentifier IS NOT NULL;

-- Index for CoreId (client tracking)
CREATE NONCLUSTERED INDEX [IX_FolderStaging_CoreId]
    ON dbo.FolderStaging([CoreId])
    WHERE CoreId IS NOT NULL;

-- Index for DossierDestFolderId
CREATE NONCLUSTERED INDEX [IX_FolderStaging_DossierDestFolderId]
    ON dbo.FolderStaging([DossierDestFolderId])
    WHERE DossierDestFolderId IS NOT NULL;

PRINT 'All indexes created successfully';

-- =============================================================================
-- STEP 5: Verify schema
-- =============================================================================
PRINT 'STEP 5: Verifying schema...';

SELECT
    c.name AS ColumnName,
    c.is_identity AS IsIdentity,
    c.is_nullable AS IsNullable
FROM sys.columns c
WHERE c.object_id = OBJECT_ID('dbo.FolderStaging')
  AND c.name = 'Id';

-- =============================================================================
-- STEP 6: Restore data (if backup was created and you want to restore)
-- =============================================================================
/*
PRINT 'STEP 6: Restoring data from backup...';

SET IDENTITY_INSERT dbo.FolderStaging ON;

INSERT INTO dbo.FolderStaging (
    Id, NodeId, ParentId, Name, Status, DestFolderId, DossierDestFolderId,
    CreatedAt, UpdatedAt, ClientType, CoreId, ClientName, MbrJmbg,
    ProductType, ContractNumber, Batch, Source, UniqueIdentifier, ProcessDate,
    Residency, Segment, ClientSubtype, Staff, OpuUser, OpuRealization,
    Barclex, Collaborator, BarCLEXName, BarCLEXOpu, BarCLEXGroupName,
    BarCLEXGroupCode, BarCLEXCode, Creator, ArchivedAt, TipDosijea,
    TargetDossierType, ClientSegment, RetryCount, Error
)
SELECT
    Id, NodeId, ParentId, Name, Status, DestFolderId, DossierDestFolderId,
    CreatedAt, UpdatedAt, ClientType, CoreId, ClientName, MbrJmbg,
    ProductType, ContractNumber, Batch, Source, UniqueIdentifier, ProcessDate,
    Residency, Segment, ClientSubtype, Staff, OpuUser, OpuRealization,
    Barclex, Collaborator, BarCLEXName, BarCLEXOpu, BarCLEXGroupName,
    BarCLEXGroupCode, BarCLEXCode, Creator, ArchivedAt, TipDosijea,
    TargetDossierType, ClientSegment, ISNULL(RetryCount, 0), Error
FROM dbo.FolderStaging_BACKUP
WHERE Id IS NOT NULL  -- Only restore rows with valid Id
ORDER BY Id;

SET IDENTITY_INSERT dbo.FolderStaging OFF;

PRINT 'Data restored: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';
*/

PRINT '';
PRINT '=============================================================================';
PRINT 'FolderStaging table recreated successfully!';
PRINT 'Next steps:';
PRINT '  1. Run DIAGNOSTIC_FolderStaging_Schema.sql to verify schema';
PRINT '  2. If you need to restore data, uncomment STEP 6 above';
PRINT '  3. Test the application with new schema';
PRINT '=============================================================================';
