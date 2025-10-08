-- ============================================================================
-- FolderStaging Table Creation Script
-- ============================================================================
-- Purpose: Staging table for folders discovered from Alfresco
-- Status Flow: READY → IN PROGRESS → PROCESSED / ERROR
-- ============================================================================

-- Drop table if exists (use with caution in production)
-- DROP TABLE FolderStaging CASCADE CONSTRAINTS;

CREATE TABLE FolderStaging
(
    -- Primary Key
    Id NUMBER(19) GENERATED ALWAYS AS IDENTITY PRIMARY KEY,

    -- Folder Identification
    NodeId VARCHAR2(500) NOT NULL,              -- Alfresco NodeId
    Name VARCHAR2(500),                          -- Folder name
    ParentId VARCHAR2(500),                      -- Parent folder NodeId
    Path VARCHAR2(2000),                         -- Full path in Alfresco

    -- Processing Status
    Status VARCHAR2(50) DEFAULT 'READY' NOT NULL,
    Error VARCHAR2(4000),                        -- Error message if failed
    RetryCount NUMBER(10) DEFAULT 0,             -- Number of retry attempts

    -- Timestamps
    CreatedAt TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    UpdatedAt TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    InsertedAtAlfresco TIMESTAMP(6) WITH TIME ZONE, -- Timestamp when folder was created in Alfresco
    DestFolderId VARCHAR2(500),                     -- Destination folder ID in target system

    -- Additional metadata
    Properties CLOB,                             -- JSON metadata from Alfresco

    -- Constraints
    CONSTRAINT chk_folder_status CHECK (Status IN ('READY', 'IN PROGRESS', 'PROCESSED', 'ERROR'))
);

-- ============================================================================
-- Indexes for Performance
-- ============================================================================

-- Primary lookup index for status-based queries
CREATE INDEX idx_folderstaging_status
    ON FolderStaging(Status);

-- Index for finding stuck items (IN PROGRESS + old UpdatedAt)
CREATE INDEX idx_folderstaging_stuck
    ON FolderStaging(Status, UpdatedAt);

-- Index for NodeId lookups (duplicate detection)
CREATE UNIQUE INDEX idx_folderstaging_nodeid
    ON FolderStaging(NodeId);

-- Composite index for status + created date queries
CREATE INDEX idx_folderstaging_status_created
    ON FolderStaging(Status, CreatedAt);

-- Index for retry tracking and monitoring
CREATE INDEX idx_folderstaging_retry
    ON FolderStaging(RetryCount)
    WHERE RetryCount > 0;

-- ============================================================================
-- Comments
-- ============================================================================

COMMENT ON TABLE FolderStaging IS
    'Staging table for folders discovered from Alfresco source system';

COMMENT ON COLUMN FolderStaging.NodeId IS
    'Unique Alfresco node identifier (workspace://SpacesStore/...)';

COMMENT ON COLUMN FolderStaging.Status IS
    'Processing status: READY (awaiting processing), IN PROGRESS (currently processing), PROCESSED (completed), ERROR (failed)';

COMMENT ON COLUMN FolderStaging.RetryCount IS
    'Number of times this folder has been retried after failure or stuck state';

COMMENT ON COLUMN FolderStaging.Properties IS
    'JSON serialized metadata from Alfresco (permissions, aspects, etc.)';

-- ============================================================================
-- Statistics
-- ============================================================================

-- Gather statistics for query optimizer
BEGIN
    DBMS_STATS.GATHER_TABLE_STATS(
        ownname => USER,
        tabname => 'FolderStaging',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE,
        cascade => TRUE
    );
END;
/

COMMIT;
