# Search Performance Optimization

## Problem
Pretraga dokumenata u `DocumentSelectionWindow` je bila spora i izazivala timeout exception-e zbog:
1. LIKE pretraga preko 70,000+ zapisa bez indeksa
2. Nedostatak `MultipleActiveResultSets=True` u connection string-u
3. Preklapajuće database operacije

## Rešenje

### 1. Kreiranje Indeksa (OBAVEZNO)

Morate izvršiti SQL script `CreateSearchIndexes.sql` da biste kreirali indekse na kolonama koje se pretražuju:
- `NAZIV`
- `NazivDokumenta`
- `sifraDokumenta`
- `TipDosijea`

**Kako pokrenuti:**

1. Otvorite SQL Server Management Studio (SSMS)
2. Konektujte se na server: `LENOVOX1-NIKOLA`
3. Otvorite fajl `CreateSearchIndexes.sql`
4. **VAŽNO:** Zamenite `[YourDatabaseName]` sa `AlfrescoMigration` u prvoj liniji USE statement-a:
   ```sql
   USE [AlfrescoMigration];
   ```
5. Izvršite script (F5)

**Vreme izvršavanja:**
- Sa 70,000 zapisa, kreiranje indeksa može trajati 1-5 minuta
- To je jednokratna operacija

**Provera da li su indeksi kreirani:**
```sql
SELECT
    i.name AS IndexName,
    OBJECT_NAME(i.object_id) AS TableName,
    COL_NAME(ic.object_id, ic.column_id) AS ColumnName
FROM
    sys.indexes AS i
    INNER JOIN sys.index_columns AS ic ON i.object_id = ic.object_id AND i.index_id = ic.index_id
WHERE
    OBJECT_NAME(i.object_id) = 'DocumentMappings'
    AND i.name LIKE 'IX_DocumentMappings%'
ORDER BY
    i.name, ic.key_ordinal;
```

### 2. Connection String (VEĆ AŽURIRANO)

Connection string je već ažuriran sa:
- `MultipleActiveResultSets=True` - omogućava `QueryMultipleAsync`
- `Connection Timeout=60` - 60 sekundi za connection timeout

### 3. Code Changes (VEĆ AŽURIRANO)

#### DocumentSelectionWindow.xaml.cs
- Dodato `_isLoading` flag da spreči preklapajuće database operacije
- `ItemsSource` se postavlja samo jednom u `Window_Loaded`
- Poboljšan error handling za rollback operacije

#### DocumentMappingRepository.cs
- Dodat `commandTimeout: 60` sekundi za search upit

## Očekivane Performanse

### Pre optimizacije:
- Pretraga: 5-30 sekundi (zavisno od search text-a)
- Često timeout exception-i
- Multiple active result sets errors

### Posle optimizacije:
- Prazna pretraga (load all): < 1 sekunda
- Search sa text-om: < 2 sekunde
- Nema više timeout-a
- Nema više multiple result set errors

## Testiranje

1. Pokrenite aplikaciju
2. Otvorite Document Selection window
3. Probajte da pretražite:
   - Prazan search (ucitava sve)
   - Pretraga po nazivu: npr. "ugovor"
   - Pretraga po šifri: npr. "DOC"
   - Brza promena search text-a (typing fast)

Sve ove operacije bi trebalo da rade glatko bez timeout-a.

## Rollback (ako je potrebno)

Ako želite da uklonite indekse:

```sql
USE [AlfrescoMigration];
GO

DROP INDEX IF EXISTS [IX_DocumentMappings_NAZIV] ON [dbo].[DocumentMappings];
DROP INDEX IF EXISTS [IX_DocumentMappings_NazivDokumenta] ON [dbo].[DocumentMappings];
DROP INDEX IF EXISTS [IX_DocumentMappings_sifraDokumenta] ON [dbo].[DocumentMappings];
DROP INDEX IF EXISTS [IX_DocumentMappings_TipDosijea] ON [dbo].[DocumentMappings];
DROP INDEX IF EXISTS [IX_DocumentMappings_Search_Composite] ON [dbo].[DocumentMappings];
GO
```

## Napomene

- Indeksi zauzimaju dodatni disk prostor (~10-20% veličine tabele)
- INSERT/UPDATE/DELETE operacije će biti malo sporije zbog održavanja indeksa
- Za tabelu koja se uglavnom čita (kao DocumentMappings), indeksi su essential za performanse
