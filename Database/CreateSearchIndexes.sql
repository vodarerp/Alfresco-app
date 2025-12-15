-- =============================================
-- Create Indexes for DocumentMappings Search Performance
-- =============================================
-- These indexes will significantly improve search performance
-- for the SearchWithPagingAsync method which searches across
-- NAZIV, NazivDokumenta, sifraDokumenta, and TipDosijea columns
-- =============================================

USE [YourDatabaseName]; -- REPLACE WITH YOUR ACTUAL DATABASE NAME
GO

-- Check if indexes already exist before creating them
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_NAZIV' AND object_id = OBJECT_ID('DocumentMappings'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DocumentMappings_NAZIV]
    ON [dbo].[DocumentMappings] ([NAZIV])
    INCLUDE ([ID], [BROJ_DOKUMENATA], [sifraDokumenta], [NazivDokumenta], [TipDosijea], [SifraDokumentaMigracija])
    WITH (ONLINE = OFF, FILLFACTOR = 90);
    PRINT 'Index IX_DocumentMappings_NAZIV created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_DocumentMappings_NAZIV already exists.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_NazivDokumenta' AND object_id = OBJECT_ID('DocumentMappings'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DocumentMappings_NazivDokumenta]
    ON [dbo].[DocumentMappings] ([NazivDokumenta])
    INCLUDE ([ID], [NAZIV], [BROJ_DOKUMENATA], [sifraDokumenta], [TipDosijea])
    WITH (ONLINE = OFF, FILLFACTOR = 90);
    PRINT 'Index IX_DocumentMappings_NazivDokumenta created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_DocumentMappings_NazivDokumenta already exists.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_sifraDokumenta' AND object_id = OBJECT_ID('DocumentMappings'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DocumentMappings_sifraDokumenta]
    ON [dbo].[DocumentMappings] ([sifraDokumenta])
    INCLUDE ([ID], [NAZIV], [BROJ_DOKUMENATA], [NazivDokumenta], [TipDosijea])
    WITH (ONLINE = OFF, FILLFACTOR = 90);
    PRINT 'Index IX_DocumentMappings_sifraDokumenta created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_DocumentMappings_sifraDokumenta already exists.';
END
GO

IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_TipDosijea' AND object_id = OBJECT_ID('DocumentMappings'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DocumentMappings_TipDosijea]
    ON [dbo].[DocumentMappings] ([TipDosijea])
    INCLUDE ([ID], [NAZIV], [BROJ_DOKUMENATA], [sifraDokumenta], [NazivDokumenta])
    WITH (ONLINE = OFF, FILLFACTOR = 90);
    PRINT 'Index IX_DocumentMappings_TipDosijea created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_DocumentMappings_TipDosijea already exists.';
END
GO

-- Optional: Create a composite index for better search performance
-- This can help when searching across multiple columns
IF NOT EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_Search_Composite' AND object_id = OBJECT_ID('DocumentMappings'))
BEGIN
    CREATE NONCLUSTERED INDEX [IX_DocumentMappings_Search_Composite]
    ON [dbo].[DocumentMappings] ([NAZIV], [NazivDokumenta], [sifraDokumenta], [TipDosijea])
    INCLUDE ([ID], [BROJ_DOKUMENATA], [SifraDokumentaMigracija], [TipProizvoda], [NazivDokumentaMigracija])
    WITH (ONLINE = OFF, FILLFACTOR = 90);
    PRINT 'Index IX_DocumentMappings_Search_Composite created successfully.';
END
ELSE
BEGIN
    PRINT 'Index IX_DocumentMappings_Search_Composite already exists.';
END
GO

PRINT 'All search indexes have been created or already exist.';
GO
