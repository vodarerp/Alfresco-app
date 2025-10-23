# Log4net SQL Server Configuration

Ovaj folder sadrži log4net konfiguraciju za SQL Server database logging.

## 📋 Setup

### 1. Kreiranje Tabele

Prvo pokreni SQL skriptu za kreiranje log tabele:

```sql
-- Skripta se nalazi u: SqlServer.Infrastructure\Scripts\01_CreateTables.sql
-- Automatski kreira tabelu: AlfrescoMigration_Logger
```

### 2. Konfiguracija Connection String-a

Otvori `log4net.sqlserver.config` i ažuriraj connection string:

```xml
<connectionString value="Server=localhost;Database=YourDatabaseName;User Id=YourUser;Password=YourPassword;TrustServerCertificate=True;" />
```

**Primer:**
```xml
<connectionString value="Server=localhost;Database=AlfrescoMigration;User Id=sa;Password=MyPassword123;TrustServerCertificate=True;" />
```

### 3. Integracija u Projekat

#### Opcija A: Program.cs ili Startup klasa

```csharp
using log4net;
using log4net.Config;
using System.IO;

// Na početku aplikacije:
var logRepository = LogManager.GetRepository(Assembly.GetEntryAssembly());
XmlConfigurator.Configure(logRepository, new FileInfo("log4net.sqlserver.config"));

// Setuj globalni AppInstance
log4net.GlobalContext.Properties["AppInstance"] = "MigrationService-1";
```

#### Opcija B: .csproj fajl (copy config na build)

```xml
<ItemGroup>
  <None Update="log4net.sqlserver.config">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

## 🎯 Korišćenje

### Basic Logging

```csharp
// Dobavi logger instancu
private static readonly ILog log = LogManager.GetLogger(typeof(MyClass));

// Jednostavno logovanje
log.Info("Document processed successfully");
log.Error("Failed to process document", exception);
log.Debug("Processing document with ID: 12345");
```

### Context Properties Logging

Za logovanje sa custom properti-jima (WorkerId, BatchId, DocumentId):

```csharp
// Postavi context properties
LogicalThreadContext.Properties["WorkerId"] = "Worker-1";
LogicalThreadContext.Properties["BatchId"] = "Batch-2025-001";
LogicalThreadContext.Properties["DocumentId"] = "DOC-12345";
LogicalThreadContext.Properties["UserId"] = "admin";

// Loguj sa context-om
log.Info("Processing document in batch");

// Cleanup nakon obrade
LogicalThreadContext.Properties.Remove("DocumentId");
```

### Named Loggers

#### DbLogger - samo u bazu
```csharp
private static readonly ILog dbLog = LogManager.GetLogger("DbLogger");
dbLog.Info("This goes to database only");
```

#### FileLogger - samo u fajl
```csharp
private static readonly ILog fileLog = LogManager.GetLogger("FileLogger");
fileLog.Debug("This goes to file only");
```

#### UiLogger - za UI monitoring
```csharp
private static readonly ILog uiLog = LogManager.GetLogger("UiLogger");
uiLog.Info("Progress: 50% completed");
```

#### HybridLogger - u bazu i fajl
```csharp
private static readonly ILog hybridLog = LogManager.GetLogger("HybridLogger");
hybridLog.Error("Critical error - logged both places", exception);
```

## 📊 Struktura Log Tabele

```sql
CREATE TABLE [AlfrescoMigration_Logger] (
    [Id] BIGINT IDENTITY(1,1) PRIMARY KEY,
    [LOG_DATE] DATETIME2 NOT NULL,
    [LOG_LEVEL] NVARCHAR(50) NOT NULL,      -- INFO, DEBUG, WARN, ERROR
    [LOGGER] NVARCHAR(255) NOT NULL,         -- Class name
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
    [APPINSTANCE] NVARCHAR(100) NULL
);
```

## 🔍 Query Primeri

### Sve greške iz poslednjeg sata
```sql
SELECT * FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL = 'ERROR'
  AND LOG_DATE >= DATEADD(HOUR, -1, GETUTCDATE())
