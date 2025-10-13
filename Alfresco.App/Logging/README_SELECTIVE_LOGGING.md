# Selektivno Logovanje u LiveLogViewer

## 🎯 Problem

Imaš više logger-a u projektu:
- `DbLogger` - Loguje u Oracle bazu
- `FileLogger` - Loguje u fajl

Ne želiš da **SVI** logeri šalju logove u `LiveLogViewer`, već samo specifične (npr. samo `FileLogger`).

---

## ✅ Rešenje 1: Allow All Loggers (Default)

Ako želiš **SVE** logere u LiveLogViewer:

### App.xaml.cs

```csharp
using Alfresco.App.Logging;

public partial class App : Application
{
    public static LiveLogViewer LogViewer { get; private set; }

    public App()
    {
        LogViewer = new LiveLogViewer();

        AppHost = Host.CreateDefaultBuilder()
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddLog4Net("log4net.config");

                // Allow ALL loggers (DbLogger, FileLogger, etc.)
                logging.AddProvider(new SelectiveLiveLoggerProvider(LogViewer));

                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();
    }
}
```

**Rezultat:**
```csharp
// U service-u:
_fileLogger.LogInformation("Test");  // ✅ Pojavljuje se u LiveLogViewer
_dbLogger.LogInformation("Test");    // ✅ Pojavljuje se u LiveLogViewer
```

---

## ✅ Rešenje 2: Allow Only FileLogger

Ako želiš **SAMO FileLogger** u LiveLogViewer:

### App.xaml.cs

```csharp
using Alfresco.App.Logging;

public partial class App : Application
{
    public static LiveLogViewer LogViewer { get; private set; }

    public App()
    {
        LogViewer = new LiveLogViewer();

        AppHost = Host.CreateDefaultBuilder()
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddLog4Net("log4net.config");

                // ⚠️ Allow ONLY "FileLogger"
                logging.AddProvider(new SelectiveLiveLoggerProvider(
                    LogViewer,
                    "FileLogger"  // Only this logger will appear in UI
                ));

                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();
    }
}
```

**Rezultat:**
```csharp
// U service-u:
_fileLogger.LogInformation("Test");  // ✅ Pojavljuje se u LiveLogViewer
_dbLogger.LogInformation("Test");    // ❌ NE pojavljuje se (filtrirano)
```

---

## ✅ Rešenje 3: Allow Multiple Specific Loggers

Ako želiš **FileLogger I neki drugi logger**, ali ne DbLogger:

### App.xaml.cs

```csharp
logging.AddProvider(new SelectiveLiveLoggerProvider(
    LogViewer,
    "FileLogger",           // Allow this
    "ConsoleLogger",        // Allow this
    "CustomLogger"          // Allow this
    // DbLogger is NOT listed, so it will be filtered out
));
```

**Rezultat:**
```csharp
_fileLogger.LogInformation("Test");     // ✅ Appears
_consoleLogger.LogInformation("Test");  // ✅ Appears
_customLogger.LogInformation("Test");   // ✅ Appears
_dbLogger.LogInformation("Test");       // ❌ Filtered out
```

---

## 🔍 How It Works

### Logger Category Name

Kada kreiraš logger sa `ILoggerFactory`, string koji proslediš postaje **category name**:

```csharp
var logger = loggerFactory.CreateLogger("FileLogger");
//                                       ^^^^^^^^^^^
//                                       Category Name
```

`SelectiveLiveLoggerProvider` proverava category name i odlučuje da li da prikaže log ili ne.

### Filtering Logic

```csharp
// U SelectiveLiveLogger.Log() metodi:
if (!_allowAll && !_allowedCategories.Contains(_categoryName))
{
    // Skip this log - not in allowed list
    return;
}

// If we get here, the log is allowed
_logViewer.AddLog(logLevel, message, exception, _categoryName);
```

---

## 🎨 UI Display

Logger category name će se prikazati u **Logger Name** koloni:

```
┌────────────────────────────────────────────────────────────┐
│ 12:34:56.789  INFO   FileLogger    Processing folder 123   │
│ 12:34:56.890  DEBUG  FileLogger    Found 45 documents      │
│ 12:34:57.123  WARN   FileLogger    Retry attempt 1         │
│                                                             │
│ (DbLogger logs are NOT shown because filtered out)         │
└────────────────────────────────────────────────────────────┘
```

---

## 🧪 Testing

### Test Case 1: Verify FileLogger Appears

```csharp
public void TestFileLogger()
{
    var loggerFactory = App.AppHost.Services.GetRequiredService<ILoggerFactory>();
    var fileLogger = loggerFactory.CreateLogger("FileLogger");

    fileLogger.LogInformation("Test FileLogger");

    // Expected: Log appears in LiveLogViewer with "FileLogger" category
}
```

### Test Case 2: Verify DbLogger is Filtered

