# SQL Server Migration Scripts

Ova folder sadrži SQL Server skripte za kreiranje i održavanje tabela za Alfresco migraciju.

## 📋 Pregled Skripti

### 01_CreateTables.sql
**Kreiranje tabela**
- Kreira sve potrebne tabele za migraciju
- Uključuje indekse za optimizaciju performansi
- Proverava da li tabela već postoji pre kreiranja
- **Tabele:**
  - `DocStaging` - staging tabela za dokumente
  - `FolderStaging` - staging tabela za foldere
  - `MigrationCheckpoint` - checkpoint tracking za servise
  - `AlfrescoMigration_Logger` - log4net logging tabela

### 02_DropTables.sql
**Brisanje tabela**
- ⚠️ **UPOZORENJE:** Briše sve tabele i podatke!
- Koristi se samo za cleanup ili re-install
- Proverava da li tabela postoji pre brisanja

### 03_TruncateTables.sql
**Pražnjenje tabela**
- Briše sve podatke ali zadržava strukturu tabela
- Resetuje IDENTITY kolone na početak
- Brže od DELETE operacije
- ⚠️ **UPOZORENJE:** Briše sve podatke!

### 04_SampleData.sql
**Test podaci**
- Insertuje sample/test podatke za testiranje
- Uključuje primere za:
  - 2 foldera (FL i PL klijenti)
  - 3 dokumenta (KDP i DUT tipovi)
  - 2 checkpoint zapisa

### 05_UsefulQueries.sql
**Korisni upiti za monitoring**
- 15 različitih upita za praćenje migracije:
  1. Migration status summary
  2. Documents ready for processing
  3. Failed documents with errors
  4. Documents by source system
  5. Folders by client type
  6. Migration progress over time
  7. Top documents with most retries
  8. Migration checkpoint status
  9. Documents requiring type transformation
  10. Overall migration statistics
  11. Orphaned documents check
  12. Processing speed metrics
  13. Log analysis - recent errors
  14. Log activity by worker
  15. Most common log errors

## 🚀 Kako Koristiti

### Inicijalna Setup
```sql
-- 1. Prvo zameni [YourDatabaseName] sa pravim imenom baze u svim skriptama
-- 2. Pokreni kreiranje tabela:
EXEC sp_executesql @script = '01_CreateTables.sql'

-- 3. (Opciono) Insertuj test podatke:
EXEC sp_executesql @script = '04_SampleData.sql'
```

### Monitoring Migracije
```sql
-- Pokreni useful queries za monitoring:
EXEC sp_executesql @script = '05_UsefulQueries.sql'
```

### Reset/Cleanup
```sql
-- Samo prazni podatke (zadrzava strukturu):
EXEC sp_executesql @script = '03_TruncateTables.sql'

-- Potpuno brisi tabele (za reinstall):
EXEC sp_executesql @script = '02_DropTables.sql'
```

## 📊 Struktura Tabela

### DocStaging
**Glavni podaci:**
- `Id` (BIGINT, IDENTITY) - Primary key
- `NodeId` - Alfresco node ID
- `Name` - Ime dokumenta
- `FromPath` / `ToPath` - Putanje za migraciju
- `Status` - Status (NEW, READY, PROCESSING, DONE, ERROR)
- `RetryCount` - Broj pokušaja
- `ErrorMsg` - Poruka greške

**Extended migration fields:**
- `DocumentType`, `DocumentTypeMigration`
- `Source` (Heimdall, DUT, etc.)
- `CoreId`, `ContractNumber`
- `Version`, `IsSigned`
- `AccountNumbers`, `ProductType`
- `RequiresTypeTransformation`, `FinalDocumentType`

**Indeksi:**
- IX_DocStaging_Status (za query po status-u)
- IX_DocStaging_NodeId (za lookup po NodeId)
- IX_DocStaging_ParentId (za parent relationships)
- IX_DocStaging_CreatedAt (za vremenske upite)

