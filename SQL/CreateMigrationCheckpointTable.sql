-- Migration Checkpoint table for resume functionality
CREATE TABLE MigrationCheckpoint
(
    Id NUMBER(19) GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    ServiceName VARCHAR2(100) NOT NULL UNIQUE,
    CheckpointData CLOB,
    LastProcessedId VARCHAR2(500),
    LastProcessedAt TIMESTAMP,
    TotalProcessed NUMBER(19) DEFAULT 0,
    TotalFailed NUMBER(19) DEFAULT 0,
    UpdatedAt TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    CreatedAt TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    BatchCounter NUMBER(10) DEFAULT 0
);

-- Index for fast service lookup
CREATE INDEX idx_checkpoint_service ON MigrationCheckpoint(ServiceName);

-- Comments
COMMENT ON TABLE MigrationCheckpoint IS 'Stores checkpoint data for migration services to support resume after restart';
COMMENT ON COLUMN MigrationCheckpoint.ServiceName IS 'Service identifier: FolderDiscovery, DocumentDiscovery, Move';
COMMENT ON COLUMN MigrationCheckpoint.CheckpointData IS 'JSON serialized checkpoint data (e.g., FolderSeekCursor)';
COMMENT ON COLUMN MigrationCheckpoint.LastProcessedId IS 'Last processed item ID for cursor-based pagination';
COMMENT ON COLUMN MigrationCheckpoint.LastProcessedAt IS 'Last processed item timestamp';
COMMENT ON COLUMN MigrationCheckpoint.TotalProcessed IS 'Total number of items successfully processed';
COMMENT ON COLUMN MigrationCheckpoint.TotalFailed IS 'Total number of items that failed processing';
COMMENT ON COLUMN MigrationCheckpoint.BatchCounter IS 'Number of batches processed';
