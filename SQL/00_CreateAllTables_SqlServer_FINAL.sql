-- ============================================================================
-- FINALNI SQL SKRIPT ZA KREIRANJE SVIH TABELA - SQL SERVER
-- ============================================================================
-- Verzija: 1.0
-- Datum: 2025-10-30
-- Napomena: DateTime polja su VARCHAR(20) kako bi se izbegao timezone issue
-- ============================================================================

USE [AlfrescoMigration]
GO

-- ============================================================================
-- 1. DROP POSTOJEĆIH TABELA (ako postoje)
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 1: Brisanje postojećih tabela...';
PRINT '============================================================================';

IF OBJECT_ID('dbo.DocStaging', 'U') IS NOT NULL
BEGIN
    PRINT 'Brisanje tabele DocStaging...';
    DROP TABLE dbo.DocStaging;
END
GO

IF OBJECT_ID('dbo.FolderStaging', 'U') IS NOT NULL
BEGIN
    PRINT 'Brisanje tabele FolderStaging...';
    DROP TABLE dbo.FolderStaging;
END
GO

IF OBJECT_ID('dbo.MigrationCheckpoint', 'U') IS NOT NULL
BEGIN
    PRINT 'Brisanje tabele MigrationCheckpoint...';
    DROP TABLE dbo.MigrationCheckpoint;
END
GO

IF OBJECT_ID('dbo.AlfrescoMigration_Logger', 'U') IS NOT NULL
BEGIN
    PRINT 'Brisanje tabele AlfrescoMigration_Logger...';
    DROP TABLE dbo.AlfrescoMigration_Logger;
END
GO

PRINT 'Sve postojeće tabele su obrisane.';
PRINT '';
GO

-- ============================================================================
-- 2. KREIRANJE TABELE: DocStaging
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 2: Kreiranje tabele DocStaging...';
PRINT '============================================================================';

CREATE TABLE dbo.DocStaging (
    -- Primary key
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,

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

    -- Audit timestamps (VARCHAR(20) umesto DATETIME)
    CreatedAt                       VARCHAR(20) NOT NULL,
    UpdatedAt                       VARCHAR(20) NOT NULL,

    -- ========== EXTENDED FIELDS FOR MIGRATION ==========

    -- Document type and metadata
    DocumentType                    NVARCHAR(50) NULL,
    DocumentTypeMigration           NVARCHAR(50) NULL,
    Source                          NVARCHAR(100) NULL,
    IsActive                        BIT NOT NULL DEFAULT 1,
    CategoryCode                    NVARCHAR(50) NULL,
    CategoryName                    NVARCHAR(200) NULL,

    -- Original timestamps (VARCHAR(20) umesto DATETIME)
    OriginalCreatedAt               VARCHAR(20) NULL,

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
    CodeWillChange                  BIT NOT NULL DEFAULT 0
);
GO

-- ============================================================================
-- 3. KREIRANJE INDEKSA ZA DocStaging
-- ============================================================================

PRINT 'Kreiranje indeksa za DocStaging...';

-- Index for NodeId lookups
CREATE NONCLUSTERED INDEX idx_docstaging_nodeid
    ON dbo.DocStaging(NodeId);
GO

-- Index for Status filtering (most common query)
CREATE NONCLUSTERED INDEX idx_docstaging_status
    ON dbo.DocStaging(Status);
GO

-- Index for ParentId (folder hierarchy)
CREATE NONCLUSTERED INDEX idx_docstaging_parentid
    ON dbo.DocStaging(ParentId);
GO

-- Index for CoreId (client tracking)
CREATE NONCLUSTERED INDEX idx_docstaging_coreid
    ON dbo.DocStaging(CoreId)
    WHERE CoreId IS NOT NULL;
GO

-- Index for DocumentType
CREATE NONCLUSTERED INDEX idx_docstaging_documenttype
    ON dbo.DocStaging(DocumentType)
    WHERE DocumentType IS NOT NULL;
GO

-- Index for TargetDossierType (migration routing)
CREATE NONCLUSTERED INDEX idx_docstaging_targetdossiertype
    ON dbo.DocStaging(TargetDossierType)
    WHERE TargetDossierType IS NOT NULL;
GO

-- Composite index for migration processing (Status + CreatedAt)
CREATE NONCLUSTERED INDEX idx_docstaging_status_created
    ON dbo.DocStaging(Status, CreatedAt);
GO

PRINT 'DocStaging tabela i indeksi kreirani!';
PRINT '';
GO

-- ============================================================================
-- 4. KREIRANJE TABELE: FolderStaging
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 3: Kreiranje tabele FolderStaging...';
PRINT '============================================================================';

