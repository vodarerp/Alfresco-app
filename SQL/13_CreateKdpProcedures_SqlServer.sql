-- ============================================================================
-- KDP DOCUMENT PROCESSING STORED PROCEDURES - SQL SERVER
-- ============================================================================
-- Verzija: 1.0
-- Datum: 2025-12-19
-- Svrha: Stored procedures za obradu KDP dokumenata
-- ============================================================================

USE [AlfrescoMigration]
GO

-- ============================================================================
-- 1. DROP POSTOJEĆIH PROCEDURA (ako postoje)
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 1: Brisanje postojećih KDP procedura...';
PRINT '============================================================================';

IF OBJECT_ID('dbo.sp_ProcessKdpDocuments', 'P') IS NOT NULL
BEGIN
    PRINT 'Brisanje procedure sp_ProcessKdpDocuments...';
    DROP PROCEDURE dbo.sp_ProcessKdpDocuments;
END
GO

PRINT 'Sve postojeće KDP procedure su obrisane.';
PRINT '';
GO

-- ============================================================================
-- 2. KREIRANJE PROCEDURE: sp_ProcessKdpDocuments
-- ============================================================================

PRINT '============================================================================';
PRINT 'KORAK 2: Kreiranje procedure sp_ProcessKdpDocuments...';
PRINT '============================================================================';
GO

