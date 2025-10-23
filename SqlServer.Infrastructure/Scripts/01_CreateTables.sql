-- =============================================
-- SQL Server Migration Tables Creation Script
-- =============================================
-- Version: 1.0
-- Description: Creates tables for Alfresco migration process
-- =============================================

USE [YourDatabaseName]
GO

-- =============================================
-- Table: DocStaging
-- Description: Staging table for document migration
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[DocStaging]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[DocStaging] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [NodeId] NVARCHAR(255) NOT NULL,
        [Name] NVARCHAR(500) NOT NULL,
        [IsFolder] BIT NOT NULL DEFAULT 0,
        [IsFile] BIT NOT NULL DEFAULT 0,
        [NodeType] NVARCHAR(100) NOT NULL,
        [ParentId] NVARCHAR(255) NOT NULL,
        [FromPath] NVARCHAR(2000) NOT NULL,
        [ToPath] NVARCHAR(2000) NOT NULL,
        [Status] NVARCHAR(50) NOT NULL DEFAULT 'NEW', -- NEW, READY, PROCESSING, DONE, ERROR
        [RetryCount] INT NOT NULL DEFAULT 0,
        [ErrorMsg] NVARCHAR(MAX) NULL,
        [CreatedAt] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        [UpdatedAt] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),

        -- Extended Migration Fields
        [DocumentType] NVARCHAR(50) NULL,
        [DocumentTypeMigration] NVARCHAR(100) NULL,
        [Source] NVARCHAR(100) NULL,
        [IsActive] BIT NOT NULL DEFAULT 1,
        [CategoryCode] NVARCHAR(50) NULL,
        [CategoryName] NVARCHAR(255) NULL,
        [OriginalCreatedAt] DATETIME2 NULL,
        [ContractNumber] NVARCHAR(100) NULL,
        [CoreId] NVARCHAR(50) NULL,
        [Version] DECIMAL(10,2) NOT NULL DEFAULT 1.0,
        [AccountNumbers] NVARCHAR(MAX) NULL,
        [RequiresTypeTransformation] BIT NOT NULL DEFAULT 0,
        [FinalDocumentType] NVARCHAR(50) NULL,
        [IsSigned] BIT NOT NULL DEFAULT 0,
        [DutOfferId] NVARCHAR(100) NULL,
        [ProductType] NVARCHAR(50) NULL,

        CONSTRAINT [PK_DocStaging] PRIMARY KEY CLUSTERED ([Id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    );

    -- Indexes for performance
    CREATE NONCLUSTERED INDEX [IX_DocStaging_Status]
        ON [dbo].[DocStaging] ([Status])
        INCLUDE ([Id], [RetryCount]);

    CREATE NONCLUSTERED INDEX [IX_DocStaging_NodeId]
        ON [dbo].[DocStaging] ([NodeId]);

    CREATE NONCLUSTERED INDEX [IX_DocStaging_ParentId]
        ON [dbo].[DocStaging] ([ParentId]);

    CREATE NONCLUSTERED INDEX [IX_DocStaging_CreatedAt]
        ON [dbo].[DocStaging] ([CreatedAt]);

    PRINT 'Table [DocStaging] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [DocStaging] already exists.';
END
GO

-- =============================================
-- Table: FolderStaging
-- Description: Staging table for folder migration
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[FolderStaging]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[FolderStaging] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [NodeId] NVARCHAR(255) NULL,
        [ParentId] NVARCHAR(255) NULL,
        [Name] NVARCHAR(500) NULL,
        [Status] NVARCHAR(50) NOT NULL DEFAULT 'NEW', -- NEW, READY, PROCESSING, DONE, ERROR
        [DestFolderId] NVARCHAR(255) NULL,
        [CreatedAt] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),
        [UpdatedAt] DATETIMEOFFSET NOT NULL DEFAULT SYSDATETIMEOFFSET(),

        -- Extended Migration Fields
        [ClientType] NVARCHAR(10) NULL, -- FL or PL
        [CoreId] NVARCHAR(50) NULL,
        [ClientName] NVARCHAR(500) NULL,
        [MbrJmbg] NVARCHAR(50) NULL,
        [ProductType] NVARCHAR(50) NULL,
        [ContractNumber] NVARCHAR(100) NULL,
        [Batch] NVARCHAR(100) NULL,
        [Source] NVARCHAR(100) NULL,
        [UniqueIdentifier] NVARCHAR(255) NULL,
        [ProcessDate] DATETIME2 NULL,
        [Residency] NVARCHAR(50) NULL,
        [Segment] NVARCHAR(100) NULL,
        [ClientSubtype] NVARCHAR(100) NULL,
        [Staff] NVARCHAR(50) NULL,
        [OpuUser] NVARCHAR(100) NULL,
        [OpuRealization] NVARCHAR(100) NULL,
        [Barclex] NVARCHAR(100) NULL,
        [Collaborator] NVARCHAR(255) NULL,
        [Creator] NVARCHAR(255) NULL,
        [ArchivedAt] DATETIME2 NULL,
        [RetryCount] INT NOT NULL DEFAULT 0,
        [Error] NVARCHAR(MAX) NULL,

        CONSTRAINT [PK_FolderStaging] PRIMARY KEY CLUSTERED ([Id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    );

    -- Indexes for performance
    CREATE NONCLUSTERED INDEX [IX_FolderStaging_Status]
        ON [dbo].[FolderStaging] ([Status])
        INCLUDE ([Id], [RetryCount]);

    CREATE NONCLUSTERED INDEX [IX_FolderStaging_NodeId]
        ON [dbo].[FolderStaging] ([NodeId]);

    CREATE NONCLUSTERED INDEX [IX_FolderStaging_ParentId]
        ON [dbo].[FolderStaging] ([ParentId]);

    CREATE NONCLUSTERED INDEX [IX_FolderStaging_UniqueIdentifier]
        ON [dbo].[FolderStaging] ([UniqueIdentifier]);

    CREATE NONCLUSTERED INDEX [IX_FolderStaging_CoreId]
        ON [dbo].[FolderStaging] ([CoreId]);

    PRINT 'Table [FolderStaging] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [FolderStaging] already exists.';
END
GO

-- =============================================
-- Table: MigrationCheckpoint
-- Description: Checkpoint tracking for migration services
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[MigrationCheckpoint]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[MigrationCheckpoint] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [ServiceName] NVARCHAR(100) NOT NULL, -- FolderDiscovery, DocumentDiscovery, Move
        [CheckpointData] NVARCHAR(MAX) NULL, -- JSON serialized checkpoint data
        [LastProcessedId] NVARCHAR(255) NULL,
        [LastProcessedAt] DATETIME2 NULL,
        [TotalProcessed] BIGINT NOT NULL DEFAULT 0,
        [TotalFailed] BIGINT NOT NULL DEFAULT 0,
        [UpdatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [CreatedAt] DATETIME2 NOT NULL DEFAULT GETUTCDATE(),
        [BatchCounter] INT NOT NULL DEFAULT 0,

        CONSTRAINT [PK_MigrationCheckpoint] PRIMARY KEY CLUSTERED ([Id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    );

    -- Unique constraint on ServiceName
    CREATE UNIQUE NONCLUSTERED INDEX [UQ_MigrationCheckpoint_ServiceName]
        ON [dbo].[MigrationCheckpoint] ([ServiceName]);

    PRINT 'Table [MigrationCheckpoint] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [MigrationCheckpoint] already exists.';
END
GO

-- =============================================
-- Table: AlfrescoMigration_Logger
-- Description: Log4net logging table
-- =============================================
IF NOT EXISTS (SELECT * FROM sys.objects WHERE object_id = OBJECT_ID(N'[dbo].[AlfrescoMigration_Logger]') AND type in (N'U'))
BEGIN
    CREATE TABLE [dbo].[AlfrescoMigration_Logger] (
        [Id] BIGINT IDENTITY(1,1) NOT NULL,
        [LOG_DATE] DATETIME2 NOT NULL,
        [LOG_LEVEL] NVARCHAR(50) NOT NULL,
        [LOGGER] NVARCHAR(255) NOT NULL,
        [MESSAGE] NVARCHAR(MAX) NULL,
        [EXCEPTION] NVARCHAR(MAX) NULL,

        -- Custom Context Properties
        [WORKERID] NVARCHAR(100) NULL,
        [BATCHID] NVARCHAR(100) NULL,
        [DOCUMENTID] NVARCHAR(100) NULL,
        [USERID] NVARCHAR(100) NULL,

        -- Automatic Properties
        [HOSTNAME] NVARCHAR(100) NULL,
        [THREADID] NVARCHAR(50) NULL,
        [APPINSTANCE] NVARCHAR(100) NULL,

        CONSTRAINT [PK_AlfrescoMigration_Logger] PRIMARY KEY CLUSTERED ([Id] ASC)
            WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON)
    );

    -- Indexes for performance
    CREATE NONCLUSTERED INDEX [IX_Logger_LogDate]
        ON [dbo].[AlfrescoMigration_Logger] ([LOG_DATE] DESC);

    CREATE NONCLUSTERED INDEX [IX_Logger_LogLevel]
        ON [dbo].[AlfrescoMigration_Logger] ([LOG_LEVEL])
        INCLUDE ([LOG_DATE], [LOGGER], [MESSAGE]);

    CREATE NONCLUSTERED INDEX [IX_Logger_WorkerId]
        ON [dbo].[AlfrescoMigration_Logger] ([WORKERID])
        WHERE [WORKERID] IS NOT NULL;

    CREATE NONCLUSTERED INDEX [IX_Logger_BatchId]
        ON [dbo].[AlfrescoMigration_Logger] ([BATCHID])
        WHERE [BATCHID] IS NOT NULL;

    CREATE NONCLUSTERED INDEX [IX_Logger_DocumentId]
        ON [dbo].[AlfrescoMigration_Logger] ([DOCUMENTID])
        WHERE [DOCUMENTID] IS NOT NULL;

    PRINT 'Table [AlfrescoMigration_Logger] created successfully.';
END
ELSE
BEGIN
    PRINT 'Table [AlfrescoMigration_Logger] already exists.';
END
GO

PRINT '========================================';
PRINT 'All migration tables created successfully!';
PRINT '========================================';
GO
