# KDP Document Processing - Implementation Plan

## 📋 Pregled

Implementacija funkcionalnosti za obradu KDP dokumenata (tipovi 00824 i 00099) koji se odnose na KDP vlasnike za FL.

### Cilj
Pronaći ACC-{coreId} foldere koji sadrže **samo neaktivne** KDP dokumente, aktivirati najmlađi dokument, eksportovati listu za banku, i omogućiti import i update nakon što banka popuni podatke.

---

## 🏗️ Arhitektura Rešenja

```
┌─────────────────────────────────────────────────────────────┐
│ FAZA 1: EKSPORT (CT → Banka)                                │
├─────────────────────────────────────────────────────────────┤
│ 1. AFTS Query → Svi KDP dokumenti (00824/00099)            │
│ 2. Bulk Insert → KdpDocumentStaging                         │
│ 3. sp_ProcessKdpDocuments → Procesuiranje                   │
│ 4. INSERT → KdpExportResult                                 │
│ 5. SELECT * → Export u Excel                                │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ FAZA 2: BANKA POPUNJAVA EXCEL                               │
│    → Lista računa koji su bili otvoreni na dan kreiranja    │
└─────────────────────────────────────────────────────────────┘
                          ↓
┌─────────────────────────────────────────────────────────────┐
│ FAZA 3: IMPORT I UPDATE (Banka → CT → Alfresco)            │
├─────────────────────────────────────────────────────────────┤
│ 1. Import Excel → KdpImportFromBank                         │
│ 2. sp_ValidateKdpImport → Validacija                        │
│ 3. sp_CreateUpdateBatch → Kreiraj batch                     │
│ 4. C# Service → Batch update Alfresco                       │
│ 5. sp_LogUpdateResult → Log rezultata                       │
│ 6. sp_RetryFailedUpdates → Retry (opciono)                  │
└─────────────────────────────────────────────────────────────┘
```

---

## 📊 Database Schema

### 1. KdpDocumentStaging (Staging Tabela)

**Svrha:** Privremeno čuvanje svih KDP dokumenata iz Alfresca.

```sql
-- Lokacija: SQL_Scripts/CREATE_KDP_TABLES.sql

CREATE TABLE KdpDocumentStaging (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Alfresco podaci
    NodeId NVARCHAR(100) NOT NULL,
    DocumentName NVARCHAR(500),
    DocumentPath NVARCHAR(2000),
    ParentFolderId NVARCHAR(100),
    ParentFolderName NVARCHAR(200),

    -- Custom properties iz Alfresca
    DocumentType NVARCHAR(10),           -- ecm:docType (00824 ili 00099)
    DocumentStatus NVARCHAR(10),         -- ecm:docStatus (2 = neaktivan, 1 = aktivan)
    CreatedDate DATETIME,                -- cm:created
    AccountNumbers NVARCHAR(500),        -- ecm:bnkAccountNumber (ako već postoji)

    -- Ekstrahovani podaci
    AccFolderName NVARCHAR(100),         -- ACC-123456 (ekstrahovano iz path-a)
    CoreId NVARCHAR(50),                 -- 123456 (klijentski broj)

    -- Metadata
    ProcessedDate DATETIME DEFAULT GETDATE(),

    -- Indexi za performance
    INDEX IX_AccFolderName (AccFolderName),
    INDEX IX_DocumentStatus (DocumentStatus),
    INDEX IX_DocumentType (DocumentType),
    INDEX IX_NodeId UNIQUE (NodeId)
);
```

---

### 2. KdpExportResult (Finalna Tabela za Eksport)

**Svrha:** Rezultati obrade - dokumenti spremni za eksport banci.

```sql
CREATE TABLE KdpExportResult (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Kolone za banku (prema zahtevima)
    ReferncaDosijea NVARCHAR(200),          -- Path ACC foldera
    KlijentskiBroj NVARCHAR(50),            -- CoreId
    ReferencaDokumenta NVARCHAR(100),       -- NodeId dokumenta
    TipDokumenta NVARCHAR(10),              -- 00824 ili 00099
    DatumKreiranjaDokumenta DATETIME,       -- cm:created

    -- Kolona za banku da popuni
    ListaRacuna NVARCHAR(500) NULL,         -- Banka popunjava (računi odvojeni zarezom)

    -- Dodatni podaci za praćenje
    DocumentName NVARCHAR(500),
    AccFolderName NVARCHAR(100),
    TotalKdpDocumentsInFolder INT,          -- Ukupan broj KDP dokumenata u folderu

    -- Metadata
    ExportDate DATETIME DEFAULT GETDATE(),
    IsActivated BIT DEFAULT 0,              -- Da li je dokument aktiviran u Alfrescu
    ActivationDate DATETIME NULL,

    INDEX IX_ReferencaDokumenta UNIQUE (ReferencaDokumenta),
    INDEX IX_KlijentskiBroj (KlijentskiBroj),
    INDEX IX_AccFolderName (AccFolderName)
);
```

---

### 3. KdpImportFromBank (Import od Banke)

**Svrha:** Import Excel fajla koji banka popuni sa listom računa.

```sql
CREATE TABLE KdpImportFromBank (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Podaci iz Excel-a (koje banka popuni)
    ReferencaDokumenta NVARCHAR(100) NOT NULL,  -- NodeId
    ListaRacuna NVARCHAR(500) NOT NULL,         -- Računi odvojeni zarezom

    -- Validacija
    IsValid BIT DEFAULT 0,
    ValidationMessage NVARCHAR(1000),

    -- Metadata
    ImportDate DATETIME DEFAULT GETDATE(),
    ImportedBy NVARCHAR(100),
    ImportBatchId UNIQUEIDENTIFIER DEFAULT NEWID(),

    INDEX IX_ReferencaDokumenta (ReferencaDokumenta),
    INDEX IX_ImportBatchId (ImportBatchId)
);
```

