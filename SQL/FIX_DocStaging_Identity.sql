-- =============================================================================
-- FIX: Recreate DocStaging table with proper IDENTITY column
-- =============================================================================
-- WARNING: This script will TRUNCATE and recreate the DocStaging table!
-- IMPORTANT: Backup your data before running this script!
-- =============================================================================
-- Problem: Id column is not IDENTITY or has NULL values
-- Solution: Drop and recreate table with proper schema
-- =============================================================================

USE [AlfrescoMigration]
GO

PRINT '=============================================================================';
PRINT 'WARNING: This will TRUNCATE and recreate DocStaging table!';
PRINT 'Press Ctrl+C to cancel, or wait 10 seconds to continue...';
PRINT '=============================================================================';
WAITFOR DELAY '00:00:10';

-- =============================================================================
-- STEP 1: Backup existing data (optional - comment out if not needed)
-- =============================================================================
PRINT 'STEP 1: Creating backup table...';

IF OBJECT_ID('dbo.DocStaging_BACKUP', 'U') IS NOT NULL
    DROP TABLE dbo.DocStaging_BACKUP;

SELECT * INTO dbo.DocStaging_BACKUP
FROM dbo.DocStaging;

PRINT 'Backup created: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows backed up';

-- =============================================================================
-- STEP 2: Drop existing table
-- =============================================================================
PRINT 'STEP 2: Dropping existing DocStaging table...';

DROP TABLE IF EXISTS dbo.DocStaging;

PRINT 'Table dropped successfully';

-- =============================================================================
-- STEP 3: Recreate table with proper IDENTITY column
-- =============================================================================
PRINT 'STEP 3: Creating DocStaging table with proper schema...';

CREATE TABLE dbo.DocStaging (
    -- Primary key - IDENTITY to auto-generate values
    Id                              BIGINT IDENTITY(1,1) NOT NULL,

    -- Basic Alfresco node info
    NodeId                          NVARCHAR(100) NOT NULL,
    Name                            NVARCHAR(500) NOT NULL,
    IsFolder                        BIT NOT NULL DEFAULT 0,
    IsFile                          BIT NOT NULL DEFAULT 0,
    NodeType                        NVARCHAR(100) NOT NULL,
    ParentId                        NVARCHAR(100) NOT NULL,

    -- Migration paths
    FromPath                        NVARCHAR(1000) NOT NULL DEFAULT '',
    ToPath                          NVARCHAR(1000) NOT NULL DEFAULT '',

    -- Migration status
    Status                          NVARCHAR(20) NOT NULL,  -- NEW, READY, IN_PROGRESS, DONE, ERROR
    RetryCount                      INT NOT NULL DEFAULT 0,
    ErrorMsg                        NVARCHAR(MAX) NULL,

    -- Audit timestamps
    CreatedAt                       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt                       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    -- ========== EXTENDED FIELDS FOR MIGRATION ==========

    -- Document type and metadata
    DocumentType                    NVARCHAR(50) NULL,
    DocumentTypeMigration           NVARCHAR(50) NULL,
    Source                          NVARCHAR(100) NULL,
    IsActive                        BIT NOT NULL DEFAULT 1,
    CategoryCode                    NVARCHAR(50) NULL,
    CategoryName                    NVARCHAR(200) NULL,

    -- Original timestamps
    OriginalCreatedAt               DATETIME2 NULL,

    -- Client and contract info
    ContractNumber                  NVARCHAR(100) NULL,
    CoreId                          NVARCHAR(50) NULL,

    -- Document version and accounts
    Version                         DECIMAL(5,2) NOT NULL DEFAULT 1.0,
    AccountNumbers                  NVARCHAR(1000) NULL,

    -- Transformation flags
    RequiresTypeTransformation      BIT NOT NULL DEFAULT 0,
    FinalDocumentType               NVARCHAR(50) NULL,
    IsSigned                        BIT NOT NULL DEFAULT 0,

    -- DUT integration
    DutOfferId                      NVARCHAR(100) NULL,
    ProductType                     NVARCHAR(50) NULL,

    -- ========== FAZA 2: NOVA POLJA ZA MAPIRANJE ==========

    -- Document name mapping
    OriginalDocumentName            NVARCHAR(500) NULL,
    NewDocumentName                 NVARCHAR(500) NULL,

    -- Document code mapping
    OriginalDocumentCode            NVARCHAR(50) NULL,
    NewDocumentCode                 NVARCHAR(50) NULL,

    -- Dossier type detection
    TipDosijea                      NVARCHAR(200) NULL,
    TargetDossierType               INT NULL,

    -- Client segment
    ClientSegment                   NVARCHAR(50) NULL,

    -- Status mapping
    OldAlfrescoStatus               NVARCHAR(50) NULL,
    NewAlfrescoStatus               NVARCHAR(50) NULL,

    -- Migration flags
    WillReceiveMigrationSuffix      BIT NOT NULL DEFAULT 0,
    CodeWillChange                  BIT NOT NULL DEFAULT 0,

    -- Document description
    DocDescription                  NVARCHAR(500) NULL,

    -- Destination dossier folder
    DossierDestFolderId             NVARCHAR(200) NULL,

    -- Primary Key Constraint
    CONSTRAINT [PK_DocStaging] PRIMARY KEY CLUSTERED ([Id] ASC)
);

