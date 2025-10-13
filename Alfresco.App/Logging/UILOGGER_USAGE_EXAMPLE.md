# 🎯 UiLogger Usage Example

## Šta je UiLogger?

**UiLogger** je poseban logger namenjen **samo za UI monitoring** u LiveLogViewer-u.

### Konfiguracija

✅ **log4net.config** - UiLogger loguje u fajl (ali NE u bazu)
✅ **App.xaml.cs** - LiveLogViewer prikazuje SAMO UiLogger
✅ **Performanse** - Minimalan overhead, optimizovano za monitoring

---

## 📊 Poređenje: DbLogger vs FileLogger vs UiLogger

| Logger | Destinacija | Prikazuje se u UI? | Level | Use Case |
|--------|-------------|-------------------|-------|----------|
| **DbLogger** | Oracle DB | ❌ Ne | Info | Persistent logging u bazu |
| **FileLogger** | Rolling File | ❌ Ne | Debug | Detaljni logovi za debugging |
| **UiLogger** | Rolling File + UI | ✅ Da | Info | Real-time monitoring u UI |

---

## 🚀 Kako Koristiti u Service-u

### Example 1: DocumentDiscoveryService

```csharp
using Microsoft.Extensions.Logging;

public class DocumentDiscoveryService : IDocumentDiscoveryService
{
    private readonly ILogger _dbLogger;      // Za Oracle DB
    private readonly ILogger _fileLogger;    // Za detaljne logove
    private readonly ILogger _uiLogger;      // Za UI monitoring

    public DocumentDiscoveryService(ILoggerFactory loggerFactory)
    {
        _dbLogger = loggerFactory.CreateLogger("DbLogger");
        _fileLogger = loggerFactory.CreateLogger("FileLogger");
        _uiLogger = loggerFactory.CreateLogger("UiLogger");
    }

    public async Task ProcessDocumentsAsync(CancellationToken ct)
    {
        // UI: Prikaži start migracije
        _uiLogger.LogInformation("🚀 Starting document migration - batch size: 1000");

        var batch = await LoadDocumentsAsync();

        // File: Detaljni log (NE prikazuje u UI)
        _fileLogger.LogDebug($"Loaded {batch.Count} documents from staging table");

        int processed = 0;
        foreach (var doc in batch)
        {
            try
            {
                // File: Svaki debug detail
                _fileLogger.LogDebug($"Processing document ID={doc.Id}, Name={doc.Name}");

                await ProcessSingleDocumentAsync(doc);

                // DB: Log u Oracle za persistent tracking
                _dbLogger.LogInformation($"Document {doc.Id} migrated successfully");

                processed++;

                // UI: Progress update svaki 100-ti dokument
                if (processed % 100 == 0)
                {
                    _uiLogger.LogInformation($"📊 Progress: {processed}/{batch.Count} ({processed * 100 / batch.Count}%)");
                }
            }
            catch (Exception ex)
            {
                // UI: Kritične greške prikaži odmah
                _uiLogger.LogError(ex, $"❌ Failed to process document {doc.Id}: {ex.Message}");

                // File: Full stack trace
                _fileLogger.LogError(ex, $"Document processing failed: {doc.Id}");

                // DB: Log error u bazu
                _dbLogger.LogError($"Document {doc.Id} failed: {ex.Message}");
            }
        }

        // UI: Završni summary
        _uiLogger.LogInformation($"✅ Migration completed - {processed}/{batch.Count} documents processed");
    }
}
```

---

### Example 2: FolderDiscoveryService

```csharp
public class FolderDiscoveryService : IFolderDiscoveryService
{
    private readonly ILogger _dbLogger;
    private readonly ILogger _fileLogger;
    private readonly ILogger _uiLogger;

    public FolderDiscoveryService(ILoggerFactory loggerFactory)
    {
        _dbLogger = loggerFactory.CreateLogger("DbLogger");
        _fileLogger = loggerFactory.CreateLogger("FileLogger");
        _uiLogger = loggerFactory.CreateLogger("UiLogger");
    }

    public async Task DiscoverFoldersAsync(CancellationToken ct)
    {
        _uiLogger.LogInformation("📂 Starting folder discovery...");

        var folders = await FetchFoldersFromAlfrescoAsync();

        _fileLogger.LogDebug($"Fetched {folders.Count} folders from Alfresco API");

        foreach (var folder in folders)
        {
            try
            {
                await ProcessFolderAsync(folder);
                _dbLogger.LogInformation($"Folder {folder.Id} saved to staging");
            }
            catch (Exception ex)
            {
                _uiLogger.LogWarning($"⚠️ Skipped folder {folder.Name}: {ex.Message}");
                _fileLogger.LogError(ex, $"Failed to process folder {folder.Id}");
            }
        }

        _uiLogger.LogInformation($"✅ Folder discovery completed - {folders.Count} folders processed");
    }
}
```

---

### Example 3: MoveService

