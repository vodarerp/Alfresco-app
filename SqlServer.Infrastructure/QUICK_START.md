# SQL Server Quick Start Guide

## ⚡ 5-Minute Setup

### Step 1: Kreiranje Baze (1 min)

```sql
-- U SQL Server Management Studio ili Azure Data Studio:
CREATE DATABASE AlfrescoMigration;
GO

USE AlfrescoMigration;
GO

-- Pokreni: SqlServer.Infrastructure\Scripts\01_CreateTables.sql
-- Rezultat: 4 tabele kreirane (DocStaging, FolderStaging, MigrationCheckpoint, AlfrescoMigration_Logger)
```

### Step 2: Connection String (1 min)

**appsettings.json:**
```json
{
  "SqlServer": {
    "ConnectionString": "Server=localhost;Database=AlfrescoMigration;User Id=sa;Password=YourPassword;TrustServerCertificate=True;"
  }
}
```

ili **appsettings.Development.json:**
```json
{
  "SqlServer": {
    "ConnectionString": "Server=localhost;Database=AlfrescoMigration;Integrated Security=true;TrustServerCertificate=True;"
  }
}
```

### Step 3: Dependency Injection (2 min)

**Program.cs:**
```csharp
using SqlServer.Abstraction.Interfaces;
using SqlServer.Infrastructure.Implementation;

// Registruj servise
builder.Services.AddScoped<IUnitOfWork>(sp =>
{
    var connString = builder.Configuration["SqlServer:ConnectionString"];
    return new SqlServerUnitOfWork(connString);
});

builder.Services.AddScoped<IDocStagingRepository, DocStagingRepository>();
builder.Services.AddScoped<IFolderStagingRepository, FolderStagingRepository>();
builder.Services.AddScoped<IMigrationCheckpointRepository, MigrationCheckpointRepository>();
```

### Step 4: Log4net Setup (1 min)

**Program.cs (pre builder.Build()):**
```csharp
using log4net;
using log4net.Config;
using System.Reflection;

// Inicijalizuj log4net
var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
XmlConfigurator.Configure(logRepository, new FileInfo("log4net.sqlserver.config"));

// Setuj AppInstance
log4net.GlobalContext.Properties["AppInstance"] = "MigrationService-1";
```

**Kopiraj config fajl:**
- Kopiraj `SqlServer.Infrastructure/Config/log4net.sqlserver.config` u root projekta
- Ažuriraj connection string u fajlu

### Step 5: Test (1 min)

```csharp
public class TestService
{
    private readonly IDocStagingRepository _repo;
    private readonly IUnitOfWork _uow;
    private static readonly ILog log = LogManager.GetLogger(typeof(TestService));

    public TestService(IDocStagingRepository repo, IUnitOfWork uow)
    {
        _repo = repo;
        _uow = uow;
    }

    public async Task TestInsert()
    {
        await _uow.BeginAsync();

        try
        {
            var id = await _repo.AddAsync(new DocStaging
            {
                NodeId = "test-123",
                Name = "Test.pdf",
                Status = "READY",
                IsFile = true,
                FromPath = "/test",
                ToPath = "/test/new"
            });

            log.Info($"Document inserted with ID: {id}");

            await _uow.CommitAsync();
        }
        catch (Exception ex)
        {
            log.Error("Test failed", ex);
            await _uow.RollbackAsync();
        }
    }
}
```

---

## 🚀 Common Use Cases

### UC1: Insert Document za Migraciju

```csharp
await _uow.BeginAsync();

var docId = await _docRepo.AddAsync(new DocStaging
{
    NodeId = nodeId,
    Name = fileName,
    Status = "READY",
    DocumentType = "00099",
    CoreId = coreId,
    ContractNumber = contractNumber,
    IsFile = true,
    FromPath = sourcePath,
    ToPath = destinationPath
});

await _uow.CommitAsync();
```

### UC2: Batch Processing Dokumenata

```csharp
await _uow.BeginAsync();

try
{
    // Uzmi ready dokumente (sa lockingom)
    var docs = await _docRepo.TakeReadyForProcessingAsync(batchSize: 10, ct);

    foreach (var doc in docs)
    {
        // Context za logging
        LogicalThreadContext.Properties["DocumentId"] = doc.Id.ToString();

        // Obrada...
        await ProcessDocumentAsync(doc);

        // Update status
        await _docRepo.SetStatusAsync(doc.Id, "DONE", null, ct);
    }

    await _uow.CommitAsync();
}
catch (Exception ex)
{
    log.Error("Batch failed", ex);
    await _uow.RollbackAsync();
}
```

### UC3: Checkpoint Save/Load

