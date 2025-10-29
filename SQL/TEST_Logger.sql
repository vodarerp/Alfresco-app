-- ============================================================================
-- Test script for AlfrescoMigration_Logger
-- ============================================================================

-- Check if table exists
IF OBJECT_ID('dbo.AlfrescoMigration_Logger', 'U') IS NOT NULL
    PRINT 'Table AlfrescoMigration_Logger EXISTS'
ELSE
    PRINT 'ERROR: Table AlfrescoMigration_Logger DOES NOT EXIST! Run 07_CreateLogTable_SqlServer.sql first'
GO

-- Show table structure
IF OBJECT_ID('dbo.AlfrescoMigration_Logger', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'Table structure:'
    SELECT
        COLUMN_NAME,
        DATA_TYPE,
        CHARACTER_MAXIMUM_LENGTH,
        IS_NULLABLE
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'AlfrescoMigration_Logger'
    ORDER BY ORDINAL_POSITION
END
GO

-- Manual test insert (same format as log4net)
IF OBJECT_ID('dbo.AlfrescoMigration_Logger', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'Testing manual insert...'

    INSERT INTO dbo.AlfrescoMigration_Logger
        (LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, EXCEPTION,
         WORKERID, BATCHID, DOCUMENTID, USERID, HOSTNAME, THREADID, APPINSTANCE)
    VALUES
        (GETDATE(), 'INFO', 'TestLogger', 'Manual test message', NULL,
         'TEST-WORKER', 'TEST-BATCH', 'TEST-DOC', 'TEST-USER', 'TEST-HOST', 'TEST-THREAD', 'TEST-INSTANCE')

    IF @@ROWCOUNT > 0
        PRINT 'SUCCESS: Manual insert completed'
    ELSE
        PRINT 'ERROR: Manual insert failed'
END
GO

-- Show recent log entries
IF OBJECT_ID('dbo.AlfrescoMigration_Logger', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'Recent log entries (last 10):'
    SELECT TOP 10
        Id,
        LOG_DATE,
        LOG_LEVEL,
        LOGGER,
        MESSAGE,
        WORKERID,
        CREATEDAT
    FROM dbo.AlfrescoMigration_Logger
    ORDER BY LOG_DATE DESC
END
GO

-- Count logs by level
IF OBJECT_ID('dbo.AlfrescoMigration_Logger', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'Log count by level:'
    SELECT
        LOG_LEVEL,
        COUNT(*) AS Count
    FROM dbo.AlfrescoMigration_Logger
    GROUP BY LOG_LEVEL
    ORDER BY Count DESC
END
GO

-- Check indexes
IF OBJECT_ID('dbo.AlfrescoMigration_Logger', 'U') IS NOT NULL
BEGIN
    PRINT ''
    PRINT 'Indexes on table:'
    SELECT
        i.name AS IndexName,
        i.type_desc AS IndexType
    FROM sys.indexes i
    WHERE i.object_id = OBJECT_ID('dbo.AlfrescoMigration_Logger')
    ORDER BY i.name
END
GO