ORDER BY LOG_DATE DESC;
```

### Log-ovi po WorkerId
```sql
SELECT LOG_DATE, LOG_LEVEL, MESSAGE, DOCUMENTID
FROM AlfrescoMigration_Logger
WHERE WORKERID = 'Worker-1'
ORDER BY LOG_DATE DESC;
```

### Log-ovi za određeni Batch
```sql
SELECT LOG_DATE, LOG_LEVEL, LOGGER, MESSAGE
FROM AlfrescoMigration_Logger
WHERE BATCHID = 'Batch-2025-001'
ORDER BY LOG_DATE;
```

### Count po Log Level-u (poslednji dan)
```sql
SELECT LOG_LEVEL, COUNT(*) AS Count
FROM AlfrescoMigration_Logger
WHERE LOG_DATE >= DATEADD(DAY, -1, GETUTCDATE())
GROUP BY LOG_LEVEL
ORDER BY Count DESC;
```

### Najčešće greške
```sql
SELECT TOP 10
    MESSAGE,
    COUNT(*) AS ErrorCount,
    MAX(LOG_DATE) AS LastOccurred
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL = 'ERROR'
GROUP BY MESSAGE
ORDER BY ErrorCount DESC;
```

## ⚙️ Konfiguracija Parametara

### Buffer Size
```xml
<bufferSize value="2" />
```
- Broj log poruka pre slanja u bazu
- Manja vrednost = real-time logging (sporije)
- Veća vrednost = batch logging (brže, ali može se izgubiti log pri crash-u)
- **Preporuka:** 1-5 za kritične aplikacije, 10-50 za high-throughput

### Log Levels
```xml
<level value="Info" />  <!-- Info, Debug, Warn, Error, Fatal -->
```

**Preporuke:**
- **Production:** Info ili Warn
- **Development:** Debug
- **Troubleshooting:** Debug

## 🚀 Performance Tips

### 1. Filtruj logove po level-u
```csharp
if (log.IsDebugEnabled)
{
    log.Debug($"Expensive string operation: {GetExpensiveDebugInfo()}");
}
```

### 2. Koristi Async Wrapper (opciono)
```xml
<appender name="AsyncWrapper" type="log4net.Appender.AsyncAppender">
  <appender-ref ref="SqlServerAdoAppender" />
</appender>
```

### 3. Maintenance - Arhiviranje Starih Log-ova
```sql
-- Arhiviraj log-ove starije od 90 dana
INSERT INTO AlfrescoMigration_Logger_Archive
SELECT * FROM AlfrescoMigration_Logger
WHERE LOG_DATE < DATEADD(DAY, -90, GETUTCDATE());

DELETE FROM AlfrescoMigration_Logger
WHERE LOG_DATE < DATEADD(DAY, -90, GETUTCDATE());
```

### 4. Index Maintenance
```sql
-- Rebuild indeksa (mesečno)
ALTER INDEX ALL ON AlfrescoMigration_Logger REBUILD;

-- Update statistike (nedeljno)
UPDATE STATISTICS AlfrescoMigration_Logger;
```

## 🛠️ Troubleshooting

### Problem: Log-ovi se ne upisuju u bazu

**Proveri:**
1. Connection string je tačan
2. SQL Server user ima INSERT privilegije
3. Tabela `AlfrescoMigration_Logger` postoji
4. Log4net je konfigurisan u Program.cs
5. Proveri log4net debug mode:
```xml
<log4net debug="true" xmlns="urn:log4net">
```

### Problem: Spore INSERT operacije

**Rešenja:**
1. Povećaj `bufferSize` sa 2 na 10-20
2. Koristi Async appender wrapper
3. Proveri indekse na tabeli
4. Razmotri particionisanje tabele

### Problem: Baza raste previše brzo

**Rešenja:**
1. Smanji log level (Debug -> Info)
2. Implementiraj log retention policy (arhiviranje)
3. Loguj samo kritične operacije u bazu
4. Koristi FileLogger za verbose logging

## 📦 NuGet Packages

Potrebni paketi:
```xml
<PackageReference Include="log4net" Version="2.0.17" />
<PackageReference Include="Microsoft.Data.SqlClient" Version="6.1.2" />
```

## 🔗 Dodatni Resursi

- [Log4net Documentation](https://logging.apache.org/log4net/)
- [AdoNetAppender Documentation](https://logging.apache.org/log4net/release/sdk/html/T_log4net_Appender_AdoNetAppender.htm)
- [SQL Server Best Practices](https://docs.microsoft.com/en-us/sql/relational-databases/sql-server-index-design-guide)

---

**Version:** 1.0
**Last Updated:** 2025-01-23
**Maintained By:** Development Team
