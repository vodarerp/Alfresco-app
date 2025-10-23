-- =============================================
-- SQL Server Log Analysis Queries
-- =============================================
-- Version: 1.0
-- Description: Advanced queries for analyzing log4net logs
-- =============================================

USE [YourDatabaseName]
GO

PRINT '========================================';
PRINT 'LOG ANALYSIS QUERIES';
PRINT '========================================';
GO

-- =============================================
-- 1. Error Rate by Hour (Last 24 hours)
-- =============================================
PRINT '1. ERROR RATE BY HOUR (LAST 24H)';
GO

SELECT
    CAST([LOG_DATE] AS DATE) AS LogDate,
    DATEPART(HOUR, [LOG_DATE]) AS LogHour,
    COUNT(*) AS TotalLogs,
    SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS ErrorCount,
    SUM(CASE WHEN [LOG_LEVEL] = 'WARN' THEN 1 ELSE 0 END) AS WarningCount,
    CAST(SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS DECIMAL(5,2)) AS ErrorRate
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [LOG_DATE] >= DATEADD(HOUR, -24, GETUTCDATE())
GROUP BY CAST([LOG_DATE] AS DATE), DATEPART(HOUR, [LOG_DATE])
ORDER BY LogDate DESC, LogHour DESC;
GO

-- =============================================
-- 2. Worker Performance Comparison
-- =============================================
PRINT '2. WORKER PERFORMANCE COMPARISON';
GO

SELECT
    [WORKERID],
    COUNT(*) AS TotalLogs,
    COUNT(DISTINCT [BATCHID]) AS BatchesProcessed,
    COUNT(DISTINCT [DOCUMENTID]) AS DocumentsProcessed,
    SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS ErrorCount,
    CAST(SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) * 100.0 / COUNT(*) AS DECIMAL(5,2)) AS ErrorRate,
    MIN([LOG_DATE]) AS FirstActivity,
    MAX([LOG_DATE]) AS LastActivity,
    DATEDIFF(MINUTE, MIN([LOG_DATE]), MAX([LOG_DATE])) AS ActiveMinutes
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [WORKERID] IS NOT NULL
    AND [LOG_DATE] >= DATEADD(DAY, -1, GETUTCDATE())
GROUP BY [WORKERID]
ORDER BY ErrorRate DESC, TotalLogs DESC;
GO

-- =============================================
-- 3. Batch Processing Timeline
-- =============================================
PRINT '3. BATCH PROCESSING TIMELINE';
GO

SELECT
    [BATCHID],
    [WORKERID],
    MIN([LOG_DATE]) AS BatchStart,
    MAX([LOG_DATE]) AS BatchEnd,
    DATEDIFF(SECOND, MIN([LOG_DATE]), MAX([LOG_DATE])) AS DurationSeconds,
    COUNT(*) AS TotalLogs,
    COUNT(DISTINCT [DOCUMENTID]) AS DocumentsInBatch,
    SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS ErrorCount
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [BATCHID] IS NOT NULL
GROUP BY [BATCHID], [WORKERID]
ORDER BY BatchStart DESC;
GO

-- =============================================
-- 4. Document Processing Details
-- =============================================
PRINT '4. DOCUMENT PROCESSING DETAILS';
GO

SELECT TOP 20
    [DOCUMENTID],
    [WORKERID],
    [BATCHID],
    MIN([LOG_DATE]) AS ProcessingStart,
    MAX([LOG_DATE]) AS ProcessingEnd,
    DATEDIFF(MILLISECOND, MIN([LOG_DATE]), MAX([LOG_DATE])) AS ProcessingTimeMs,
    COUNT(*) AS LogEntries,
    SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS ErrorCount,
    MAX(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN [MESSAGE] ELSE NULL END) AS LastError
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [DOCUMENTID] IS NOT NULL
GROUP BY [DOCUMENTID], [WORKERID], [BATCHID]
ORDER BY ProcessingTimeMs DESC;
GO

-- =============================================
-- 5. Exception Pattern Analysis
-- =============================================
PRINT '5. EXCEPTION PATTERN ANALYSIS';
GO

