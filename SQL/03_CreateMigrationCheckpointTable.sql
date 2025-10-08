-- ============================================================================
-- MigrationCheckpoint Table Creation Script
-- ============================================================================
-- Purpose: Stores checkpoint data for migration services to support resume
--          after application restart or crash
-- ============================================================================

-- Drop table if exists (use with caution in production)
DROP TABLE if EXISTS MigrationCheckpoint CASCADE CONSTRAINTS;

CREATE TABLE MigrationCheckpoint
(
    -- Primary Key
    Id NUMBER(19) GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Service Identification
    ServiceName VARCHAR2(100) NOT NULL UNIQUE,  -- FolderDiscovery, DocumentDiscovery, Move

    -- Checkpoint Data
    CheckpointData CLOB,                         -- JSON serialized checkpoint (e.g., FolderSeekCursor)
    LastProcessedId VARCHAR2(500),               -- Last processed item ID
    LastProcessedAt TIMESTAMP,                   -- Last processed item timestamp

    -- Progress Metrics
    TotalProcessed NUMBER(19) DEFAULT 0,         -- Total items successfully processed
    TotalFailed NUMBER(19) DEFAULT 0,            -- Total items that failed
    BatchCounter NUMBER(10) DEFAULT 0,           -- Number of batches completed

    -- Timestamps
    UpdatedAt TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    CreatedAt TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,

    -- Constraints
    CONSTRAINT chk_checkpoint_service CHECK (ServiceName IN ('FolderDiscovery', 'DocumentDiscovery', 'Move'))
);

-- ============================================================================
-- Indexes
-- ============================================================================

-- Primary lookup by service name (most common query)
CREATE UNIQUE INDEX idx_checkpoint_service
    ON MigrationCheckpoint(ServiceName);

-- Index for monitoring updated checkpoints
CREATE INDEX idx_checkpoint_updated
    ON MigrationCheckpoint(UpdatedAt);

-- ============================================================================
-- Comments
-- ============================================================================

COMMENT ON TABLE MigrationCheckpoint IS
    'Stores checkpoint data for migration services to support resume after restart';

COMMENT ON COLUMN MigrationCheckpoint.ServiceName IS
    'Service identifier: FolderDiscovery, DocumentDiscovery, or Move';

COMMENT ON COLUMN MigrationCheckpoint.CheckpointData IS
    'JSON serialized checkpoint data (e.g., FolderSeekCursor with LastObjectId and LastObjectCreated)';

COMMENT ON COLUMN MigrationCheckpoint.LastProcessedId IS
    'Last processed item ID - used for cursor-based pagination';

COMMENT ON COLUMN MigrationCheckpoint.LastProcessedAt IS
    'Timestamp of last processed item - used for cursor-based pagination';

COMMENT ON COLUMN MigrationCheckpoint.TotalProcessed IS
    'Cumulative count of items successfully processed since service start';

COMMENT ON COLUMN MigrationCheckpoint.TotalFailed IS
    'Cumulative count of items that failed processing';

COMMENT ON COLUMN MigrationCheckpoint.BatchCounter IS
    'Number of batches completed - useful for tracking progress';

-- ============================================================================
-- Statistics
-- ============================================================================

BEGIN
    DBMS_STATS.GATHER_TABLE_STATS(
        ownname => USER,
        tabname => 'MigrationCheckpoint',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE,
        cascade => TRUE
    );
END;
/

COMMIT;
