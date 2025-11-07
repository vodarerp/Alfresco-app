-- =====================================================
-- Indeksi za DocumentMappings tabelu
-- =====================================================
-- VAŽNO: Ovi indeksi su OBAVEZNI za optimalne performanse!
-- Sa 70,000+ zapisa, pretrage bez indeksa će biti VEOMA spore.
-- =====================================================

USE [YourDatabaseName]; -- PROMENI IME BAZE!
GO

-- Provera da li indeksi već postoje
IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_NAZIV' AND object_id = OBJECT_ID('DocumentMappings'))
BEGIN
    PRINT 'Index IX_DocumentMappings_NAZIV already exists, dropping...';
    DROP INDEX IX_DocumentMappings_NAZIV ON DocumentMappings;
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_sifraDokumenta' AND object_id = OBJECT_ID('DocumentMappings'))
BEGIN
    PRINT 'Index IX_DocumentMappings_sifraDokumenta already exists, dropping...';
    DROP INDEX IX_DocumentMappings_sifraDokumenta ON DocumentMappings;
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_NazivDokumenta' AND object_id = OBJECT_ID('DocumentMappings'))
BEGIN
    PRINT 'Index IX_DocumentMappings_NazivDokumenta already exists, dropping...';
    DROP INDEX IX_DocumentMappings_NazivDokumenta ON DocumentMappings;
END
GO

IF EXISTS (SELECT * FROM sys.indexes WHERE name = 'IX_DocumentMappings_NazivDokumenta_migracija' AND object_id = OBJECT_ID('DocumentMappings'))
BEGIN
    PRINT 'Index IX_DocumentMappings_NazivDokumenta_migracija already exists, dropping...';
    DROP INDEX IX_DocumentMappings_NazivDokumenta_migracija ON DocumentMappings;
END
GO

-- =====================================================
-- Kreiranje indeksa
-- =====================================================

-- 1. Indeks na NAZIV (engleski naziv dokumenta)
-- Koristi se u FindByOriginalNameAsync()
PRINT 'Creating index IX_DocumentMappings_NAZIV...';
CREATE NONCLUSTERED INDEX IX_DocumentMappings_NAZIV
ON DocumentMappings (NAZIV)
INCLUDE (
    ID,
    BROJ_DOKUMENATA,
    sifraDokumenta,
    NazivDokumenta,
    TipDosijea,
    TipProizvoda,
    sifraDokumenta_migracija,
    NazivDokumenta_migracija,
    ExcelFileName,
    ExcelFileSheet
)
WITH (ONLINE = OFF, FILLFACTOR = 90);
GO

-- 2. Indeks na sifraDokumenta (originalna šifra)
-- Koristi se u FindByOriginalCodeAsync()
PRINT 'Creating index IX_DocumentMappings_sifraDokumenta...';
CREATE NONCLUSTERED INDEX IX_DocumentMappings_sifraDokumenta
ON DocumentMappings (sifraDokumenta)
INCLUDE (
    ID,
    NAZIV,
    BROJ_DOKUMENATA,
    NazivDokumenta,
    TipDosijea,
    TipProizvoda,
    sifraDokumenta_migracija,
    NazivDokumenta_migracija,
    ExcelFileName,
    ExcelFileSheet
)
WITH (ONLINE = OFF, FILLFACTOR = 90);
GO

-- 3. Indeks na NazivDokumenta (srpski naziv)
-- Koristi se u FindBySerbianNameAsync()
PRINT 'Creating index IX_DocumentMappings_NazivDokumenta...';
CREATE NONCLUSTERED INDEX IX_DocumentMappings_NazivDokumenta
ON DocumentMappings (NazivDokumenta)
INCLUDE (
    ID,
    NAZIV,
    BROJ_DOKUMENATA,
    sifraDokumenta,
    TipDosijea,
    TipProizvoda,
    sifraDokumenta_migracija,
    NazivDokumenta_migracija,
    ExcelFileName,
    ExcelFileSheet
)
WITH (ONLINE = OFF, FILLFACTOR = 90);
GO

-- 4. Indeks na NazivDokumenta_migracija (migrirani naziv)
-- Koristi se u FindByMigratedNameAsync()
PRINT 'Creating index IX_DocumentMappings_NazivDokumenta_migracija...';
CREATE NONCLUSTERED INDEX IX_DocumentMappings_NazivDokumenta_migracija
ON DocumentMappings (NazivDokumenta_migracija)
INCLUDE (
    ID,
    NAZIV,
    BROJ_DOKUMENATA,
    sifraDokumenta,
    NazivDokumenta,
    TipDosijea,
    TipProizvoda,
    sifraDokumenta_migracija,
    ExcelFileName,
    ExcelFileSheet
)
WITH (ONLINE = OFF, FILLFACTOR = 90);
GO

-- =====================================================
-- Statistike
-- =====================================================

PRINT 'Updating statistics...';
UPDATE STATISTICS DocumentMappings WITH FULLSCAN;
GO

-- =====================================================
-- Provera indeksa
-- =====================================================

PRINT 'Verifying indexes...';
SELECT
    i.name AS IndexName,
    i.type_desc AS IndexType,
    ds.page_count AS Pages,
    ds.page_count * 8 / 1024.0 AS SizeMB,
    i.fill_factor AS FillFactor
FROM sys.indexes i
INNER JOIN sys.dm_db_partition_stats ds
    ON i.object_id = ds.object_id
    AND i.index_id = ds.index_id
WHERE i.object_id = OBJECT_ID('DocumentMappings')
ORDER BY i.index_id;
GO

PRINT '✅ Indexes created successfully!';
PRINT 'IMPORTANT: Run this script on all environments (DEV, TEST, PROD)';
GO