CREATE TABLE dbo.FolderStaging (
    -- Primary key
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Basic folder info
    NodeId                          NVARCHAR(100) NULL,
    ParentId                        NVARCHAR(100) NULL,
    Name                            NVARCHAR(500) NULL,

    -- Migration status
    Status                          NVARCHAR(20) NOT NULL,  -- NEW, READY, IN_PROGRESS, DONE, ERROR

    -- Destination folder info
    DestFolderId                    NVARCHAR(100) NULL,
    DossierDestFolderId             NVARCHAR(100) NULL,

    -- Audit timestamps (VARCHAR(20) umesto DATETIME)
    CreatedAt                       VARCHAR(20) NOT NULL,
    UpdatedAt                       VARCHAR(20) NOT NULL,

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

    -- Process date (VARCHAR(20) umesto DATETIME)
    ProcessDate                     VARCHAR(20) NULL,

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

    -- Audit fields
    Creator                         NVARCHAR(200) NULL,
    ArchivedAt                      VARCHAR(20) NULL,

    -- ========== FAZA 2: NOVA POLJA ZA MAPIRANJE ==========

    -- Dossier type detection
    TipDosijea                      NVARCHAR(200) NULL,
    TargetDossierType               INT NULL,

    -- Client segment
    ClientSegment                   NVARCHAR(50) NULL
);
GO

-- ============================================================================
-- 5. KREIRANJE INDEKSA ZA FolderStaging
-- ============================================================================

PRINT 'Kreiranje indeksa za FolderStaging...';

-- Index for NodeId lookups
CREATE NONCLUSTERED INDEX idx_folderstaging_nodeid
    ON dbo.FolderStaging(NodeId);
GO

-- Index for Status filtering (most common query)
CREATE NONCLUSTERED INDEX idx_folderstaging_status
    ON dbo.FolderStaging(Status);
GO

-- Index for CoreId (client tracking)
CREATE NONCLUSTERED INDEX idx_folderstaging_coreid
    ON dbo.FolderStaging(CoreId)
    WHERE CoreId IS NOT NULL;
GO

-- Index for ParentId
CREATE NONCLUSTERED INDEX idx_folderstaging_parentid
    ON dbo.FolderStaging(ParentId)
    WHERE ParentId IS NOT NULL;
GO

-- Index for TargetDossierType
CREATE NONCLUSTERED INDEX idx_folderstaging_targetdossiertype
    ON dbo.FolderStaging(TargetDossierType)
    WHERE TargetDossierType IS NOT NULL;
GO

-- Composite index for processing (Status + CreatedAt)
CREATE NONCLUSTERED INDEX idx_folderstaging_status_created
    ON dbo.FolderStaging(Status, CreatedAt);
GO

PRINT 'FolderStaging tabela i indeksi kreirani!';
PRINT '';
GO

-- ============================================================================
-- 6. KREIRANJE TABELE: MigrationCheckpoint
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 4: Kreiranje tabele MigrationCheckpoint...';
PRINT '============================================================================';

CREATE TABLE dbo.MigrationCheckpoint (
    -- Primary key
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Service identification
    ServiceName                     NVARCHAR(100) NOT NULL,  -- FolderDiscovery, DocumentDiscovery, Move

    -- Checkpoint data
    CheckpointData                  NVARCHAR(MAX) NULL,      -- JSON serialized state
    LastProcessedId                 NVARCHAR(100) NULL,

    -- Last processed timestamp (VARCHAR(20) umesto DATETIME)
    LastProcessedAt                 VARCHAR(20) NULL,

    -- Progress tracking
    TotalProcessed                  BIGINT NOT NULL DEFAULT 0,
    TotalFailed                     BIGINT NOT NULL DEFAULT 0,
    BatchCounter                    INT NOT NULL DEFAULT 0,

    -- Audit timestamps (VARCHAR(20) umesto DATETIME)
    UpdatedAt                       VARCHAR(20) NOT NULL,
    CreatedAt                       VARCHAR(20) NOT NULL,

    -- Unique constraint on ServiceName
    CONSTRAINT UQ_MigrationCheckpoint_ServiceName UNIQUE (ServiceName)
);
GO

-- ============================================================================
-- 7. KREIRANJE INDEKSA ZA MigrationCheckpoint
-- ============================================================================

PRINT 'Kreiranje indeksa za MigrationCheckpoint...';

-- Index for ServiceName lookups (already unique, but explicit index for clarity)
CREATE NONCLUSTERED INDEX idx_checkpoint_servicename
    ON dbo.MigrationCheckpoint(ServiceName);
GO

PRINT 'MigrationCheckpoint tabela i indeksi kreirani!';
PRINT '';
GO

-- ============================================================================
-- 8. KREIRANJE TABELE: AlfrescoMigration_Logger
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 5: Kreiranje tabele AlfrescoMigration_Logger...';
PRINT '============================================================================';

