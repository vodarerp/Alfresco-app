-- ============================================================================
-- Views and Helper Objects
-- ============================================================================
-- Purpose: Create useful views for monitoring and reporting
-- ============================================================================

-- ============================================================================
-- View: Document Status Summary
-- ============================================================================

CREATE OR REPLACE VIEW vw_DocStaging_Summary AS
SELECT
    Status,
    COUNT(*) AS ItemCount,
    ROUND(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER (), 2) AS Percentage,
    MIN(CreatedAt) AS OldestItem,
    MAX(CreatedAt) AS NewestItem,
    AVG(Size) AS AvgSizeBytes,
    SUM(Size) AS TotalSizeBytes
FROM DocStaging
GROUP BY Status;

-- COMMENT ON VIEW vw_DocStaging_Summary IS
--     'Summary view showing document counts and statistics by status';

-- ============================================================================
-- View: Folder Status Summary
-- ============================================================================

CREATE OR REPLACE VIEW vw_FolderStaging_Summary AS
SELECT
    Status,
    COUNT(*) AS ItemCount,
    ROUND(COUNT(*) * 100.0 / SUM(COUNT(*)) OVER (), 2) AS Percentage,
    MIN(CreatedAt) AS OldestItem,
    MAX(CreatedAt) AS NewestItem
FROM FolderStaging
GROUP BY Status;

-- COMMENT ON VIEW vw_FolderStaging_Summary IS
--     'Summary view showing folder counts by status';

-- ============================================================================
-- View: Stuck Items Detection
-- ============================================================================

CREATE OR REPLACE VIEW vw_StuckItems AS
SELECT
    'DocStaging' AS TableName,
    Id,
    NodeId,
    Name,
    Status,
    UpdatedAt,
    EXTRACT(MINUTE FROM (SYSTIMESTAMP - UpdatedAt)) AS MinutesStuck,
    RetryCount,
    ErrorMsg
FROM DocStaging
WHERE Status = 'IN PROGRESS'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '10' MINUTE

UNION ALL

SELECT
    'FolderStaging' AS TableName,
    Id,
    NodeId,
    Name,
    Status,
    UpdatedAt,
    EXTRACT(MINUTE FROM (SYSTIMESTAMP - UpdatedAt)) AS MinutesStuck,
    RetryCount,
    Error AS ErrorMsg
FROM FolderStaging
WHERE Status = 'IN PROGRESS'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '10' MINUTE;

-- COMMENT ON VIEW vw_StuckItems IS
--     'Shows all items stuck in IN PROGRESS status for more than 10 minutes';

-- ============================================================================
-- View: Error Analysis
-- ============================================================================

CREATE OR REPLACE VIEW vw_ErrorAnalysis AS
SELECT
    'DocStaging' AS TableName,
    SUBSTR(ErrorMsg, 1, 100) AS ErrorSnippet,
    COUNT(*) AS ErrorCount,
    AVG(RetryCount) AS AvgRetries,
    MIN(UpdatedAt) AS FirstError,
    MAX(UpdatedAt) AS LastError
FROM DocStaging
WHERE Status = 'ERROR'
  AND ErrorMsg IS NOT NULL
GROUP BY SUBSTR(ErrorMsg, 1, 100)

UNION ALL

SELECT
    'FolderStaging' AS TableName,
    SUBSTR(Error, 1, 100) AS ErrorSnippet,
    COUNT(*) AS ErrorCount,
    AVG(RetryCount) AS AvgRetries,
    MIN(UpdatedAt) AS FirstError,
    MAX(UpdatedAt) AS LastError
FROM FolderStaging
WHERE Status = 'ERROR'
  AND Error IS NOT NULL
GROUP BY SUBSTR(Error, 1, 100)

ORDER BY ErrorCount DESC;

-- COMMENT ON VIEW vw_ErrorAnalysis IS
--     'Groups similar errors together for analysis';

-- ============================================================================
-- View: Migration Progress
-- ============================================================================

CREATE OR REPLACE VIEW vw_MigrationProgress AS
SELECT
    c.ServiceName,
    c.TotalProcessed,
    c.TotalFailed,
    c.BatchCounter,
    c.UpdatedAt AS LastUpdate,
    CASE c.ServiceName
        WHEN 'FolderDiscovery' THEN
            (SELECT COUNT(*) FROM FolderStaging WHERE Status = 'READY')
        WHEN 'DocumentDiscovery' THEN
            (SELECT COUNT(*) FROM FolderStaging WHERE Status = 'READY')
        WHEN 'Move' THEN
            (SELECT COUNT(*) FROM DocStaging WHERE Status = 'READY')
    END AS ItemsRemaining,
    CASE c.ServiceName
        WHEN 'FolderDiscovery' THEN
            (SELECT COUNT(*) FROM FolderStaging WHERE Status = 'IN PROGRESS')
        WHEN 'DocumentDiscovery' THEN
            (SELECT COUNT(*) FROM FolderStaging WHERE Status = 'IN PROGRESS')
        WHEN 'Move' THEN
            (SELECT COUNT(*) FROM DocStaging WHERE Status = 'IN PROGRESS')
    END AS ItemsInProgress,
    ROUND(
        c.TotalProcessed * 100.0 /
        NULLIF(c.TotalProcessed + CASE c.ServiceName
            WHEN 'FolderDiscovery' THEN
                (SELECT COUNT(*) FROM FolderStaging WHERE Status IN ('READY', 'IN PROGRESS'))
            WHEN 'DocumentDiscovery' THEN
                (SELECT COUNT(*) FROM FolderStaging WHERE Status IN ('READY', 'IN PROGRESS'))
            WHEN 'Move' THEN
                (SELECT COUNT(*) FROM DocStaging WHERE Status IN ('READY', 'IN PROGRESS'))
        END, 0),
    2) AS PercentComplete
FROM MigrationCheckpoint c;

-- COMMENT ON VIEW vw_MigrationProgress IS
--     'Shows overall migration progress for all services';

-- ============================================================================
-- View: Retry Statistics
-- ============================================================================

CREATE OR REPLACE VIEW vw_RetryStatistics AS
SELECT
    'DocStaging' AS TableName,
    RetryCount,
    COUNT(*) AS ItemCount,
    SUM(CASE WHEN Status = 'READY' THEN 1 ELSE 0 END) AS Ready,
    SUM(CASE WHEN Status = 'IN PROGRESS' THEN 1 ELSE 0 END) AS InProgress,
    SUM(CASE WHEN Status = 'DONE' THEN 1 ELSE 0 END) AS Done,
    SUM(CASE WHEN Status = 'ERROR' THEN 1 ELSE 0 END) AS Error
FROM DocStaging
WHERE RetryCount > 0
GROUP BY RetryCount

UNION ALL

SELECT
    'FolderStaging' AS TableName,
    RetryCount,
    COUNT(*) AS ItemCount,
    SUM(CASE WHEN Status = 'READY' THEN 1 ELSE 0 END) AS Ready,
    SUM(CASE WHEN Status = 'IN PROGRESS' THEN 1 ELSE 0 END) AS InProgress,
    SUM(CASE WHEN Status = 'PROCESSED' THEN 1 ELSE 0 END) AS Processed,
    SUM(CASE WHEN Status = 'ERROR' THEN 1 ELSE 0 END) AS Error
FROM FolderStaging
WHERE RetryCount > 0
GROUP BY RetryCount

ORDER BY TableName, RetryCount;

-- COMMENT ON VIEW vw_RetryStatistics IS
--     'Shows retry statistics for items that have been retried';

COMMIT;
