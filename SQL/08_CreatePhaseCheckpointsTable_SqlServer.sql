-- ============================================================================
-- PhaseCheckpoints Table Creation Script - SQL SERVER
-- ============================================================================
-- Purpose: Stores phase-level checkpoint data for migration orchestration
--          Supports resume capability at the phase level (FolderDiscovery,
--          DocumentDiscovery, FolderPreparation, Move)
-- ============================================================================
-- Verzija: 1.0
-- Datum: 2025-01-17
-- ============================================================================

USE [AlfrescoMigration]
GO

-- ============================================================================
-- 1. DROP POSTOJEĆE TABELE (ako postoji)
-- ============================================================================

PRINT '============================================================================';
PRINT 'Kreiranje tabele PhaseCheckpoints...';
PRINT '============================================================================';

IF OBJECT_ID('dbo.PhaseCheckpoints', 'U') IS NOT NULL
BEGIN
    PRINT 'Brisanje postojeće tabele PhaseCheckpoints...';
    DROP TABLE dbo.PhaseCheckpoints;
END
GO

-- ============================================================================
-- 2. KREIRANJE TABELE: PhaseCheckpoints
-- ============================================================================

CREATE TABLE dbo.PhaseCheckpoints (
    -- Primary key
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Phase identification
    Phase                           INT NOT NULL,
    -- 1 = FolderDiscovery, 2 = DocumentDiscovery, 3 = FolderPreparation, 4 = Move

    -- Phase status
    Status                          INT NOT NULL,
    -- 0 = NotStarted, 1 = InProgress, 2 = Completed, 3 = Failed

    -- Resumability fields (used by service-level checkpoint)
    LastProcessedIndex              INT NULL,
    LastProcessedId                 NVARCHAR(500) NULL,

    -- Phase lifecycle timestamps
    StartedAt                       DATETIME2 NULL,
    CompletedAt                     DATETIME2 NULL,

    -- Error tracking
    ErrorMessage                    NVARCHAR(MAX) NULL,

    -- Progress metrics
    TotalProcessed                  BIGINT NOT NULL DEFAULT 0,
    TotalItems                      BIGINT NULL,

    -- Audit timestamps
    CreatedAt                       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
    UpdatedAt                       DATETIME2 NOT NULL DEFAULT GETUTCDATE(),

    -- Constraints
    CONSTRAINT UQ_PhaseCheckpoints_Phase UNIQUE (Phase),
    CONSTRAINT CHK_PhaseCheckpoints_Phase
        CHECK (Phase IN (1, 2, 3, 4)),  -- FolderDiscovery, DocumentDiscovery, FolderPreparation, Move
    CONSTRAINT CHK_PhaseCheckpoints_Status
        CHECK (Status IN (0, 1, 2, 3))  -- NotStarted, InProgress, Completed, Failed
);
GO

-- ============================================================================
-- 3. KREIRANJE INDEKSA
-- ============================================================================

PRINT 'Kreiranje indeksa za PhaseCheckpoints...';

-- Primary lookup by Phase (most common query) - already covered by UNIQUE constraint
-- But we add explicit index for clarity
CREATE NONCLUSTERED INDEX idx_phasecheckpoints_phase
    ON dbo.PhaseCheckpoints(Phase);
GO

-- Index for monitoring updated checkpoints
CREATE NONCLUSTERED INDEX idx_phasecheckpoints_updated
    ON dbo.PhaseCheckpoints(UpdatedAt DESC);
GO

-- Index for status filtering (finding in-progress or failed phases)
CREATE NONCLUSTERED INDEX idx_phasecheckpoints_status
    ON dbo.PhaseCheckpoints(Status);
GO

-- Composite index for status + phase (common query pattern)
CREATE NONCLUSTERED INDEX idx_phasecheckpoints_status_phase
    ON dbo.PhaseCheckpoints(Status, Phase);
GO

PRINT 'PhaseCheckpoints tabela i indeksi kreirani!';
PRINT '';
GO

-- ============================================================================
-- 4. INICIJALIZACIJA - Kreiraj default checkpoint zapise za sve faze
-- ============================================================================

PRINT 'Inicijalizacija default checkpoint zapisa...';