```csharp
// Save checkpoint
var checkpoint = new MigrationCheckpoint
{
    ServiceName = "DocumentDiscovery",
    LastProcessedId = lastNodeId,
    TotalProcessed = processedCount,
    CheckpointData = JsonSerializer.Serialize(state)
};

await _checkpointRepo.UpsertAsync(checkpoint, ct);

// Load checkpoint
var checkpoint = await _checkpointRepo.GetByServiceNameAsync("DocumentDiscovery", ct);
if (checkpoint != null)
{
    var state = JsonSerializer.Deserialize<State>(checkpoint.CheckpointData);
    // Resume od checkpoint-a
}
```

### UC4: Contextual Logging

```csharp
string batchId = $"Batch-{DateTime.UtcNow:yyyyMMddHHmmss}";

// Batch-level context
LogicalThreadContext.Properties["WorkerId"] = "Worker-1";
LogicalThreadContext.Properties["BatchId"] = batchId;

dbLog.Info($"Starting batch {batchId}");

foreach (var doc in documents)
{
    // Document-level context
    LogicalThreadContext.Properties["DocumentId"] = doc.Id.ToString();

    try
    {
        // Process...
        log.Info("Document processed");
    }
    catch (Exception ex)
    {
        log.Error("Failed", ex);
    }
    finally
    {
        LogicalThreadContext.Properties.Remove("DocumentId");
    }
}

dbLog.Info($"Batch {batchId} completed");
```

---

## 📊 Quick Monitoring Queries

### Provera Statusa
```sql
-- Document statuses
SELECT Status, COUNT(*) AS Count
FROM DocStaging
GROUP BY Status;

-- Folder statuses
SELECT Status, COUNT(*) AS Count
FROM FolderStaging
GROUP BY Status;
```

### Recent Errors
```sql
-- DocStaging errors
SELECT TOP 10 * FROM DocStaging
WHERE Status = 'ERROR'
ORDER BY UpdatedAt DESC;

-- Log errors
SELECT TOP 20 LOG_DATE, LOGGER, MESSAGE
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL = 'ERROR'
ORDER BY LOG_DATE DESC;
```

### Processing Rate
```sql
-- Documents processed per hour (last 24h)
SELECT
    DATEPART(HOUR, UpdatedAt) AS Hour,
    COUNT(*) AS Processed
FROM DocStaging
WHERE Status = 'DONE'
  AND UpdatedAt >= DATEADD(HOUR, -24, GETUTCDATE())
GROUP BY DATEPART(HOUR, UpdatedAt)
ORDER BY Hour DESC;
```

---

## 🔧 Troubleshooting

### Problem: "Table does not exist"
```sql
-- Proveri da li tabele postoje:
SELECT name FROM sys.tables WHERE name LIKE '%Staging%';

-- Ako ne postoje, pokreni:
-- SqlServer.Infrastructure\Scripts\01_CreateTables.sql
```

### Problem: "Cannot insert NULL into column"
```csharp
// Setuj obavezna polja:
var doc = new DocStaging
{
    NodeId = "...",        // Required
    Name = "...",          // Required
    Status = "READY",      // Required
    IsFile = true,         // Required
    FromPath = "...",      // Required
    ToPath = "...",        // Required
    NodeType = "cm:content" // Required
};
```

### Problem: "Login failed for user"
```
1. Proveri connection string
2. Proveri da user ima pristup bazi
3. Proveri firewall pravila
4. Koristiti TrustServerCertificate=True za development
```

### Problem: Log4net ne upisuje u bazu
```csharp
// Enable debug mode:
// U log4net.config:
<log4net debug="true" xmlns="urn:log4net">

// Proveri output window za greške
```

---

## 📁 Key Files Locations

| File | Location |
|------|----------|
| Create Tables | `SqlServer.Infrastructure/Scripts/01_CreateTables.sql` |
| Log4net Config | `SqlServer.Infrastructure/Config/log4net.sqlserver.config` |
| Logger Example | `SqlServer.Infrastructure/Config/LoggerExample.cs` |
| Monitoring Queries | `SqlServer.Infrastructure/Scripts/05_UsefulQueries.sql` |
| Log Analysis | `SqlServer.Infrastructure/Scripts/06_LogAnalysisQueries.sql` |
| Full Documentation | `SqlServer.Infrastructure/Scripts/README.md` |

---

## 🎯 Next Steps

1. ✅ Setup completed
2. ⏭️ Run `04_SampleData.sql` za test podatke
3. ⏭️ Test insert/update/delete operacije
4. ⏭️ Integrisati sa existing services
5. ⏭️ Setup monitoring dashboard

---

**Need Help?** Pogledaj:
- `IMPLEMENTATION_SUMMARY.md` - Kompletan pregled
- `Scripts/README.md` - SQL skripta dokumentacija
- `Config/README_LOG4NET.md` - Log4net dokumentacija

