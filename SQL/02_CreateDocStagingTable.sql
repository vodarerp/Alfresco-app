-- ============================================================================
-- DocStaging Table Creation Script
-- ============================================================================
-- Purpose: Staging table for documents discovered from Alfresco folders
-- Status Flow: READY → IN PROGRESS → DONE / ERROR
-- ============================================================================

-- Drop table if exists (use with caution in production)
BEGIN
   EXECUTE IMMEDIATE 'DROP TABLE DocStaging CASCADE CONSTRAINTS';
EXCEPTION
   WHEN OTHERS THEN NULL;
END;
/

BEGIN
   EXECUTE IMMEDIATE 'DROP SEQUENCE DocStaging_SEQ';
EXCEPTION
   WHEN OTHERS THEN NULL;
END;
/

-- Create sequence for ID generation
CREATE SEQUENCE DocStaging_SEQ
    START WITH 1
    INCREMENT BY 1
    NOCACHE
    NOCYCLE;

CREATE TABLE DocStaging
(
    -- Primary Key
    Id NUMBER(19) DEFAULT DocStaging_SEQ.NEXTVAL NOT NULL,
    CONSTRAINT pk_docstaging PRIMARY KEY (Id),

    -- Document Identification
    NodeId VARCHAR2(500) NOT NULL,              -- Alfresco NodeId of document
    Name VARCHAR2(500),                          -- Document name (with extension)
    ParentId VARCHAR2(500),                      -- Source parent folder NodeId

    -- Migration Destination
    ToPath VARCHAR2(500),                        -- Destination folder NodeId for move operation
    FromPath      VARCHAR2(4000 CHAR),               -- Original full path in Alfresco
    IsFolder      NUMBER(1)              DEFAULT 0 NOT NULL,
    IsFile        NUMBER(1)              DEFAULT 0 NOT NULL,
    NodeType      VARCHAR2(100 CHAR),

    -- Document Metadata
    ContentType VARCHAR2(200),                   -- MIME type (e.g., application/pdf)
    DocSize NUMBER(19),                             -- File size in bytes
    VersionLabel VARCHAR2(50),                   -- Version (e.g., 1.0, 2.1)

    -- Processing Status
    Status VARCHAR2(50) DEFAULT 'READY' NOT NULL,
    ErrorMsg VARCHAR2(4000),                     -- Error message if failed
    RetryCount NUMBER(10) DEFAULT 0,             -- Number of retry attempts

    -- Timestamps
    CreatedAt TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,
    UpdatedAt TIMESTAMP DEFAULT SYSTIMESTAMP NOT NULL,

    -- Alfresco timestamps
    CreatedDateAlf TIMESTAMP,                    -- Creation date in Alfresco
    ModifiedDateAlf TIMESTAMP,                   -- Last modified date in Alfresco

    -- Additional metadata
    Properties CLOB,                             -- JSON metadata from Alfresco

    -- Constraints
    CONSTRAINT chk_doc_status CHECK (Status IN ('READY', 'IN PROGRESS', 'DONE', 'ERROR'))
);

-- ============================================================================
-- Indexes for Performance
-- ============================================================================

-- Primary lookup index for status-based queries (MOST IMPORTANT!)
CREATE INDEX idx_docstaging_status
    ON DocStaging(Status);

-- Index for finding stuck items (IN PROGRESS + old UpdatedAt)
CREATE INDEX idx_docstaging_stuck
    ON DocStaging(Status, UpdatedAt);

-- Index for NodeId lookups (duplicate detection and joins)
CREATE UNIQUE INDEX idx_docstaging_nodeid
    ON DocStaging(NodeId);

-- Composite index for batch processing queries
-- Used by: TakeReadyForProcessingAsync with FOR UPDATE SKIP LOCKED
CREATE INDEX idx_docstaging_status_id
    ON DocStaging(Status, Id);

-- Index for monitoring and reporting
CREATE INDEX idx_docstaging_status_updated
    ON DocStaging(Status, UpdatedAt);

-- Index for destination folder (useful for migration planning)
CREATE INDEX idx_docstaging_topath
    ON DocStaging(ToPath);

-- Index for retry tracking
CREATE INDEX idx_docstaging_retry
    ON DocStaging(RetryCount);

-- Index for parent folder (useful for folder-based queries)
CREATE INDEX idx_docstaging_parent
    ON DocStaging(ParentId);

-- Composite index for error analysis
CREATE INDEX idx_docstaging_error
    ON DocStaging(Status, ErrorMsg);

-- ============================================================================
-- Comments
-- ============================================================================

COMMENT ON TABLE DocStaging IS
    'Staging table for documents to be moved/migrated in Alfresco';

COMMENT ON COLUMN DocStaging.NodeId IS
    'Unique Alfresco node identifier for the document';

COMMENT ON COLUMN DocStaging.ToPath IS
    'Target destination folder NodeId where document will be moved';

COMMENT ON COLUMN DocStaging.Status IS
    'Processing status: READY (ready to move), IN PROGRESS (currently moving), DONE (completed), ERROR (failed)';

COMMENT ON COLUMN DocStaging.RetryCount IS
    'Number of times this document has been retried after failure or stuck state';

COMMENT ON COLUMN DocStaging.ErrorMsg IS
    'Error message from last failed attempt (truncated to 4000 chars)';

COMMENT ON COLUMN DocStaging.CreatedDateAlf IS
    'Original creation date from Alfresco metadata';

COMMENT ON COLUMN DocStaging.ModifiedDateAlf IS
    'Last modified date from Alfresco metadata';

-- ============================================================================
-- Statistics
-- ============================================================================

BEGIN
    DBMS_STATS.GATHER_TABLE_STATS(
        ownname => USER,
        tabname => 'DOCSTAGING',
        estimate_percent => DBMS_STATS.AUTO_SAMPLE_SIZE,
        cascade => TRUE
    );
EXCEPTION
    WHEN OTHERS THEN
        DBMS_OUTPUT.PUT_LINE('Warning: Unable to gather statistics - ' || SQLERRM);
END;
/

COMMIT;
