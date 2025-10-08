-- ============================================================================
-- Monitoring and Troubleshooting Queries
-- ============================================================================
-- Purpose: Useful queries for monitoring migration progress and troubleshooting
-- ============================================================================

-- ============================================================================
-- 1. Overall Status Summary
-- ============================================================================

SELECT 'Documents' AS Type, Status, COUNT(*) AS Count
FROM DocStaging
GROUP BY Status
UNION ALL
SELECT 'Folders' AS Type, Status, COUNT(*) AS Count
FROM FolderStaging
GROUP BY Status
ORDER BY Type, Status;


-- ============================================================================
-- 2. Migration Progress Dashboard
-- ============================================================================

SELECT * FROM vw_MigrationProgress;


-- ============================================================================
-- 3. Find Stuck Items
-- ============================================================================

-- Using the view
SELECT * FROM vw_StuckItems
ORDER BY MinutesStuck DESC;

-- Alternative: Parameterized query
SELECT
    'DocStaging' AS TableName,
    Id, NodeId, Name, Status, UpdatedAt,
    ROUND((SYSTIMESTAMP - UpdatedAt) * 24 * 60) AS MinutesStuck
FROM DocStaging
WHERE Status = 'IN PROGRESS'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '10' MINUTE;


-- ============================================================================
-- 4. Error Analysis
-- ============================================================================

-- Top 10 most common errors
SELECT * FROM vw_ErrorAnalysis
FETCH FIRST 10 ROWS ONLY;

-- Detailed error list for investigation
SELECT Id, NodeId, Name, ErrorMsg, RetryCount, UpdatedAt
FROM DocStaging
WHERE Status = 'ERROR'
ORDER BY UpdatedAt DESC
FETCH FIRST 20 ROWS ONLY;


-- ============================================================================
-- 5. Processing Rate Analysis
-- ============================================================================

-- Documents processed per hour (last 24 hours)
SELECT
    TO_CHAR(UpdatedAt, 'YYYY-MM-DD HH24') AS Hour,
    COUNT(*) AS DocsProcessed,
    ROUND(AVG(Size)/1024/1024, 2) AS AvgSizeMB
FROM DocStaging
WHERE Status = 'DONE'
  AND UpdatedAt >= SYSTIMESTAMP - INTERVAL '24' HOUR
GROUP BY TO_CHAR(UpdatedAt, 'YYYY-MM-DD HH24')
ORDER BY Hour DESC;


-- ============================================================================
-- 6. Checkpoint Status
-- ============================================================================

SELECT
    ServiceName,
    TotalProcessed,
    TotalFailed,
    BatchCounter,
    TO_CHAR(UpdatedAt, 'YYYY-MM-DD HH24:MI:SS') AS LastCheckpoint,
    ROUND((SYSTIMESTAMP - UpdatedAt) * 24 * 60) AS MinutesSinceLastUpdate
FROM MigrationCheckpoint
ORDER BY ServiceName;


-- ============================================================================
-- 7. Retry Statistics
-- ============================================================================

SELECT * FROM vw_RetryStatistics
ORDER BY TableName, RetryCount;


-- ============================================================================
-- 8. Slowest Items to Process
-- ============================================================================

-- Documents that took longest to process
SELECT
    NodeId,
    Name,
    Status,
    ROUND((UpdatedAt - CreatedAt) * 24 * 60 * 60) AS ProcessingTimeSeconds,
    RetryCount
FROM DocStaging
WHERE Status IN ('DONE', 'ERROR')
  AND (UpdatedAt - CreatedAt) > INTERVAL '1' MINUTE
ORDER BY (UpdatedAt - CreatedAt) DESC
FETCH FIRST 20 ROWS ONLY;


-- ============================================================================
-- 9. Items by Parent Folder
-- ============================================================================

-- Group documents by parent folder to see distribution
SELECT
    ParentId,
    Status,
    COUNT(*) AS Count,
    ROUND(SUM(Size)/1024/1024, 2) AS TotalSizeMB
FROM DocStaging
GROUP BY ParentId, Status
ORDER BY COUNT(*) DESC
FETCH FIRST 20 ROWS ONLY;


-- ============================================================================
-- 10. Performance Metrics (Last Hour)
-- ============================================================================

