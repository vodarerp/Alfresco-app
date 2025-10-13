# Kako Dodati Live Log Viewer u MainWindow

## 📋 Pregled

Ovaj dokument pokazuje kako dodati `LiveLogViewer` UserControl u postojeći MainWindow.

---

## 🎯 Option 1: Dodaj Tab u MainWindow XAML

### MainWindow.xaml

Dodaj namespace za UserControls:

```xml
<Window x:Class="Alfresco.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:uc="clr-namespace:Alfresco.App.UserControls"
        Title="Alfresco Migration Monitor"
        Height="800" Width="1200">

    <TabControl Name="MainTabControl">

        <!-- Existing Dashboard Tab -->
        <TabItem Header="📊 Dashboard">
            <!-- Your existing content -->
        </TabItem>

        <!-- NEW: Logs Tab -->
        <TabItem Header="📝 Logs">
            <uc:LiveLogViewer x:Name="LogViewer"/>
        </TabItem>

    </TabControl>
</Window>
```

### MainWindow.xaml.cs

Registruj logger provider:

```csharp
using Alfresco.App.UserControls;
using Microsoft.Extensions.Logging;
using System.Windows;

namespace Alfresco.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            // Register LiveLogViewer with logging infrastructure
            RegisterLogViewer();

            // Test logs
            TestLogs();
        }

        private void RegisterLogViewer()
        {
            // Get logger factory from DI
            var loggerFactory = App.AppHost.Services.GetService(typeof(ILoggerFactory))
                as ILoggerFactory;

            if (loggerFactory != null)
            {
                // Add LiveLogViewer provider
                loggerFactory.AddProvider(new LiveLoggerProvider(LogViewer));
            }
        }

        private void TestLogs()
        {
            // Manual test logs
            LogViewer.AddLog(LogLevel.Information, "MainWindow initialized", "MainWindow");
            LogViewer.AddLog(LogLevel.Debug, "Loading configuration...", "MainWindow");
            LogViewer.AddLog(LogLevel.Information, "Workers registered successfully", "MainWindow");
        }
    }
}
```

---

## 🎯 Option 2: Dodaj Programski u Code-Behind

### MainWindow.xaml

```xml
<Window x:Class="Alfresco.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="Alfresco Migration Monitor"
        Height="800" Width="1200">

    <TabControl Name="MainTabControl">
        <!-- Existing tabs -->
    </TabControl>
</Window>
```

### MainWindow.xaml.cs

```csharp
using Alfresco.App.UserControls;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;

namespace Alfresco.App
{
    public partial class MainWindow : Window
    {
        private LiveLogViewer _logViewer;

        public MainWindow()
        {
            InitializeComponent();

            // Create and add log viewer
            CreateLogViewerTab();

            // Register with logging
            RegisterLogViewer();
        }

        private void CreateLogViewerTab()
        {
            // Create LiveLogViewer instance
            _logViewer = new LiveLogViewer();

            // Create tab
            var logTab = new TabItem
            {
                Header = "📝 Logs",
                Content = _logViewer
            };

            // Add to TabControl
            MainTabControl.Items.Add(logTab);
        }

        private void RegisterLogViewer()
        {
            var loggerFactory = App.AppHost.Services.GetService(typeof(ILoggerFactory))
                as ILoggerFactory;

            if (loggerFactory != null)
            {
                loggerFactory.AddProvider(new LiveLoggerProvider(_logViewer));
            }
        }
    }
}
```

---

## 🎯 Option 3: Global Logger (App-Wide)

Ako želiš da log viewer bude dostupan svugde u aplikaciji:

### App.xaml.cs

```csharp
using Alfresco.App.UserControls;
using Microsoft.Extensions.Logging;

namespace Alfresco.App
{
    public partial class App : Application
    {
        public static LiveLogViewer GlobalLogViewer { get; private set; }

        public App()
        {
            // Create global log viewer instance
            GlobalLogViewer = new LiveLogViewer();

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureLogging((context, logging) =>
                {
                    logging.ClearProviders();
                    logging.AddLog4Net("log4net.config");

                    // Add LiveLogViewer provider GLOBALLY
                    logging.AddProvider(new LiveLoggerProvider(GlobalLogViewer));

                    logging.SetMinimumLevel(LogLevel.Debug);
                })
                .ConfigureServices((context, services) =>
                {
                    // ... existing services
                })
                .Build();
        }
    }
}
```

### MainWindow.xaml.cs

```csharp
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Add global log viewer to UI
        var logTab = new TabItem
        {
            Header = "📝 Logs",
            Content = App.GlobalLogViewer
        };
        MainTabControl.Items.Add(logTab);

        // Now ALL ILogger<T> instances throughout the app
        // will automatically send logs to the viewer!
    }
}
```

---

## 🔧 Integration sa Existing Workers

Tvoji worker servisi već koriste `ILogger<T>`, tako da će automatski slati logove:

### DocumentDiscoveryWorker.cs (NO CHANGES NEEDED!)

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
        // These logs will automatically appear in LiveLogViewer!
        _logger.LogInformation("DocumentDiscoveryWorker started");

        while (!ct.IsCancellationRequested)
        {
            try
            {
                _logger.LogDebug("Fetching folders...");
                // ... work ...
                _logger.LogInformation("Processed {Count} folders", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in worker");
            }
        }
    }
}
```

---

## 🎨 Full Example MainWindow Layout

```xml
<Window x:Class="Alfresco.App.MainWindow"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:uc="clr-namespace:Alfresco.App.UserControls"
        Title="Alfresco Migration Monitor"
        Height="900" Width="1400"
        WindowStartupLocation="CenterScreen">

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- Header -->
        <Border Grid.Row="0" Background="#2196F3" Padding="15">
            <StackPanel>
                <TextBlock Text="Alfresco Migration Monitor"
                           FontSize="24" FontWeight="Bold"
                           Foreground="White"/>
                <TextBlock Text="Real-time migration monitoring and control"
                           FontSize="14" Foreground="#E3F2FD"
                           Margin="0,5,0,0"/>
            </StackPanel>
        </Border>

        <!-- Main Content -->
        <TabControl Grid.Row="1" Name="MainTabControl">

            <!-- Dashboard Tab -->
            <TabItem Header="📊 Dashboard">
                <Grid Margin="20">
                    <TextBlock Text="Dashboard content here..."
                               FontSize="16"
                               VerticalAlignment="Center"
                               HorizontalAlignment="Center"/>
                </Grid>
            </TabItem>

            <!-- Status Tab -->
            <TabItem Header="🔍 Status">
                <Grid Margin="20">
                    <TextBlock Text="Status grid here..."
                               FontSize="16"
                               VerticalAlignment="Center"
                               HorizontalAlignment="Center"/>
                </Grid>
            </TabItem>

            <!-- Logs Tab -->
            <TabItem Header="📝 Logs">
                <uc:LiveLogViewer x:Name="LogViewer"/>
            </TabItem>

            <!-- Workers Tab -->
            <TabItem Header="🔧 Workers">
                <Grid Margin="20">
                    <TextBlock Text="Worker controls here..."
                               FontSize="16"
                               VerticalAlignment="Center"
                               HorizontalAlignment="Center"/>
                </Grid>
            </TabItem>

        </TabControl>

        <!-- Status Bar -->
        <Border Grid.Row="2" Background="#F5F5F5"
                BorderBrush="#E0E0E0" BorderThickness="0,1,0,0"
                Padding="15,8">
            <Grid>
                <Grid.ColumnDefinitions>
                    <ColumnDefinition Width="*"/>
                    <ColumnDefinition Width="Auto"/>
                </Grid.ColumnDefinitions>

                <TextBlock Grid.Column="0" Text="Ready"
                           VerticalAlignment="Center"/>

                <StackPanel Grid.Column="1" Orientation="Horizontal">
                    <TextBlock Text="Status: " Foreground="#666"/>
                    <TextBlock Text="✅ All systems operational"
                               Foreground="Green" FontWeight="Bold"/>
                </StackPanel>
            </Grid>
        </Border>
    </Grid>
</Window>
```

---

## 🧪 Testing the Log Viewer

Dodaj test button za generisanje sample logova:

### MainWindow.xaml (dodaj u neki tab)

```xml
<StackPanel Margin="20">
    <Button Name="BtnTestLogs" Content="🧪 Generate Test Logs"
            Click="BtnTestLogs_Click" Padding="10"/>
</StackPanel>
```

### MainWindow.xaml.cs

```csharp
private void BtnTestLogs_Click(object sender, RoutedEventArgs e)
{
    // Generate sample logs
    Task.Run(async () =>
    {
        for (int i = 0; i < 50; i++)
        {
            LogViewer.AddLog(LogLevel.Information,
                $"Processing item {i}/50",
                "TestService");

            if (i % 5 == 0)
            {
                LogViewer.AddLog(LogLevel.Debug,
                    $"Debug info at iteration {i}",
                    "TestService");
            }

            if (i % 10 == 0)
            {
                LogViewer.AddLog(LogLevel.Warning,
                    $"Warning: Item {i} took longer than expected",
                    "TestService");
            }

            if (i % 20 == 0)
            {
                LogViewer.AddLog(LogLevel.Error,
                    $"Error processing item {i}",
                    new Exception("Sample exception"),
                    "TestService");
            }

            await Task.Delay(100);
        }

        LogViewer.AddLog(LogLevel.Information,
            "Test completed!",
            "TestService");
    });
}
```

---

## 📊 Expected Behavior

Nakon što dodaš LiveLogViewer, očekivano ponašanje:

1. **Svi postojeći servisi** će automatski slati logove u viewer (ako koristiš `ILogger<T>`)
2. **Real-time updates** - logovi se pojavljuju čim nastanu
3. **Auto-scroll** - automatski skroluje na najnoviji log (može se isključiti)
4. **Filtering** - klikni na filter button da vidiš samo određeni level
5. **Search** - ukucaj tekst da pretraziš logove
6. **Export** - sačuvaj logove u TXT/CSV fajl
7. **Pause** - pauzirj logging da preglediš postojeće logove
8. **Statistics** - vidiš broj logova po level-u u footer-u

---

## 🎯 Next Steps

Nakon što implementiraš Log Viewer, razmotri:

1. **Dodaj više UI tabova** (Dashboard, Performance Charts, Database Stats)
2. **Integriši sa existing workers** (oni već koriste ILogger)
3. **Dodaj alerts** za kritične greške
4. **Persistent logging** u fajl ili database
5. **Remote logging** (centralized logging server)

---

**LiveLogViewer je spreman! Dodaj ga u MainWindow i uživaj u real-time monitoring-u!** 🎉