---

### 4. KdpUpdateLog (Log Update-ova)

**Svrha:** Tracking svih update-ova ka Alfrescu (success/failure, retry).

```sql
CREATE TABLE KdpUpdateLog (
    Id BIGINT IDENTITY(1,1) PRIMARY KEY,

    -- Reference
    NodeId NVARCHAR(100) NOT NULL,
    AccFolderName NVARCHAR(100),
    CoreId NVARCHAR(50),

    -- Update podaci (before/after)
    OldDocType NVARCHAR(10),
    NewDocType NVARCHAR(10),
    OldStatus NVARCHAR(10),
    NewStatus NVARCHAR(10),
    AccountNumbers NVARCHAR(500),

    -- Status
    UpdateStatus NVARCHAR(20) DEFAULT 'Pending',  -- Pending, InProgress, Success, Failed
    ErrorMessage NVARCHAR(2000),
    RetryCount INT DEFAULT 0,

    -- Metadata
    CreatedDate DATETIME DEFAULT GETDATE(),
    UpdatedDate DATETIME,
    UpdatedBy NVARCHAR(100),
    ImportBatchId UNIQUEIDENTIFIER,

    INDEX IX_NodeId (NodeId),
    INDEX IX_UpdateStatus (UpdateStatus),
    INDEX IX_ImportBatchId (ImportBatchId)
);
```

---

## 🔧 SQL Stored Procedures

### 1. sp_ProcessKdpDocuments

**Svrha:** Pronalazi foldere sa samo neaktivnim KDP dokumentima i kreira eksport rezultate.

```sql
-- Lokacija: SQL_Scripts/PROCEDURES/sp_ProcessKdpDocuments.sql

CREATE PROCEDURE sp_ProcessKdpDocuments
AS
BEGIN
    SET NOCOUNT ON;

    BEGIN TRY
        BEGIN TRANSACTION;

        -- Očisti prethodne rezultate (opciono - može se zakomentarisati ako želiš da čuvaš istoriju)
        -- TRUNCATE TABLE KdpExportResult;

        -- CTE 1: Folderi sa bar jednim neaktivnim KDP dokumentom
        WITH InactiveFolders AS (
            SELECT DISTINCT AccFolderName
            FROM KdpDocumentStaging
            WHERE DocumentStatus = '2'
        ),

        -- CTE 2: Folderi sa bar jednim aktivnim KDP dokumentom
        ActiveFolders AS (
            SELECT DISTINCT AccFolderName
            FROM KdpDocumentStaging
            WHERE DocumentStatus != '2'
        ),

        -- CTE 3: Kandidat folderi = Imaju neaktivne ALI NEMAJU aktivne
        CandidateFolders AS (
            SELECT i.AccFolderName
            FROM InactiveFolders i
            WHERE NOT EXISTS (
                SELECT 1
                FROM ActiveFolders a
                WHERE a.AccFolderName = i.AccFolderName
            )
        ),

        -- CTE 4: Brojanje KDP dokumenata po folderu
        DocumentCounts AS (
            SELECT
                AccFolderName,
                COUNT(*) as TotalDocs
            FROM KdpDocumentStaging
            WHERE DocumentStatus = '2'
            GROUP BY AccFolderName
        ),

        -- CTE 5: Najmlađi dokument po folderu (ROW_NUMBER za sortiranje po datumu)
        YoungestDocuments AS (
            SELECT
                kds.*,
                dc.TotalDocs,
                ROW_NUMBER() OVER (
                    PARTITION BY kds.AccFolderName
                    ORDER BY kds.CreatedDate DESC
                ) as RowNum
            FROM KdpDocumentStaging kds
            INNER JOIN CandidateFolders cf
                ON kds.AccFolderName = cf.AccFolderName
            INNER JOIN DocumentCounts dc
                ON kds.AccFolderName = dc.AccFolderName
            WHERE kds.DocumentStatus = '2'
        )

        -- Upisivanje u finalnu tabelu (samo najmlađi dokument po folderu)
        INSERT INTO KdpExportResult (
            ReferncaDosijea,
            KlijentskiBroj,
            ReferencaDokumenta,
            TipDokumenta,
            DatumKreiranjaDokumenta,
            DocumentName,
            AccFolderName,
            TotalKdpDocumentsInFolder,
            ListaRacuna  -- NULL initially, banka će popuniti
        )
        SELECT
            DocumentPath as ReferncaDosijea,
            CoreId as KlijentskiBroj,
            NodeId as ReferencaDokumenta,
            DocumentType as TipDokumenta,
            CreatedDate as DatumKreiranjaDokumenta,
            DocumentName,
            AccFolderName,
            TotalDocs as TotalKdpDocumentsInFolder,
            NULL as ListaRacuna
        FROM YoungestDocuments
        WHERE RowNum = 1
        ORDER BY AccFolderName;

        COMMIT TRANSACTION;

        -- Vraćanje statistike
        SELECT
            COUNT(*) as TotalCandidates,
            SUM(TotalKdpDocumentsInFolder) as TotalDocumentsInFolders,
            MIN(DatumKreiranjaDokumenta) as OldestDocument,
            MAX(DatumKreiranjaDokumenta) as NewestDocument
        FROM KdpExportResult
        WHERE ExportDate >= DATEADD(MINUTE, -5, GETDATE());  -- Rezultati iz poslednjih 5 minuta

    END TRY
    BEGIN CATCH
        IF @@TRANCOUNT > 0
            ROLLBACK TRANSACTION;

        DECLARE @ErrorMessage NVARCHAR(4000) = ERROR_MESSAGE();
        DECLARE @ErrorSeverity INT = ERROR_SEVERITY();
        DECLARE @ErrorState INT = ERROR_STATE();

        RAISERROR (@ErrorMessage, @ErrorSeverity, @ErrorState);
    END CATCH
END;
GO
```

