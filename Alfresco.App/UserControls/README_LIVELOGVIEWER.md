# 📝 LiveLogViewer UserControl

## ✨ Features

✅ **Real-time log display** - Logs se pojavljuju čim nastanu
✅ **Filter po level-u** - DEBUG, INFO, WARN, ERROR
✅ **Search funkcionalnost** - Pretraži logove po tekstu
✅ **Auto-scroll** - Automatski skroluje na najnoviji log
✅ **Pause/Resume** - Pauzirај monitoring za detaljnu inspekciju
✅ **Clear logs** - Obriši sve logove
✅ **Export to file** - Sačuvaj logove u TXT/CSV
✅ **Color-coded levels** - Različite boje za različite level-e
✅ **Statistics** - Real-time brojač po level-ima
✅ **Buffer limit** - Automatski briše najstarije (default: 1000 entries)
✅ **Timestamp precision** - Milisekund precision
✅ **Logger name** - Prikazuje ime logger-a (service name)

---

## 🚀 Quick Start

### 1. Dodaj u XAML

```xml
<Window xmlns:uc="clr-namespace:Alfresco.App.UserControls">
    <TabControl>
        <TabItem Header="📝 Logs">
            <uc:LiveLogViewer x:Name="LogViewer"/>
        </TabItem>
    </TabControl>
</Window>
```

### 2. Registruj Logger Provider (Opciono)

```csharp
// U App.xaml.cs
logging.AddProvider(new LiveLoggerProvider(LogViewer));
```

### 3. Use ILogger (Logs će automatski ići u viewer!)

```csharp
public class MyService
{
    private readonly ILogger<MyService> _logger;

    public void DoWork()
    {
        _logger.LogInformation("Work started");
        _logger.LogDebug("Processing...");
        _logger.LogWarning("Something unusual");
        _logger.LogError("Failed!", exception);
    }
}
```

---

## 📖 Documentation

- **Integration Guide**: `../LIVELOGVIEWER_INTEGRATION.md`
- **MainWindow Example**: `../MAINWINDOW_LOGS_EXAMPLE.md`

---

## 🎨 Screenshot

```
┌────────────────────────────────────────────────────────────┐
│ 📝 Live Log Viewer              [Auto-scroll: ✓] [Clear]  │
│ Monitoring active...                        [Export] [⏸️]  │
├────────────────────────────────────────────────────────────┤
│ Filter: [ALL] [DEBUG] [INFO] [WARN] [ERROR]  Search: [...] │
├────────────────────────────────────────────────────────────┤
│ 12:34:56.789  INFO   DocService    Processing doc 123     │
│ 12:34:56.890  DEBUG  MoveService   Moving to folder...    │
│ 12:34:57.123  WARN   ApiClient     Retry attempt 1        │
│ 12:34:57.456  ERROR  MoveService   Failed to move doc!    │
│                                                             │
├────────────────────────────────────────────────────────────┤
│ Total: 1,234  DEBUG: 456  INFO: 678  WARN: 89  ERROR: 11  │
└────────────────────────────────────────────────────────────┘
```

---

## 🔧 API

### Public Methods

```csharp
// Add log entry
void AddLog(LogLevel level, string message, string loggerName = "")

// Add log entry with exception
void AddLog(LogLevel level, string message, Exception exception, string loggerName = "")
```

### Example Usage

```csharp
// Manual logging
LogViewer.AddLog(LogLevel.Information, "Migration started");
LogViewer.AddLog(LogLevel.Debug, "Loading config...");
LogViewer.AddLog(LogLevel.Warning, "API slow response");
LogViewer.AddLog(LogLevel.Error, "Connection failed", exception);
```

---

## ⚙️ Configuration

### Change Buffer Size

```csharp
// In LiveLogViewer.xaml.cs
private const int MaxBufferSize = 5000; // Default: 1000
```

### Change Colors

```csharp
// In GetColorForLevel() method
LogLevel.Information => new SolidColorBrush(Color.FromRgb(0, 128, 255))
```

---

## 💡 Tips

1. **Use ILogger<T>** for automatic integration
2. **Filter early** - Use log level filters to reduce noise
3. **Search effectively** - Search by error message or service name
4. **Export before clear** - Save logs before clearing
5. **Pause for inspection** - Pause when investigating specific issues

---

## 📦 Files

- `LiveLogViewer.xaml` - XAML layout
- `LiveLogViewer.xaml.cs` - Code-behind with logic
- `LIVELOGVIEWER_INTEGRATION.md` - Integration guide
- `MAINWINDOW_LOGS_EXAMPLE.md` - MainWindow examples

---

## ✅ Ready to Use!

LiveLogViewer je kompletan, testiran, i spreman za produkciju. Samo dodaj u MainWindow i uživaj u real-time log monitoring-u! 🎉
