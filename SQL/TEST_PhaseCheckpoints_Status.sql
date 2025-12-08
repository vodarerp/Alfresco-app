-- =====================================================
-- TEST SCRIPT: PhaseCheckpoints Status Monitoring
-- =====================================================
-- Ovaj script proverava status i progress svih faza
-- Izvršite ga dok migracija radi da vidite real-time stanje
-- =====================================================

-- 1. Trenutni status svih faza
SELECT
    Phase,
    CASE Phase
        WHEN 1 THEN 'FolderDiscovery/DocumentSearch'
        WHEN 2 THEN 'DocumentDiscovery'
        WHEN 3 THEN 'FolderPreparation'
        WHEN 4 THEN 'Move'
        ELSE 'Unknown'
    END AS PhaseName,
    CASE Status
        WHEN 0 THEN 'NotStarted'
        WHEN 1 THEN 'InProgress'
        WHEN 2 THEN 'Completed'
        WHEN 3 THEN 'Failed'
        ELSE 'Unknown'
    END AS StatusText,
    TotalItems,
    TotalProcessed,
    CASE
        WHEN TotalItems IS NOT NULL AND TotalItems > 0
        THEN CAST((TotalProcessed * 100.0 / TotalItems) AS DECIMAL(5,2))
        ELSE NULL
    END AS ProgressPercentage,
    LastProcessedIndex,
    StartedAt,
    CompletedAt,
    DATEDIFF(SECOND, StartedAt, ISNULL(CompletedAt, GETUTCDATE())) AS DurationSeconds,
    UpdatedAt,
    ErrorMessage
FROM PhaseCheckpoints
ORDER BY Phase;

-- 2. Provera da li faze imaju Status = InProgress (1)
PRINT '';
PRINT '=== Faze u InProgress statusu ===';
SELECT
    Phase,
    CASE Phase
        WHEN 1 THEN 'FolderDiscovery/DocumentSearch'
        WHEN 2 THEN 'DocumentDiscovery'
        WHEN 3 THEN 'FolderPreparation'
        WHEN 4 THEN 'Move'
    END AS PhaseName,
    TotalProcessed,
    TotalItems,
    UpdatedAt
FROM PhaseCheckpoints
WHERE Status = 1;  -- InProgress

IF @@ROWCOUNT = 0
BEGIN
    PRINT 'Nema faza u InProgress statusu!';
    PRINT 'Mogući uzroci:';
    PRINT '1. Migracija nije pokrenuta';
    PRINT '2. Sve faze su završene';
    PRINT '3. MigrationWorker ne postavlja Status na InProgress';
END;

-- 3. Provera da li se TotalProcessed ažurira (poslednjih 10 update-a)
PRINT '';
PRINT '=== Poslednje ažurirane faze (top 10) ===';
SELECT TOP 10
    Phase,
    CASE Phase
        WHEN 1 THEN 'FolderDiscovery/DocumentSearch'
        WHEN 2 THEN 'DocumentDiscovery'
        WHEN 3 THEN 'FolderPreparation'
        WHEN 4 THEN 'Move'
    END AS PhaseName,
    TotalProcessed,
    TotalItems,
    UpdatedAt,
    DATEDIFF(SECOND, UpdatedAt, GETUTCDATE()) AS SecondsAgo
FROM PhaseCheckpoints
ORDER BY UpdatedAt DESC;

-- 4. Provera da li se Status menja tokom vremena
PRINT '';
PRINT '=== Status za svaku fazu ===';
SELECT
    Phase,
    CASE Status
        WHEN 0 THEN 'NotStarted'
        WHEN 1 THEN 'InProgress'
        WHEN 2 THEN 'Completed'
        WHEN 3 THEN 'Failed'
    END AS StatusText,
    StartedAt,
    UpdatedAt,
    CASE
        WHEN Status = 1 AND DATEDIFF(SECOND, UpdatedAt, GETUTCDATE()) > 10
        THEN 'WARNING: InProgress ali nije ažurirano više od 10 sekundi!'
        WHEN Status = 1
        THEN 'OK: InProgress i nedavno ažurirano'
        ELSE 'N/A'
    END AS StatusCheck
FROM PhaseCheckpoints
ORDER BY Phase;

-- 5. Simulacija CalculatePhaseProgress logike
PRINT '';
PRINT '=== Simulacija UI Progress Calculation ===';
SELECT
    Phase,
    CASE Phase
        WHEN 1 THEN 'FolderDiscovery/DocumentSearch'
        WHEN 2 THEN 'DocumentDiscovery'
        WHEN 3 THEN 'FolderPreparation'
        WHEN 4 THEN 'Move'
    END AS PhaseName,
    Status,
    TotalItems,
    TotalProcessed,
    CASE
        -- If TotalItems is known
        WHEN TotalItems IS NOT NULL AND TotalItems > 0
        THEN CAST((TotalProcessed * 100.0 / TotalItems) AS INT)
        -- If TotalItems unknown but has processed items
        WHEN Status = 1 AND TotalProcessed > 0
        THEN CAST(LEAST(95, 10 + (TotalProcessed / 100)) AS INT)
        -- Fixed values based on status
        WHEN Status = 0 THEN 0   -- NotStarted
        WHEN Status = 1 THEN 10  -- InProgress (no items yet)
        WHEN Status = 2 THEN 100 -- Completed
        WHEN Status = 3 THEN 0   -- Failed
        ELSE 0
    END AS CalculatedProgress,
    CASE
        WHEN TotalItems IS NOT NULL AND TotalItems > 0 THEN 'Using TotalItems formula'
        WHEN Status = 1 AND TotalProcessed > 0 THEN 'Using incremental formula'
        ELSE 'Using fixed status value'
    END AS ProgressMethod
FROM PhaseCheckpoints
ORDER BY Phase;

-- 6. Dijagnostika
PRINT '';
PRINT '=== DIJAGNOSTIKA ===';

DECLARE @InProgressCount INT;
SELECT @InProgressCount = COUNT(*) FROM PhaseCheckpoints WHERE Status = 1;

IF @InProgressCount = 0
BEGIN
    PRINT 'PROBLEM: Nijedna faza nije u InProgress statusu!';
    PRINT '';
    PRINT 'Provera:';
    PRINT '1. Da li je MigrationWorker pokrenut?';
    PRINT '2. Proverite logove za ERROR poruke';
    PRINT '3. Da li ExecutePhaseAsync postavlja Status = 1?';

    SELECT
        'Trenutni status faza:' AS Info,
        Phase,
        CASE Status WHEN 0 THEN 'NotStarted' WHEN 1 THEN 'InProgress' WHEN 2 THEN 'Completed' WHEN 3 THEN 'Failed' END AS Status
    FROM PhaseCheckpoints;
END
ELSE
BEGIN
    PRINT 'OK: Ima ' + CAST(@InProgressCount AS VARCHAR) + ' faza u InProgress statusu';

    -- Provera da li se ažurira
    DECLARE @RecentUpdateCount INT;
    SELECT @RecentUpdateCount = COUNT(*)
    FROM PhaseCheckpoints
    WHERE Status = 1 AND DATEDIFF(SECOND, UpdatedAt, GETUTCDATE()) < 10;

    IF @RecentUpdateCount = 0
    BEGIN
        PRINT 'WARNING: InProgress faze nisu ažurirane u poslednjih 10 sekundi!';
        PRINT 'Mogući problem sa checkpoint tracking-om.';
    END
    ELSE
    BEGIN
        PRINT 'OK: InProgress faze se aktivno ažuriraju';
    END;
END;

PRINT '';
PRINT '=== Kraj testa ===';