---

### 2. sp_ValidateKdpImport

**Svrha:** Validacija podataka iz Excel-a pre slanja ka Alfrescu.

```sql
-- Lokacija: SQL_Scripts/PROCEDURES/sp_ValidateKdpImport.sql

CREATE PROCEDURE sp_ValidateKdpImport
AS
BEGIN
    SET NOCOUNT ON;

    -- Validacija 1: Da li NodeId postoji u KdpExportResult?
    UPDATE imp
    SET IsValid = 0,
        ValidationMessage = 'NodeId ne postoji u export rezultatima'
    FROM KdpImportFromBank imp
    WHERE NOT EXISTS (
        SELECT 1 FROM KdpExportResult exp
        WHERE exp.ReferencaDokumenta = imp.ReferencaDokumenta
    )
    AND ValidationMessage IS NULL;

    -- Validacija 2: Da li su računi popunjeni?
    UPDATE imp
    SET IsValid = 0,
        ValidationMessage = 'Lista računa nije popunjena'
    FROM KdpImportFromBank imp
    WHERE (ListaRacuna IS NULL OR LTRIM(RTRIM(ListaRacuna)) = '')
    AND ValidationMessage IS NULL;

    -- Validacija 3: Da li su računi validnog formata? (brojevi i zarezi)
    UPDATE imp
    SET IsValid = 0,
        ValidationMessage = 'Lista računa nije validnog formata (očekuju se brojevi odvojeni zarezom)'
    FROM KdpImportFromBank imp
    WHERE ListaRacuna NOT LIKE '%[0-9]%'
       OR ListaRacuna LIKE '%[^0-9,]%'  -- Sadrži karaktere koji nisu brojevi ili zarezi
    AND ValidationMessage IS NULL;

    -- Validacija 4: Da li je dokument već update-ovan?
    UPDATE imp
    SET IsValid = 0,
        ValidationMessage = 'Dokument je već update-ovan'
    FROM KdpImportFromBank imp
    INNER JOIN KdpExportResult exp
        ON imp.ReferencaDokumenta = exp.ReferencaDokumenta
    WHERE exp.IsActivated = 1
    AND imp.ValidationMessage IS NULL;

    -- Validni redovi
    UPDATE imp
    SET IsValid = 1,
        ValidationMessage = 'OK - Spreman za update'
    FROM KdpImportFromBank imp
    WHERE ValidationMessage IS NULL;

    -- Vraćanje statistike
    SELECT
        COUNT(*) as TotalRows,
        SUM(CASE WHEN IsValid = 1 THEN 1 ELSE 0 END) as ValidRows,
        SUM(CASE WHEN IsValid = 0 THEN 1 ELSE 0 END) as InvalidRows
    FROM KdpImportFromBank;

    -- Vraćanje invalid redova za pregled
    SELECT
        ReferencaDokumenta,
        ListaRacuna,
        ValidationMessage
    FROM KdpImportFromBank
    WHERE IsValid = 0
    ORDER BY Id;

END;
GO
```

---

### 3. sp_CreateUpdateBatch

**Svrha:** Kreira batch za update Alfresco dokumenata.

```sql
-- Lokacija: SQL_Scripts/PROCEDURES/sp_CreateUpdateBatch.sql

CREATE PROCEDURE sp_CreateUpdateBatch
    @ImportBatchId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Generiši novi batch ID ako nije prosleđen
    IF @ImportBatchId IS NULL
        SET @ImportBatchId = NEWID();

    -- Ažuriraj ImportBatchId za sve validne import-e
    UPDATE KdpImportFromBank
    SET ImportBatchId = @ImportBatchId
    WHERE IsValid = 1 AND ImportBatchId IS NULL;

    -- Kreiraj batch za update (samo validni redovi)
    INSERT INTO KdpUpdateLog (
        NodeId,
        AccFolderName,
        CoreId,
        OldDocType,
        NewDocType,
        OldStatus,
        NewStatus,
        AccountNumbers,
        UpdateStatus,
        ImportBatchId
    )
    SELECT
        exp.ReferencaDokumenta as NodeId,
        exp.AccFolderName,
        exp.KlijentskiBroj as CoreId,
        exp.TipDokumenta as OldDocType,
        CASE
            WHEN exp.TipDokumenta = '00824' THEN '00099'
            ELSE exp.TipDokumenta
        END as NewDocType,
        '2' as OldStatus,  -- Neaktivan
        '1' as NewStatus,  -- Aktivan
        imp.ListaRacuna as AccountNumbers,
        'Pending' as UpdateStatus,
        @ImportBatchId
    FROM KdpImportFromBank imp
    INNER JOIN KdpExportResult exp
        ON imp.ReferencaDokumenta = exp.ReferencaDokumenta
    WHERE imp.IsValid = 1
      AND imp.ImportBatchId = @ImportBatchId
      AND NOT EXISTS (
          SELECT 1 FROM KdpUpdateLog log
          WHERE log.NodeId = exp.ReferencaDokumenta
            AND log.UpdateStatus IN ('Pending', 'InProgress', 'Success')
      );

    -- Vraćanje broja kreiranih batch-ova
    SELECT
        @ImportBatchId as BatchId,
        COUNT(*) as TotalBatchItems
    FROM KdpUpdateLog
    WHERE ImportBatchId = @ImportBatchId
      AND UpdateStatus = 'Pending';

END;
GO
```

