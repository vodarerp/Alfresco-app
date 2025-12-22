-- ============================================================================
-- KDP DOCUMENT PROCESSING TABLES - SQL SERVER
-- ============================================================================
-- Verzija: 1.0
-- Datum: 2025-12-19
-- Svrha: Tabele za obradu KDP dokumenata (tipovi 00824 i 00099)
-- ============================================================================

USE [AlfrescoMigration]
GO

-- ============================================================================
-- 1. DROP POSTOJEĆIH TABELA (ako postoje)
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 1: Brisanje postojećih KDP tabela...';
PRINT '============================================================================';

IF OBJECT_ID('dbo.KdpExportResult', 'U') IS NOT NULL
BEGIN
    PRINT 'Brisanje tabele KdpExportResult...';
    DROP TABLE dbo.KdpExportResult;
END
GO

IF OBJECT_ID('dbo.KdpDocumentStaging', 'U') IS NOT NULL
BEGIN
    PRINT 'Brisanje tabele KdpDocumentStaging...';
    DROP TABLE dbo.KdpDocumentStaging;
END
GO

PRINT 'Sve postojeće KDP tabele su obrisane.';
PRINT '';
GO

-- ============================================================================
-- 2. KREIRANJE TABELE: KdpDocumentStaging
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 2: Kreiranje tabele KdpDocumentStaging...';
PRINT '============================================================================';

CREATE TABLE dbo.KdpDocumentStaging (
    -- Primary key
    Id                      BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Alfresco node identifikacija
    NodeId                  NVARCHAR(100) NOT NULL,
    DocumentName            NVARCHAR(500) NULL,
    DocumentPath            NVARCHAR(2000) NULL,
    ParentFolderId          NVARCHAR(100) NULL,
    ParentFolderName        NVARCHAR(200) NULL,

    -- Custom properties iz Alfresca
    DocumentType            NVARCHAR(10) NULL,          -- ecm:docType (00824 ili 00099)
    DocumentStatus          NVARCHAR(10) NULL,          -- ecm:docStatus (2 = neaktivan, 1 = aktivan)
    CreatedDate             DATETIME NULL,              -- cm:created
    AccountNumbers          NVARCHAR(500) NULL,         -- ecm:bnkAccountNumber (ako postoji)

    -- Ekstrahovani podaci iz path-a
    AccFolderName           NVARCHAR(100) NULL,         -- ACC-123456 (ekstrahovano iz path-a)
    CoreId                  NVARCHAR(50) NULL,          -- 123456 (klijentski broj)

    -- Metadata
    ProcessedDate           DATETIME DEFAULT GETDATE() NOT NULL
);
GO

PRINT 'Tabela KdpDocumentStaging kreirana.';
GO

-- ============================================================================
-- 3. KREIRANJE INDEKSA ZA KdpDocumentStaging
-- ============================================================================

PRINT 'Kreiranje indeksa za KdpDocumentStaging...';

-- Index za NodeId (UNIQUE - svaki dokument jednom)
CREATE UNIQUE NONCLUSTERED INDEX idx_kdpdocstaging_nodeid
    ON dbo.KdpDocumentStaging(NodeId);

-- Index za ACC folder lookup
CREATE NONCLUSTERED INDEX idx_kdpdocstaging_accfolder
    ON dbo.KdpDocumentStaging(AccFolderName);

-- Index za status filtering
CREATE NONCLUSTERED INDEX idx_kdpdocstaging_status
    ON dbo.KdpDocumentStaging(DocumentStatus);

-- Index za document type filtering
CREATE NONCLUSTERED INDEX idx_kdpdocstaging_doctype
    ON dbo.KdpDocumentStaging(DocumentType);

PRINT 'Indeksi za KdpDocumentStaging kreirani.';
PRINT '';
GO

-- ============================================================================
-- 4. KREIRANJE TABELE: KdpExportResult
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 3: Kreiranje tabele KdpExportResult...';
PRINT '============================================================================';

CREATE TABLE dbo.KdpExportResult (
    -- Primary key
    Id                              BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Kolone za banku (prema zahtevima)
    ReferncaDosijea                 NVARCHAR(200) NULL,         -- Path ACC foldera
    KlijentskiBroj                  NVARCHAR(50) NULL,          -- CoreId
    ReferencaDokumenta              NVARCHAR(100) NOT NULL,     -- NodeId dokumenta
    TipDokumenta                    NVARCHAR(10) NULL,          -- 00824 ili 00099
    DatumKreiranjaDokumenta         DATETIME NULL,              -- cm:created

    -- Kolona za banku da popuni (za buduću upotrebu)
    ListaRacuna                     NVARCHAR(500) NULL,         -- Banka popunjava (računi odvojeni zarezom)

    -- Dodatni podaci za praćenje
    DocumentName                    NVARCHAR(500) NULL,
    AccFolderName                   NVARCHAR(100) NULL,
    TotalKdpDocumentsInFolder       INT NULL,                   -- Ukupan broj KDP dokumenata u folderu

    -- Metadata
    ExportDate                      DATETIME DEFAULT GETDATE() NOT NULL,
    IsActivated                     BIT DEFAULT 0 NOT NULL,     -- Da li je dokument aktiviran u Alfrescu
    ActivationDate                  DATETIME NULL
);
GO

PRINT 'Tabela KdpExportResult kreirana.';
GO

-- ============================================================================
-- 5. KREIRANJE INDEKSA ZA KdpExportResult
-- ============================================================================

PRINT 'Kreiranje indeksa za KdpExportResult...';

-- Index za NodeId (UNIQUE - svaki dokument jednom u export-u)
CREATE UNIQUE NONCLUSTERED INDEX idx_kdpexport_nodeid
    ON dbo.KdpExportResult(ReferencaDokumenta);

-- Index za CoreId lookup
CREATE NONCLUSTERED INDEX idx_kdpexport_coreid
    ON dbo.KdpExportResult(KlijentskiBroj);

-- Index za ACC folder lookup
CREATE NONCLUSTERED INDEX idx_kdpexport_accfolder
    ON dbo.KdpExportResult(AccFolderName);

PRINT 'Indeksi za KdpExportResult kreirani.';
PRINT '';
GO

-- ============================================================================
-- KRAJ SKRIPTE
-- ============================================================================

PRINT '============================================================================';
PRINT 'SVE KDP TABELE SU USPEŠNO KREIRANE!';
PRINT '============================================================================';
PRINT '';
PRINT 'Kreirane tabele:';
PRINT '  1. KdpDocumentStaging  - Staging tabela za sve KDP dokumente';
PRINT '  2. KdpExportResult     - Finalni rezultati za eksport';
PRINT '';
PRINT 'Sledeći korak: Pokrenuti skriptu za kreiranje stored procedure sp_ProcessKdpDocuments';
PRINT '============================================================================';
GO