CREATE TABLE dbo.AlfrescoMigration_Logger (
    -- Primary key
    Id                              INT IDENTITY(1,1) PRIMARY KEY,

    -- Standard log4net fields
    LOG_DATE                        VARCHAR(20) NOT NULL,
    LOG_LEVEL                       NVARCHAR(50) NOT NULL,
    LOGGER                          NVARCHAR(255) NOT NULL,
    MESSAGE                         NVARCHAR(4000) NULL,
    EXCEPTION                       NVARCHAR(MAX) NULL,

    -- Custom business fields
    WORKERID                        NVARCHAR(100) NULL,
    BATCHID                         NVARCHAR(100) NULL,
    DOCUMENTID                      NVARCHAR(100) NULL,
    USERID                          NVARCHAR(100) NULL,

    -- System fields
    HOSTNAME                        NVARCHAR(100) NULL,
    THREADID                        NVARCHAR(50) NULL,
    APPINSTANCE                     NVARCHAR(100) NULL,

    -- Audit field (VARCHAR(20) umesto DATETIME)
    CREATEDAT                       VARCHAR(20) NOT NULL
);
GO

-- ============================================================================
-- 9. KREIRANJE INDEKSA ZA AlfrescoMigration_Logger
-- ============================================================================

PRINT 'Kreiranje indeksa za AlfrescoMigration_Logger...';

-- Index for querying by date range (most common query)
CREATE NONCLUSTERED INDEX idx_logger_date
    ON dbo.AlfrescoMigration_Logger(LOG_DATE DESC);
GO

-- Index for filtering by log level
CREATE NONCLUSTERED INDEX idx_logger_level
    ON dbo.AlfrescoMigration_Logger(LOG_LEVEL);
GO

-- Index for finding specific document logs
CREATE NONCLUSTERED INDEX idx_logger_documentid
    ON dbo.AlfrescoMigration_Logger(DOCUMENTID)
    WHERE DOCUMENTID IS NOT NULL;
GO

-- Index for batch tracking
CREATE NONCLUSTERED INDEX idx_logger_batchid
    ON dbo.AlfrescoMigration_Logger(BATCHID)
    WHERE BATCHID IS NOT NULL;
GO

-- Composite index for common queries (level + date)
CREATE NONCLUSTERED INDEX idx_logger_level_date
    ON dbo.AlfrescoMigration_Logger(LOG_LEVEL, LOG_DATE DESC);
GO

PRINT 'AlfrescoMigration_Logger tabela i indeksi kreirani!';
PRINT '';
GO

-- ============================================================================
-- 10. FINALNI REPORT
-- ============================================================================

PRINT '';
PRINT '============================================================================';
PRINT 'SVE TABELE SU USPEŠNO KREIRANE!';
PRINT '============================================================================';
PRINT '';
PRINT 'Kreirane tabele:';
PRINT '  1. DocStaging (sa 37 kolona + 7 indeksa)';
PRINT '  2. FolderStaging (sa 30 kolona + 6 indeksa)';
PRINT '  3. MigrationCheckpoint (sa 10 kolona + 1 indeks)';
PRINT '  4. AlfrescoMigration_Logger (sa 13 kolona + 5 indeksa)';
PRINT '';
PRINT 'Napomene:';
PRINT '  - DateTime polja su VARCHAR(20) kako bi se izbegao timezone issue';
PRINT '  - Format datuma: "yyyy-MM-dd HH:mm:ss" ili ISO 8601';
PRINT '  - Svi indeksi su kreirani za optimalne performanse';
PRINT '';
PRINT 'Sledeći koraci:';
PRINT '  1. Proveri sve tabele: SELECT * FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME LIKE ''%Staging'' OR TABLE_NAME LIKE ''%Logger'' OR TABLE_NAME LIKE ''%Checkpoint'';';
PRINT '  2. Proveri indekse: EXEC sp_helpindex ''DocStaging'';';
PRINT '  3. Pokreni aplikaciju i testiraj migraciju!';
PRINT '';
PRINT '============================================================================';
GO

-- ============================================================================
-- 11. VERIFICATION QUERIES
-- ============================================================================

-- Provera kreiranih tabela
SELECT
    TABLE_NAME AS [Tabela],
    (SELECT COUNT(*)
     FROM INFORMATION_SCHEMA.COLUMNS
     WHERE TABLE_NAME = t.TABLE_NAME) AS [Broj Kolona]
FROM INFORMATION_SCHEMA.TABLES t
WHERE TABLE_NAME IN ('DocStaging', 'FolderStaging', 'MigrationCheckpoint', 'AlfrescoMigration_Logger')
ORDER BY TABLE_NAME;
GO

-- Provera indeksa
SELECT
    t.name AS [Tabela],
    i.name AS [Indeks],
    i.type_desc AS [Tip]
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
WHERE t.name IN ('DocStaging', 'FolderStaging', 'MigrationCheckpoint', 'AlfrescoMigration_Logger')
  AND i.type > 0 -- Exclude heap
ORDER BY t.name, i.name;
GO