---

### 4. sp_RetryFailedUpdates

**Svrha:** Retry failed update-ova (max N pokušaja).

```sql
-- Lokacija: SQL_Scripts/PROCEDURES/sp_RetryFailedUpdates.sql

CREATE PROCEDURE sp_RetryFailedUpdates
    @MaxRetries INT = 3,
    @ImportBatchId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Resetuj status na Pending za failed update-e (do max retries)
    UPDATE KdpUpdateLog
    SET UpdateStatus = 'Pending',
        RetryCount = RetryCount + 1,
        ErrorMessage = NULL,
        UpdatedDate = GETDATE()
    WHERE UpdateStatus = 'Failed'
      AND RetryCount < @MaxRetries
      AND (@ImportBatchId IS NULL OR ImportBatchId = @ImportBatchId);

    -- Vraćanje broja retry-a
    SELECT
        COUNT(*) as RetriedCount,
        @ImportBatchId as BatchId
    FROM KdpUpdateLog
    WHERE UpdateStatus = 'Pending'
      AND RetryCount > 0
      AND (@ImportBatchId IS NULL OR ImportBatchId = @ImportBatchId);

END;
GO
```

---

### 5. sp_GetUpdateStatistics

**Svrha:** Reporting - statistika update-ova.

```sql
-- Lokacija: SQL_Scripts/PROCEDURES/sp_GetUpdateStatistics.sql

CREATE PROCEDURE sp_GetUpdateStatistics
    @ImportBatchId UNIQUEIDENTIFIER = NULL
AS
BEGIN
    SET NOCOUNT ON;

    -- Ukupna statistika
    SELECT
        COUNT(*) as TotalUpdates,
        SUM(CASE WHEN UpdateStatus = 'Pending' THEN 1 ELSE 0 END) as PendingCount,
        SUM(CASE WHEN UpdateStatus = 'InProgress' THEN 1 ELSE 0 END) as InProgressCount,
        SUM(CASE WHEN UpdateStatus = 'Success' THEN 1 ELSE 0 END) as SuccessCount,
        SUM(CASE WHEN UpdateStatus = 'Failed' THEN 1 ELSE 0 END) as FailedCount,
        AVG(CAST(RetryCount as FLOAT)) as AvgRetryCount,
        MIN(CreatedDate) as FirstUpdateDate,
        MAX(UpdatedDate) as LastUpdateDate
    FROM KdpUpdateLog
    WHERE @ImportBatchId IS NULL OR ImportBatchId = @ImportBatchId;

    -- Failed updates detalji
    SELECT TOP 100
        NodeId,
        AccFolderName,
        CoreId,
        ErrorMessage,
        RetryCount,
        UpdatedDate
    FROM KdpUpdateLog
    WHERE UpdateStatus = 'Failed'
      AND (@ImportBatchId IS NULL OR ImportBatchId = @ImportBatchId)
    ORDER BY UpdatedDate DESC;

END;
GO
```

---

## 💻 C# Implementation

### 1. Project Structure

```
Migration.Abstraction/
  └── Interfaces/
      └── IKdpDocumentProcessingService.cs

Migration.Infrastructure/
  └── Implementation/
      └── Services/
          └── KdpDocumentProcessingService.cs

Alfresco.Contracts/
  └── Models/
      ├── KdpDocumentStaging.cs
      ├── KdpExportResult.cs
      ├── KdpImportFromBank.cs
      └── KdpUpdateLog.cs

Alfresco.App/
  └── Windows/
      └── KdpProcessingWindow.xaml
      └── KdpProcessingWindow.xaml.cs
```

---

### 2. Interface - IKdpDocumentProcessingService

```csharp
// Lokacija: Migration.Abstraction/Interfaces/IKdpDocumentProcessingService.cs

namespace Migration.Abstraction.Interfaces
{
    public interface IKdpDocumentProcessingService
    {
        /// <summary>
        /// FAZA 1: Učitava sve KDP dokumente iz Alfresca i puni staging tabelu
        /// </summary>
        Task<int> LoadKdpDocumentsToStagingAsync(CancellationToken ct = default);

        /// <summary>
        /// FAZA 1: Procesuira staging podatke i kreira eksport rezultate
        /// </summary>
        Task<(int totalCandidates, int totalDocuments)> ProcessKdpDocumentsAsync(CancellationToken ct = default);

        /// <summary>
        /// FAZA 1: Eksportuje rezultate u Excel fajl
        /// </summary>
        Task ExportToExcelAsync(string filePath, CancellationToken ct = default);

        /// <summary>
        /// FAZA 1: Aktivira najmlađe dokumente u Alfrescu (opciono - može se raditi i nakon import-a)
        /// </summary>
        Task<int> ActivateYoungestDocumentsAsync(CancellationToken ct = default);

        /// <summary>
        /// FAZA 3: Importuje Excel fajl od banke
        /// </summary>
        Task<Guid> ImportFromBankExcelAsync(string filePath, CancellationToken ct = default);

        /// <summary>
        /// FAZA 3: Validira import podatke
        /// </summary>
        Task<(int total, int valid, int invalid)> ValidateImportAsync(Guid? batchId = null, CancellationToken ct = default);

        /// <summary>
        /// FAZA 3: Kreira batch za update
        /// </summary>
        Task<(Guid batchId, int count)> CreateUpdateBatchAsync(Guid? batchId = null, CancellationToken ct = default);

        /// <summary>
        /// FAZA 3: Izvršava batch update ka Alfrescu
        /// </summary>
        Task<(int success, int failed, int total)> ExecuteUpdateBatchAsync(Guid? batchId = null, CancellationToken ct = default);

        /// <summary>
        /// FAZA 3: Retry failed update-ova
        /// </summary>
        Task<int> RetryFailedUpdatesAsync(int maxRetries = 3, Guid? batchId = null, CancellationToken ct = default);

        /// <summary>
        /// Reporting: Dobavi statistiku update-ova
        /// </summary>
        Task<KdpUpdateStatistics> GetUpdateStatisticsAsync(Guid? batchId = null, CancellationToken ct = default);

        /// <summary>
        /// Utility: Očisti staging tabelu (za novo pokretanje)
        /// </summary>
        Task ClearStagingAsync(CancellationToken ct = default);
    }
}
```

