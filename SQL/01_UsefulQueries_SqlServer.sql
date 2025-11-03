-- ============================================================================
-- KORISNI QUERY-JI ZA MONITORING MIGRACIJE - SQL SERVER
-- ============================================================================
-- Verzija: 1.0
-- Datum: 2025-10-30
-- ============================================================================

USE [AlfrescoMigration]
GO

-- ============================================================================
-- 1. PROVERA TABELA I STRUKTURE
-- ============================================================================

-- Provera svih tabela
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
    i.type_desc AS [Tip],
    (SELECT COUNT(*) FROM sys.index_columns ic WHERE ic.object_id = i.object_id AND ic.index_id = i.index_id) AS [Broj Kolona]
FROM sys.indexes i
INNER JOIN sys.tables t ON i.object_id = t.object_id
WHERE t.name IN ('DocStaging', 'FolderStaging', 'MigrationCheckpoint', 'AlfrescoMigration_Logger')
  AND i.type > 0
ORDER BY t.name, i.name;
GO

-- ============================================================================
-- 2. DocStaging - STATUS MONITORING
-- ============================================================================

-- Ukupan broj dokumenata po statusu
SELECT
    Status,
    COUNT(*) AS [Broj Dokumenata],
    CAST(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM DocStaging) AS DECIMAL(5,2)) AS [Procenat]
FROM DocStaging
GROUP BY Status
ORDER BY COUNT(*) DESC;
GO

-- Dokumenti po tipu dosijea
SELECT
    TipDosijea,
    TargetDossierType,
    COUNT(*) AS [Broj Dokumenata]
FROM DocStaging
WHERE TipDosijea IS NOT NULL
GROUP BY TipDosijea, TargetDossierType
ORDER BY COUNT(*) DESC;
GO

-- Aktivni vs neaktivni dokumenti
SELECT
    CASE WHEN IsActive = 1 THEN 'Aktivni' ELSE 'Neaktivni' END AS [Status],
    COUNT(*) AS [Broj Dokumenata],
    CAST(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM DocStaging) AS DECIMAL(5,2)) AS [Procenat]
FROM DocStaging
GROUP BY IsActive
ORDER BY IsActive DESC;
GO

-- Dokumenti sa sufiksom "-migracija"
SELECT
    CASE WHEN WillReceiveMigrationSuffix = 1 THEN 'Sa sufiksom' ELSE 'Bez sufiksa' END AS [Tip],
    COUNT(*) AS [Broj Dokumenata]
FROM DocStaging
GROUP BY WillReceiveMigrationSuffix
ORDER BY WillReceiveMigrationSuffix DESC;
GO

-- Top 10 tipova dokumenata
SELECT TOP 10
    OriginalDocumentCode,
    NewDocumentCode,
    COUNT(*) AS [Broj Dokumenata]
FROM DocStaging
WHERE OriginalDocumentCode IS NOT NULL
GROUP BY OriginalDocumentCode, NewDocumentCode
ORDER BY COUNT(*) DESC;
GO

-- Dokumenti po source-u (Heimdall vs DUT)
SELECT
    Source,
    COUNT(*) AS [Broj Dokumenata],
    CAST(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM DocStaging) AS DECIMAL(5,2)) AS [Procenat]
FROM DocStaging
WHERE Source IS NOT NULL
GROUP BY Source
ORDER BY COUNT(*) DESC;
GO

-- Dokumenti sa greškama
SELECT
    Id,
    NodeId,
    Name,
    Status,
    RetryCount,
    ErrorMsg,
    UpdatedAt
FROM DocStaging
WHERE Status = 'ERROR'
ORDER BY UpdatedAt DESC;
GO

-- ============================================================================
-- 3. FolderStaging - STATUS MONITORING
-- ============================================================================

-- Ukupan broj foldera po statusu
SELECT
    Status,
    COUNT(*) AS [Broj Foldera],
    CAST(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM FolderStaging) AS DECIMAL(5,2)) AS [Procenat]
FROM FolderStaging
GROUP BY Status
ORDER BY COUNT(*) DESC;
GO

-- Folderi po tipu dosijea
SELECT
    TipDosijea,
    TargetDossierType,
    COUNT(*) AS [Broj Foldera]
FROM FolderStaging
WHERE TipDosijea IS NOT NULL
GROUP BY TipDosijea, TargetDossierType
ORDER BY COUNT(*) DESC;
GO

-- Folderi po client segment-u (FL vs PL)
SELECT
    ClientSegment,
    COUNT(*) AS [Broj Foldera],
    CAST(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM FolderStaging WHERE ClientSegment IS NOT NULL) AS DECIMAL(5,2)) AS [Procenat]
FROM FolderStaging
WHERE ClientSegment IS NOT NULL
GROUP BY ClientSegment
ORDER BY COUNT(*) DESC;
GO

-- Top 10 klijenata sa najviše foldera
SELECT TOP 10
    CoreId,
    ClientName,
    COUNT(*) AS [Broj Foldera]
