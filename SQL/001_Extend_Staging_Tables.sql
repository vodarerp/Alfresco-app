-- =====================================================================================
-- SQL Migration Script: Extend DOC_STAGING and FOLDER_STAGING Tables
-- Version: 1.0
-- Date: 2025-01-XX
-- Description: Adds new columns required for migration per documentation requirements
--
-- IMPORTANT: Review and test in DEV environment before applying to PROD!
-- =====================================================================================

-- =====================================================================================
-- PART 1: Extend DOC_STAGING Table
-- =====================================================================================

PROMPT Adding new columns to DOC_STAGING table...

ALTER TABLE DOC_STAGING ADD (
    -- Document type fields
    DOCUMENT_TYPE VARCHAR2(50),
    DOCUMENT_TYPE_MIGRATION VARCHAR2(100),
    SOURCE VARCHAR2(50),
    IS_ACTIVE NUMBER(1) DEFAULT 1,

    -- Category fields
    CATEGORY_CODE VARCHAR2(50),
    CATEGORY_NAME VARCHAR2(200),

    -- Date and identification fields
    ORIGINAL_CREATED_AT TIMESTAMP,
    CONTRACT_NUMBER VARCHAR2(50),
    CORE_ID VARCHAR2(50),

    -- Version and transformation fields
    VERSION NUMBER(3,1) DEFAULT 1.0,
    ACCOUNT_NUMBERS VARCHAR2(4000),
    REQUIRES_TYPE_TRANSFORMATION NUMBER(1) DEFAULT 0,
    FINAL_DOCUMENT_TYPE VARCHAR2(50),

    -- Additional fields for DUT integration
    IS_SIGNED NUMBER(1) DEFAULT 0,
    DUT_OFFER_ID VARCHAR2(100),
    PRODUCT_TYPE VARCHAR2(50)
);

PROMPT DOC_STAGING table columns added successfully.

-- =====================================================================================
-- PART 2: Add Comments to DOC_STAGING Columns
-- =====================================================================================

PROMPT Adding column comments to DOC_STAGING...

COMMENT ON COLUMN DOC_STAGING.DOCUMENT_TYPE IS
'Document type code (e.g., 00099, 00824, 00130). Used for mapping to new document types during migration.';

COMMENT ON COLUMN DOC_STAGING.DOCUMENT_TYPE_MIGRATION IS
'Document type with "migracija" suffix for versioned documents (e.g., 00824-migracija). Per doc line 31-34.';

COMMENT ON COLUMN DOC_STAGING.SOURCE IS
'Source system identifier (Heimdall, DUT, Depo kartoni_Validan, etc.). Per doc line 116.';

COMMENT ON COLUMN DOC_STAGING.IS_ACTIVE IS
'Document activity status. 1=Active, 0=Inactive. Complex rules apply for KDP documents. Per doc line 51-72.';

COMMENT ON COLUMN DOC_STAGING.CATEGORY_CODE IS
'Document category code for classification. Per doc line 117.';

COMMENT ON COLUMN DOC_STAGING.CATEGORY_NAME IS
'Document category name for classification.';

COMMENT ON COLUMN DOC_STAGING.ORIGINAL_CREATED_AT IS
'Original creation date from old Alfresco (NOT migration date!). Per doc line 193-194.';

COMMENT ON COLUMN DOC_STAGING.CONTRACT_NUMBER IS
'Contract number (Broj Ugovora) for deposit documents. Used in unique folder identifier.';

COMMENT ON COLUMN DOC_STAGING.CORE_ID IS
'Client Core ID for linking to ClientAPI data.';

COMMENT ON COLUMN DOC_STAGING.VERSION IS
'Document version (1.1 for unsigned, 1.2 for signed). Per doc line 168-170.';

COMMENT ON COLUMN DOC_STAGING.ACCOUNT_NUMBERS IS
'Comma-separated account numbers for KDP documents (00099, 00824). Per doc line 123-129.';

COMMENT ON COLUMN DOC_STAGING.REQUIRES_TYPE_TRANSFORMATION IS
'Flag indicating document needs type transformation after migration. 1=Yes, 0=No.';

COMMENT ON COLUMN DOC_STAGING.FINAL_DOCUMENT_TYPE IS
'Final document type after transformation (e.g., 00099 from 00824-migracija). Per doc line 67-68.';

COMMENT ON COLUMN DOC_STAGING.IS_SIGNED IS
'Indicates if document is signed (for DUT deposits). 1=Signed (v1.2), 0=Unsigned (v1.1).';

COMMENT ON COLUMN DOC_STAGING.DUT_OFFER_ID IS
'Offer ID from DUT system for deposit documents. Links to OfferBO table.';