---

### 3. Models

```csharp
// Lokacija: Alfresco.Contracts/Models/KdpUpdateStatistics.cs

namespace Alfresco.Contracts.Models
{
    public class KdpUpdateStatistics
    {
        public int TotalUpdates { get; set; }
        public int PendingCount { get; set; }
        public int InProgressCount { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public double AvgRetryCount { get; set; }
        public DateTime? FirstUpdateDate { get; set; }
        public DateTime? LastUpdateDate { get; set; }
        public List<FailedUpdateInfo> FailedUpdates { get; set; } = new();
    }

    public class FailedUpdateInfo
    {
        public string NodeId { get; set; } = string.Empty;
        public string AccFolderName { get; set; } = string.Empty;
        public string CoreId { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public DateTime? UpdatedDate { get; set; }
    }
}
```

---

### 4. Service Implementation (Skeleton)

```csharp
// Lokacija: Migration.Infrastructure/Implementation/Services/KdpDocumentProcessingService.cs

using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Alfresco.Abstraction.Interfaces;
using System.Data;

namespace Migration.Infrastructure.Implementation.Services
{
    public class KdpDocumentProcessingService : IKdpDocumentProcessingService
    {
        private readonly IAlfrescoReadApi _alfrescoReadApi;
        private readonly IAlfrescoWriteApi _alfrescoWriteApi;
        private readonly string _connectionString;
        private readonly ILogger<KdpDocumentProcessingService> _logger;

        public KdpDocumentProcessingService(
            IAlfrescoReadApi alfrescoReadApi,
            IAlfrescoWriteApi alfrescoWriteApi,
            IConfiguration configuration,
            ILogger<KdpDocumentProcessingService> logger)
        {
            _alfrescoReadApi = alfrescoReadApi;
            _alfrescoWriteApi = alfrescoWriteApi;
            _connectionString = configuration.GetConnectionString("SqlServerConnection");
            _logger = logger;
        }

        // ============================================
        // FAZA 1: EKSPORT
        // ============================================

        public async Task<int> LoadKdpDocumentsToStagingAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Početak učitavanja KDP dokumenata iz Alfresca...");

            // 1. Očisti staging tabelu
            await ClearStagingAsync(ct);

            // 2. Učitaj sve KDP dokumente iz Alfresca
            var allKdpDocs = await LoadAllKdpDocumentsFromAlfrescoAsync(ct);

            _logger.LogInformation($"Učitano {allKdpDocs.Count} KDP dokumenata iz Alfresca");

            // 3. Bulk insert u staging tabelu
            await BulkInsertToStagingAsync(allKdpDocs, ct);

            _logger.LogInformation($"Uspešno upisano {allKdpDocs.Count} dokumenata u staging tabelu");

            return allKdpDocs.Count;
        }

        public async Task<(int totalCandidates, int totalDocuments)> ProcessKdpDocumentsAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Pokretanje sp_ProcessKdpDocuments...");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            using var command = new SqlCommand("sp_ProcessKdpDocuments", connection);
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 300; // 5 minuta

            using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                var totalCandidates = reader.GetInt32(0);
                var totalDocs = reader.GetInt32(1);

                _logger.LogInformation($"Procesuirano: {totalCandidates} kandidata, {totalDocs} dokumenata");

                return (totalCandidates, totalDocs);
            }

            return (0, 0);
        }

        public async Task ExportToExcelAsync(string filePath, CancellationToken ct = default)
        {
            // TODO: Implementirati Excel export korišćenjem ClosedXML ili EPPlus
            // SELECT * FROM KdpExportResult
            throw new NotImplementedException();
        }

        public async Task<int> ActivateYoungestDocumentsAsync(CancellationToken ct = default)
        {
            // TODO: Implementirati aktivaciju najmladjeg dokumenta
            // Čitaj iz KdpExportResult, pozovi UpdateNodePropertiesAsync za svaki
            throw new NotImplementedException();
        }

        // ============================================
        // FAZA 3: IMPORT I UPDATE
        // ============================================

        public async Task<Guid> ImportFromBankExcelAsync(string filePath, CancellationToken ct = default)
        {
            // TODO: Implementirati import Excel-a u KdpImportFromBank tabelu
            throw new NotImplementedException();
        }

        public async Task<(int total, int valid, int invalid)> ValidateImportAsync(Guid? batchId = null, CancellationToken ct = default)
        {
            _logger.LogInformation("Pokretanje sp_ValidateKdpImport...");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            using var command = new SqlCommand("sp_ValidateKdpImport", connection);
            command.CommandType = CommandType.StoredProcedure;
            command.CommandTimeout = 60;

            using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                var total = reader.GetInt32(0);
                var valid = reader.GetInt32(1);
                var invalid = reader.GetInt32(2);

                _logger.LogInformation($"Validacija završena: {valid}/{total} validnih, {invalid} invalid");

                return (total, valid, invalid);
            }

            return (0, 0, 0);
        }

        public async Task<(Guid batchId, int count)> CreateUpdateBatchAsync(Guid? batchId = null, CancellationToken ct = default)
        {
            batchId ??= Guid.NewGuid();

            _logger.LogInformation($"Kreiranje update batch-a: {batchId}");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            using var command = new SqlCommand("sp_CreateUpdateBatch", connection);
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@ImportBatchId", batchId);
            command.CommandTimeout = 60;

            using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                var count = reader.GetInt32(1);

                _logger.LogInformation($"Kreiran batch sa {count} dokumenata");

                return (batchId.Value, count);
            }

            return (batchId.Value, 0);
        }

        public async Task<(int success, int failed, int total)> ExecuteUpdateBatchAsync(Guid? batchId = null, CancellationToken ct = default)
        {
            // TODO: Implementirati batch update sa paralelizacijom
            // Čitaj Pending update-e iz KdpUpdateLog
            // Pozovi UpdateNodePropertiesAsync za svaki
            // Loguj rezultate nazad u KdpUpdateLog
            throw new NotImplementedException();
        }

        public async Task<int> RetryFailedUpdatesAsync(int maxRetries = 3, Guid? batchId = null, CancellationToken ct = default)
        {
            _logger.LogInformation($"Pokretanje retry za failed update-e (max retries: {maxRetries})...");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            using var command = new SqlCommand("sp_RetryFailedUpdates", connection);
            command.CommandType = CommandType.StoredProcedure;
            command.Parameters.AddWithValue("@MaxRetries", maxRetries);
            if (batchId.HasValue)
                command.Parameters.AddWithValue("@ImportBatchId", batchId.Value);
            command.CommandTimeout = 60;

            using var reader = await command.ExecuteReaderAsync(ct);

            if (await reader.ReadAsync(ct))
            {
                var retriedCount = reader.GetInt32(0);

                _logger.LogInformation($"Retry-ovano {retriedCount} update-ova");

                return retriedCount;
            }

            return 0;
        }

        public async Task<KdpUpdateStatistics> GetUpdateStatisticsAsync(Guid? batchId = null, CancellationToken ct = default)
        {
            // TODO: Implementirati čitanje statistike
            throw new NotImplementedException();
        }

        public async Task ClearStagingAsync(CancellationToken ct = default)
        {
            _logger.LogInformation("Čišćenje staging tabele...");

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync(ct);

            using var command = new SqlCommand("TRUNCATE TABLE KdpDocumentStaging", connection);
            command.CommandTimeout = 60;

            await command.ExecuteNonQueryAsync(ct);

            _logger.LogInformation("Staging tabela očišćena");
        }

        // ============================================
        // PRIVATE HELPER METHODS
        // ============================================

        private async Task<List<Entry>> LoadAllKdpDocumentsFromAlfrescoAsync(CancellationToken ct)
        {
            var query = "(=ecm\\:docType:\"00824\" OR =ecm\\:docType:\"00099\") AND TYPE:\"cm:content\"";
            var allDocs = new List<Entry>();
            int skipCount = 0;
            const int maxItems = 1000;

            while (true)
            {
                var request = new PostSearchRequest
                {
                    Query = new QueryRequest { Query = query, Language = "afts" },
                    Include = new[] { "properties", "path" },
                    Paging = new PagingRequest { MaxItems = maxItems, SkipCount = skipCount }
                };

                var response = await _alfrescoReadApi.SearchAsync(request, ct);
                var batch = response.List.Entries.Select(e => e.Entry).ToList();

                allDocs.AddRange(batch);

                _logger.LogInformation($"Učitano {batch.Count} dokumenata (ukupno: {allDocs.Count})");

                if (batch.Count < maxItems)
                    break;

                skipCount += maxItems;
            }

            return allDocs;
        }

        private async Task BulkInsertToStagingAsync(List<Entry> documents, CancellationToken ct)
        {
            var dataTable = CreateDataTableForStaging(documents);

            using var bulkCopy = new SqlBulkCopy(_connectionString);
            bulkCopy.DestinationTableName = "KdpDocumentStaging";
            bulkCopy.BatchSize = 1000;
            bulkCopy.BulkCopyTimeout = 300; // 5 minuta

            // Column mappings
            bulkCopy.ColumnMappings.Add("NodeId", "NodeId");
            bulkCopy.ColumnMappings.Add("DocumentName", "DocumentName");
            bulkCopy.ColumnMappings.Add("DocumentPath", "DocumentPath");
            bulkCopy.ColumnMappings.Add("ParentFolderId", "ParentFolderId");
            bulkCopy.ColumnMappings.Add("ParentFolderName", "ParentFolderName");
            bulkCopy.ColumnMappings.Add("DocumentType", "DocumentType");
            bulkCopy.ColumnMappings.Add("DocumentStatus", "DocumentStatus");
            bulkCopy.ColumnMappings.Add("CreatedDate", "CreatedDate");
            bulkCopy.ColumnMappings.Add("AccountNumbers", "AccountNumbers");
            bulkCopy.ColumnMappings.Add("AccFolderName", "AccFolderName");
            bulkCopy.ColumnMappings.Add("CoreId", "CoreId");

            await bulkCopy.WriteToServerAsync(dataTable, ct);
        }

        private DataTable CreateDataTableForStaging(List<Entry> documents)
        {
            var table = new DataTable();

            // Definiši kolone
            table.Columns.Add("NodeId", typeof(string));
            table.Columns.Add("DocumentName", typeof(string));
            table.Columns.Add("DocumentPath", typeof(string));
            table.Columns.Add("ParentFolderId", typeof(string));
            table.Columns.Add("ParentFolderName", typeof(string));
            table.Columns.Add("DocumentType", typeof(string));
            table.Columns.Add("DocumentStatus", typeof(string));
            table.Columns.Add("CreatedDate", typeof(DateTime));
            table.Columns.Add("AccountNumbers", typeof(string));
            table.Columns.Add("AccFolderName", typeof(string));
            table.Columns.Add("CoreId", typeof(string));

            // Popuni redove
            foreach (var doc in documents)
            {
                var row = table.NewRow();

                row["NodeId"] = doc.Id;
                row["DocumentName"] = doc.Name ?? DBNull.Value;
                row["DocumentPath"] = doc.Path?.Name ?? DBNull.Value;
                row["ParentFolderId"] = doc.ParentId ?? DBNull.Value;
                row["ParentFolderName"] = ExtractParentFolderName(doc.Path?.Name);
                row["DocumentType"] = doc.Properties?.GetValueOrDefault("ecm:docType")?.ToString() ?? DBNull.Value;
                row["DocumentStatus"] = doc.Properties?.GetValueOrDefault("ecm:docStatus")?.ToString() ?? DBNull.Value;
                row["CreatedDate"] = doc.CreatedAt;
                row["AccountNumbers"] = doc.Properties?.GetValueOrDefault("ecm:bnkAccountNumber")?.ToString() ?? DBNull.Value;

                var accFolderName = ExtractAccFolderFromPath(doc.Path?.Name);
                row["AccFolderName"] = accFolderName ?? DBNull.Value;
                row["CoreId"] = ExtractCoreId(accFolderName);

                table.Rows.Add(row);
            }

            return table;
        }

        private string? ExtractAccFolderFromPath(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            // Primer: /Company Home/Sites/bank/documentLibrary/ACC-123456/DOSSIERS-FL/...
            var match = System.Text.RegularExpressions.Regex.Match(path, @"ACC-\d+");
            return match.Success ? match.Value : null;
        }

        private string? ExtractCoreId(string? accFolderName)
        {
            if (string.IsNullOrEmpty(accFolderName))
                return null;

            // "ACC-123456" → "123456"
            return accFolderName.Replace("ACC-", "");
        }

        private string? ExtractParentFolderName(string? path)
        {
            if (string.IsNullOrEmpty(path))
                return null;

            var parts = path.Split('/');
            return parts.Length > 1 ? parts[^2] : null;
        }
    }
}
```

