-- =============================================
-- SQL Server Truncate Migration Tables Script
-- =============================================
-- Version: 1.0
-- Description: Truncates all migration tables (clears data, keeps structure)
-- WARNING: This will delete all data but preserve table structure!
-- =============================================

USE [YourDatabaseName]
GO

PRINT '========================================';
PRINT 'WARNING: Truncating all migration tables!';
PRINT '========================================';
GO

-- Truncate MigrationCheckpoint
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[MigrationCheckpoint]') AND type in (N'U'))
BEGIN
    TRUNCATE TABLE [dbo].[MigrationCheckpoint];
    PRINT 'Table [MigrationCheckpoint] truncated.';
END
ELSE
BEGIN
    PRINT 'Table [MigrationCheckpoint] does not exist.';
END
GO

-- Truncate FolderStaging
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FolderStaging]') AND type in (N'U'))
BEGIN
    TRUNCATE TABLE [dbo].[FolderStaging];
    PRINT 'Table [FolderStaging] truncated.';
END
ELSE
BEGIN
    PRINT 'Table [FolderStaging] does not exist.';
END
GO

-- Truncate DocStaging
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DocStaging]') AND type in (N'U'))
BEGIN
    TRUNCATE TABLE [dbo].[DocStaging];
    PRINT 'Table [DocStaging] truncated.';
END
ELSE
BEGIN
    PRINT 'Table [DocStaging] does not exist.';
END
GO

-- Truncate AlfrescoMigration_Logger
IF EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AlfrescoMigration_Logger]') AND type in (N'U'))
BEGIN
    TRUNCATE TABLE [dbo].[AlfrescoMigration_Logger];
    PRINT 'Table [AlfrescoMigration_Logger] truncated.';
END
ELSE
BEGIN
    PRINT 'Table [AlfrescoMigration_Logger] does not exist.';
END
GO

PRINT '========================================';
PRINT 'All migration tables truncated successfully!';
PRINT 'IDENTITY columns have been reset.';
PRINT '========================================';
GO
