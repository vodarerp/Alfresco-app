-- ============================================================================
-- Log Table for log4net (SQL Server)
-- ============================================================================
-- Purpose: Store application logs for debugging and monitoring
-- ============================================================================

-- Drop table if exists
IF OBJECT_ID('dbo.AlfrescoMigration_Logger', 'U') IS NOT NULL
    DROP TABLE dbo.AlfrescoMigration_Logger;
GO

-- ============================================================================
-- Create AlfrescoMigration_Logger Table
-- ============================================================================

CREATE TABLE dbo.AlfrescoMigration_Logger (
    -- Primary key
    Id                INT IDENTITY(1,1) PRIMARY KEY,

    -- Standard log4net fields
    LOG_DATE          DATETIME2(3) NOT NULL,
    LOG_LEVEL         NVARCHAR(50) NOT NULL,
    LOGGER            NVARCHAR(255) NOT NULL,
    MESSAGE           NVARCHAR(4000),
    EXCEPTION         NVARCHAR(MAX),

    -- Custom business fields
    WORKERID          NVARCHAR(100),
    BATCHID           NVARCHAR(100),
    DOCUMENTID        NVARCHAR(100),
    USERID            NVARCHAR(100),

    -- System fields
    HOSTNAME          NVARCHAR(100),
    THREADID          NVARCHAR(50),
    APPINSTANCE       NVARCHAR(100),

    -- Audit field
    CREATEDAT         DATETIME2(3) DEFAULT GETDATE() NOT NULL
);
GO

-- ============================================================================
-- Indexes for Performance
-- ============================================================================

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

-- ============================================================================
-- Useful Queries
-- ============================================================================

-- Recent errors (last 24 hours)
/*
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, DOCUMENTID, EXCEPTION
FROM dbo.AlfrescoMigration_Logger
WHERE LOG_LEVEL IN ('ERROR', 'FATAL')
  AND LOG_DATE >= DATEADD(HOUR, -24, GETDATE())
ORDER BY LOG_DATE DESC;
*/

-- Logs for specific document
/*
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, WORKERID
FROM dbo.AlfrescoMigration_Logger
WHERE DOCUMENTID = 'your-node-id-here'
ORDER BY LOG_DATE ASC;
*/

-- Logs for specific batch
/*
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, DOCUMENTID, WORKERID
FROM dbo.AlfrescoMigration_Logger
WHERE BATCHID = 'your-batch-id-here'
ORDER BY LOG_DATE ASC;
*/

-- Error summary by logger
/*
SELECT LOGGER, COUNT(*) AS ErrorCount, MAX(LOG_DATE) AS LastError
FROM dbo.AlfrescoMigration_Logger
WHERE LOG_LEVEL = 'ERROR'
  AND LOG_DATE >= DATEADD(DAY, -7, GETDATE())
GROUP BY LOGGER
ORDER BY ErrorCount DESC;
*/

-- ============================================================================
-- Maintenance
-- ============================================================================

-- Archive old logs (older than 30 days)
/*
-- Create archive table
SELECT *
INTO dbo.AlfrescoMigration_Logger_Archive
FROM dbo.AlfrescoMigration_Logger
WHERE 1=0;

-- Copy old records
INSERT INTO dbo.AlfrescoMigration_Logger_Archive
SELECT * FROM dbo.AlfrescoMigration_Logger
WHERE LOG_DATE < DATEADD(DAY, -30, GETDATE());

-- Delete old records
DELETE FROM dbo.AlfrescoMigration_Logger
WHERE LOG_DATE < DATEADD(DAY, -30, GETDATE());
*/

-- Update statistics
/*
UPDATE STATISTICS dbo.AlfrescoMigration_Logger WITH FULLSCAN;
*/

PRINT '============================================================================';
PRINT 'AlfrescoMigration_Logger table created successfully!';
PRINT '============================================================================';
PRINT '';
PRINT 'Indexes created:';
PRINT '  - idx_logger_date (date range queries)';
PRINT '  - idx_logger_level (filter by level)';
PRINT '  - idx_logger_documentid (document tracking)';
PRINT '  - idx_logger_batchid (batch tracking)';
PRINT '  - idx_logger_level_date (composite)';
PRINT '';
PRINT 'Usage:';
PRINT '  - Recent errors: SELECT * FROM AlfrescoMigration_Logger WHERE LOG_LEVEL=''ERROR'' AND LOG_DATE >= DATEADD(HOUR, -1, GETDATE());';
PRINT '  - Document logs: SELECT * FROM AlfrescoMigration_Logger WHERE DOCUMENTID = ''node-id'' ORDER BY LOG_DATE;';
PRINT '  - Batch logs: SELECT * FROM AlfrescoMigration_Logger WHERE BATCHID = ''batch-id'' ORDER BY LOG_DATE;';
PRINT '';
PRINT 'Maintenance:';
PRINT '  - Archive logs older than 30 days periodically';
PRINT '  - Update statistics weekly for optimal performance';
PRINT '============================================================================';
GO
