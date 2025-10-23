-- =============================================
-- SQL Server Useful Queries for Migration Monitoring
-- =============================================
-- Version: 1.0
-- Description: Common queries for monitoring migration progress
-- =============================================

USE [YourDatabaseName]
GO

-- =============================================
-- 1. Check Migration Status Summary
-- =============================================
PRINT '========================================';
PRINT '1. MIGRATION STATUS SUMMARY';
PRINT '========================================';
GO

SELECT
    'DocStaging' AS TableName,
    [Status],
    COUNT(*) AS Count,
    SUM(CASE WHEN RetryCount > 0 THEN 1 ELSE 0 END) AS WithRetries,
    AVG(CAST(RetryCount AS FLOAT)) AS AvgRetries
FROM [dbo].[DocStaging]
GROUP BY [Status]
ORDER BY [Status];

SELECT
    'FolderStaging' AS TableName,
    [Status],
    COUNT(*) AS Count,
    SUM(CASE WHEN RetryCount > 0 THEN 1 ELSE 0 END) AS WithRetries,
    AVG(CAST(RetryCount AS FLOAT)) AS AvgRetries
FROM [dbo].[FolderStaging]
GROUP BY [Status]
ORDER BY [Status];
GO

-- =============================================
-- 2. Find Documents Ready for Processing
-- =============================================
PRINT '========================================';
PRINT '2. DOCUMENTS READY FOR PROCESSING';
PRINT '========================================';
GO

SELECT TOP 10
    [Id],
    [NodeId],
    [Name],
    [Status],
    [RetryCount],
    [FromPath],
    [CreatedAt]
FROM [dbo].[DocStaging]
WHERE [Status] = 'READY'
ORDER BY [CreatedAt] ASC;
GO

-- =============================================
-- 3. Find Failed Documents with Errors
-- =============================================
PRINT '========================================';
PRINT '3. FAILED DOCUMENTS WITH ERRORS';
PRINT '========================================';
GO

SELECT TOP 20
    [Id],
    [NodeId],
    [Name],
    [Status],
    [RetryCount],
    [ErrorMsg],
    [UpdatedAt]
FROM [dbo].[DocStaging]
WHERE [Status] = 'ERROR'
ORDER BY [UpdatedAt] DESC;
GO

-- =============================================
-- 4. Documents by Source System
-- =============================================
PRINT '========================================';
PRINT '4. DOCUMENTS BY SOURCE SYSTEM';
PRINT '========================================';
GO

SELECT
    [Source],
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN [Status] = 'DONE' THEN 1 ELSE 0 END) AS Completed,
    SUM(CASE WHEN [Status] = 'ERROR' THEN 1 ELSE 0 END) AS Failed,
    SUM(CASE WHEN [Status] = 'READY' THEN 1 ELSE 0 END) AS Ready
FROM [dbo].[DocStaging]
GROUP BY [Source]
ORDER BY TotalCount DESC;
GO

-- =============================================
-- 5. Folders by Client Type
-- =============================================
PRINT '========================================';
PRINT '5. FOLDERS BY CLIENT TYPE';
PRINT '========================================';
GO

SELECT
    [ClientType],
    COUNT(*) AS TotalCount,
    SUM(CASE WHEN [Status] = 'DONE' THEN 1 ELSE 0 END) AS Completed,
    SUM(CASE WHEN [Status] = 'ERROR' THEN 1 ELSE 0 END) AS Failed,
    SUM(CASE WHEN [Status] = 'READY' THEN 1 ELSE 0 END) AS Ready
FROM [dbo].[FolderStaging]
GROUP BY [ClientType]
ORDER BY TotalCount DESC;
GO

-- =============================================
-- 6. Migration Progress Over Time (Last 24 hours)
-- =============================================
PRINT '========================================';
PRINT '6. MIGRATION PROGRESS (LAST 24 HOURS)';
PRINT '========================================';
GO