CREATE PROCEDURE dbo.sp_ProcessKdpDocuments
AS
BEGIN
    SET NOCOUNT ON;

    DECLARE @ProcessedCount INT = 0;
    DECLARE @ErrorMessage NVARCHAR(4000);
    DECLARE @ErrorSeverity INT;
    DECLARE @ErrorState INT;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- ================================================================
        -- KORAK 1: Očisti prethodne rezultate (opciono)
        -- ================================================================
        -- Komentiraj sledeću liniju ako želiš da čuvaš istoriju
        TRUNCATE TABLE dbo.KdpExportResult;

        -- ================================================================
        -- KORAK 2: Pronađi kandidat foldere
        -- ================================================================
        -- CTE 1: Folderi sa bar jednim neaktivnim KDP dokumentom (status = '2')
        WITH InactiveFolders AS (
            SELECT DISTINCT AccFolderName
            FROM dbo.KdpDocumentStaging
            WHERE DocumentStatus = '2'
              AND AccFolderName IS NOT NULL
        ),

        -- CTE 2: Folderi sa bar jednim aktivnim KDP dokumentom (status != '2')
        ActiveFolders AS (
            SELECT DISTINCT AccFolderName
            FROM dbo.KdpDocumentStaging
            WHERE DocumentStatus != '2'
              AND AccFolderName IS NOT NULL
        ),

        -- CTE 3: Kandidat folderi = Imaju neaktivne ALI NEMAJU aktivne
        CandidateFolders AS (
            SELECT i.AccFolderName
            FROM InactiveFolders i
            WHERE NOT EXISTS (
                SELECT 1
                FROM ActiveFolders a
                WHERE a.AccFolderName = i.AccFolderName
            )
        ),

        -- CTE 4: Brojanje KDP dokumenata po folderu
        DocumentCounts AS (
            SELECT
                AccFolderName,
                COUNT(*) as TotalDocs
            FROM dbo.KdpDocumentStaging
            WHERE DocumentStatus = '2'
              AND AccFolderName IS NOT NULL
            GROUP BY AccFolderName
        ),

        -- CTE 5: Najmlađi dokument po folderu (ROW_NUMBER za sortiranje po datumu)
        YoungestDocuments AS (
            SELECT
                kds.Id,
                kds.NodeId,
                kds.DocumentName,
                kds.DocumentPath,
                kds.DocumentType,
                kds.DocumentStatus,
                kds.CreatedDate,
                kds.AccountNumbers,
                kds.AccFolderName,
                kds.CoreId,
                dc.TotalDocs,
                ROW_NUMBER() OVER (
                    PARTITION BY kds.AccFolderName
                    ORDER BY kds.CreatedDate DESC
                ) as RowNum
            FROM dbo.KdpDocumentStaging kds
            INNER JOIN CandidateFolders cf
                ON kds.AccFolderName = cf.AccFolderName
            INNER JOIN DocumentCounts dc
                ON kds.AccFolderName = dc.AccFolderName
            WHERE kds.DocumentStatus = '2'
        )

        -- ================================================================
        -- KORAK 3: Upisivanje u finalnu tabelu (samo najmlađi dokument po folderu)
        -- ================================================================
        INSERT INTO dbo.KdpExportResult (
            ReferncaDosijea,
            KlijentskiBroj,
            ReferencaDokumenta,
            TipDokumenta,
            DatumKreiranjaDokumenta,
            DocumentName,
            AccFolderName,
            TotalKdpDocumentsInFolder,
            ListaRacuna,
            ExportDate,
            IsActivated,
            ActivationDate
        )
        SELECT
            yd.DocumentPath as ReferncaDosijea,
            yd.CoreId as KlijentskiBroj,
            yd.NodeId as ReferencaDokumenta,
            yd.DocumentType as TipDokumenta,
            yd.CreatedDate as DatumKreiranjaDokumenta,
            yd.DocumentName,
            yd.AccFolderName,
            yd.TotalDocs as TotalKdpDocumentsInFolder,
            NULL as ListaRacuna,             -- NULL initially, banka će popuniti kasnije
            GETDATE() as ExportDate,
            0 as IsActivated,                -- Nije aktiviran još
            NULL as ActivationDate
        FROM YoungestDocuments yd
        WHERE yd.RowNum = 1                  -- Samo najmlađi dokument
        ORDER BY yd.AccFolderName;

        SET @ProcessedCount = @@ROWCOUNT;

        COMMIT TRANSACTION;

        -- ================================================================
        -- KORAK 4: Vraćanje statistike
        -- ================================================================
        SELECT
            @ProcessedCount as TotalCandidates,
            SUM(TotalKdpDocumentsInFolder) as TotalDocumentsInFolders,
            MIN(DatumKreiranjaDokumenta) as OldestDocument,
            MAX(DatumKreiranjaDokumenta) as NewestDocument
        FROM dbo.KdpExportResult
        WHERE ExportDate >= DATEADD(MINUTE, -5, GETDATE());  -- Rezultati iz poslednjih 5 minuta

        -- Success message
        PRINT '============================================================================';
        PRINT 'sp_ProcessKdpDocuments - Uspešno završena obrada';
        PRINT '============================================================================';
        PRINT 'Procesuirano foldera: ' + CAST(@ProcessedCount AS VARCHAR(10));
        PRINT '============================================================================';

    END TRY
    BEGIN CATCH
        -- Rollback transakcije u slučaju greške
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        -- Priprema error poruke
        SET @ErrorMessage = ERROR_MESSAGE();
        SET @ErrorSeverity = ERROR_SEVERITY();
        SET @ErrorState = ERROR_STATE();

        -- Log greške
        PRINT '============================================================================';
        PRINT 'ERROR u sp_ProcessKdpDocuments:';
        PRINT @ErrorMessage;
        PRINT '============================================================================';

        -- Re-throw error
        RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END;
GO

PRINT 'Procedura sp_ProcessKdpDocuments kreirana.';
GO

-- ============================================================================
-- KRAJ SKRIPTE
-- ============================================================================

PRINT '============================================================================';
PRINT 'SVE KDP PROCEDURE SU USPEŠNO KREIRANE!';
PRINT '============================================================================';
PRINT '';
PRINT 'Kreirane procedure:';
PRINT '  1. sp_ProcessKdpDocuments - Procesuira KDP dokumente i kreira export rezultate';
PRINT '';
PRINT 'Test upotreba:';
PRINT '  EXEC dbo.sp_ProcessKdpDocuments;';
PRINT '============================================================================';
GO