FROM FolderStaging
WHERE CoreId IS NOT NULL
GROUP BY CoreId, ClientName
ORDER BY COUNT(*) DESC;
GO

-- ============================================================================
-- 4. MigrationCheckpoint - PROGRESS TRACKING
-- ============================================================================

-- Status svih servisa
SELECT
    ServiceName,
    TotalProcessed,
    TotalFailed,
    BatchCounter,
    LastProcessedAt,
    UpdatedAt,
    CASE
        WHEN TotalFailed = 0 THEN 'OK'
        WHEN TotalFailed < 10 THEN 'WARNING'
        ELSE 'ERROR'
    END AS [Status]
FROM MigrationCheckpoint
ORDER BY ServiceName;
GO

-- Progress po servisima (grafički prikaz)
SELECT
    ServiceName,
    TotalProcessed AS [Obrađeno],
    TotalFailed AS [Neuspešno],
    CAST(TotalFailed * 100.0 / NULLIF(TotalProcessed + TotalFailed, 0) AS DECIMAL(5,2)) AS [% Neuspešnih],
    REPLICATE('█', CAST(TotalProcessed / 100 AS INT)) AS [Progress Bar]
FROM MigrationCheckpoint
ORDER BY ServiceName;
GO

-- ============================================================================
-- 5. AlfrescoMigration_Logger - LOG ANALYSIS
-- ============================================================================

-- Poslednje greške (zadnjih 100)
SELECT TOP 100
    LOG_DATE,
    LOG_LEVEL,
    LOGGER,
    MESSAGE,
    DOCUMENTID,
    BATCHID,
    SUBSTRING(EXCEPTION, 1, 200) AS [Exception_Preview]
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL IN ('ERROR', 'FATAL')
ORDER BY Id DESC;
GO

-- Broj logova po nivou
SELECT
    LOG_LEVEL,
    COUNT(*) AS [Broj Logova],
    CAST(COUNT(*) * 100.0 / (SELECT COUNT(*) FROM AlfrescoMigration_Logger) AS DECIMAL(5,2)) AS [Procenat]
FROM AlfrescoMigration_Logger
GROUP BY LOG_LEVEL
ORDER BY COUNT(*) DESC;
GO

-- Broj grešaka po logger-u
SELECT TOP 20
    LOGGER,
    COUNT(*) AS [Broj Grešaka]
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL IN ('ERROR', 'FATAL')
GROUP BY LOGGER
ORDER BY COUNT(*) DESC;
GO

-- Logovi za specifičan dokument (zameni 'NODE_ID' sa pravim ID-jem)
/*
SELECT
    LOG_DATE,
    LOG_LEVEL,
    LOGGER,
    MESSAGE,
    WORKERID,
    BATCHID
FROM AlfrescoMigration_Logger
WHERE DOCUMENTID = 'NODE_ID'
ORDER BY Id ASC;
*/

-- Logovi za specifičan batch (zameni 'BATCH_ID' sa pravim ID-jem)
/*
SELECT
    LOG_DATE,
    LOG_LEVEL,
    LOGGER,
    MESSAGE,
    DOCUMENTID,
    WORKERID
FROM AlfrescoMigration_Logger
WHERE BATCHID = 'BATCH_ID'
ORDER BY Id ASC;
*/

-- ============================================================================
-- 6. CROSS-TABLE ANALYTICS
-- ============================================================================

-- Dokumenti bez odgovarajućeg foldera
SELECT
    d.Id,
    d.NodeId,
    d.Name,
    d.ParentId,
    d.Status
FROM DocStaging d
LEFT JOIN FolderStaging f ON d.ParentId = f.NodeId
WHERE f.NodeId IS NULL
  AND d.Status NOT IN ('DONE', 'ERROR');
GO

-- Folderi bez dokumenata
SELECT
    f.Id,
    f.NodeId,
    f.Name,
    f.Status,
    (SELECT COUNT(*) FROM DocStaging d WHERE d.ParentId = f.NodeId) AS [Broj Dokumenata]
FROM FolderStaging f
WHERE f.Status = 'DONE'
HAVING (SELECT COUNT(*) FROM DocStaging d WHERE d.ParentId = f.NodeId) = 0;
GO

-- Top 10 foldera sa najviše dokumenata
SELECT TOP 10
    f.Id AS [Folder_Id],
    f.NodeId AS [Folder_NodeId],
    f.Name AS [Folder_Name],
    f.CoreId,
    f.ClientName,
    COUNT(d.Id) AS [Broj Dokumenata]
FROM FolderStaging f
INNER JOIN DocStaging d ON d.ParentId = f.NodeId
GROUP BY f.Id, f.NodeId, f.Name, f.CoreId, f.ClientName
ORDER BY COUNT(d.Id) DESC;
GO

