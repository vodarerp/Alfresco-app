-- ============================================================================
-- Log Table for log4net
-- ============================================================================
-- Purpose: Store application logs for debugging and monitoring
-- ============================================================================

-- Drop table if exists (use with caution!)
DROP TABLE IF EXISTS AlfrescoMigration_Logger CASCADE CONSTRAINTS;

-- ============================================================================
-- Create AlfrescoMigration_Logger Table
-- ============================================================================

CREATE TABLE AlfrescoMigration_Logger (
    -- Primary key
    Id                NUMBER GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Standard log4net fields
    LOG_DATE          TIMESTAMP(6) NOT NULL,
    LOG_LEVEL         VARCHAR2(50) NOT NULL,
    LOGGER            VARCHAR2(255) NOT NULL,
    MESSAGE           VARCHAR2(4000),
    EXCEPTION         CLOB,

    -- Custom business fields
    WORKERID          VARCHAR2(100),
    BATCHID           VARCHAR2(100),
    DOCUMENTID        VARCHAR2(100),
    USERID            VARCHAR2(100),

    -- System fields
    HOSTNAME          VARCHAR2(100),
    THREADID          VARCHAR2(50),
    APPINSTANCE       VARCHAR2(100),

    -- Audit field
    CREATEDAT         TIMESTAMP(6) DEFAULT SYSTIMESTAMP NOT NULL
);

-- ============================================================================
-- Comments
-- ============================================================================

COMMENT ON TABLE AlfrescoMigration_Logger IS
    'Application log table used by log4net for debugging and monitoring';

COMMENT ON COLUMN AlfrescoMigration_Logger.Id IS
    'Auto-generated primary key';

COMMENT ON COLUMN AlfrescoMigration_Logger.LOG_DATE IS
    'Timestamp when log entry was created';

COMMENT ON COLUMN AlfrescoMigration_Logger.LOG_LEVEL IS
    'Log level: DEBUG, INFO, WARN, ERROR, FATAL';

COMMENT ON COLUMN AlfrescoMigration_Logger.LOGGER IS
    'Logger name (usually class name)';

COMMENT ON COLUMN AlfrescoMigration_Logger.MESSAGE IS
    'Log message (max 4000 chars)';

COMMENT ON COLUMN AlfrescoMigration_Logger.EXCEPTION IS
    'Exception details and stack trace (CLOB for large exceptions)';

COMMENT ON COLUMN AlfrescoMigration_Logger.WORKERID IS
    'Worker/Service identifier (e.g., MoveService-Worker-1)';

COMMENT ON COLUMN AlfrescoMigration_Logger.BATCHID IS
    'Batch identifier for grouping related operations';

COMMENT ON COLUMN AlfrescoMigration_Logger.DOCUMENTID IS
    'Document NodeId being processed';

COMMENT ON COLUMN AlfrescoMigration_Logger.USERID IS
    'User identifier if applicable';

COMMENT ON COLUMN AlfrescoMigration_Logger.HOSTNAME IS
    'Hostname where application is running';

COMMENT ON COLUMN AlfrescoMigration_Logger.THREADID IS
    'Thread identifier for concurrency tracking';

COMMENT ON COLUMN AlfrescoMigration_Logger.APPINSTANCE IS
    'Application instance identifier for multi-instance deployments';

-- ============================================================================
-- Indexes for Performance
-- ============================================================================

-- Index for querying by date range (most common query)
CREATE INDEX idx_logger_date
    ON AlfrescoMigration_Logger(LOG_DATE DESC);

-- Index for filtering by log level
CREATE INDEX idx_logger_level
    ON AlfrescoMigration_Logger(LOG_LEVEL);

-- Index for finding specific document logs
CREATE INDEX idx_logger_documentid
    ON AlfrescoMigration_Logger(DOCUMENTID);

-- Index for batch tracking
CREATE INDEX idx_logger_batchid
    ON AlfrescoMigration_Logger(BATCHID);
-- Composite index for common queries (level + date)
CREATE INDEX idx_logger_level_date
    ON AlfrescoMigration_Logger(LOG_LEVEL, LOG_DATE DESC);

-- Index for finding errors
CREATE INDEX idx_logger_errors
    ON AlfrescoMigration_Logger(LOG_LEVEL, LOG_DATE DESC);

-- ============================================================================
-- Useful Queries
-- ============================================================================

-- Recent errors (last 24 hours)
/*
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, DOCUMENTID, EXCEPTION
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL IN ('ERROR', 'FATAL')
  AND LOG_DATE >= SYSTIMESTAMP - INTERVAL '24' HOUR
ORDER BY LOG_DATE DESC;
*/

-- Logs for specific document
/*
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, WORKERID
FROM AlfrescoMigration_Logger
WHERE DOCUMENTID = 'your-node-id-here'
ORDER BY LOG_DATE ASC;
*/

-- Logs for specific batch
/*
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, DOCUMENTID, WORKERID
FROM AlfrescoMigration_Logger
WHERE BATCHID = 'your-batch-id-here'
ORDER BY LOG_DATE ASC;
*/

-- Error summary by logger
/*
SELECT LOGGER, COUNT(*) AS ErrorCount, MAX(LOG_DATE) AS LastError
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL = 'ERROR'
  AND LOG_DATE >= SYSTIMESTAMP - INTERVAL '7' DAY
GROUP BY LOGGER
ORDER BY ErrorCount DESC;
*/

-- ============================================================================
-- Maintenance
-- ============================================================================

-- Archive old logs (older than 30 days)
/*
CREATE TABLE AlfrescoMigration_Logger_Archive AS
SELECT * FROM AlfrescoMigration_Logger WHERE 1=0;

INSERT INTO AlfrescoMigration_Logger_Archive
SELECT * FROM AlfrescoMigration_Logger
WHERE LOG_DATE < SYSTIMESTAMP - INTERVAL '30' DAY;

DELETE FROM AlfrescoMigration_Logger
WHERE LOG_DATE < SYSTIMESTAMP - INTERVAL '30' DAY;

COMMIT;
*/

-- Update statistics
/*
BEGIN
    DBMS_STATS.GATHER_TABLE_STATS(
        ownname => USER,
        tabname => 'AlfrescoMigration_Logger',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE,
        cascade => TRUE
    );
END;
/
*/

PROMPT ============================================================================
PROMPT AlfrescoMigration_Logger table created successfully!
PROMPT ============================================================================
PROMPT
PROMPT Indexes created:
PROMPT   - idx_logger_date (date range queries)
PROMPT   - idx_logger_level (filter by level)
PROMPT   - idx_logger_documentid (document tracking)
PROMPT   - idx_logger_batchid (batch tracking)
PROMPT   - idx_logger_level_date (composite)
PROMPT   - idx_logger_errors (error queries)
PROMPT
PROMPT Usage:
PROMPT   - Recent errors: SELECT * FROM AlfrescoMigration_Logger WHERE LOG_LEVEL='ERROR' AND LOG_DATE >= SYSTIMESTAMP - INTERVAL '1' HOUR;
PROMPT   - Document logs: SELECT * FROM AlfrescoMigration_Logger WHERE DOCUMENTID = 'node-id' ORDER BY LOG_DATE;
PROMPT   - Batch logs: SELECT * FROM AlfrescoMigration_Logger WHERE BATCHID = 'batch-id' ORDER BY LOG_DATE;
PROMPT
PROMPT Maintenance:
PROMPT   - Archive logs older than 30 days periodically
PROMPT   - Update statistics weekly for optimal performance
PROMPT ============================================================================

COMMIT;
