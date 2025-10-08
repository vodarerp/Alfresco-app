-- ============================================================================
-- Schema Creation Script for Alfresco Migration
-- ============================================================================
-- Purpose: Creates Oracle schema/user for migration application
-- ============================================================================

-- Note: Run this script as SYSDBA or user with CREATE USER privilege

-- Drop user if exists (optional - use with caution in production)
-- DROP USER APPUSER CASCADE;

-- Create user
CREATE USER APPUSER
    IDENTIFIED BY appPass
    DEFAULT TABLESPACE USERS
    TEMPORARY TABLESPACE TEMP
    QUOTA UNLIMITED ON USERS;

-- Grant necessary privileges
GRANT CONNECT TO APPUSER;
GRANT RESOURCE TO APPUSER;
GRANT CREATE SESSION TO APPUSER;
GRANT CREATE TABLE TO APPUSER;
GRANT CREATE VIEW TO APPUSER;
GRANT CREATE SEQUENCE TO APPUSER;
GRANT CREATE SYNONYM TO APPUSER;

-- Additional privileges for advanced features
GRANT CREATE PROCEDURE TO APPUSER;
GRANT CREATE TRIGGER TO APPUSER;

COMMIT;