```csharp
public class MoveService : IMoveService
{
    private readonly ILogger _dbLogger;
    private readonly ILogger _fileLogger;
    private readonly ILogger _uiLogger;

    public MoveService(ILoggerFactory loggerFactory)
    {
        _dbLogger = loggerFactory.CreateLogger("DbLogger");
        _fileLogger = loggerFactory.CreateLogger("FileLogger");
        _uiLogger = loggerFactory.CreateLogger("UiLogger");
    }

    public async Task MoveDocumentsAsync(CancellationToken ct)
    {
        _uiLogger.LogInformation("🔄 Starting document move operations...");

        var moveTasks = await LoadMoveTasksAsync();

        _fileLogger.LogDebug($"Loaded {moveTasks.Count} move tasks from staging");

        int successCount = 0;
        int failCount = 0;

        foreach (var task in moveTasks)
        {
            try
            {
                _fileLogger.LogDebug($"Moving document {task.DocumentId} to folder {task.TargetFolderId}");

                await MoveDocumentAsync(task);

                _dbLogger.LogInformation($"Document {task.DocumentId} moved successfully");
                successCount++;

                // UI: Update svaki 50-ti
                if ((successCount + failCount) % 50 == 0)
                {
                    _uiLogger.LogInformation($"📦 Move progress: {successCount + failCount}/{moveTasks.Count} (Success: {successCount}, Failed: {failCount})");
                }
            }
            catch (Exception ex)
            {
                _uiLogger.LogError($"❌ Move failed for document {task.DocumentId}: {ex.Message}");
                _fileLogger.LogError(ex, $"Move operation failed: Doc={task.DocumentId}, Target={task.TargetFolderId}");
                _dbLogger.LogError($"Move failed: {task.DocumentId} - {ex.Message}");
                failCount++;
            }
        }

        // UI: Final summary
        if (failCount > 0)
        {
            _uiLogger.LogWarning($"⚠️ Move completed with errors: {successCount} succeeded, {failCount} failed");
        }
        else
        {
            _uiLogger.LogInformation($"✅ All move operations completed successfully: {successCount}/{moveTasks.Count}");
        }
    }
}
```

---

## 🎨 UI Display Example

Kada koristiš UiLogger, LiveLogViewer će prikazati:

```
┌──────────────────────────────────────────────────────────────────┐
│ 📝 Live Log Viewer                    [Auto-scroll: ✓] [Clear]  │
│ Monitoring active...                            [Export] [⏸️]    │
├──────────────────────────────────────────────────────────────────┤
│ 12:34:56.123  INFO   UiLogger  🚀 Starting document migration    │
│ 12:34:57.456  INFO   UiLogger  📊 Progress: 100/1000 (10%)       │
│ 12:34:58.789  INFO   UiLogger  📊 Progress: 200/1000 (20%)       │
│ 12:35:00.012  WARN   UiLogger  ⚠️ Skipped folder: Invalid ID    │
│ 12:35:01.345  INFO   UiLogger  📊 Progress: 300/1000 (30%)       │
│ 12:35:02.678  ERROR  UiLogger  ❌ Failed to process doc 12345    │
│ 12:35:10.901  INFO   UiLogger  ✅ Migration completed 950/1000   │
└──────────────────────────────────────────────────────────────────┘
```

**Čist, jasan, real-time monitoring!** 🎯

---

## 💡 Best Practices

### ✅ DO: Koristi UiLogger Za

1. **High-level progress updates** - "Processing 100/1000 documents"
2. **Kritične greške** - Greške koje korisnik treba da vidi
3. **Start/Stop eventi** - "Migration started", "Migration completed"
4. **Performance warnings** - "API slow response", "Retry attempt 3"
5. **Summary reports** - "1000 docs processed, 5 failed"

### ❌ DON'T: Ne Koristi UiLogger Za

1. **Debug details** - Koristi FileLogger
2. **Svaki document** - Samo batch progress (svakih 50-100)
3. **HTTP requests** - Koristi FileLogger
4. **Database queries** - Koristi DbLogger
5. **Detaljne stack traces** - Koristi FileLogger

---

## 🔍 Filtriranje u UI

UiLogger automatski podržava filtriranje:

```
[ALL] - Prikaži sve UiLogger logove
[DEBUG] - Prikaži Debug+ (neće biti puno, jer je level=Info u config)
[INFO] - Prikaži Info+ (većina UiLogger poruka)
[WARN] - Prikaži samo upozorenja
[ERROR] - Prikaži samo greške
```

---

## 📦 Export Logova

LiveLogViewer može exportovati UiLogger logove u:
- **TXT** - Plain text format
- **CSV** - Za Excel analizu

---

## 🎯 Summary

| Akcija | Logger | Destinacija |
|--------|--------|-------------|
| Detaljni debug | FileLogger | Rolling file (logs/app.log) |
| Persistent tracking | DbLogger | Oracle DB (AlfrescoMigration_Logger) |
| UI monitoring | UiLogger | Rolling file + LiveLogViewer UI |

**UiLogger = Best of both worlds:** Real-time monitoring u UI + persistent logging u file! 🎉

---

## ✅ Gotovo!

Sada možeš koristiti `_uiLogger` u svojim service-ima za real-time monitoring u LiveLogViewer! 🚀
