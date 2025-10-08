# Alfresco Migration - Database Setup Scripts

## 📋 Overview

This directory contains all SQL scripts needed to set up the Oracle database for Alfresco migration application.

## 🚀 Quick Start

### Option 1: Run All Scripts at Once (Recommended)

```sql
-- As SYSDBA:
@00_CreateSchema.sql

-- As APPUSER:
@99_RunAll.sql
```

### Option 2: Run Scripts Individually

Run scripts in this order:

1. `00_CreateSchema.sql` - Create APPUSER schema (run as SYSDBA)
2. `01_CreateFolderStagingTable.sql` - Create FolderStaging table
3. `02_CreateDocStagingTable.sql` - Create DocStaging table
4. `03_CreateMigrationCheckpointTable.sql` - Create checkpoint table
5. `04_CreateViewsAndHelpers.sql` - Create monitoring views
6. `07_CreateLogTable.sql` - Create log table for log4net

## 📁 Script Descriptions

### Core Setup Scripts

| Script | Purpose | Run As |
|--------|---------|--------|
| `00_CreateSchema.sql` | Creates APPUSER schema and grants privileges | SYSDBA |
| `01_CreateFolderStagingTable.sql` | Creates FolderStaging table with indexes | APPUSER |
| `02_CreateDocStagingTable.sql` | Creates DocStaging table with indexes | APPUSER |
| `03_CreateMigrationCheckpointTable.sql` | Creates checkpoint table for resume functionality | APPUSER |
| `04_CreateViewsAndHelpers.sql` | Creates monitoring and reporting views | APPUSER |
| `07_CreateLogTable.sql` | Creates log table for log4net debugging and monitoring | APPUSER |

### Utility Scripts

| Script | Purpose |
|--------|---------|
| `05_MonitoringQueries.sql` | Ready-to-use queries for monitoring migration |
| `06_MaintenanceScripts.sql` | Maintenance, cleanup, and troubleshooting scripts |
| `99_RunAll.sql` | Master script that runs all setup scripts |
| `LOG4NET_USAGE_EXAMPLES.md` | Code examples for using log4net logging in application |

## 📊 Database Schema

### FolderStaging Table

Stores discovered folders from Alfresco source.

**Status Flow**: `READY` → `IN PROGRESS` → `PROCESSED` / `ERROR`

**Key Columns**:
- `NodeId` - Alfresco unique identifier
- `Status` - Processing status
- `RetryCount` - Number of retry attempts

**Indexes**:
- `idx_folderstaging_status` - Status lookup
- `idx_folderstaging_stuck` - Stuck items detection
- `idx_folderstaging_nodeid` - Unique NodeId lookup

### DocStaging Table

Stores documents to be moved/migrated.

**Status Flow**: `READY` → `IN PROGRESS` → `DONE` / `ERROR`

**Key Columns**:
- `NodeId` - Alfresco document identifier
- `ToPath` - Destination folder NodeId
- `Status` - Processing status
- `ErrorMsg` - Error message if failed

**Indexes**:
- `idx_docstaging_status` - Most important! Used for batch queries
- `idx_docstaging_stuck` - Stuck items detection
- `idx_docstaging_nodeid` - Unique document lookup
- `idx_docstaging_status_id` - FOR UPDATE SKIP LOCKED optimization

### MigrationCheckpoint Table

Stores checkpoint data for resume functionality.

**Services**:
- `FolderDiscovery` - Folder discovery progress
- `DocumentDiscovery` - Document discovery progress
- `Move` - Move operation progress

**Key Columns**:
- `ServiceName` - Service identifier (unique)
- `CheckpointData` - JSON serialized cursor data
- `TotalProcessed` - Total items processed
- `TotalFailed` - Total items failed

### AlfrescoMigration_Logger Table

Stores application logs for debugging and monitoring (log4net integration).

**Log Levels**: `DEBUG`, `INFO`, `WARN`, `ERROR`, `FATAL`

**Key Columns**:
- `LOG_DATE` - Log timestamp
- `LOG_LEVEL` - Log severity level
- `LOGGER` - Logger name (usually class name)
- `MESSAGE` - Log message (max 4000 chars)
- `EXCEPTION` - Exception details (CLOB for stack traces)
- `WORKERID` - Worker/Service identifier
- `BATCHID` - Batch identifier for grouping operations
- `DOCUMENTID` - Document NodeId being processed
- `HOSTNAME` - Server hostname
- `THREADID` - Thread identifier
- `APPINSTANCE` - Application instance for multi-instance deployments

**Indexes**:
- `idx_logger_date` - Date range queries
- `idx_logger_level` - Filter by log level
- `idx_logger_documentid` - Document tracking
- `idx_logger_batchid` - Batch tracking
- `idx_logger_level_date` - Composite index
- `idx_logger_errors` - Fast error queries

## 🔍 Monitoring Views

### vw_DocStaging_Summary
Summary of document counts by status.

```sql
SELECT * FROM vw_DocStaging_Summary;
```

### vw_FolderStaging_Summary
Summary of folder counts by status.

```sql
SELECT * FROM vw_FolderStaging_Summary;
```

### vw_StuckItems
Shows items stuck in IN PROGRESS status.