### FolderStaging
**Glavni podaci:**
- `Id` (BIGINT, IDENTITY) - Primary key
- `NodeId` - Alfresco node ID
- `ParentId` - Parent folder ID
- `Name` - Ime foldera
- `Status` - Status (NEW, READY, PROCESSING, DONE, ERROR)
- `DestFolderId` - Destination folder u novom Alfrescu

**Extended migration fields:**
- `ClientType` (FL/PL)
- `CoreId`, `ClientName`, `MbrJmbg`
- `ProductType`, `ContractNumber`, `Batch`
- `UniqueIdentifier` (DE-{CoreId}-{ProductType}-{ContractNumber})
- `ProcessDate`, `ArchivedAt`
- ClientAPI fields: `Residency`, `Segment`, `ClientSubtype`, `Staff`, etc.

**Indeksi:**
- IX_FolderStaging_Status
- IX_FolderStaging_NodeId
- IX_FolderStaging_ParentId
- IX_FolderStaging_UniqueIdentifier
- IX_FolderStaging_CoreId

### MigrationCheckpoint
**Checkpoint tracking:**
- `Id` (BIGINT, IDENTITY) - Primary key
- `ServiceName` - Ime servisa (FolderDiscovery, DocumentDiscovery, Move)
- `CheckpointData` - JSON serialized checkpoint
- `LastProcessedId`, `LastProcessedAt`
- `TotalProcessed`, `TotalFailed`
- `BatchCounter`

**Unique constraint:**
- UQ_MigrationCheckpoint_ServiceName (jedan checkpoint po servisu)

### AlfrescoMigration_Logger
**Log4net logging tabela:**
- `Id` (BIGINT, IDENTITY) - Primary key
- `LOG_DATE` - Vreme logovanja
- `LOG_LEVEL` - Level (INFO, DEBUG, WARN, ERROR, FATAL)
- `LOGGER` - Logger name (class name)
- `MESSAGE` - Log poruka
- `EXCEPTION` - Exception stack trace

**Custom context fields:**
- `WORKERID`, `BATCHID`, `DOCUMENTID`, `USERID`
- `HOSTNAME`, `THREADID`, `APPINSTANCE`

**Indeksi:**
- IX_Logger_LogDate (descending)
- IX_Logger_LogLevel (sa included kolonama)
- IX_Logger_WorkerId, IX_Logger_BatchId, IX_Logger_DocumentId (filtered)

## 💡 Best Practices

1. **Pre Production Deploya:**
   - Testirati sve skripte na test bazi
   - Backup postojeće baze
   - Proveriti indekse i performance

2. **Monitoring:**
   - Redovno pokretati upite iz `05_UsefulQueries.sql`
   - Pratiti broj ERROR status-a
   - Proveravati RetryCount za potencijalne probleme

3. **Performance:**
   - Batch insert operacije koristiti za bulk data
   - Indeksi su već optimizovani za tipične upite
   - Razmisliti o particionisanju za jako velike količine podataka

4. **Maintenance:**
   - Periodično čišćenje DONE zapisa (arhiviranje)
   - Rebuild indeksa ako je potrebno
   - Praćenje veličine baze

## ⚙️ Konfiguracija

Pre pokretanja skripti, zameni `[YourDatabaseName]` sa stvarnim imenom baze podataka:

```sql
-- Primer:
USE [AlfrescoMigration]
GO
```

## 🔐 Permissions

Za izvršavanje ovih skripti potrebne su sledeće SQL Server permisije:
- `CREATE TABLE`
- `DROP TABLE`
- `TRUNCATE TABLE`
- `INSERT`
- `SELECT`
- `CREATE INDEX`

## 📞 Support

Za pitanja ili probleme sa skriptama, kontaktiraj razvojni tim.

---

**Version:** 1.0
**Last Updated:** 2025-01-23
**Maintained By:** Development Team
