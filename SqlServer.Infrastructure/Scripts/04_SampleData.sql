-- =============================================
-- SQL Server Sample/Test Data Script
-- =============================================
-- Version: 1.0
-- Description: Inserts sample data for testing migration process
-- =============================================

USE [YourDatabaseName]
GO

PRINT '========================================';
PRINT 'Inserting sample test data...';
PRINT '========================================';
GO

-- =============================================
-- Sample FolderStaging Data
-- =============================================
INSERT INTO [dbo].[FolderStaging]
(
    [NodeId], [ParentId], [Name], [Status], [DestFolderId],
    [ClientType], [CoreId], [ClientName], [MbrJmbg], [ProductType],
    [ContractNumber], [Source], [UniqueIdentifier], [ProcessDate],
    [CreatedAt], [UpdatedAt]
)
VALUES
(
    'folder-node-001',
    'parent-node-001',
    'Test Folder - Fizicko Lice',
    'READY',
    NULL,
    'FL',
    '10194302',
    'Petar Petrović',
    '1234567890123',
    '00008',
    '10104302_20241105154459',
    'Heimdall',
    'DE-10194302-00008-10104302_20241105154459',
    '2024-01-15',
    SYSDATETIMEOFFSET(),
    SYSDATETIMEOFFSET()
),
(
    'folder-node-002',
    'parent-node-001',
    'Test Folder - Pravno Lice',
    'READY',
    NULL,
    'PL',
    '20194302',
    'Test DOO Beograd',
    '9876543210987',
    '00010',
    '20104302_20241105154459',
    'Heimdall',
    'DE-20194302-00010-20104302_20241105154459',
    '2024-02-20',
    SYSDATETIMEOFFSET(),
    SYSDATETIMEOFFSET()
);

PRINT 'Inserted 2 sample folders.';
GO

-- =============================================
-- Sample DocStaging Data
-- =============================================
INSERT INTO [dbo].[DocStaging]
(
    [NodeId], [Name], [IsFolder], [IsFile], [NodeType], [ParentId],
    [FromPath], [ToPath], [Status], [DocumentType], [Source],
    [IsActive], [CategoryCode], [CategoryName], [OriginalCreatedAt],
    [ContractNumber], [CoreId], [Version], [IsSigned], [ProductType],
    [CreatedAt], [UpdatedAt]
)
VALUES
(
    'doc-node-001',
    'KDP-Ugovor-FL.pdf',
    0,
    1,
    'cm:content',
    'folder-node-001',
    '/old-alfresco/documents/kdp-001.pdf',
    '/new-alfresco/clients/FL/10194302/deposits',
    'READY',
    '00099',
    'Heimdall',
    1,
    'KDP',
    'Kamate Depozita Fizicko Lice',
    '2024-01-15 10:30:00',
    '10104302_20241105154459',
    '10194302',
    1.2,
    1,
    '00008',
    SYSDATETIMEOFFSET(),
    SYSDATETIMEOFFSET()
),
(
    'doc-node-002',
    'DUT-Depozit-Unsigned.pdf',
    0,
    1,
    'cm:content',
    'folder-node-001',
    '/old-alfresco/documents/dut-002.pdf',
    '/new-alfresco/clients/FL/10194302/deposits',
    'READY',
    '00130',
    'DUT',
    1,
    'DUT',
    'Depozit Ugovor Template',
    '2024-01-14 09:00:00',
    '10104302_20241105154459',
    '10194302',
    1.1,
    0,
    '00008',
    SYSDATETIMEOFFSET(),
    SYSDATETIMEOFFSET()
),
(
    'doc-node-003',
    'KDP-Ugovor-PL.pdf',
    0,
    1,
    'cm:content',
    'folder-node-002',
    '/old-alfresco/documents/kdp-pl-001.pdf',
    '/new-alfresco/clients/PL/20194302/deposits',
    'READY',
    '00824-migracija',
    'Heimdall',
    0,
    'KDP',
    'Kamate Depozita Pravno Lice',
    '2024-02-20 14:45:00',
    '20104302_20241105154459',
    '20194302',
    1.0,
    0,
    '00010',
    SYSDATETIMEOFFSET(),
    SYSDATETIMEOFFSET()
);

PRINT 'Inserted 3 sample documents.';
GO

-- =============================================
-- Sample MigrationCheckpoint Data
-- =============================================
INSERT INTO [dbo].[MigrationCheckpoint]
(
    [ServiceName], [CheckpointData], [LastProcessedId], [LastProcessedAt],
    [TotalProcessed], [TotalFailed], [BatchCounter],
    [CreatedAt], [UpdatedAt]
)
VALUES
(
    'FolderDiscovery',
    '{"LastSkipToken":"abc123","PageSize":100}',
    'folder-node-100',
    '2024-11-20 10:00:00',
    150,
    5,
    3,
    GETUTCDATE(),
    GETUTCDATE()
),
(
    'DocumentDiscovery',
    '{"LastSkipToken":"xyz789","PageSize":100}',
    'doc-node-500',
    '2024-11-20 11:30:00',
    500,
    10,
    5,
    GETUTCDATE(),
    GETUTCDATE()
);

PRINT 'Inserted 2 sample checkpoints.';
GO

PRINT '========================================';
PRINT 'Sample data inserted successfully!';
PRINT '========================================';
GO

-- Query to verify inserted data
SELECT 'FolderStaging' AS TableName, COUNT(*) AS RecordCount FROM [dbo].[FolderStaging]
UNION ALL
SELECT 'DocStaging', COUNT(*) FROM [dbo].[DocStaging]
UNION ALL
SELECT 'MigrationCheckpoint', COUNT(*) FROM [dbo].[MigrationCheckpoint];
GO
