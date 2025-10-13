# Live Log Viewer - Integration Guide

## 📋 Overview

`LiveLogViewer` je custom UserControl koji prikazuje logove u real-time sa mogućnostima:
- ✅ Filtriranje po log level-u (DEBUG, INFO, WARN, ERROR)
- ✅ Search po text-u
- ✅ Auto-scroll
- ✅ Pause/Resume
- ✅ Clear logs
- ✅ Export to file (TXT/CSV)
- ✅ Real-time statistics
- ✅ Buffer limit (1000 entries)
- ✅ Color-coded log levels

---

## 🚀 Quick Integration

### Option 1: Add to MainWindow XAML

```xml
<Window x:Class="Alfresco.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:uc="clr-namespace:Alfresco.App.UserControls"
        Title="Alfresco Migration" Height="800" Width="1200">

    <TabControl>
        <!-- Existing tabs... -->

        <!-- New Logs Tab -->
        <TabItem Header="📝 Logs">
            <uc:LiveLogViewer x:Name="LogViewer"/>
        </TabItem>
    </TabControl>
</Window>
```

### Option 2: Add Programmatically in Code-Behind

```csharp
public partial class MainWindow : Window
{
    private LiveLogViewer _logViewer;

    public MainWindow()
    {
        InitializeComponent();

        // Create log viewer
        _logViewer = new LiveLogViewer();

        // Add to tab or grid
        var logTab = new TabItem
        {
            Header = "📝 Logs",
            Content = _logViewer
        };
        MainTabControl.Items.Add(logTab);
    }
}
```

---

## 🔌 Integration with Logging Infrastructure

### Method 1: Custom Logger Provider (Recommended)

#### Step 1: Register in App.xaml.cs

```csharp
public partial class App : Application
{
    public static LiveLogViewer LogViewer { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Create LogViewer instance
        LogViewer = new LiveLogViewer();

        // Register custom logger provider
        AppHost = Host.CreateDefaultBuilder()
            .ConfigureLogging((context, logging) =>
            {
                logging.ClearProviders();
                logging.AddLog4Net("log4net.config");

                // Add LiveLogViewer provider
                logging.AddProvider(new LiveLoggerProvider(LogViewer));

                logging.SetMinimumLevel(LogLevel.Debug);
            })
            .ConfigureServices((context, services) =>
            {
                // ... existing services
            })
            .Build();

        base.OnStartup(e);
    }
}
```

#### Step 2: Use in MainWindow

```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Add log viewer from App
        var logTab = new TabItem
        {
            Header = "📝 Logs",
            Content = App.LogViewer
        };
        MainTabControl.Items.Add(logTab);
    }
}
```

#### Step 3: Logs Automatically Flow

Now all `ILogger<T>` instances will automatically send logs to the viewer:

```csharp
public class MyService
{
    private readonly ILogger<MyService> _logger;

    public MyService(ILogger<MyService> logger)
    {
        _logger = logger;
    }

    public void DoWork()
    {
        _logger.LogInformation("Starting work...");
        _logger.LogDebug("Processing item 1");
        _logger.LogWarning("Something unusual happened");
        _logger.LogError("Failed to process item", exception);
    }
}
```

---

### Method 2: Manual Logging

If you want more control or don't want to use the provider:

```csharp
public partial class MainWindow : Window
{
    private LiveLogViewer _logViewer;

    public MainWindow()
    {
        InitializeComponent();
        _logViewer = new LiveLogViewer();

        // Add logs manually
        _logViewer.AddLog(LogLevel.Information, "Application started");
        _logViewer.AddLog(LogLevel.Debug, "Initializing services...");
    }

    private void OnMigrationStarted()
    {
        _logViewer.AddLog(LogLevel.Information, "Migration started", "MigrationService");
    }

    private void OnError(Exception ex)
    {
        _logViewer.AddLog(
            LogLevel.Error,
            "Migration failed",
            ex,
            "MigrationService"
        );
    }
}
```

---

## 🎨 Customization