SELECT
    CAST([UpdatedAt] AS DATE) AS ProcessDate,
    DATEPART(HOUR, [UpdatedAt]) AS ProcessHour,
    COUNT(*) AS ProcessedCount,
    [Status]
FROM [dbo].[DocStaging]
WHERE [UpdatedAt] >= DATEADD(HOUR, -24, GETUTCDATE())
GROUP BY CAST([UpdatedAt] AS DATE), DATEPART(HOUR, [UpdatedAt]), [Status]
ORDER BY ProcessDate DESC, ProcessHour DESC;
GO

-- =============================================
-- 7. Top Documents with Most Retries
-- =============================================
PRINT '========================================';
PRINT '7. TOP DOCUMENTS WITH MOST RETRIES';
PRINT '========================================';
GO

SELECT TOP 10
    [Id],
    [NodeId],
    [Name],
    [Status],
    [RetryCount],
    [ErrorMsg],
    [UpdatedAt]
FROM [dbo].[DocStaging]
WHERE [RetryCount] > 0
ORDER BY [RetryCount] DESC, [UpdatedAt] DESC;
GO

-- =============================================
-- 8. Migration Checkpoint Status
-- =============================================
PRINT '========================================';
PRINT '8. MIGRATION CHECKPOINT STATUS';
PRINT '========================================';
GO

SELECT
    [ServiceName],
    [LastProcessedId],
    [LastProcessedAt],
    [TotalProcessed],
    [TotalFailed],
    [BatchCounter],
    [UpdatedAt],
    DATEDIFF(MINUTE, [UpdatedAt], GETUTCDATE()) AS MinutesSinceLastUpdate
FROM [dbo].[MigrationCheckpoint]
ORDER BY [ServiceName];
GO

-- =============================================
-- 9. Documents Requiring Type Transformation
-- =============================================
PRINT '========================================';
PRINT '9. DOCUMENTS REQUIRING TYPE TRANSFORMATION';
PRINT '========================================';
GO

SELECT
    [Id],
    [NodeId],
    [Name],
    [DocumentType],
    [FinalDocumentType],
    [RequiresTypeTransformation],
    [Status]
FROM [dbo].[DocStaging]
WHERE [RequiresTypeTransformation] = 1
ORDER BY [Id];
GO

-- =============================================
-- 10. Overall Migration Statistics
-- =============================================
PRINT '========================================';
PRINT '10. OVERALL MIGRATION STATISTICS';
PRINT '========================================';
GO

SELECT
    'Total Documents' AS Metric,
    COUNT(*) AS Value
FROM [dbo].[DocStaging]
UNION ALL
SELECT
    'Total Folders',
    COUNT(*)
FROM [dbo].[FolderStaging]
UNION ALL
SELECT
    'Documents Completed',
    COUNT(*)
FROM [dbo].[DocStaging]
WHERE [Status] = 'DONE'
UNION ALL
SELECT
    'Documents Failed',
    COUNT(*)
FROM [dbo].[DocStaging]
WHERE [Status] = 'ERROR'
UNION ALL
SELECT
    'Documents Ready',
    COUNT(*)
FROM [dbo].[DocStaging]
WHERE [Status] = 'READY'
UNION ALL
SELECT
    'Folders Completed',
    COUNT(*)
FROM [dbo].[FolderStaging]
WHERE [Status] = 'DONE'
UNION ALL
SELECT
    'Total Retries (Docs)',
    SUM(RetryCount)
FROM [dbo].[DocStaging]
UNION ALL
SELECT
    'Total Retries (Folders)',
    SUM(RetryCount)
FROM [dbo].[FolderStaging];
GO

-- =============================================
-- 11. Find Orphaned Documents (Parent folder not in staging)
-- =============================================
PRINT '========================================';
PRINT '11. ORPHANED DOCUMENTS CHECK';
PRINT '========================================';
GO