SELECT
    'Documents' AS Metric,
    COUNT(*) AS Processed,
    ROUND(COUNT(*) / 60.0, 2) AS PerMinute,
    ROUND(SUM(Size)/1024/1024, 2) AS TotalMB,
    ROUND(AVG(Size)/1024/1024, 2) AS AvgMB
FROM DocStaging
WHERE Status = 'DONE'
  AND UpdatedAt >= SYSTIMESTAMP - INTERVAL '1' HOUR;


-- ============================================================================
-- 11. System Health Check
-- ============================================================================

-- Check for potential issues
SELECT
    'Stuck Documents' AS Issue,
    COUNT(*) AS Count,
    'Critical' AS Severity
FROM DocStaging
WHERE Status = 'IN PROGRESS'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '30' MINUTE

UNION ALL

SELECT
    'High Retry Count' AS Issue,
    COUNT(*) AS Count,
    'Warning' AS Severity
FROM DocStaging
WHERE RetryCount >= 3

UNION ALL

SELECT
    'Large Error Queue' AS Issue,
    COUNT(*) AS Count,
    CASE WHEN COUNT(*) > 100 THEN 'Critical' ELSE 'Warning' END AS Severity
FROM DocStaging
WHERE Status = 'ERROR';


-- ============================================================================
-- 12. Estimate Time to Completion
-- ============================================================================

WITH RecentRate AS (
    SELECT
        COUNT(*) AS ProcessedLastHour,
        COUNT(*) / 60.0 AS ProcessedPerMinute
    FROM DocStaging
    WHERE Status = 'DONE'
      AND UpdatedAt >= SYSTIMESTAMP - INTERVAL '1' HOUR
),
Remaining AS (
    SELECT COUNT(*) AS RemainingCount
    FROM DocStaging
    WHERE Status IN ('READY', 'IN PROGRESS')
)
SELECT
    r.RemainingCount AS RemainingItems,
    rr.ProcessedLastHour AS ProcessedLastHour,
    ROUND(rr.ProcessedPerMinute, 2) AS ItemsPerMinute,
    CASE
        WHEN rr.ProcessedPerMinute > 0 THEN
            ROUND(r.RemainingCount / rr.ProcessedPerMinute / 60, 2)
        ELSE NULL
    END AS EstimatedHoursToComplete
FROM Remaining r, RecentRate rr;


-- ============================================================================
-- 13. Space Usage Analysis
-- ============================================================================

SELECT
    segment_name AS TableName,
    ROUND(bytes/1024/1024, 2) AS SizeMB,
    blocks,
    extents
FROM user_segments
WHERE segment_name IN ('DOCSTAGING', 'FOLDERSTAGING', 'MIGRATIONCHECKPOINT', 'ALFRESCOMIGRATION_LOGGER')
ORDER BY bytes DESC;


-- ============================================================================
-- 14. Application Log Analysis
-- ============================================================================

-- Recent errors (last 24 hours)
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, DOCUMENTID, EXCEPTION
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL IN ('ERROR', 'FATAL')
  AND LOG_DATE >= SYSTIMESTAMP - INTERVAL '24' HOUR
ORDER BY LOG_DATE DESC
FETCH FIRST 50 ROWS ONLY;

-- Log summary by level (last 24 hours)
SELECT
    LOG_LEVEL,
    COUNT(*) AS Count,
    MIN(LOG_DATE) AS FirstLog,
    MAX(LOG_DATE) AS LastLog
FROM AlfrescoMigration_Logger
WHERE LOG_DATE >= SYSTIMESTAMP - INTERVAL '24' HOUR
GROUP BY LOG_LEVEL
ORDER BY
    CASE LOG_LEVEL
        WHEN 'FATAL' THEN 1
        WHEN 'ERROR' THEN 2
        WHEN 'WARN' THEN 3
        WHEN 'INFO' THEN 4
        WHEN 'DEBUG' THEN 5
    END;

-- Error summary by logger (last 7 days)
SELECT
    LOGGER,
    COUNT(*) AS ErrorCount,
    MAX(LOG_DATE) AS LastError,
    COUNT(DISTINCT DOCUMENTID) AS AffectedDocuments
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL = 'ERROR'
  AND LOG_DATE >= SYSTIMESTAMP - INTERVAL '7' DAY
GROUP BY LOGGER
ORDER BY ErrorCount DESC
FETCH FIRST 10 ROWS ONLY;