PRINT 'Table created successfully';

-- =============================================================================
-- STEP 4: Create indexes
-- =============================================================================
PRINT 'STEP 4: Creating indexes...';

-- Index for NodeId lookups
CREATE NONCLUSTERED INDEX [IX_DocStaging_NodeId]
    ON dbo.DocStaging([NodeId]);

-- Index for Status filtering (most common query)
CREATE NONCLUSTERED INDEX [IX_DocStaging_Status]
    ON dbo.DocStaging([Status])
    INCLUDE ([Id], [RetryCount]);

-- Index for ParentId (folder hierarchy)
CREATE NONCLUSTERED INDEX [IX_DocStaging_ParentId]
    ON dbo.DocStaging([ParentId]);

-- Index for CoreId (client tracking)
CREATE NONCLUSTERED INDEX [IX_DocStaging_CoreId]
    ON dbo.DocStaging([CoreId])
    WHERE CoreId IS NOT NULL;

-- Index for DocumentType
CREATE NONCLUSTERED INDEX [IX_DocStaging_DocumentType]
    ON dbo.DocStaging([DocumentType])
    WHERE DocumentType IS NOT NULL;

-- Index for TargetDossierType (migration routing)
CREATE NONCLUSTERED INDEX [IX_DocStaging_TargetDossierType]
    ON dbo.DocStaging([TargetDossierType])
    WHERE TargetDossierType IS NOT NULL;

-- Composite index for migration processing (Status + CreatedAt)
CREATE NONCLUSTERED INDEX [IX_DocStaging_Status_Created]
    ON dbo.DocStaging([Status], [CreatedAt]);

-- Index for DossierDestFolderId
CREATE NONCLUSTERED INDEX [IX_DocStaging_DossierDestFolderId]
    ON dbo.DocStaging([DossierDestFolderId])
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
WHERE c.object_id = OBJECT_ID('dbo.DocStaging')
  AND c.name = 'Id';

-- =============================================================================
-- STEP 6: Restore data (if backup was created and you want to restore)
-- =============================================================================
/*
PRINT 'STEP 6: Restoring data from backup...';

SET IDENTITY_INSERT dbo.DocStaging ON;

INSERT INTO dbo.DocStaging (
    Id, NodeId, Name, IsFolder, IsFile, NodeType, ParentId, FromPath, ToPath,
    Status, RetryCount, ErrorMsg, CreatedAt, UpdatedAt,
    DocumentType, DocumentTypeMigration, Source, IsActive, CategoryCode, CategoryName,
    OriginalCreatedAt, ContractNumber, CoreId, Version, AccountNumbers,
    RequiresTypeTransformation, FinalDocumentType, IsSigned, DutOfferId, ProductType,
    OriginalDocumentName, NewDocumentName, OriginalDocumentCode, NewDocumentCode,
    TipDosijea, TargetDossierType, ClientSegment, OldAlfrescoStatus, NewAlfrescoStatus,
    WillReceiveMigrationSuffix, CodeWillChange, DocDescription, DossierDestFolderId
)
SELECT
    Id, NodeId, Name, IsFolder, IsFile, NodeType, ParentId, FromPath, ToPath,
    Status, ISNULL(RetryCount, 0), ErrorMsg, CreatedAt, UpdatedAt,
    DocumentType, DocumentTypeMigration, Source, ISNULL(IsActive, 1), CategoryCode, CategoryName,
    OriginalCreatedAt, ContractNumber, CoreId, ISNULL(Version, 1.0), AccountNumbers,
    ISNULL(RequiresTypeTransformation, 0), FinalDocumentType, ISNULL(IsSigned, 0), DutOfferId, ProductType,
    OriginalDocumentName, NewDocumentName, OriginalDocumentCode, NewDocumentCode,
    TipDosijea, TargetDossierType, ClientSegment, OldAlfrescoStatus, NewAlfrescoStatus,
    ISNULL(WillReceiveMigrationSuffix, 0), ISNULL(CodeWillChange, 0), DocDescription, DossierDestFolderId
FROM dbo.DocStaging_BACKUP
WHERE Id IS NOT NULL  -- Only restore rows with valid Id
  AND NodeId IS NOT NULL AND NodeId != ''  -- Skip invalid NodeId
ORDER BY Id;

SET IDENTITY_INSERT dbo.DocStaging OFF;

PRINT 'Data restored: ' + CAST(@@ROWCOUNT AS VARCHAR(10)) + ' rows';
*/

PRINT '';
PRINT '=============================================================================';
PRINT 'DocStaging table recreated successfully!';
PRINT 'Next steps:';
PRINT '  1. Run DIAGNOSTIC_DocStaging_Schema.sql to verify schema';
PRINT '  2. If you need to restore data, uncomment STEP 6 above';
PRINT '  3. Test the application with new schema';
PRINT '=============================================================================';
