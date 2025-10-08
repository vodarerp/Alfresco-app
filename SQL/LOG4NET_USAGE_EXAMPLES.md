# log4net Usage Examples

## 📋 Overview

This document shows how to use log4net for logging in the Alfresco Migration application with database and file logging.

## 🔧 Configuration

The application uses two named loggers configured in `log4net.config`:

- **DbLogger** - Logs to Oracle database table `AlfrescoMigration_Logger`
- **FileLogger** - Logs to rolling file `logs/app.log`

## 💡 Basic Usage

### 1. Get Logger Instance

```csharp
using log4net;

public class MoveService
{
    private static readonly ILog _dbLogger = LogManager.GetLogger("DbLogger");
    private static readonly ILog _fileLogger = LogManager.GetLogger("FileLogger");

    // ... your code
}
```

### 2. Simple Logging

```csharp
// Info level (DB + File)
_dbLogger.Info("Starting document move operation");
_fileLogger.Debug("Detailed debug information"); // Only in file

// Warning
_dbLogger.Warn("Retry count exceeded, marking as error");

// Error with exception
try
{
    // ... risky operation
}
catch (Exception ex)
{
    _dbLogger.Error($"Failed to move document", ex);
}
```

### 3. Contextual Logging (Custom Properties)

Add custom properties for better tracking:

```csharp
using log4net;
using log4net.Core;

// Set context properties BEFORE logging
LogicalThreadContext.Properties["WorkerId"] = "MoveService-Worker-1";
LogicalThreadContext.Properties["BatchId"] = $"Batch-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
LogicalThreadContext.Properties["DocumentId"] = document.NodeId;

// Now log - properties will be stored in DB columns
_dbLogger.Info($"Moving document: {document.Name}");

// Clear after use (optional)
LogicalThreadContext.Properties.Remove("DocumentId");
```

### 4. Set Global AppInstance (Startup)

In `App.xaml.cs` or startup code:

```csharp
// Set once at application start
GlobalContext.Properties["AppInstance"] = $"Instance-{Environment.MachineName}-{Process.GetCurrentProcess().Id}";
```

## 📝 Real-World Examples

### Example 1: MoveService with Context

```csharp
public class MoveService : IMoveService
{
    private static readonly ILog _dbLogger = LogManager.GetLogger("DbLogger");
    private static readonly ILog _fileLogger = LogManager.GetLogger("FileLogger");

    public async Task MoveBatchAsync(IEnumerable<DocStaging> documents, CancellationToken ct)
    {
        var batchId = $"Batch-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
        LogicalThreadContext.Properties["BatchId"] = batchId;
        LogicalThreadContext.Properties["WorkerId"] = "MoveService-Main";

        _dbLogger.Info($"Starting batch move: {documents.Count()} documents");
        _fileLogger.Debug($"Batch details: MaxDOP={_options.Value.MaxDegreeOfParallelism}");

        var moved = 0;
        var failed = 0;

        foreach (var doc in documents)
        {
            LogicalThreadContext.Properties["DocumentId"] = doc.NodeId;

            try
            {
                await MoveDocumentAsync(doc, ct);
                moved++;

                _fileLogger.Debug($"Document moved successfully: {doc.Name}");
            }
            catch (Exception ex)
            {
                failed++;
                _dbLogger.Error($"Failed to move document: {doc.Name} to {doc.ToPath}", ex);
            }
            finally
            {
                LogicalThreadContext.Properties.Remove("DocumentId");
            }
        }

        _dbLogger.Info($"Batch completed: {moved} moved, {failed} failed");
        LogicalThreadContext.Properties.Remove("BatchId");
    }
}
```

### Example 2: DocumentDiscoveryService with Error Tracking

```csharp
public class DocumentDiscoveryService : IDocumentDiscoveryService
{
    private static readonly ILog _dbLogger = LogManager.GetLogger("DbLogger");
    private static readonly ILog _fileLogger = LogManager.GetLogger("FileLogger");

    public async Task ProcessFoldersAsync(CancellationToken ct)
    {
        LogicalThreadContext.Properties["WorkerId"] = "DocumentDiscovery-Main";

        _dbLogger.Info("Starting document discovery");

        while (!ct.IsCancellationRequested)
        {
            var folders = await AcquireFoldersForProcessingAsync(ct);

            if (!folders.Any())
            {
                _fileLogger.Debug("No folders to process, waiting...");
                await Task.Delay(5000, ct);
                continue;
            }

            var batchId = $"DocDiscovery-{DateTime.UtcNow:yyyyMMdd-HHmmss}";
            LogicalThreadContext.Properties["BatchId"] = batchId;

            _dbLogger.Info($"Processing {folders.Count} folders");

            foreach (var folder in folders)
            {
                try
                {
                    var docs = await DiscoverDocumentsInFolderAsync(folder, ct);
                    await InsertManyAsync(docs, ct);

                    _dbLogger.Info($"Discovered {docs.Count} documents in folder: {folder.Name}");
                }
                catch (Exception ex)
                {
                    _dbLogger.Error($"Failed to discover documents in folder: {folder.Name}", ex);
                }
            }

            LogicalThreadContext.Properties.Remove("BatchId");
        }
    }
}
```

