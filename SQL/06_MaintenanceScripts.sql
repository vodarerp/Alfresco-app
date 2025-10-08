-- ============================================================================
-- Maintenance Scripts
-- ============================================================================
-- Purpose: Scripts for data cleanup, reset, and maintenance operations
-- ============================================================================

-- ============================================================================
-- 1. Reset All Items to READY (Use with caution!)
-- ============================================================================

-- Reset all documents stuck in IN PROGRESS
UPDATE DocStaging
SET Status = 'READY',
    ErrorMsg = 'Manual reset to READY',
    RetryCount = NVL(RetryCount, 0) + 1,
    UpdatedAt = SYSTIMESTAMP
WHERE Status = 'IN PROGRESS';

-- Reset all folders stuck in IN PROGRESS
UPDATE FolderStaging
SET Status = 'READY',
    Error = 'Manual reset to READY',
    RetryCount = NVL(RetryCount, 0) + 1,
    UpdatedAt = SYSTIMESTAMP
WHERE Status = 'IN PROGRESS';

COMMIT;


-- ============================================================================
-- 2. Clear All Checkpoints (Start Fresh)
-- ============================================================================

DELETE FROM MigrationCheckpoint;
COMMIT;


-- ============================================================================
-- 3. Reset Failed Items for Retry
-- ============================================================================

-- Reset documents in ERROR status to READY for retry
UPDATE DocStaging
SET Status = 'READY',
    ErrorMsg = 'Reset from ERROR for retry',
    RetryCount = NVL(RetryCount, 0) + 1,
    UpdatedAt = SYSTIMESTAMP
WHERE Status = 'ERROR'
  AND RetryCount < 3;  -- Only retry if less than 3 attempts

-- Reset folders in ERROR status
UPDATE FolderStaging
SET Status = 'READY',
    Error = 'Reset from ERROR for retry',
    RetryCount = NVL(RetryCount, 0) + 1,
    UpdatedAt = SYSTIMESTAMP
WHERE Status = 'ERROR'
  AND RetryCount < 3;

COMMIT;


-- ============================================================================
-- 4. Archive Completed Items
-- ============================================================================

-- Create archive tables (run once)
CREATE TABLE DocStaging_Archive AS
SELECT * FROM DocStaging WHERE 1=0;

CREATE TABLE FolderStaging_Archive AS
SELECT * FROM FolderStaging WHERE 1=0;

-- Move completed items to archive
INSERT INTO DocStaging_Archive
SELECT * FROM DocStaging
WHERE Status = 'DONE'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '7' DAY;

INSERT INTO FolderStaging_Archive
SELECT * FROM FolderStaging
WHERE Status = 'PROCESSED'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '7' DAY;

-- Delete archived items from main tables
DELETE FROM DocStaging
WHERE Status = 'DONE'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '7' DAY;

DELETE FROM FolderStaging
WHERE Status = 'PROCESSED'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '7' DAY;

COMMIT;


-- ============================================================================
-- 5. Rebuild Indexes (Performance Optimization)
-- ============================================================================

-- Rebuild all indexes on DocStaging
BEGIN
    FOR idx IN (SELECT index_name FROM user_indexes WHERE table_name = 'DOCSTAGING') LOOP
        EXECUTE IMMEDIATE 'ALTER INDEX ' || idx.index_name || ' REBUILD';
    END LOOP;
END;
/

-- Rebuild all indexes on FolderStaging
BEGIN
    FOR idx IN (SELECT index_name FROM user_indexes WHERE table_name = 'FOLDERSTAGING') LOOP
        EXECUTE IMMEDIATE 'ALTER INDEX ' || idx.index_name || ' REBUILD';
    END LOOP;
END;
/


-- ============================================================================
-- 6. Update Statistics (Query Performance)
-- ============================================================================

BEGIN
    DBMS_STATS.GATHER_TABLE_STATS(
        ownname => USER,
        tabname => 'DocStaging',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE,
        cascade => TRUE
    );

    DBMS_STATS.GATHER_TABLE_STATS(
        ownname => USER,
        tabname => 'FolderStaging',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE,
        cascade => TRUE
    );

    DBMS_STATS.GATHER_TABLE_STATS(
        ownname => USER,
        tabname => 'MigrationCheckpoint',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE,
        cascade => TRUE
    );
