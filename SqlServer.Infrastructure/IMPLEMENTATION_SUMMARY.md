# SQL Server Implementation Summary

## ✅ Šta je Implementirano

### 1. **SQL Server Projekti**

#### SqlServer.Abstraction
- `IRepository<T, TKey>` - osnovni repository interfejs
- `IUnitOfWork` - Unit of Work pattern
- `IDocStagingRepository` - specijalizovani interfejs
- `IFolderStagingRepository` - specijalizovani interfejs
- `IMigrationCheckpointRepository` - checkpoint interfejs

#### SqlServer.Infrastructure
- `SqlServerRepository<T, TKey>` - bazna CRUD implementacija
- `SqlServerUnitOfWork` - transakcije sa SqlConnection
- `SqlServerHelpers<T>` - helper za WHERE klauzule i metadata
- `DocStagingRepository` - implementacija sa custom metodama
- `FolderStagingRepository` - implementacija sa custom metodama
- `MigrationCheckpointRepository` - checkpoint sa upsert logikom

### 2. **SQL Server Skripte**

| Skripta | Opis |
|---------|------|
| `01_CreateTables.sql` | Kreiranje svih tabela + indeksi |
| `02_DropTables.sql` | Drop svih tabela (cleanup) |
| `03_TruncateTables.sql` | Truncate svih tabela (reset) |
| `04_SampleData.sql` | Test podaci za development |
| `05_UsefulQueries.sql` | 15 monitoring upita |
| `06_LogAnalysisQueries.sql` | 13 log analiza upita |

### 3. **Tabele**

#### DocStaging (34 kolone)
- Osnovni podaci (Id, NodeId, Name, Status, FromPath, ToPath)
- Extended migration fields (DocumentType, CoreId, ContractNumber, Version, etc.)
- 4 indeksa (Status, NodeId, ParentId, CreatedAt)

#### FolderStaging (31 kolona)
- Osnovni podaci (Id, NodeId, ParentId, Name, Status, DestFolderId)
- Extended migration fields (ClientType, CoreId, ProductType, UniqueIdentifier)
- ClientAPI fields (Residency, Segment, Staff, OpuUser, etc.)
- 5 indeksa

#### MigrationCheckpoint (10 kolona)
- ServiceName, CheckpointData, LastProcessedId, TotalProcessed, etc.
- Unique constraint na ServiceName

#### AlfrescoMigration_Logger (13 kolona)
- Log4net tabela (LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE, EXCEPTION)
- Custom context (WORKERID, BATCHID, DOCUMENTID, USERID)
- Auto fields (HOSTNAME, THREADID, APPINSTANCE)
- 5 indeksa za performanse

### 4. **Log4net Konfiguracija**

#### Config fajlovi:
- `log4net.sqlserver.config` - SQL Server AdoNetAppender
- `README_LOG4NET.md` - Kompletna dokumentacija
- `LoggerExample.cs` - 8 primera korišćenja

#### Named Loggers:
- **DbLogger** - samo u bazu
- **FileLogger** - samo u fajl
- **UiLogger** - UI monitoring
- **HybridLogger** - baza + fajl

## 🔑 Ključne Razlike: SQL Server vs Oracle

| Feature | Oracle | SQL Server |
|---------|--------|------------|
| **Parametri** | `:paramName` | `@paramName` |
| **RETURNING** | `RETURNING ... INTO :outId` | `OUTPUT INSERTED.Id` |
| **TOP N** | `FETCH FIRST :n ROWS ONLY` | `TOP (@n)` |
| **Locking** | `FOR UPDATE SKIP LOCKED` | `WITH (ROWLOCK, UPDLOCK, READPAST)` |
| **NULL Handling** | `NVL(col, 0)` | `ISNULL(col, 0)` |
| **Date/Time** | `SYSTIMESTAMP` | `SYSDATETIMEOFFSET()` |
| **String Size** | `VARCHAR2(4000)` | `NVARCHAR(MAX)` (size = -1) |
| **Provider** | `Oracle.ManagedDataAccess` | `Microsoft.Data.SqlClient` |

## 📦 NuGet Paketi

```xml
<!-- SqlServer.Infrastructure.csproj -->
<PackageReference Include="Dapper" Version="2.1.66" />
<PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.2" />

<!-- Za Log4net (u glavnom projektu) -->
<PackageReference Include="log4net" Version="2.0.17" />
```

## 🚀 Setup Uputstvo

### 1. Kreiranje Baze i Tabela

```sql
-- 1. Kreiraj bazu
CREATE DATABASE AlfrescoMigration;
GO

-- 2. Pokreni create tables skriptu
USE AlfrescoMigration;
GO
-- Pokreni: SqlServer.Infrastructure\Scripts\01_CreateTables.sql
```

### 2. Connection String

**appsettings.json:**
```json
{
  "SqlServer": {
    "ConnectionString": "Server=localhost;Database=AlfrescoMigration;User Id=sa;Password=YourPassword;TrustServerCertificate=True;",
    "CommandTimeoutSeconds": 120,
    "BulkBatchSize": 1000
  }
}
```

### 3. Dependency Injection Setup