WITH ExceptionPatterns AS (
    SELECT
        CASE
            WHEN [EXCEPTION] LIKE '%TimeoutException%' THEN 'Timeout'
            WHEN [EXCEPTION] LIKE '%SqlException%' THEN 'Database'
            WHEN [EXCEPTION] LIKE '%NullReferenceException%' THEN 'NullReference'
            WHEN [EXCEPTION] LIKE '%IOException%' THEN 'IO'
            WHEN [EXCEPTION] LIKE '%HttpRequestException%' THEN 'HTTP'
            WHEN [EXCEPTION] LIKE '%UnauthorizedException%' THEN 'Authorization'
            ELSE 'Other'
        END AS ExceptionType,
        [WORKERID],
        [LOG_DATE]
    FROM [dbo].[AlfrescoMigration_Logger]
    WHERE [EXCEPTION] IS NOT NULL
        AND [LOG_DATE] >= DATEADD(DAY, -7, GETUTCDATE())
)
SELECT
    ExceptionType,
    COUNT(*) AS OccurrenceCount,
    COUNT(DISTINCT [WORKERID]) AS AffectedWorkers,
    MIN([LOG_DATE]) AS FirstOccurrence,
    MAX([LOG_DATE]) AS LastOccurrence
FROM ExceptionPatterns
GROUP BY ExceptionType
ORDER BY OccurrenceCount DESC;
GO

-- =============================================
-- 6. Slow Operations (Top 20)
-- =============================================
PRINT '6. SLOW OPERATIONS (TOP 20)';
GO

WITH OperationTiming AS (
    SELECT
        [DOCUMENTID],
        [WORKERID],
        MIN([LOG_DATE]) AS StartTime,
        MAX([LOG_DATE]) AS EndTime,
        DATEDIFF(SECOND, MIN([LOG_DATE]), MAX([LOG_DATE])) AS DurationSeconds,
        STRING_AGG([MESSAGE], ' | ') WITHIN GROUP (ORDER BY [LOG_DATE]) AS Operations
    FROM [dbo].[AlfrescoMigration_Logger]
    WHERE [DOCUMENTID] IS NOT NULL
        AND [LOG_DATE] >= DATEADD(DAY, -1, GETUTCDATE())
    GROUP BY [DOCUMENTID], [WORKERID]
    HAVING COUNT(*) > 1
)
SELECT TOP 20
    [DOCUMENTID],
    [WORKERID],
    StartTime,
    EndTime,
    DurationSeconds,
    LEFT(Operations, 200) AS SampleOperations
FROM OperationTiming
WHERE DurationSeconds > 0
ORDER BY DurationSeconds DESC;
GO

-- =============================================
-- 7. Logger Activity Heatmap (by logger name)
-- =============================================
PRINT '7. LOGGER ACTIVITY HEATMAP';
GO

SELECT
    [LOGGER],
    COUNT(*) AS TotalLogs,
    SUM(CASE WHEN [LOG_LEVEL] = 'DEBUG' THEN 1 ELSE 0 END) AS Debug,
    SUM(CASE WHEN [LOG_LEVEL] = 'INFO' THEN 1 ELSE 0 END) AS Info,
    SUM(CASE WHEN [LOG_LEVEL] = 'WARN' THEN 1 ELSE 0 END) AS Warn,
    SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS Error,
    SUM(CASE WHEN [LOG_LEVEL] = 'FATAL' THEN 1 ELSE 0 END) AS Fatal
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [LOG_DATE] >= DATEADD(DAY, -1, GETUTCDATE())
GROUP BY [LOGGER]
ORDER BY TotalLogs DESC;
GO

-- =============================================
-- 8. Failed Documents (with full context)
-- =============================================
PRINT '8. FAILED DOCUMENTS (FULL CONTEXT)';
GO

WITH FailedDocs AS (
    SELECT DISTINCT [DOCUMENTID]
    FROM [dbo].[AlfrescoMigration_Logger]
    WHERE [LOG_LEVEL] = 'ERROR'
        AND [DOCUMENTID] IS NOT NULL
        AND [LOG_DATE] >= DATEADD(HOUR, -24, GETUTCDATE())
)
SELECT
    l.[DOCUMENTID],
    l.[WORKERID],
    l.[BATCHID],
    l.[LOG_DATE],
    l.[LOG_LEVEL],
    l.[LOGGER],
    l.[MESSAGE],
    LEFT(l.[EXCEPTION], 500) AS ExceptionPreview
FROM [dbo].[AlfrescoMigration_Logger] l
INNER JOIN FailedDocs fd ON l.[DOCUMENTID] = fd.[DOCUMENTID]
WHERE l.[LOG_DATE] >= DATEADD(HOUR, -24, GETUTCDATE())
ORDER BY l.[DOCUMENTID], l.[LOG_DATE];
GO

-- =============================================
-- 9. Peak Activity Times
-- =============================================
PRINT '9. PEAK ACTIVITY TIMES';
GO