---

## 🖥️ UI Implementation (WPF Window)

### KdpProcessingWindow.xaml (Skeleton)

```xml
<!-- Lokacija: Alfresco.App/Windows/KdpProcessingWindow.xaml -->

<Window x:Class="Alfresco.App.Windows.KdpProcessingWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:ui="http://schemas.modernwpf.com/2019"
        ui:WindowHelper.UseModernWindowStyle="True"
        Title="KDP Document Processing"
        Height="600" Width="900"
        WindowStartupLocation="CenterScreen">

    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- FAZA 1: EKSPORT -->
        <GroupBox Grid.Row="0" Header="FAZA 1: Eksport za Banku" Margin="0,0,0,10">
            <StackPanel>
                <Button x:Name="BtnLoadDocuments" Content="1. Učitaj KDP Dokumente iz Alfresca"
                        Click="BtnLoadDocuments_Click" Margin="5"/>
                <Button x:Name="BtnProcessDocuments" Content="2. Procesuiraj Dokumente"
                        Click="BtnProcessDocuments_Click" Margin="5"/>
                <Button x:Name="BtnExportToExcel" Content="3. Eksportuj u Excel"
                        Click="BtnExportToExcel_Click" Margin="5"/>
                <Button x:Name="BtnActivateDocuments" Content="4. Aktiviraj Najmlađe Dokumente (opciono)"
                        Click="BtnActivateDocuments_Click" Margin="5"/>
            </StackPanel>
        </GroupBox>

        <!-- FAZA 3: IMPORT -->
        <GroupBox Grid.Row="1" Header="FAZA 3: Import od Banke i Update" Margin="0,0,0,10">
            <StackPanel>
                <Button x:Name="BtnImportFromBank" Content="1. Importuj Excel od Banke"
                        Click="BtnImportFromBank_Click" Margin="5"/>
                <Button x:Name="BtnValidateImport" Content="2. Validacija Import-a"
                        Click="BtnValidateImport_Click" Margin="5"/>
                <Button x:Name="BtnCreateBatch" Content="3. Kreiraj Update Batch"
                        Click="BtnCreateBatch_Click" Margin="5"/>
                <Button x:Name="BtnExecuteUpdate" Content="4. Izvrši Update u Alfrescu"
                        Click="BtnExecuteUpdate_Click" Margin="5"/>
                <Button x:Name="BtnRetryFailed" Content="5. Retry Failed Update-ova"
                        Click="BtnRetryFailed_Click" Margin="5"/>
            </StackPanel>
        </GroupBox>

        <!-- STATUS I LOG -->
        <GroupBox Grid.Row="2" Header="Status i Log" MaxHeight="200">
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <TextBlock x:Name="TxtLog" TextWrapping="Wrap" FontFamily="Consolas" FontSize="11"/>
            </ScrollViewer>
        </GroupBox>
    </Grid>
</Window>
```