SELECT
    d.[Id],
    d.[NodeId],
    d.[Name],
    d.[ParentId],
    d.[Status]
FROM [dbo].[DocStaging] d
WHERE d.[ParentId] NOT IN (SELECT [NodeId] FROM [dbo].[FolderStaging] WHERE [NodeId] IS NOT NULL)
    AND d.[Status] <> 'DONE'
ORDER BY d.[Id];
GO

-- =============================================
-- 12. Performance Metrics - Processing Speed
-- =============================================
PRINT '========================================';
PRINT '12. PROCESSING SPEED METRICS';
PRINT '========================================';
GO

WITH RecentDocs AS (
    SELECT
        [Status],
        [UpdatedAt],
        LAG([UpdatedAt]) OVER (ORDER BY [UpdatedAt]) AS PrevUpdateTime
    FROM [dbo].[DocStaging]
    WHERE [UpdatedAt] >= DATEADD(HOUR, -1, GETUTCDATE())
        AND [Status] IN ('DONE', 'ERROR')
)
SELECT
    AVG(DATEDIFF(MILLISECOND, PrevUpdateTime, UpdatedAt)) AS AvgProcessingTimeMs,
    MIN(DATEDIFF(MILLISECOND, PrevUpdateTime, UpdatedAt)) AS MinProcessingTimeMs,
    MAX(DATEDIFF(MILLISECOND, PrevUpdateTime, UpdatedAt)) AS MaxProcessingTimeMs,
    COUNT(*) AS TotalProcessed
FROM RecentDocs
WHERE PrevUpdateTime IS NOT NULL;
GO

-- =============================================
-- 13. Log Analysis - Recent Errors
-- =============================================
PRINT '========================================';
PRINT '13. RECENT LOG ERRORS';
PRINT '========================================';
GO

SELECT TOP 20
    [Id],
    [LOG_DATE],
    [LOG_LEVEL],
    [LOGGER],
    [MESSAGE],
    [WORKERID],
    [BATCHID],
    [DOCUMENTID]
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [LOG_LEVEL] = 'ERROR'
ORDER BY [LOG_DATE] DESC;
GO

-- =============================================
-- 14. Log Analysis - Activity by Worker
-- =============================================
PRINT '========================================';
PRINT '14. LOG ACTIVITY BY WORKER';
PRINT '========================================';
GO

SELECT
    [WORKERID],
    COUNT(*) AS TotalLogs,
    SUM(CASE WHEN [LOG_LEVEL] = 'ERROR' THEN 1 ELSE 0 END) AS Errors,
    SUM(CASE WHEN [LOG_LEVEL] = 'WARN' THEN 1 ELSE 0 END) AS Warnings,
    SUM(CASE WHEN [LOG_LEVEL] = 'INFO' THEN 1 ELSE 0 END) AS Info,
    MIN([LOG_DATE]) AS FirstLog,
    MAX([LOG_DATE]) AS LastLog
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [WORKERID] IS NOT NULL
GROUP BY [WORKERID]
ORDER BY TotalLogs DESC;
GO

-- =============================================
-- 15. Log Analysis - Most Common Errors
-- =============================================
PRINT '========================================';
PRINT '15. MOST COMMON LOG ERRORS';
PRINT '========================================';
GO

SELECT TOP 10
    LEFT([MESSAGE], 100) AS ErrorMessage,
    COUNT(*) AS OccurrenceCount,
    MAX([LOG_DATE]) AS LastOccurred,
    COUNT(DISTINCT [WORKERID]) AS AffectedWorkers
FROM [dbo].[AlfrescoMigration_Logger]
WHERE [LOG_LEVEL] = 'ERROR'
GROUP BY LEFT([MESSAGE], 100)
ORDER BY OccurrenceCount DESC;
GO

PRINT '========================================';
PRINT 'Useful queries completed!';
PRINT '========================================';
GO