```sql
SELECT * FROM vw_StuckItems;
```

### vw_ErrorAnalysis
Groups similar errors for analysis.

```sql
SELECT * FROM vw_ErrorAnalysis;
```

### vw_MigrationProgress
Overall migration progress dashboard.

```sql
SELECT * FROM vw_MigrationProgress;
```

### vw_RetryStatistics
Retry statistics for troubleshooting.

```sql
SELECT * FROM vw_RetryStatistics;
```

## 🛠️ Common Tasks

### Check Migration Status

```sql
-- Quick overview
SELECT 'Documents' AS Type, Status, COUNT(*) AS Count
FROM DocStaging GROUP BY Status
UNION ALL
SELECT 'Folders' AS Type, Status, COUNT(*) AS Count
FROM FolderStaging GROUP BY Status;

-- Detailed progress
SELECT * FROM vw_MigrationProgress;
```

### Find Stuck Items

```sql
SELECT * FROM vw_StuckItems
ORDER BY MinutesStuck DESC;
```

### Reset Stuck Items Manually

```sql
-- Reset stuck documents
UPDATE DocStaging
SET Status = 'READY',
    ErrorMsg = 'Manual reset from stuck state',
    RetryCount = NVL(RetryCount, 0) + 1,
    UpdatedAt = SYSTIMESTAMP
WHERE Status = 'IN PROGRESS'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '10' MINUTE;

COMMIT;
```

### Clear Checkpoint (Start Fresh)

```sql
DELETE FROM MigrationCheckpoint WHERE ServiceName = 'FolderDiscovery';
COMMIT;
```

### Archive Completed Items

```sql
-- Move completed items older than 7 days to archive
INSERT INTO DocStaging_Archive
SELECT * FROM DocStaging
WHERE Status = 'DONE'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '7' DAY;

DELETE FROM DocStaging
WHERE Status = 'DONE'
  AND UpdatedAt < SYSTIMESTAMP - INTERVAL '7' DAY;

COMMIT;
```

### Query Application Logs

```sql
-- Recent errors (last 24 hours)
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, DOCUMENTID, EXCEPTION
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL IN ('ERROR', 'FATAL')
  AND LOG_DATE >= SYSTIMESTAMP - INTERVAL '24' HOUR
ORDER BY LOG_DATE DESC;

-- Logs for specific document
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, WORKERID
FROM AlfrescoMigration_Logger
WHERE DOCUMENTID = 'your-node-id-here'
ORDER BY LOG_DATE ASC;

-- Error summary
SELECT LOGGER, COUNT(*) AS ErrorCount, MAX(LOG_DATE) AS LastError
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL = 'ERROR'
  AND LOG_DATE >= SYSTIMESTAMP - INTERVAL '7' DAY
GROUP BY LOGGER
ORDER BY ErrorCount DESC;
```

## ⚡ Performance Tuning

### Rebuild Indexes

```sql
-- Run periodically for optimal performance
BEGIN
    FOR idx IN (SELECT index_name FROM user_indexes
                WHERE table_name IN ('DOCSTAGING', 'FOLDERSTAGING'))
    LOOP
        EXECUTE IMMEDIATE 'ALTER INDEX ' || idx.index_name || ' REBUILD';
    END LOOP;
END;
/
```

### Update Statistics

```sql
BEGIN
    DBMS_STATS.GATHER_TABLE_STATS(USER, 'DocStaging',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE, cascade => TRUE);
    DBMS_STATS.GATHER_TABLE_STATS(USER, 'FolderStaging',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE, cascade => TRUE);
END;
/
```

## 🔐 Security Notes

- Default username: `APPUSER`
- Default password: `appPass`
- **⚠️ IMPORTANT**: Change password in production!

```sql
ALTER USER APPUSER IDENTIFIED BY your_secure_password;
```

## 📈 Estimated Space Requirements

| Scenario | Folders | Documents | Estimated Space |
|----------|---------|-----------|-----------------|
| Small | 1,000 | 10,000 | ~50 MB |
| Medium | 10,000 | 100,000 | ~500 MB |
| Large | 100,000 | 1,000,000 | ~5 GB |
| Enterprise | 1,000,000+ | 10,000,000+ | ~50 GB+ |

## 🐛 Troubleshooting

### ORA-00955: name is already used by an existing object

Table already exists. Either:
1. Drop table: `DROP TABLE DocStaging CASCADE CONSTRAINTS;`
2. Or skip table creation

### ORA-01950: no privileges on tablespace

Grant quota:
```sql
ALTER USER APPUSER QUOTA UNLIMITED ON USERS;
```

### Slow queries

1. Check indexes exist: `SELECT * FROM user_indexes WHERE table_name = 'DOCSTAGING';`
2. Update statistics (see above)
3. Rebuild indexes (see above)

## 📞 Support

For issues or questions, check:
- Application logs in `logs/` directory
- Database views for status: `SELECT * FROM vw_MigrationProgress;`
- Stuck items: `SELECT * FROM vw_StuckItems;`

## 📝 Version History

- **v1.0** - Initial release with core tables
- **v1.1** - Added checkpoint/resume functionality
- **v1.2** - Added stuck items recovery
- **v1.3** - Added monitoring views and maintenance scripts
