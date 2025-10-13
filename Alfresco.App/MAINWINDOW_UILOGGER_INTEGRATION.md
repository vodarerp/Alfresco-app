# 🚀 MainWindow Integration sa UiLogger

## ✅ Sve je Već Konfigurisano!

Trebaju ti samo **3 jednostavna koraka** da dodaš LiveLogViewer u MainWindow:

---

## 📋 Step 1: Dodaj Tab u MainWindow.xaml

```xml
<Window x:Class="Alfresco.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:uc="clr-namespace:Alfresco.App.UserControls"
        Title="Alfresco Migration Monitor"
        Height="900" Width="1400">

    <TabControl Name="MainTabControl">

        <!-- Postojeći tabovi... -->
        <TabItem Header="📊 Dashboard">
            <!-- Tvoj dashboard content -->
        </TabItem>

        <!-- NEW: Logs Tab sa UiLogger -->
        <TabItem Header="📝 Logs">
            <!-- Koristi globalni LogViewer iz App.xaml.cs -->
            <ContentControl Content="{x:Static local:App.LogViewer}"/>
        </TabItem>

    </TabControl>
</Window>
```

**Alternativno (Programski način):**

```xml
<Window x:Class="Alfresco.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Alfresco Migration Monitor"
        Height="900" Width="1400">

    <TabControl Name="MainTabControl">
        <!-- Dodaj tabove programski u code-behind -->
    </TabControl>
</Window>
```

---

## 📋 Step 2: Dodaj Tab u MainWindow.xaml.cs (Programski način)

```csharp
using System.Windows;
using System.Windows.Controls;

namespace Alfresco.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Dodaj LiveLogViewer tab programski
            AddLogViewerTab();

            // Test logovi (opciono)
            TestUiLogger();
        }

        private void AddLogViewerTab()
        {
            // Koristi globalni LogViewer instancu iz App.xaml.cs
            var logTab = new TabItem
            {
                Header = "📝 Logs",
                Content = App.LogViewer
            };

            MainTabControl.Items.Add(logTab);
        }

        private void TestUiLogger()
        {
            // Manual test logs u UiLogger
            App.LogViewer.AddLog(
                Microsoft.Extensions.Logging.LogLevel.Information,
                "🚀 MainWindow initialized - monitoring active",
                "MainWindow"
            );
        }
    }
}
```

---

## 📋 Step 3: Koristi UiLogger u Service-ima

### Example: DocumentDiscoveryService

```csharp
public class DocumentDiscoveryService : IDocumentDiscoveryService
{
    private readonly ILogger _dbLogger;
    private readonly ILogger _fileLogger;
    private readonly ILogger _uiLogger;  // NEW: Za UI monitoring

    public DocumentDiscoveryService(ILoggerFactory loggerFactory)
    {
        _dbLogger = loggerFactory.CreateLogger("DbLogger");
        _fileLogger = loggerFactory.CreateLogger("FileLogger");
        _uiLogger = loggerFactory.CreateLogger("UiLogger");  // NEW
    }

    public async Task ProcessDocumentsAsync(CancellationToken ct)
    {
        // UI: Start monitoring
        _uiLogger.LogInformation("🚀 Document migration started");

        // File: Debug details (NE prikazuje u UI)
        _fileLogger.LogDebug("Loading documents from staging...");

        var docs = await LoadDocumentsAsync();

        // UI: Progress update
        _uiLogger.LogInformation($"📊 Loaded {docs.Count} documents for processing");

        int processed = 0;
        foreach (var doc in docs)
        {
            try
            {
                await ProcessDocumentAsync(doc);

                // DB: Persistent log
                _dbLogger.LogInformation($"Document {doc.Id} migrated");

                processed++;

                // UI: Progress update svaki 100-ti
                if (processed % 100 == 0)
                {
                    _uiLogger.LogInformation($"📊 Progress: {processed}/{docs.Count} ({processed * 100 / docs.Count}%)");
                }
            }
            catch (Exception ex)
            {
                // UI: Prikaži kritične greške
                _uiLogger.LogError(ex, $"❌ Failed to process document {doc.Id}");

                // File: Full details
                _fileLogger.LogError(ex, $"Document {doc.Id} failed");
            }
        }

        // UI: Summary
        _uiLogger.LogInformation($"✅ Migration completed: {processed}/{docs.Count} documents");
    }
}
```

---

## 🎨 Šta Vidiš u UI

Kada dodaš LiveLogViewer u MainWindow, videćeš:

```
┌────────────────────────────────────────────────────────────────────┐
│ 📝 Live Log Viewer                      [Auto-scroll: ✓] [Clear]  │
│ Monitoring active...                              [Export] [⏸️]    │
├────────────────────────────────────────────────────────────────────┤
│ Filter: [ALL] [DEBUG] [INFO] [WARN] [ERROR]    Search: [        ] │
├────────────────────────────────────────────────────────────────────┤
│ 12:34:56.123  INFO   UiLogger  🚀 MainWindow initialized          │
│ 12:34:57.456  INFO   UiLogger  🚀 Document migration started      │
│ 12:34:58.789  INFO   UiLogger  📊 Loaded 1000 documents           │
│ 12:35:00.012  INFO   UiLogger  📊 Progress: 100/1000 (10%)        │
│ 12:35:01.345  INFO   UiLogger  📊 Progress: 200/1000 (20%)        │
│ 12:35:02.678  WARN   UiLogger  ⚠️ API slow response - retry 1    │
│ 12:35:03.901  INFO   UiLogger  📊 Progress: 300/1000 (30%)        │
│ 12:35:05.234  ERROR  UiLogger  ❌ Failed to process doc 12345     │
│ 12:35:20.567  INFO   UiLogger  ✅ Migration completed: 950/1000   │
│                                                                     │
├────────────────────────────────────────────────────────────────────┤
│ Total: 1,234  DEBUG: 0  INFO: 1,180  WARN: 45  ERROR: 9          │
└────────────────────────────────────────────────────────────────────┘
```

**Real-time monitoring sa čistim, high-level update-ima!** 🎯

---

## 🧪 Testiranje

### Test 1: Manualni Logovi

```csharp
// U MainWindow.xaml.cs
private void BtnTest_Click(object sender, RoutedEventArgs e)
{
    App.LogViewer.AddLog(
        Microsoft.Extensions.Logging.LogLevel.Information,
        "✅ Test log from MainWindow",
        "MainWindow"
    );

    App.LogViewer.AddLog(
        Microsoft.Extensions.Logging.LogLevel.Warning,
        "⚠️ Test warning",
        "MainWindow"
    );

    App.LogViewer.AddLog(
        Microsoft.Extensions.Logging.LogLevel.Error,
        "❌ Test error",
        new Exception("Sample exception"),
        "MainWindow"
    );
}
```

### Test 2: UiLogger iz Service-a

```csharp
// U bilo kom service-u
public class TestService
{
    private readonly ILogger _uiLogger;

    public TestService(ILoggerFactory loggerFactory)
    {
        _uiLogger = loggerFactory.CreateLogger("UiLogger");
    }

    public void Test()
    {
        _uiLogger.LogInformation("🚀 Test from service - pojavljuje se u UI!");
    }
}
```

---

## 🔧 Features

✅ **Real-time logging** - Logovi se pojavljuju čim nastanu
✅ **Filtering po level-u** - DEBUG, INFO, WARN, ERROR
✅ **Search** - Pretraži logove po tekstu
✅ **Auto-scroll** - Automatski skroluje na najnoviji log
✅ **Pause/Resume** - Pauzirај za detaljnu inspekciju
✅ **Clear** - Obriši sve logove
✅ **Export** - Sačuvaj u TXT/CSV
✅ **Color-coded** - Različite boje za različite level-e
✅ **Statistics** - Real-time brojač po level-ima
✅ **Buffer limit** - Max 1000 entries (automatski briše najstarije)

---

## 📊 Arhitektura

```
App.xaml.cs
├─ App.LogViewer (global static)
│  └─ LiveLogViewer UserControl instance
│
├─ ConfigureLogging()
│  └─ SelectiveLiveLoggerProvider("UiLogger")
│     └─ Šalje SAMO UiLogger logove u LiveLogViewer
│
└─ log4net.config
   ├─ DbLogger → Oracle DB
   ├─ FileLogger → Rolling File
   └─ UiLogger → Rolling File + LiveLogViewer UI
```

---

## ✅ Gotovo!

Sada imaš:
1. ✅ **log4net.config** - UiLogger konfigurisan
2. ✅ **App.xaml.cs** - LiveLogViewer registrovan
3. ✅ **MainWindow** - Dodaj samo tab (3 reda koda)
4. ✅ **Service-i** - Koristi `_uiLogger` za monitoring

**Sve je spremno za real-time monitoring!** 🎉

---

## 🎯 Next Steps

1. Dodaj LiveLogViewer tab u MainWindow (3 reda koda)
2. Dodaj `_uiLogger` u tvoje service-e
3. Loguj key events: start, progress, errors, completion
4. Pokreni aplikaciju i vidi real-time logove!

**Happy monitoring!** 🚀