---

## 🔄 Workflow

### **FAZA 1: Eksport za Banku**

```
1. Korisnik klikne: "Učitaj KDP Dokumente iz Alfresca"
   → LoadKdpDocumentsToStagingAsync()
   → AFTS Query + Bulk Insert

2. Korisnik klikne: "Procesuiraj Dokumente"
   → ProcessKdpDocumentsAsync()
   → sp_ProcessKdpDocuments

3. Korisnik klikne: "Eksportuj u Excel"
   → ExportToExcelAsync()
   → Kreira Excel fajl sa SELECT * FROM KdpExportResult
   → Šalje banci

4. (Opciono) Korisnik klikne: "Aktiviraj Najmlađe Dokumente"
   → ActivateYoungestDocumentsAsync()
   → UpdateNodePropertiesAsync za svaki dokument
```

### **FAZA 2: Banka Popunjava**

```
Banka otvara Excel fajl i popunjava kolonu "ListaRacuna"
```

### **FAZA 3: Import i Update**

```
1. Korisnik klikne: "Importuj Excel od Banke"
   → ImportFromBankExcelAsync()
   → Učitaj Excel u KdpImportFromBank tabelu

2. Korisnik klikne: "Validacija Import-a"
   → ValidateImportAsync()
   → sp_ValidateKdpImport
   → Prikaz rezultata (valid/invalid)

3. Korisnik klikne: "Kreiraj Update Batch"
   → CreateUpdateBatchAsync()
   → sp_CreateUpdateBatch

4. Korisnik klikne: "Izvrši Update u Alfrescu"
   → ExecuteUpdateBatchAsync()
   → Batch update sa paralelizacijom
   → UpdateNodePropertiesAsync za svaki dokument

5. (Ako ima failed) Korisnik klikne: "Retry Failed Update-ova"
   → RetryFailedUpdatesAsync()
   → sp_RetryFailedUpdates
   → Ponovi korak 4
```

