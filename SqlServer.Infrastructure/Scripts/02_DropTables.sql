-- =============================================
-- SQL Server Drop Migration Tables Script
-- =============================================
-- Version: 1.0
-- Description: Drops all migration tables (use with caution!)
-- WARNING: This will delete all data!
-- =============================================

USE [YourDatabaseName]
GO

PRINT '========================================';
PRINT 'WARNING: Dropping all migration tables!';
PRINT '========================================';
GO

-- Drop MigrationCheckpoint
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[MigrationCheckpoint]') AND type in (N'U'))
BEGIN
    DROP TABLE [dbo].[MigrationCheckpoint];
    PRINT 'Table [MigrationCheckpoint] dropped.';
END
ELSE
BEGIN
    PRINT 'Table [MigrationCheckpoint] does not exist.';
END
GO

-- Drop FolderStaging
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FolderStaging]') AND type in (N'U'))
BEGIN
    DROP TABLE [dbo].[FolderStaging];
    PRINT 'Table [FolderStaging] dropped.';
END
ELSE
BEGIN
    PRINT 'Table [FolderStaging] does not exist.';
END
GO

-- Drop DocStaging
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DocStaging]') AND type in (N'U'))
BEGIN
    DROP TABLE [dbo].[DocStaging];
    PRINT 'Table [DocStaging] dropped.';
END
ELSE
BEGIN
    PRINT 'Table [DocStaging] does not exist.';
END
GO

-- Drop AlfrescoMigration_Logger
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AlfrescoMigration_Logger]') AND type in (N'U'))
BEGIN
    DROP TABLE [dbo].[AlfrescoMigration_Logger];
    PRINT 'Table [AlfrescoMigration_Logger] dropped.';
END
ELSE
BEGIN
    PRINT 'Table [AlfrescoMigration_Logger] does not exist.';
END
GO

PRINT '========================================';
PRINT 'All migration tables dropped successfully!';
PRINT '========================================';
GO