```csharp
// Program.cs ili Startup.cs
services.AddScoped<IUnitOfWork>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var connString = config["SqlServer:ConnectionString"];
    return new SqlServerUnitOfWork(connString);
});

services.AddScoped<IDocStagingRepository, DocStagingRepository>();
services.AddScoped<IFolderStagingRepository, FolderStagingRepository>();
services.AddScoped<IMigrationCheckpointRepository, MigrationCheckpointRepository>();
```

### 4. Log4net Setup

```csharp
// Program.cs
using log4net;
using log4net.Config;

var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
XmlConfigurator.Configure(logRepository, new FileInfo("log4net.sqlserver.config"));

log4net.GlobalContext.Properties["AppInstance"] = "MigrationService-1";
```

### 5. Primena Korišćenja

```csharp
public class MigrationService
{
    private readonly IDocStagingRepository _docRepo;
    private readonly IUnitOfWork _uow;
    private static readonly ILog log = LogManager.GetLogger(typeof(MigrationService));

    public async Task ProcessDocumentsAsync(int batchSize, CancellationToken ct)
    {
        await _uow.BeginAsync(ct: ct);

        try
        {
            // Uzmi dokumente za obradu
            var docs = await _docRepo.TakeReadyForProcessingAsync(batchSize, ct);

            foreach (var doc in docs)
            {
                // Setuj context za logging
                LogicalThreadContext.Properties["DocumentId"] = doc.Id.ToString();

                log.Info($"Processing document: {doc.Name}");

                // Obrada dokumenta...

                await _docRepo.SetStatusAsync(doc.Id, "DONE", null, ct);
            }

            await _uow.CommitAsync(ct);
        }
        catch (Exception ex)
        {
            log.Error("Batch processing failed", ex);
            await _uow.RollbackAsync(ct);
            throw;
        }
    }
}
```

## 📊 Monitoring

### Provera Statusa
```sql
-- Pokreni: 05_UsefulQueries.sql
-- ili pojedinačno:

SELECT Status, COUNT(*) FROM DocStaging GROUP BY Status;
SELECT Status, COUNT(*) FROM FolderStaging GROUP BY Status;
```

### Analiza Logova
```sql
-- Pokreni: 06_LogAnalysisQueries.sql
-- ili:

SELECT LOG_LEVEL, COUNT(*)
FROM AlfrescoMigration_Logger
WHERE LOG_DATE >= DATEADD(HOUR, -1, GETUTCDATE())
GROUP BY LOG_LEVEL;
```

## 🔧 Maintenance

### Arhiviranje Starih Logova (90+ dana)
```sql
-- Backup
SELECT * INTO AlfrescoMigration_Logger_Archive
FROM AlfrescoMigration_Logger
WHERE LOG_DATE < DATEADD(DAY, -90, GETUTCDATE());

-- Delete
DELETE FROM AlfrescoMigration_Logger
WHERE LOG_DATE < DATEADD(DAY, -90, GETUTCDATE());
```

### Rebuild Indeksa
```sql
-- Mesečno
ALTER INDEX ALL ON DocStaging REBUILD;
ALTER INDEX ALL ON FolderStaging REBUILD;
ALTER INDEX ALL ON AlfrescoMigration_Logger REBUILD;
```

## 📂 Struktura Foldera

```
SqlServer.Infrastructure/
├── Config/
│   ├── log4net.sqlserver.config
│   ├── README_LOG4NET.md
│   └── LoggerExample.cs
├── Helpers/
│   └── SqlServerHelpers.cs
├── Implementation/
│   ├── SqlServerRepository.cs
│   ├── SqlServerUnitOfWork.cs
│   ├── DocStagingRepository.cs
│   ├── FolderStagingRepository.cs
│   └── MigrationCheckpointRepository.cs
└── Scripts/
    ├── 01_CreateTables.sql
    ├── 02_DropTables.sql
    ├── 03_TruncateTables.sql
    ├── 04_SampleData.sql
    ├── 05_UsefulQueries.sql
    ├── 06_LogAnalysisQueries.sql
    └── README.md

SqlServer.Abstraction/
└── Interfaces/
    ├── IRepository.cs
    ├── IUnitOfWork.cs
    ├── IDocStagingRepository.cs
    ├── IFolderStagingRepository.cs
    └── IMigrationCheckpointRepository.cs
```

## ✅ Testiranje

### 1. Build Projekata
```bash
dotnet build SqlServer.Abstraction/SqlServer.Abstraction.csproj
dotnet build SqlServer.Infrastructure/SqlServer.Infrastructure.csproj
```

### 2. Kreiranje Test Baze
```sql
USE AlfrescoMigration;
EXEC sp_executesql @script = '01_CreateTables.sql'
EXEC sp_executesql @script = '04_SampleData.sql'
```

### 3. Test Insert
```csharp
await using var uow = new SqlServerUnitOfWork(connectionString);
await uow.BeginAsync();

var docRepo = new DocStagingRepository(uow);
var newId = await docRepo.AddAsync(new DocStaging
{
    NodeId = "test-001",
    Name = "TestDoc.pdf",
    Status = "READY"
});

await uow.CommitAsync();
Console.WriteLine($"Inserted ID: {newId}");
```

## 🎯 Next Steps

1. ✅ Implementacija završena
2. ⏭️ Integration sa postojećim servisima
3. ⏭️ Unit testovi
4. ⏭️ Performance testiranje
5. ⏭️ Production deployment

---

**Version:** 1.0
**Created:** 2025-01-23
**Status:** ✅ Ready for Integration