---

## ✅ Testing Plan

### 1. Unit Tests

```csharp
// Test LoadKdpDocumentsToStagingAsync
[Fact]
public async Task LoadKdpDocuments_ShouldInsertIntoStaging()
{
    // Arrange
    var service = CreateService();

    // Act
    var count = await service.LoadKdpDocumentsToStagingAsync();

    // Assert
    Assert.True(count > 0);
}

// Test ProcessKdpDocumentsAsync
[Fact]
public async Task ProcessKdpDocuments_ShouldFindCandidates()
{
    // Arrange
    var service = CreateService();
    await service.LoadKdpDocumentsToStagingAsync();

    // Act
    var (candidates, docs) = await service.ProcessKdpDocumentsAsync();

    // Assert
    Assert.True(candidates > 0);
}
```

### 2. Integration Tests

```
1. Test sa test Alfresco instance
2. Test sa test SQL database
3. Test celog workflow-a (end-to-end)
```

### 3. Manual Testing

```
1. Kreirati test ACC foldere sa KDP dokumentima
2. Pokrenuti FAZU 1 i proveriti Excel
3. Ručno popuniti Excel
4. Pokrenuti FAZU 3 i proveriti update u Alfrescu
```

---

## 🚀 Deployment

### 1. Database Deployment

```bash
# Pokrenuti SQL skripte redosledom:
1. SQL_Scripts/CREATE_KDP_TABLES.sql
2. SQL_Scripts/PROCEDURES/sp_ProcessKdpDocuments.sql
3. SQL_Scripts/PROCEDURES/sp_ValidateKdpImport.sql
4. SQL_Scripts/PROCEDURES/sp_CreateUpdateBatch.sql
5. SQL_Scripts/PROCEDURES/sp_RetryFailedUpdates.sql
6. SQL_Scripts/PROCEDURES/sp_GetUpdateStatistics.sql
```

### 2. Application Deployment

```bash
# Build aplikacije
dotnet build -c Release

# Publish aplikacije
dotnet publish -c Release -o ./publish

# Deploy na server
# (Kopirati publish folder na server)
```

### 3. Configuration

```json
// appsettings.json
{
  "ConnectionStrings": {
    "SqlServerConnection": "Server=localhost;Database=AlfrescoMigration;..."
  },
  "Alfresco": {
    "BaseUrl": "http://localhost:8080/alfresco/api/-default-/public/alfresco/versions/1",
    "Username": "admin",
    "Password": "admin"
  }
}
```

---

## 📝 Notes

### SQL Performance Tips

1. **Indexi su kreirani na staging tabeli** - AccFolderName, DocumentStatus, DocumentType
2. **Bulk Insert koristi batch size od 1000** - Optimalno za SQL Server
3. **Transaction timeout je 5 minuta** - Za velike volume podataka
4. **ROW_NUMBER() se koristi za sortiranje** - Bolje performanse od ORDER BY + TOP 1

### C# Performance Tips

1. **Paralelizacija update-ova** - Max 10 concurrent (ne preopterećuje Alfresco)
2. **SqlBulkCopy za insert** - 10,000+ redova/sec
3. **Connection pooling** - Automatski u SqlClient
4. **CancellationToken support** - Može se prekinuti dugačak proces

### Production Considerations

1. **Backup baze pre pokretanja** - Za rollback ako nešto pođe po zlu
2. **Logovanje svih akcija** - ILogger + SQL tabela za audit
3. **Error handling** - Try-catch + retry logika
4. **Monitoring** - SQL Server Profiler ili Application Insights
5. **Schedule-ovanje** - Može se pokrenuti kao scheduled job (npr. noću)

---

## 🎯 Summary

**Pristup sa SQL procedurom pruža:**

- ✅ **Minimalan broj HTTP poziva** (1 za učitavanje, N za update)
- ✅ **SQL optimizovano za grupisanje i sortiranje**
- ✅ **Perzistencija podataka** - može se ponoviti bez novog API poziva
- ✅ **Validacija i tracking** - sve je u bazi
- ✅ **Retry logika** - automatski retry za failed update-e
- ✅ **Audit trail** - puna istorija svih izmena
- ✅ **Production-ready** - skalabilno i pouzdano

**Procenjeno vreme implementacije:** 3-5 dana

**Procenjeno vreme izvršavanja (100K dokumenata):** 1-3 minuta

---

**End of Implementation Plan**