```csharp
public void TestDbLoggerFiltered()
{
    var loggerFactory = App.AppHost.Services.GetRequiredService<ILoggerFactory>();
    var dbLogger = loggerFactory.CreateLogger("DbLogger");

    dbLogger.LogInformation("Test DbLogger");

    // Expected: Log does NOT appear in LiveLogViewer (filtered)
}
```

---

## 📊 Comparison: Original vs Selective

| Feature | `LiveLoggerProvider` | `SelectiveLiveLoggerProvider` |
|---------|---------------------|-------------------------------|
| Allow all loggers | ✅ Yes | ✅ Yes (default) |
| Filter by category | ❌ No | ✅ Yes |
| Constructor options | 1 (logViewer) | 2 (logViewer, categories) |
| Use case | Show everything | Show specific loggers only |

---

## 🎯 Tvoj Use Case

Ako želiš:
1. **FileLogger** → LiveLogViewer ✅
2. **DbLogger** → Oracle database (ali NE u LiveLogViewer) ❌

**Solution:**

```csharp
// U App.xaml.cs
logging.AddProvider(new SelectiveLiveLoggerProvider(
    LogViewer,
    "FileLogger"  // Only FileLogger goes to UI
));
```

Sada:
- `_fileLogger.LogInformation(...)` → **Pojavljuje se u UI**
- `_dbLogger.LogInformation(...)` → **NE pojavljuje se u UI** (ide samo u DB preko Log4Net)

---

## 🔧 Advanced: Filter by Pattern

Ako želiš da filtriraš po pattern-u (npr. svi logeri koji sadrže "File"):

### Custom Implementation

```csharp
public class PatternLiveLoggerProvider : ILoggerProvider
{
    private readonly LiveLogViewer _logViewer;
    private readonly string _pattern;

    public PatternLiveLoggerProvider(LiveLogViewer logViewer, string pattern)
    {
        _logViewer = logViewer;
        _pattern = pattern;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new PatternLiveLogger(categoryName, _logViewer, _pattern);
    }

    public void Dispose() { }

    private class PatternLiveLogger : ILogger
    {
        private readonly string _categoryName;
        private readonly LiveLogViewer _logViewer;
        private readonly string _pattern;

        public PatternLiveLogger(string categoryName, LiveLogViewer logViewer, string pattern)
        {
            _categoryName = categoryName;
            _logViewer = logViewer;
            _pattern = pattern;
        }

        public IDisposable BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception exception,
            Func<TState, Exception, string> formatter)
        {
            // Filter by pattern
            if (!_categoryName.Contains(_pattern, StringComparison.OrdinalIgnoreCase))
            {
                return; // Skip
            }

            var message = formatter(state, exception);
            _logViewer.AddLog(logLevel, message, exception, _categoryName);
        }
    }
}
```

**Usage:**
```csharp
// Allow all loggers that contain "File" in name
logging.AddProvider(new PatternLiveLoggerProvider(LogViewer, "File"));

// FileLogger → ✅ Shows
// DbLogger → ❌ Filtered
// CustomFileLogger → ✅ Shows
```

---

## 🚀 Recommended Setup

### Za Tvoj Projekat:

```csharp
public partial class App : Application
{
    public static LiveLogViewer LogViewer { get; private set; }

    public App()
    {
        LogViewer = new LiveLogViewer();

        AppHost = Host.CreateDefaultBuilder()
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();

                // Log4Net handles file and DB logging
                logging.AddLog4Net("log4net.config");

                // LiveLogViewer shows ONLY FileLogger
                logging.AddProvider(new SelectiveLiveLoggerProvider(
                    LogViewer,
                    "FileLogger"  // Show only FileLogger in UI
                ));

                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();
    }
}
```

### U Tvom Service-u (NO CHANGES NEEDED!)

```csharp
public class DocumentDiscoveryService
{
    private readonly ILogger _dbLogger;
    private readonly ILogger _fileLogger;

    public DocumentDiscoveryService(ILoggerFactory logger)
    {
        _dbLogger = logger.CreateLogger("DbLogger");
        _fileLogger = logger.CreateLogger("FileLogger");
    }

    public void Process()
    {
        // Goes to DB (via Log4Net) but NOT to LiveLogViewer
        _dbLogger.LogInformation("Saved to DB");

        // Goes to file (via Log4Net) AND to LiveLogViewer
        _fileLogger.LogInformation("Processing folder");
    }
}
```

---

## ✅ Summary

| Logger | Log4Net (File/DB) | LiveLogViewer UI |
|--------|------------------|------------------|
| FileLogger | ✅ Yes | ✅ Yes (selectively allowed) |
| DbLogger | ✅ Yes | ❌ No (filtered out) |

**Best of both worlds:** Log4Net still logs everything, but LiveLogViewer shows only what you want! 🎉