SELECT
    DATEPART(HOUR, [LOG_DATE]) AS Hour,
    COUNT(*) AS LogCount,
    COUNT(DISTINCT [WORKERID]) AS ActiveWorkers,
    SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS ErrorCount,
    AVG(CAST(DATEDIFF(MILLISECOND, LAG([LOG_DATE]) OVER (ORDER BY [LOG_DATE]), [LOG_DATE]) AS FLOAT)) AS AvgGapMs
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [LOG_DATE] >= DATEADD(DAY, -7, GETUTCDATE())
GROUP BY DATEPART(HOUR, [LOG_DATE])
ORDER BY LogCount DESC;
GO

-- =============================================
-- 10. Application Instance Health
-- =============================================
PRINT '10. APPLICATION INSTANCE HEALTH';
GO

SELECT
    [APPINSTANCE],
    [HOSTNAME],
    COUNT(*) AS TotalLogs,
    COUNT(DISTINCT [WORKERID]) AS WorkersCount,
    MIN([LOG_DATE]) AS FirstSeen,
    MAX([LOG_DATE]) AS LastSeen,
    DATEDIFF(MINUTE, MIN([LOG_DATE]), MAX([LOG_DATE])) AS UptimeMinutes,
    SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS ErrorCount,
    SUM(CASE WHEN [LOG_LEVEL] = 'FATAL' THEN 1 ELSE 0 END) AS FatalCount
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [APPINSTANCE] IS NOT NULL
    AND [LOG_DATE] >= DATEADD(DAY, -1, GETUTCDATE())
GROUP BY [APPINSTANCE], [HOSTNAME]
ORDER BY LastSeen DESC;
GO

-- =============================================
-- 11. Correlation: DocStaging Errors vs Logs
-- =============================================
PRINT '11. CORRELATION: DOCSTAGING ERRORS VS LOGS';
GO

SELECT
    ds.[Id] AS DocStagingId,
    ds.[NodeId],
    ds.[Name],
    ds.[Status],
    ds.[RetryCount],
    ds.[ErrorMsg] AS StagingError,
    COUNT(l.[Id]) AS LogEntries,
    SUM(CASE WHEN l.[LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS LogErrors,
    MAX(l.[LOG_DATE]) AS LastLogTime,
    MAX(CASE WHEN l.[LOG_LEVEL] = 'ERROR' THEN l.[MESSAGE] ELSE NULL END) AS LastLogError
FROM [dbo].[DocStaging] ds
LEFT JOIN [dbo].[AlfrescoMigration_Logger] l ON CAST(ds.[Id] AS NVARCHAR(100)) = l.[DOCUMENTID]
WHERE ds.[Status] = 'ERROR'
    AND ds.[UpdatedAt] >= DATEADD(HOUR, -24, GETUTCDATE())
GROUP BY ds.[Id], ds.[NodeId], ds.[Name], ds.[Status], ds.[RetryCount], ds.[ErrorMsg]
ORDER BY ds.[UpdatedAt] DESC;
GO

-- =============================================
-- 12. Thread Activity Analysis
-- =============================================
PRINT '12. THREAD ACTIVITY ANALYSIS';
GO

SELECT
    [THREADID],
    [WORKERID],
    COUNT(*) AS LogCount,
    MIN([LOG_DATE]) AS FirstLog,
    MAX([LOG_DATE]) AS LastLog,
    DATEDIFF(SECOND, MIN([LOG_DATE]), MAX([LOG_DATE])) AS ThreadLifetimeSeconds,
    SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS ErrorCount
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [THREADID] IS NOT NULL
    AND [LOG_DATE] >= DATEADD(HOUR, -24, GETUTCDATE())
GROUP BY [THREADID], [WORKERID]
ORDER BY LogCount DESC;
GO

-- =============================================
-- 13. Log Retention Analysis
-- =============================================
PRINT '13. LOG RETENTION ANALYSIS';
GO

SELECT
    CAST([LOG_DATE] AS DATE) AS LogDate,
    COUNT(*) AS LogCount,
    SUM(DATALENGTH([MESSAGE]) + COALESCE(DATALENGTH([EXCEPTION]), 0)) / 1024.0 / 1024.0 AS SizeMB,
    MIN([LOG_DATE]) AS FirstLog,
    MAX([LOG_DATE]) AS LastLog
FROM [dbo].[AlfrescoMigration_Logger]
GROUP BY CAST([LOG_DATE] AS DATE)
ORDER BY LogDate DESC;
GO

PRINT '========================================';
PRINT 'Log analysis queries completed!';
PRINT 'RECOMMENDATION: Archive logs older than 90 days';
PRINT '========================================';
GO
