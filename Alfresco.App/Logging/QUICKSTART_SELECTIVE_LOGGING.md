# 🚀 Quick Start - Selective Logging

## Problem
Imaš `DbLogger` i `FileLogger`, ali želiš **SAMO FileLogger** u LiveLogViewer UI.

---

## ✅ Solution (3 Steps)

### Step 1: Use SelectiveLiveLoggerProvider

**File: `App.xaml.cs`**

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

                // ⚠️ KEY LINE: Only allow "FileLogger"
                logging.AddProvider(new SelectiveLiveLoggerProvider(
                    LogViewer,
                    "FileLogger"  // <-- Add logger names you want to show
                ));

                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .Build();
    }
}
```

### Step 2: Add LiveLogViewer to MainWindow

**File: `MainWindow.xaml`**

```xml
<TabItem Header="📝 Logs">
    <uc:LiveLogViewer x:Name="LogViewer"/>
</TabItem>
```

**File: `MainWindow.xaml.cs`**

```csharp
public MainWindow()
{
    InitializeComponent();

    // Use global log viewer from App
    var logTab = new TabItem
    {
        Header = "📝 Logs",
        Content = App.LogViewer
    };
    MainTabControl.Items.Add(logTab);
}
```

### Step 3: No Changes Needed in Services!

**Tvoj existing service radi kako jeste:**

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
        _dbLogger.LogInformation("Saved to DB");        // ❌ NOT in UI
        _fileLogger.LogInformation("Processing...");    // ✅ Shows in UI
    }
}
```

---

## 🎯 Result

```
LiveLogViewer UI Shows:
┌────────────────────────────────────────────────────────┐
│ 12:34:56.789  INFO   FileLogger   Processing folder   │
│ 12:34:56.890  DEBUG  FileLogger   Found 45 docs       │
│ 12:34:57.123  WARN   FileLogger   Retry attempt 1     │
│                                                         │
│ (DbLogger logs are NOT shown - filtered out)          │
└────────────────────────────────────────────────────────┘

Log4Net Still Logs Everything:
- DbLogger → Oracle DB ✅
- FileLogger → File ✅
```

---

## 📊 Options

### Show All Loggers
```csharp
logging.AddProvider(new SelectiveLiveLoggerProvider(LogViewer));
```

### Show Only FileLogger
```csharp
logging.AddProvider(new SelectiveLiveLoggerProvider(LogViewer, "FileLogger"));
```

### Show FileLogger + ConsoleLogger
```csharp
logging.AddProvider(new SelectiveLiveLoggerProvider(
    LogViewer,
    "FileLogger",
    "ConsoleLogger"
));
```

---

## ✅ Done!

Sada imaš:
- ✅ FileLogger → UI (za real-time monitoring)
- ✅ DbLogger → Oracle (za persistent storage)
- ✅ Log4Net → File (za archival)

**Najbolje od svih svetova!** 🎉