COMMENT ON COLUMN DOC_STAGING.PRODUCT_TYPE IS
'Product type code (00008 for FL deposits, 00010 for PL deposits). Per doc line 148.';

-- =====================================================================================
-- PART 3: Create Indexes on DOC_STAGING for Performance
-- =====================================================================================

PROMPT Creating indexes on DOC_STAGING...

CREATE INDEX IDX_DOC_STAGING_COREID_TYPE
    ON DOC_STAGING(CORE_ID, DOCUMENT_TYPE);

CREATE INDEX IDX_DOC_STAGING_SOURCE
    ON DOC_STAGING(SOURCE);

CREATE INDEX IDX_DOC_STAGING_ACTIVE
    ON DOC_STAGING(IS_ACTIVE);

CREATE INDEX IDX_DOC_STAGING_TRANS_FLAG
    ON DOC_STAGING(REQUIRES_TYPE_TRANSFORMATION);

CREATE INDEX IDX_DOC_STAGING_CONTRACT
    ON DOC_STAGING(CONTRACT_NUMBER);

CREATE INDEX IDX_DOC_STAGING_DUT_OFFER
    ON DOC_STAGING(DUT_OFFER_ID);

PROMPT DOC_STAGING indexes created successfully.

-- =====================================================================================
-- PART 4: Extend FOLDER_STAGING Table
-- =====================================================================================

PROMPT Adding new columns to FOLDER_STAGING table...

ALTER TABLE FOLDER_STAGING ADD (
    -- Client identification fields
    CLIENT_TYPE VARCHAR2(10),
    CORE_ID VARCHAR2(50),
    CLIENT_NAME VARCHAR2(500),
    MBR_JMBG VARCHAR2(50),

    -- Product and contract fields
    PRODUCT_TYPE VARCHAR2(50),
    CONTRACT_NUMBER VARCHAR2(50),
    BATCH VARCHAR2(50),
    SOURCE VARCHAR2(50),

    -- Unique identifier fields
    UNIQUE_IDENTIFIER VARCHAR2(200),
    PROCESS_DATE TIMESTAMP,

    -- Client metadata from ClientAPI
    RESIDENCY VARCHAR2(50),
    SEGMENT VARCHAR2(50),
    CLIENT_SUBTYPE VARCHAR2(50),
    STAFF VARCHAR2(50),
    OPU_USER VARCHAR2(50),
    OPU_REALIZATION VARCHAR2(50),
    BARCLEX VARCHAR2(50),
    COLLABORATOR VARCHAR2(50),

    -- Additional fields
    CREATOR VARCHAR2(100),
    ARCHIVED_AT TIMESTAMP
);

PROMPT FOLDER_STAGING table columns added successfully.

-- =====================================================================================
-- PART 5: Add Comments to FOLDER_STAGING Columns
-- =====================================================================================

PROMPT Adding column comments to FOLDER_STAGING...

COMMENT ON COLUMN FOLDER_STAGING.CLIENT_TYPE IS
'Client type: FL (Fizicko Lice) or PL (Pravno Lice). Determines folder type to create.';

COMMENT ON COLUMN FOLDER_STAGING.CORE_ID IS
'Client Core ID from core banking system. Required for ClientAPI enrichment. Per doc line 146.';

COMMENT ON COLUMN FOLDER_STAGING.CLIENT_NAME IS
'Full client name retrieved from ClientAPI. Per doc line 28-29.';

COMMENT ON COLUMN FOLDER_STAGING.MBR_JMBG IS
'MBR (for legal entities) or JMBG (for natural persons). Retrieved from ClientAPI.';

COMMENT ON COLUMN FOLDER_STAGING.PRODUCT_TYPE IS
'Product type: 00008 (FL deposits), 00010 (PL deposits). Per doc line 148.';

COMMENT ON COLUMN FOLDER_STAGING.CONTRACT_NUMBER IS
'Contract number (Broj Ugovora). Essential for deposit folder unique identifier. Per doc line 149.';

COMMENT ON COLUMN FOLDER_STAGING.BATCH IS
'Batch number (Partija). Optional attribute. Per doc line 150-151.';

COMMENT ON COLUMN FOLDER_STAGING.SOURCE IS
'Source system identifier (Heimdall, DUT, etc.). Important for tracking origin.';

COMMENT ON COLUMN FOLDER_STAGING.UNIQUE_IDENTIFIER IS
'Unique folder identifier for deposits: DE-{CoreId}{ProductType}-{ContractNumber}. Per doc line 156. Example: DE-10194302-00008-10104302_20241105154459';