### Change Buffer Size

Edit `LiveLogViewer.xaml.cs`:

```csharp
private const int MaxBufferSize = 5000; // Default: 1000
```

### Change Auto-Refresh Interval

```csharp
_updateTimer = new DispatcherTimer
{
    Interval = TimeSpan.FromMilliseconds(1000) // Default: 500ms
};
```

### Change Colors

Edit `LiveLogViewer.xaml.cs` in `GetColorForLevel()`:

```csharp
private Brush GetColorForLevel(LogLevel level)
{
    return level switch
    {
        LogLevel.Debug => new SolidColorBrush(Color.FromRgb(128, 128, 128)), // Custom gray
        LogLevel.Information => new SolidColorBrush(Color.FromRgb(0, 128, 255)), // Custom blue
        // ... etc
    };
}
```

---

## 🔧 Advanced Integration

### Integration with Log4Net

If you're using Log4Net, create a custom appender:

```csharp
public class LiveLogViewerAppender : AppenderSkeleton
{
    private readonly LiveLogViewer _logViewer;

    public LiveLogViewerAppender(LiveLogViewer logViewer)
    {
        _logViewer = logViewer;
    }

    protected override void Append(LoggingEvent loggingEvent)
    {
        var logLevel = loggingEvent.Level.Name switch
        {
            "DEBUG" => LogLevel.Debug,
            "INFO" => LogLevel.Information,
            "WARN" => LogLevel.Warning,
            "ERROR" => LogLevel.Error,
            "FATAL" => LogLevel.Critical,
            _ => LogLevel.Information
        };

        var message = RenderLoggingEvent(loggingEvent);
        var loggerName = loggingEvent.LoggerName;

        Application.Current?.Dispatcher.InvokeAsync(() =>
        {
            _logViewer.AddLog(logLevel, message, loggerName);
        });
    }
}
```

Register in `log4net.config`:

```xml
<log4net>
    <appender name="LiveLogViewer" type="Alfresco.App.Logging.LiveLogViewerAppender, Alfresco.App">
        <!-- Configuration -->
    </appender>

    <root>
        <level value="DEBUG" />
        <appender-ref ref="LiveLogViewer" />
        <appender-ref ref="RollingFileAppender" />
    </root>
</log4net>
```

---

## 📊 Usage Examples

### Example 1: Worker Service Integration

```csharp
public class DocumentDiscoveryWorker : BackgroundService
{
    private readonly ILogger<DocumentDiscoveryWorker> _logger;

    public DocumentDiscoveryWorker(ILogger<DocumentDiscoveryWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _logger.LogInformation("DocumentDiscoveryWorker started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Fetching folders from queue...");

                var folders = await GetFoldersAsync(ct);
                _logger.LogInformation("Found {Count} folders to process", folders.Count);

                foreach (var folder in folders)
                {
                    _logger.LogDebug("Processing folder: {FolderId}", folder.Id);
                    await ProcessFolderAsync(folder, ct);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing folders");
            }

            await Task.Delay(1000, ct);
        }

        _logger.LogInformation("DocumentDiscoveryWorker stopped");
    }
}
```

### Example 2: Migration Service Integration

```csharp
public class MigrationService
{
    private readonly ILogger<MigrationService> _logger;

    public async Task RunMigrationAsync()
    {
        _logger.LogInformation("========================================");
        _logger.LogInformation("Starting Alfresco Migration");
        _logger.LogInformation("========================================");

        try
        {
            _logger.LogInformation("Phase 1: Folder Discovery");
            await DiscoverFoldersAsync();

            _logger.LogInformation("Phase 2: Document Discovery");
            await DiscoverDocumentsAsync();

            _logger.LogInformation("Phase 3: Document Move");
            await MoveDocumentsAsync();

            _logger.LogInformation("========================================");
            _logger.LogInformation("✅ Migration completed successfully!");
            _logger.LogInformation("========================================");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Migration failed!");
            throw;
        }
    }
}
```

---

