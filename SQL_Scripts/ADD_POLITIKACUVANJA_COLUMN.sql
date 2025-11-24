-- =============================================
-- Migration Script: Add PolitikaCuvanja Column
-- Description: Dodaje kolonu PolitikaCuvanja u DocumentMappings tabelu
--              za određivanje statusa dokumenta nakon migracije
-- Date: 2025-11-24
-- =============================================

USE [AlfrescoStagingDb]
GO

-- Proveri da li kolona već postoji
IF NOT EXISTS (
    SELECT 1
    FROM sys.columns
    WHERE object_id = OBJECT_ID(N'[dbo].[DocumentMappings]')
    AND name = 'PolitikaCuvanja'
)
BEGIN
    PRINT 'Adding PolitikaCuvanja column to DocumentMappings table...'

    -- Dodaj kolonu
    ALTER TABLE [dbo].[DocumentMappings]
    ADD [PolitikaCuvanja] NVARCHAR(100) NULL;

    PRINT 'PolitikaCuvanja column added successfully!'

    -- Opciono: Dodaj komentar na kolonu
    EXEC sp_addextendedproperty
        @name = N'MS_Description',
        @value = N'Politika čuvanja dokumenta - utiče na određivanje statusa. Moguće vrednosti: "Nova verzija", "Novi dokument", null/empty',
        @level0type = N'SCHEMA', @level0name = N'dbo',
        @level1type = N'TABLE',  @level1name = N'DocumentMappings',
        @level2type = N'COLUMN', @level2name = N'PolitikaCuvanja';

    PRINT 'Column description added successfully!'
END
ELSE
BEGIN
    PRINT 'PolitikaCuvanja column already exists in DocumentMappings table.'
END
GO

-- =============================================
-- LOGIKA ZA ODREĐIVANJE STATUSA DOKUMENTA
-- =============================================
-- Prioriteti (od najvišeg ka najnižem):
-- 1. Ako SifraDokumentaMigracija = '00824' → AKTIVAN (ecm:status='validiran', ecm:active=true)
-- 2. Ako PolitikaCuvanja = 'Nova verzija' ili 'Novi dokument' → NEAKTIVAN (ecm:status='poništen', ecm:active=false)
-- 3. Ako PolitikaCuvanja je prazna/null, proverava se NazivDokumentaMigracija:
--    - Ako ima sufiks '- migracija' → NEAKTIVAN (ecm:status='poništen', ecm:active=false)
--    - Ako nema sufiks '- migracija' → AKTIVAN (ecm:status='validiran', ecm:active=true)
-- =============================================

-- Opciono: Kreiranje view-a za testiranje logike
IF OBJECT_ID(N'[dbo].[vw_DocumentMappingStatusCheck]', N'V') IS NOT NULL
    DROP VIEW [dbo].[vw_DocumentMappingStatusCheck];
GO

CREATE VIEW [dbo].[vw_DocumentMappingStatusCheck]
AS
SELECT
    ID,
    Naziv,
    SifraDokumenta,
    SifraDokumentaMigracija,
    NazivDokumentaMigracija,
    PolitikaCuvanja,
    CASE
        -- Prioritet 1: Šifra 00824 je uvek aktivna
        WHEN SifraDokumentaMigracija = '00824' THEN 'validiran'
        -- Prioritet 2: PolitikaCuvanja određuje status
        WHEN PolitikaCuvanja IN ('Nova verzija', 'Novi dokument') THEN 'poništen'
        -- Prioritet 3: Sufiks '- migracija' određuje status
        WHEN NazivDokumentaMigracija LIKE '%- migracija' OR NazivDokumentaMigracija LIKE '%– migracija' THEN 'poništen'
        ELSE 'validiran'
    END AS [ecm:status],
    CASE
        -- Prioritet 1: Šifra 00824 je uvek aktivna
        WHEN SifraDokumentaMigracija = '00824' THEN 1
        -- Prioritet 2: PolitikaCuvanja određuje status
        WHEN PolitikaCuvanja IN ('Nova verzija', 'Novi dokument') THEN 0
        -- Prioritet 3: Sufiks '- migracija' određuje status
        WHEN NazivDokumentaMigracija LIKE '%- migracija' OR NazivDokumentaMigracija LIKE '%– migracija' THEN 0
        ELSE 1
    END AS [ecm:active],
    CASE
        WHEN SifraDokumentaMigracija = '00824' THEN 'Prioritet 1: Šifra 00824'
        WHEN PolitikaCuvanja IN ('Nova verzija', 'Novi dokument') THEN 'Prioritet 2: PolitikaCuvanja'
        WHEN NazivDokumentaMigracija LIKE '%- migracija' OR NazivDokumentaMigracija LIKE '%– migracija' THEN 'Prioritet 3: Sufiks migracija'
        ELSE 'Default: Aktivan'
    END AS StatusReason
FROM [dbo].[DocumentMappings];
GO

PRINT ''
PRINT 'Migration completed successfully!'
PRINT 'Use the following query to verify the status logic:'
PRINT 'SELECT * FROM vw_DocumentMappingStatusCheck ORDER BY ID'
GO