COMMENT ON COLUMN FOLDER_STAGING.PROCESS_DATE IS
'Date when deposit was processed (NOT migration date!). Per doc line 190-191.';

COMMENT ON COLUMN FOLDER_STAGING.RESIDENCY IS
'Client residency status (Resident/Non-resident). Retrieved from ClientAPI.';

COMMENT ON COLUMN FOLDER_STAGING.SEGMENT IS
'Client segment classification. Retrieved from ClientAPI.';

COMMENT ON COLUMN FOLDER_STAGING.CLIENT_SUBTYPE IS
'Client subtype for additional classification. Retrieved from ClientAPI.';

COMMENT ON COLUMN FOLDER_STAGING.STAFF IS
'Staff indicator (if client is bank employee). Retrieved from ClientAPI.';

COMMENT ON COLUMN FOLDER_STAGING.OPU_USER IS
'OPU (Organizational Unit) of the user. Retrieved from ClientAPI.';

COMMENT ON COLUMN FOLDER_STAGING.OPU_REALIZATION IS
'OPU/ID of realization. Retrieved from ClientAPI.';

COMMENT ON COLUMN FOLDER_STAGING.BARCLEX IS
'Barclex identifier. Retrieved from ClientAPI.';

COMMENT ON COLUMN FOLDER_STAGING.COLLABORATOR IS
'Collaborator/Partner information. Retrieved from ClientAPI.';

COMMENT ON COLUMN FOLDER_STAGING.CREATOR IS
'Creator of the folder/document.';

COMMENT ON COLUMN FOLDER_STAGING.ARCHIVED_AT IS
'Archival date (may differ from creation date).';

-- =====================================================================================
-- PART 6: Create Indexes on FOLDER_STAGING for Performance
-- =====================================================================================

PROMPT Creating indexes on FOLDER_STAGING...

CREATE INDEX IDX_FOLDER_STAGING_COREID
    ON FOLDER_STAGING(CORE_ID);

CREATE INDEX IDX_FOLDER_STAGING_UNIQUE_ID
    ON FOLDER_STAGING(UNIQUE_IDENTIFIER);

CREATE INDEX IDX_FOLDER_STAGING_CONTRACT
    ON FOLDER_STAGING(CONTRACT_NUMBER);

CREATE INDEX IDX_FOLDER_STAGING_SOURCE
    ON FOLDER_STAGING(SOURCE);

CREATE INDEX IDX_FOLDER_STAGING_CLIENT_TYPE
    ON FOLDER_STAGING(CLIENT_TYPE);

PROMPT FOLDER_STAGING indexes created successfully.

-- =====================================================================================
-- PART 7: Verification Queries
-- =====================================================================================

PROMPT Running verification queries...

-- Check DOC_STAGING structure
SELECT COUNT(*) AS DOC_STAGING_NEW_COLUMNS
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = 'DOC_STAGING'
  AND COLUMN_NAME IN (
      'DOCUMENT_TYPE', 'DOCUMENT_TYPE_MIGRATION', 'SOURCE', 'IS_ACTIVE',
      'CATEGORY_CODE', 'CATEGORY_NAME', 'ORIGINAL_CREATED_AT',
      'CONTRACT_NUMBER', 'CORE_ID', 'VERSION', 'ACCOUNT_NUMBERS',
      'REQUIRES_TYPE_TRANSFORMATION', 'FINAL_DOCUMENT_TYPE',
      'IS_SIGNED', 'DUT_OFFER_ID', 'PRODUCT_TYPE'
  );

-- Check FOLDER_STAGING structure
SELECT COUNT(*) AS FOLDER_STAGING_NEW_COLUMNS
FROM USER_TAB_COLUMNS
WHERE TABLE_NAME = 'FOLDER_STAGING'
  AND COLUMN_NAME IN (
      'CLIENT_TYPE', 'CORE_ID', 'CLIENT_NAME', 'MBR_JMBG',
      'PRODUCT_TYPE', 'CONTRACT_NUMBER', 'BATCH', 'SOURCE',
      'UNIQUE_IDENTIFIER', 'PROCESS_DATE', 'RESIDENCY', 'SEGMENT',
      'CLIENT_SUBTYPE', 'STAFF', 'OPU_USER', 'OPU_REALIZATION',
      'BARCLEX', 'COLLABORATOR', 'CREATOR', 'ARCHIVED_AT'
  );

-- Check indexes
SELECT INDEX_NAME, TABLE_NAME, UNIQUENESS
FROM USER_INDEXES
WHERE TABLE_NAME IN ('DOC_STAGING', 'FOLDER_STAGING')
  AND INDEX_NAME LIKE 'IDX_%'