-- Initialize all 4 phases with NotStarted status
INSERT INTO dbo.PhaseCheckpoints (Phase, Status, TotalProcessed, CreatedAt, UpdatedAt)
VALUES
    (1, 0, 0, GETUTCDATE(), GETUTCDATE()),  -- FolderDiscovery = NotStarted
    (2, 0, 0, GETUTCDATE(), GETUTCDATE()),  -- DocumentDiscovery = NotStarted
    (3, 0, 0, GETUTCDATE(), GETUTCDATE()),  -- FolderPreparation = NotStarted
    (4, 0, 0, GETUTCDATE(), GETUTCDATE());  -- Move = NotStarted
GO

PRINT 'Default checkpoint zapisi kreirani za sve 4 faze!';
PRINT '';
GO

-- ============================================================================
-- 5. COMMENTS / DOKUMENTACIJA
-- ============================================================================

EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Stores phase-level checkpoint data for migration orchestration. Each phase (FolderDiscovery, DocumentDiscovery, FolderPreparation, Move) has exactly one checkpoint record.',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE',  @level1name = 'PhaseCheckpoints';
GO

EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Phase identifier: 1=FolderDiscovery, 2=DocumentDiscovery, 3=FolderPreparation, 4=Move',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE',  @level1name = 'PhaseCheckpoints',
    @level2type = N'COLUMN', @level2name = 'Phase';
GO

EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Status: 0=NotStarted, 1=InProgress, 2=Completed, 3=Failed',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE',  @level1name = 'PhaseCheckpoints',
    @level2type = N'COLUMN', @level2name = 'Status';
GO

EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Last processed index within phase (for service-level resumability)',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE',  @level1name = 'PhaseCheckpoints',
    @level2type = N'COLUMN', @level2name = 'LastProcessedIndex';
GO

EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Last processed item ID within phase (for service-level resumability)',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE',  @level1name = 'PhaseCheckpoints',
    @level2type = N'COLUMN', @level2name = 'LastProcessedId';
GO

EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Timestamp when phase started execution',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE',  @level1name = 'PhaseCheckpoints',
    @level2type = N'COLUMN', @level2name = 'StartedAt';
GO

EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Timestamp when phase completed (successfully or with failure)',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE',  @level1name = 'PhaseCheckpoints',
    @level2type = N'COLUMN', @level2name = 'CompletedAt';
GO

EXEC sp_addextendedproperty
    @name = N'MS_Description',
    @value = N'Error message if phase failed (Status=3)',
    @level0type = N'SCHEMA', @level0name = 'dbo',
    @level1type = N'TABLE',  @level1name = 'PhaseCheckpoints',
    @level2type = N'COLUMN', @level2name = 'ErrorMessage';
GO

-- ============================================================================
-- 6. FINALNI REPORT
-- ============================================================================

PRINT '';
PRINT '============================================================================';
PRINT 'PhaseCheckpoints TABELA USPEŠNO KREIRANA!';
PRINT '============================================================================';
PRINT '';
PRINT 'Kreirano:';
PRINT '  - PhaseCheckpoints tabela';
PRINT '  - 4 indeksa za optimalne performanse';
PRINT '  - 4 default checkpoint zapisa (po jedan za svaku fazu)';
PRINT '';
PRINT 'Faze (Phase enum):';
PRINT '  1 = FolderDiscovery';
PRINT '  2 = DocumentDiscovery';
PRINT '  3 = FolderPreparation (NOVA FAZA)';
PRINT '  4 = Move';
PRINT '';
PRINT 'Statusi (Status enum):';
PRINT '  0 = NotStarted';
PRINT '  1 = InProgress';
PRINT '  2 = Completed';
PRINT '  3 = Failed';
PRINT '';
PRINT 'Verification query:';
PRINT '  SELECT * FROM PhaseCheckpoints ORDER BY Phase;';
PRINT '';
PRINT '============================================================================';
GO

-- ============================================================================
-- 7. VERIFICATION QUERY
-- ============================================================================

SELECT
    Phase,
    CASE Phase
        WHEN 1 THEN 'FolderDiscovery'
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
    StartedAt,
    CompletedAt,
    TotalProcessed,
    CreatedAt
FROM PhaseCheckpoints
ORDER BY Phase;
GO
