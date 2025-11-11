-- =============================================
-- SQL Server Migration Table Alteration Script
-- =============================================
-- Version: 1.0
-- Description: Adds BarCLEX columns to FolderStaging table
-- Date: 2025-11-11
-- =============================================

USE [YourDatabaseName]
GO

-- =============================================
-- Add BarCLEX columns to FolderStaging table
-- =============================================

-- Add BarCLEXName column
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[FolderStaging]')
               AND name = 'BarCLEXName')
BEGIN
    ALTER TABLE [dbo].[FolderStaging]
    ADD [BarCLEXName] NVARCHAR(255) NULL;

    PRINT 'Column [BarCLEXName] added to [FolderStaging] table.';
END
ELSE
BEGIN
    PRINT 'Column [BarCLEXName] already exists in [FolderStaging] table.';
END
GO

-- Add BarCLEXOpu column
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[FolderStaging]')
               AND name = 'BarCLEXOpu')
BEGIN
    ALTER TABLE [dbo].[FolderStaging]
    ADD [BarCLEXOpu] NVARCHAR(100) NULL;

    PRINT 'Column [BarCLEXOpu] added to [FolderStaging] table.';
END
ELSE
BEGIN
    PRINT 'Column [BarCLEXOpu] already exists in [FolderStaging] table.';
END
GO

-- Add BarCLEXGroupName column
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[FolderStaging]')
               AND name = 'BarCLEXGroupName')
BEGIN
    ALTER TABLE [dbo].[FolderStaging]
    ADD [BarCLEXGroupName] NVARCHAR(255) NULL;

    PRINT 'Column [BarCLEXGroupName] added to [FolderStaging] table.';
END
ELSE
BEGIN
    PRINT 'Column [BarCLEXGroupName] already exists in [FolderStaging] table.';
END
GO

-- Add BarCLEXGroupCode column
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[FolderStaging]')
               AND name = 'BarCLEXGroupCode')
BEGIN
    ALTER TABLE [dbo].[FolderStaging]
    ADD [BarCLEXGroupCode] NVARCHAR(100) NULL;

    PRINT 'Column [BarCLEXGroupCode] added to [FolderStaging] table.';
END
ELSE
BEGIN
    PRINT 'Column [BarCLEXGroupCode] already exists in [FolderStaging] table.';
END
GO

-- Add BarCLEXCode column
IF NOT EXISTS (SELECT * FROM sys.columns
               WHERE object_id = OBJECT_ID(N'[dbo].[FolderStaging]')
               AND name = 'BarCLEXCode')
BEGIN
    ALTER TABLE [dbo].[FolderStaging]
    ADD [BarCLEXCode] NVARCHAR(100) NULL;

    PRINT 'Column [BarCLEXCode] added to [FolderStaging] table.';
END
ELSE
BEGIN
    PRINT 'Column [BarCLEXCode] already exists in [FolderStaging] table.';
END
GO

PRINT 'BarCLEX columns alteration completed successfully.';
GO