### Example 3: Checkpoint/Resume Logging

```csharp
private async Task SaveCheckpointAsync(CancellationToken ct)
{
    try
    {
        var checkpoint = new MigrationCheckpoint
        {
            ServiceName = ServiceName,
            TotalProcessed = _totalMoved,
            TotalFailed = _totalFailed,
            BatchCounter = _batchCounter
        };

        await _checkpointRepo.UpsertAsync(checkpoint, ct);

        _fileLogger.Debug($"Checkpoint saved: Processed={_totalMoved}, Failed={_totalFailed}, Batches={_batchCounter}");
    }
    catch (Exception ex)
    {
        _dbLogger.Error("Failed to save checkpoint", ex);
        // Don't throw - checkpoint failure shouldn't stop processing
    }
}

private async Task LoadCheckpointAsync(CancellationToken ct)
{
    try
    {
        var checkpoint = await _checkpointRepo.GetByServiceNameAsync(ServiceName, ct);

        if (checkpoint != null)
        {
            _totalMoved = checkpoint.TotalProcessed;
            _totalFailed = checkpoint.TotalFailed;
            _batchCounter = checkpoint.BatchCounter;

            _dbLogger.Info($"Checkpoint loaded: Resuming from Processed={_totalMoved}, Failed={_totalFailed}");
        }
        else
        {
            _dbLogger.Info("No checkpoint found, starting fresh");
        }
    }
    catch (Exception ex)
    {
        _dbLogger.Error("Failed to load checkpoint, starting fresh", ex);
    }
}
```

## 🎯 Best Practices

### When to Log to Database (DbLogger)

✅ **DO** log to database:
- Important business events (batch started/completed)
- Errors and warnings
- Checkpoint events
- Document/Folder processing failures
- Performance metrics (e.g., batch completion time)

❌ **DON'T** log to database:
- Debug information
- Trace/verbose logs
- High-frequency logs (per-document success)
- Sensitive information

### When to Log to File (FileLogger)

✅ **DO** log to file:
- Debug information
- Detailed trace logs
- Configuration details
- All levels (DEBUG, INFO, WARN, ERROR)

### Log Levels

- **DEBUG** - Development/troubleshooting (file only)
- **INFO** - Important business events (DB + file)
- **WARN** - Potential issues (DB + file)
- **ERROR** - Failures that need attention (DB + file)
- **FATAL** - Critical failures (DB + file)

### Performance Tips

1. **Use conditional logging for expensive operations:**
```csharp
if (_fileLogger.IsDebugEnabled)
{
    _fileLogger.Debug($"Expensive computation: {ExpensiveOperation()}");
}
```

2. **Batch database logging** - log4net bufferSize is set to 2, so logs are buffered before writing

3. **Clean up properties** - Always remove context properties when done to prevent memory leaks

4. **Avoid logging in tight loops** - Log summary instead:
```csharp
// BAD
foreach (var doc in documents)
{
    _dbLogger.Info($"Processing {doc.Name}"); // Too many DB writes!
}

// GOOD
_dbLogger.Info($"Processing batch of {documents.Count} documents");
foreach (var doc in documents)
{
    // ... process
}
_dbLogger.Info($"Batch completed: {successCount} success, {failCount} failed");
```

## 📊 Querying Logs

See `SQL/05_MonitoringQueries.sql` section 14 for useful log queries:

```sql
-- Recent errors
SELECT LOG_DATE, LOGGER, MESSAGE, DOCUMENTID, EXCEPTION
FROM AlfrescoMigration_Logger
WHERE LOG_LEVEL = 'ERROR'
  AND LOG_DATE >= SYSTIMESTAMP - INTERVAL '24' HOUR
ORDER BY LOG_DATE DESC;

-- Logs for specific document
SELECT LOG_DATE, LOG_LEVEL, MESSAGE, WORKERID
FROM AlfrescoMigration_Logger
WHERE DOCUMENTID = 'your-node-id'
ORDER BY LOG_DATE ASC;

-- Batch tracking
SELECT LOG_DATE, LOG_LEVEL, MESSAGE, DOCUMENTID
FROM AlfrescoMigration_Logger
WHERE BATCHID = 'Batch-20250108-143025'
ORDER BY LOG_DATE ASC;
```

## 🧹 Maintenance

Archive old logs regularly (see `SQL/06_MaintenanceScripts.sql` section 11):

```sql
-- Archive logs older than 30 days
INSERT INTO AlfrescoMigration_Logger_Archive
SELECT * FROM AlfrescoMigration_Logger
WHERE LOG_DATE < SYSTIMESTAMP - INTERVAL '30' DAY;

DELETE FROM AlfrescoMigration_Logger
WHERE LOG_DATE < SYSTIMESTAMP - INTERVAL '30' DAY;

COMMIT;
```
