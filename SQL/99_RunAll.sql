-- ============================================================================
-- Master Setup Script - Run All
-- ============================================================================
-- Purpose: Execute all setup scripts in correct order
-- Usage: Run this script as APPUSER (after running 00_CreateSchema.sql as SYSDBA)
-- ============================================================================

-- Set session parameters
SET SERVEROUTPUT ON
SET FEEDBACK ON
SET ECHO ON

PROMPT ============================================================================
PROMPT Starting Alfresco Migration Database Setup
PROMPT ============================================================================

PROMPT
PROMPT Step 1: Creating FolderStaging table...
@@01_CreateFolderStagingTable.sql

PROMPT
PROMPT Step 2: Creating DocStaging table...
@@02_CreateDocStagingTable.sql

PROMPT
PROMPT Step 3: Creating MigrationCheckpoint table...
@@03_CreateMigrationCheckpointTable.sql

PROMPT
PROMPT Step 4: Creating views and helpers...
@@04_CreateViewsAndHelpers.sql

PROMPT
PROMPT Step 5: Creating log table for log4net...
@@07_CreateLogTable.sql

PROMPT
PROMPT ============================================================================
PROMPT Setup Complete! Verification:
PROMPT ============================================================================

-- Verify tables
SELECT 'Tables Created:' AS Status FROM DUAL;
SELECT table_name, num_rows
FROM user_tables
WHERE table_name IN ('DOCSTAGING', 'FOLDERSTAGING', 'MIGRATIONCHECKPOINT', 'ALFRESCOMIGRATION_LOGGER')
ORDER BY table_name;

-- Verify indexes
SELECT 'Indexes Created:' AS Status FROM DUAL;
SELECT table_name, COUNT(*) AS index_count
FROM user_indexes
WHERE table_name IN ('DOCSTAGING', 'FOLDERSTAGING', 'MIGRATIONCHECKPOINT', 'ALFRESCOMIGRATION_LOGGER')
GROUP BY table_name
ORDER BY table_name;

-- Verify views
SELECT 'Views Created:' AS Status FROM DUAL;
SELECT view_name
FROM user_views
WHERE view_name LIKE 'VW_%'
ORDER BY view_name;

PROMPT
PROMPT ============================================================================
PROMPT Setup verification complete!
PROMPT
PROMPT Next steps:
PROMPT 1. Review monitoring queries in: 05_MonitoringQueries.sql
PROMPT 2. Start your migration application
PROMPT 3. Monitor progress using: SELECT * FROM vw_MigrationProgress;
PROMPT ============================================================================