## 🎯 Features Demo

### Filtering Demo

```csharp
// Generate sample logs with different levels
for (int i = 0; i < 100; i++)
{
    _logViewer.AddLog(LogLevel.Debug, $"Debug message {i}", "TestLogger");
    _logViewer.AddLog(LogLevel.Information, $"Info message {i}", "TestLogger");

    if (i % 10 == 0)
    {
        _logViewer.AddLog(LogLevel.Warning, $"Warning at iteration {i}", "TestLogger");
    }

    if (i % 25 == 0)
    {
        _logViewer.AddLog(LogLevel.Error, $"Error at iteration {i}", "TestLogger");
    }
}

// User can now filter by clicking:
// - ALL: Shows all 400 messages
// - INFO: Shows only 100 info messages
// - WARN: Shows only 10 warning messages
// - ERROR: Shows only 4 error messages
```

### Search Demo

```csharp
_logViewer.AddLog(LogLevel.Information, "Processing document ABC-123");
_logViewer.AddLog(LogLevel.Information, "Processing document XYZ-456");
_logViewer.AddLog(LogLevel.Information, "Processing document ABC-789");

// User types "ABC" in search box -> Shows only 2 messages
```

---

## 🐛 Troubleshooting

### Problem: Logs not appearing

**Solution**: Make sure `LiveLoggerProvider` is registered:

```csharp
logging.AddProvider(new LiveLoggerProvider(LogViewer));
```

### Problem: UI freezes with many logs

**Solution**: Buffer is limited to 1000 entries by default. Oldest entries are removed automatically. You can increase buffer size if needed.

### Problem: Auto-scroll not working

**Solution**: Check if "Auto-scroll" checkbox is enabled in UI.

### Problem: Export fails

**Solution**: Make sure the application has write permissions to the selected directory.

---

## 📝 API Reference

### Public Methods

```csharp
// Add a log entry
void AddLog(LogLevel level, string message, string loggerName = "")

// Add a log entry with exception
void AddLog(LogLevel level, string message, Exception exception, string loggerName = "")
```

### Public Properties

None (all interaction through UI or AddLog methods)

---

## 🎨 UI Layout

```
┌─────────────────────────────────────────────────────────────┐
│ 📝 Live Log Viewer                    [Auto-scroll: ✓]     │
│ Monitoring active...                  [Clear] [Export] [⏸️] │
├─────────────────────────────────────────────────────────────┤
│ Filter: [ALL] [DEBUG] [INFO] [WARN] [ERROR]  Search: [___] │
├─────────────────────────────────────────────────────────────┤
│                                                               │
│ 12:34:56.789  INFO   FolderService   Processing folder 123  │
│ 12:34:56.890  DEBUG  DocService      Found 45 documents     │
│ 12:34:57.123  WARN   MoveService     Retry attempt 1        │
│ 12:34:57.456  ERROR  ApiClient       Connection failed      │
│                                                               │
├─────────────────────────────────────────────────────────────┤
│ Total: 1,234  🔍 DEBUG: 456  ℹ️ INFO: 678  ⚠️ WARN: 89...  │
└─────────────────────────────────────────────────────────────┘
```

---

## 💡 Tips

1. **Use structured logging** with `ILogger<T>` for automatic integration
2. **Set appropriate log levels** in appsettings.json to avoid spam
3. **Use the search** to quickly find specific log entries
4. **Export logs** before clearing for archival purposes
5. **Pause logging** when investigating specific entries
6. **Filter by level** to focus on errors/warnings

---

## 🚀 Next Steps

After implementing the log viewer, consider adding:
- [ ] Log aggregation from multiple sources
- [ ] Persistent log storage (SQLite/file)
- [ ] Advanced search (regex, multiple criteria)
- [ ] Log highlighting/bookmarks
- [ ] Email alerts for critical errors
- [ ] Integration with centralized logging (ELK, Seq, etc.)

---

**LiveLogViewer is ready to use! Add it to your MainWindow and start monitoring.** 🎉