ORDER BY TABLE_NAME, INDEX_NAME;

PROMPT Migration script completed successfully!

-- =====================================================================================
-- ROLLBACK SCRIPT (use in case of issues)
-- =====================================================================================
/*
-- CAUTION: This will remove all added columns and indexes. Data will be lost!

PROMPT Rolling back changes...

-- Drop indexes
DROP INDEX IDX_DOC_STAGING_COREID_TYPE;
DROP INDEX IDX_DOC_STAGING_SOURCE;
DROP INDEX IDX_DOC_STAGING_ACTIVE;
DROP INDEX IDX_DOC_STAGING_TRANS_FLAG;
DROP INDEX IDX_DOC_STAGING_CONTRACT;
DROP INDEX IDX_DOC_STAGING_DUT_OFFER;
DROP INDEX IDX_FOLDER_STAGING_COREID;
DROP INDEX IDX_FOLDER_STAGING_UNIQUE_ID;
DROP INDEX IDX_FOLDER_STAGING_CONTRACT;
DROP INDEX IDX_FOLDER_STAGING_SOURCE;
DROP INDEX IDX_FOLDER_STAGING_CLIENT_TYPE;

-- Drop DOC_STAGING columns
ALTER TABLE DOC_STAGING DROP COLUMN DOCUMENT_TYPE;
ALTER TABLE DOC_STAGING DROP COLUMN DOCUMENT_TYPE_MIGRATION;
ALTER TABLE DOC_STAGING DROP COLUMN SOURCE;
ALTER TABLE DOC_STAGING DROP COLUMN IS_ACTIVE;
ALTER TABLE DOC_STAGING DROP COLUMN CATEGORY_CODE;
ALTER TABLE DOC_STAGING DROP COLUMN CATEGORY_NAME;
ALTER TABLE DOC_STAGING DROP COLUMN ORIGINAL_CREATED_AT;
ALTER TABLE DOC_STAGING DROP COLUMN CONTRACT_NUMBER;
ALTER TABLE DOC_STAGING DROP COLUMN CORE_ID;
ALTER TABLE DOC_STAGING DROP COLUMN VERSION;
ALTER TABLE DOC_STAGING DROP COLUMN ACCOUNT_NUMBERS;
ALTER TABLE DOC_STAGING DROP COLUMN REQUIRES_TYPE_TRANSFORMATION;
ALTER TABLE DOC_STAGING DROP COLUMN FINAL_DOCUMENT_TYPE;
ALTER TABLE DOC_STAGING DROP COLUMN IS_SIGNED;
ALTER TABLE DOC_STAGING DROP COLUMN DUT_OFFER_ID;
ALTER TABLE DOC_STAGING DROP COLUMN PRODUCT_TYPE;

-- Drop FOLDER_STAGING columns
ALTER TABLE FOLDER_STAGING DROP COLUMN CLIENT_TYPE;
ALTER TABLE FOLDER_STAGING DROP COLUMN CORE_ID;
ALTER TABLE FOLDER_STAGING DROP COLUMN CLIENT_NAME;
ALTER TABLE FOLDER_STAGING DROP COLUMN MBR_JMBG;
ALTER TABLE FOLDER_STAGING DROP COLUMN PRODUCT_TYPE;
ALTER TABLE FOLDER_STAGING DROP COLUMN CONTRACT_NUMBER;
ALTER TABLE FOLDER_STAGING DROP COLUMN BATCH;
ALTER TABLE FOLDER_STAGING DROP COLUMN SOURCE;
ALTER TABLE FOLDER_STAGING DROP COLUMN UNIQUE_IDENTIFIER;
ALTER TABLE FOLDER_STAGING DROP COLUMN PROCESS_DATE;
ALTER TABLE FOLDER_STAGING DROP COLUMN RESIDENCY;
ALTER TABLE FOLDER_STAGING DROP COLUMN SEGMENT;
ALTER TABLE FOLDER_STAGING DROP COLUMN CLIENT_SUBTYPE;
ALTER TABLE FOLDER_STAGING DROP COLUMN STAFF;
ALTER TABLE FOLDER_STAGING DROP COLUMN OPU_USER;
ALTER TABLE FOLDER_STAGING DROP COLUMN OPU_REALIZATION;
ALTER TABLE FOLDER_STAGING DROP COLUMN BARCLEX;
ALTER TABLE FOLDER_STAGING DROP COLUMN COLLABORATOR;
ALTER TABLE FOLDER_STAGING DROP COLUMN CREATOR;
ALTER TABLE FOLDER_STAGING DROP COLUMN ARCHIVED_AT;

PROMPT Rollback completed.
*/