END;
/


-- ============================================================================
-- 7. Cleanup Duplicate Entries (Safety Check)
-- ============================================================================

-- Find duplicate documents by NodeId
SELECT NodeId, COUNT(*) AS DuplicateCount
FROM DocStaging
GROUP BY NodeId
HAVING COUNT(*) > 1;

-- Find duplicate folders by NodeId
SELECT NodeId, COUNT(*) AS DuplicateCount
FROM FolderStaging
GROUP BY NodeId
HAVING COUNT(*) > 1;

-- Delete duplicates (keeps the oldest entry)
DELETE FROM DocStaging
WHERE Id NOT IN (
    SELECT MIN(Id)
    FROM DocStaging
    GROUP BY NodeId
);

DELETE FROM FolderStaging
WHERE Id NOT IN (
    SELECT MIN(Id)
    FROM FolderStaging
    GROUP BY NodeId
);

COMMIT;


-- ============================================================================
-- 8. Reset Specific Service Checkpoint
-- ============================================================================

-- Reset FolderDiscovery checkpoint
UPDATE MigrationCheckpoint
SET TotalProcessed = 0,
    TotalFailed = 0,
    BatchCounter = 0,
    CheckpointData = NULL,
    LastProcessedId = NULL,
    LastProcessedAt = NULL,
    UpdatedAt = SYSTIMESTAMP
WHERE ServiceName = 'FolderDiscovery';

-- Or delete it completely to start fresh
-- DELETE FROM MigrationCheckpoint WHERE ServiceName = 'FolderDiscovery';

COMMIT;


-- ============================================================================
-- 9. Analyze Slow Queries (Performance Tuning)
-- ============================================================================

-- Check index usage
SELECT
    i.index_name,
    i.uniqueness,
    i.status,
    COUNT(ic.column_name) AS column_count
FROM user_indexes i
LEFT JOIN user_ind_columns ic ON i.index_name = ic.index_name
WHERE i.table_name IN ('DOCSTAGING', 'FOLDERSTAGING')
GROUP BY i.index_name, i.uniqueness, i.status
ORDER BY i.table_name, i.index_name;


-- ============================================================================
-- 10. Emergency Stop - Mark All IN PROGRESS as ERROR
-- ============================================================================

-- Use this if you need to emergency stop and investigate
UPDATE DocStaging
SET Status = 'ERROR',
    ErrorMsg = 'Emergency stop - manual investigation required',
    UpdatedAt = SYSTIMESTAMP
WHERE Status = 'IN PROGRESS';

UPDATE FolderStaging
SET Status = 'ERROR',
    Error = 'Emergency stop - manual investigation required',
    UpdatedAt = SYSTIMESTAMP
WHERE Status = 'IN PROGRESS';

COMMIT;


-- ============================================================================
-- 11. Archive Old Logs (Cleanup)
-- ============================================================================

-- Create archive table (run once)
CREATE TABLE AlfrescoMigration_Logger_Archive AS
SELECT * FROM AlfrescoMigration_Logger WHERE 1=0;

-- Archive logs older than 30 days
INSERT INTO AlfrescoMigration_Logger_Archive
SELECT * FROM AlfrescoMigration_Logger
WHERE LOG_DATE < SYSTIMESTAMP - INTERVAL '30' DAY;

DELETE FROM AlfrescoMigration_Logger
WHERE LOG_DATE < SYSTIMESTAMP - INTERVAL '30' DAY;

COMMIT;

-- Alternative: Purge old logs completely (no archive)
/*
DELETE FROM AlfrescoMigration_Logger
WHERE LOG_DATE < SYSTIMESTAMP - INTERVAL '30' DAY;
COMMIT;
*/


-- ============================================================================
-- 12. Update Log Table Statistics
-- ============================================================================

BEGIN
    DBMS_STATS.GATHER_TABLE_STATS(
        ownname => USER,
        tabname => 'AlfrescoMigration_Logger',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE,
        cascade => TRUE
    );
END;
/