-- Ukupan progress migracije
SELECT
    'Folderi' AS [Entitet],
    COUNT(*) AS [Ukupno],
    SUM(CASE WHEN Status = 'DONE' THEN 1 ELSE 0 END) AS [Završeno],
    SUM(CASE WHEN Status = 'ERROR' THEN 1 ELSE 0 END) AS [Greške],
    CAST(SUM(CASE WHEN Status = 'DONE' THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS DECIMAL(5,2)) AS [% Završeno]
FROM FolderStaging
UNION ALL
SELECT
    'Dokumenti' AS [Entitet],
    COUNT(*) AS [Ukupno],
    SUM(CASE WHEN Status = 'DONE' THEN 1 ELSE 0 END) AS [Završeno],
    SUM(CASE WHEN Status = 'ERROR' THEN 1 ELSE 0 END) AS [Greške],
    CAST(SUM(CASE WHEN Status = 'DONE' THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS DECIMAL(5,2)) AS [% Završeno]
FROM DocStaging;
GO

-- ============================================================================
-- 7. PERFORMANCE QUERIES
-- ============================================================================

-- Top 10 najsporijih dokumenata (na osnovu retry count-a)
SELECT TOP 10
    Id,
    NodeId,
    Name,
    Status,
    RetryCount,
    ErrorMsg
FROM DocStaging
WHERE RetryCount > 0
ORDER BY RetryCount DESC, UpdatedAt DESC;
GO

-- Dokumenti koji su dugo u IN_PROGRESS stanju (mogu biti "zaglavljeni")
-- Pretpostavka: ako je UpdatedAt stariji od 1 sat, dokument je vjerovatno zaglavljen
/*
SELECT
    Id,
    NodeId,
    Name,
    Status,
    UpdatedAt,
    DATEDIFF(MINUTE, CAST(UpdatedAt AS DATETIME), GETDATE()) AS [Minuta u IN_PROGRESS]
FROM DocStaging
WHERE Status = 'IN_PROGRESS'
  AND DATEDIFF(MINUTE, CAST(UpdatedAt AS DATETIME), GETDATE()) > 60
ORDER BY UpdatedAt ASC;
*/

-- ============================================================================
-- 8. CLEANUP & MAINTENANCE
-- ============================================================================

-- Resetuj stuck dokumente (status IN_PROGRESS duže od 1 sat)
/*
UPDATE DocStaging
SET Status = 'READY',
    RetryCount = RetryCount + 1,
    UpdatedAt = FORMAT(GETDATE(), 'yyyy-MM-dd HH:mm:ss')
WHERE Status = 'IN_PROGRESS'
  AND DATEDIFF(MINUTE, CAST(UpdatedAt AS DATETIME), GETDATE()) > 60;
*/

-- Resetuj sve greške na READY za retry
/*
UPDATE DocStaging
SET Status = 'READY',
    RetryCount = RetryCount + 1,
    ErrorMsg = NULL,
    UpdatedAt = FORMAT(GETDATE(), 'yyyy-MM-dd HH:mm:ss')
WHERE Status = 'ERROR';
*/

-- Obriši stare logove (starije od 30 dana)
/*
DELETE FROM AlfrescoMigration_Logger
WHERE CAST(LOG_DATE AS DATETIME) < DATEADD(DAY, -30, GETDATE());
*/

-- Update statistics za optimalne performanse
/*
UPDATE STATISTICS DocStaging WITH FULLSCAN;
UPDATE STATISTICS FolderStaging WITH FULLSCAN;
UPDATE STATISTICS AlfrescoMigration_Logger WITH FULLSCAN;
*/

-- ============================================================================
-- 9. EXPORT QUERIES (za reporting)
-- ============================================================================

-- Export dokumenata sa svim detaljima (za Excel/CSV)
SELECT
    Id,
    NodeId,
    Name,
    OriginalDocumentName,
    NewDocumentName,
    OriginalDocumentCode,
    NewDocumentCode,
    TipDosijea,
    CASE TargetDossierType
        WHEN 300 THEN 'Dosije paket računa'
        WHEN 400 THEN 'Dosije pravnog lica'
        WHEN 500 THEN 'Dosije fizičkog lica'
        WHEN 700 THEN 'Dosije depozita'
        WHEN 999 THEN 'Dosije - Unknown'
        ELSE 'N/A'
    END AS [Destination Dossier],
    CASE WHEN IsActive = 1 THEN 'Aktivni' ELSE 'Neaktivni' END AS [Status Dokumenta],
    CASE WHEN WillReceiveMigrationSuffix = 1 THEN 'Da' ELSE 'Ne' END AS [Sufiks Migracija],
    Source,
    CoreId,
    Status AS [Migration Status],
    CreatedAt,
    UpdatedAt
FROM DocStaging
ORDER BY Id;
GO

PRINT '============================================================================';
PRINT 'SVI KORISNI QUERY-JI SU SPREMNI ZA IZVRŠAVANJE!';
PRINT '============================================================================';
PRINT 'Koristi Ctrl+Shift+E u SSMS za izvršavanje selektovanih query-ja.';
PRINT '============================================================================';
GO
