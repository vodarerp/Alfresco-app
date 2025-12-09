using Alfresco.Abstraction.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Migration.Abstraction.Interfaces;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace Alfresco.App.UserControls
{
    public partial class SettingsUC : UserControl, INotifyPropertyChanged
    {
        private readonly IAlfrescoReadApi _alfrescoService;
        private readonly IClientApi _clientApi;
        private string _appSettingsPath;
        private string _appSettingsConnectionsPath;
        private string _log4netConfigPath;

        public SettingsUC()
        {
            InitializeComponent();
            DataContext = this;

            _alfrescoService = App.AppHost.Services.GetRequiredService<IAlfrescoReadApi>();
            _clientApi = App.AppHost.Services.GetRequiredService<IClientApi>();

            // Get paths to appsettings files
            // In Development: Use source files in Alfresco.App project folder
            // In Production/Publish: Use files in the same directory as the executable
            var basePath = AppDomain.CurrentDomain.BaseDirectory;

            // Check if we are in development mode (bin\Debug or bin\Release exists in path)
            if (basePath.Contains(@"\bin\Debug\") || basePath.Contains(@"\bin\Release\"))
            {
                // Development mode - go up to project root
                var projectRoot = Directory.GetParent(basePath).Parent.Parent.Parent.FullName;
                _appSettingsPath = Path.Combine(projectRoot, "appsettings.json");
                _appSettingsConnectionsPath = Path.Combine(projectRoot, "appsettings.Connections.json");
                _log4netConfigPath = Path.Combine(projectRoot, "log4net.config");
            }
            else
            {
                // Production mode - use files next to executable
                _appSettingsPath = Path.Combine(basePath, "appsettings.json");
                _appSettingsConnectionsPath = Path.Combine(basePath, "appsettings.Connections.json");
                _log4netConfigPath = Path.Combine(basePath, "log4net.config");
            }

            LoadCurrentConfiguration();
        }

        private void LoadCurrentConfiguration()
        {
            try
            {
                var configuration = App.AppHost.Services.GetRequiredService<IConfiguration>();

                // Load SQL Server settings
                var sqlConnectionString = configuration.GetSection("SqlServer:ConnectionString").Value ?? "";
                ParseSqlConnectionString(sqlConnectionString);

                // Load Alfresco settings
                AlfrescoBaseUrlTextBox.Text = configuration.GetSection("Alfresco:BaseUrl").Value ?? "";
                AlfrescoUsernameTextBox.Text = configuration.GetSection("Alfresco:Username").Value ?? "";
                AlfrescoPasswordBox.Password = configuration.GetSection("Alfresco:Password").Value ?? "";

                // Load Client API settings
                ClientApiBaseUrlTextBox.Text = configuration.GetSection("ClientApi:BaseUrl").Value ?? "";

                StatusTextBlock.Text = "Configuration loaded successfully.";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error loading configuration: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void ParseSqlConnectionString(string connectionString)
        {
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                SqlServerTextBox.Text = builder.DataSource;
                SqlDatabaseTextBox.Text = builder.InitialCatalog;
                SqlUserTextBox.Text = builder.UserID;
                SqlPasswordBox.Password = builder.Password;
                SqlIntegratedSecurityCheckBox.IsChecked = builder.IntegratedSecurity;
            }
            catch
            {
                // If parsing fails, leave fields empty
            }
        }

        private string BuildSqlConnectionString()
        {
            var builder = new SqlConnectionStringBuilder();
            builder.DataSource = SqlServerTextBox.Text;
            builder.InitialCatalog = SqlDatabaseTextBox.Text;
            builder.IntegratedSecurity = SqlIntegratedSecurityCheckBox.IsChecked ?? false;

            if (!builder.IntegratedSecurity)
            {
                builder.UserID = SqlUserTextBox.Text;
                builder.Password = SqlPasswordBox.Password;
            }

            builder.TrustServerCertificate = true;

            return builder.ConnectionString;
        }

        private async void TestSqlButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Testing SQL Server connection...";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Blue;
            TestSqlButton.IsEnabled = false;

            try
            {
                var connectionString = BuildSqlConnectionString();
                using (var conn = new SqlConnection(connectionString))
                {
                    await conn.OpenAsync();
                    StatusTextBlock.Text = "SQL Server connection successful!";
                    StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"SQL Server connection failed: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                TestSqlButton.IsEnabled = true;
            }
        }

        private async void TestAlfrescoButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Testing Alfresco connection...";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Blue;
            TestAlfrescoButton.IsEnabled = false;

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.BaseAddress = new Uri(AlfrescoBaseUrlTextBox.Text);
                    httpClient.Timeout = TimeSpan.FromSeconds(10);

                    var credentials = Convert.ToBase64String(
                        System.Text.Encoding.ASCII.GetBytes($"{AlfrescoUsernameTextBox.Text}:{AlfrescoPasswordBox.Password}")
                    );
                    httpClient.DefaultRequestHeaders.Authorization =
                        new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);

                    var response = await httpClient.GetAsync("/alfresco/api/discovery");

                    if (response.IsSuccessStatusCode)
                    {
                        StatusTextBlock.Text = "Alfresco connection successful!";
                        StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    }
                    else
                    {
                        StatusTextBlock.Text = $"Alfresco connection failed: {response.StatusCode}";
                        StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Alfresco connection failed: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                TestAlfrescoButton.IsEnabled = true;
            }
        }

        private async void TestClientApiButton_Click(object sender, RoutedEventArgs e)
        {
            StatusTextBlock.Text = "Testing Client API connection...";
            StatusTextBlock.Foreground = System.Windows.Media.Brushes.Blue;
            TestClientApiButton.IsEnabled = false;

            try
            {
                using (var httpClient = new HttpClient())
                {
                    httpClient.BaseAddress = new Uri(ClientApiBaseUrlTextBox.Text);
                    httpClient.Timeout = TimeSpan.FromSeconds(10);

                    var response = await httpClient.GetAsync("/api/Client/GetClientDetailExtended/test");

                    if (response.IsSuccessStatusCode)
                    {
                        StatusTextBlock.Text = "Client API connection successful!";
                        StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;
                    }
                    else
                    {
                        StatusTextBlock.Text = $"Client API connection failed: {response.StatusCode}";
                        StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Client API connection failed: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
            }
            finally
            {
                TestClientApiButton.IsEnabled = true;
            }
        }

        private async void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveButton.IsEnabled = false;
                StatusTextBlock.Text = "Saving configuration...";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Blue;

                // Save to appsettings.Connections.json
                await SaveConnectionsConfiguration();

                // Update log4net.config with SQL connection string
                UpdateLog4NetConfig();

                StatusTextBlock.Text = "Configuration saved successfully. Restarting application...";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Green;

                // Wait a moment for user to see the message
                await Task.Delay(1500);

                // Restart application
                RestartApplication();
            }
            catch (Exception ex)
            {
                StatusTextBlock.Text = $"Error saving configuration: {ex.Message}";
                StatusTextBlock.Foreground = System.Windows.Media.Brushes.Red;
                SaveButton.IsEnabled = true;
            }
        }

        private async Task SaveConnectionsConfiguration()
        {
            // Read existing appsettings.Connections.json
            var connectionsConfig = new
            {
                AlfrescoDatabase = new
                {
                    ConnectionString = "Host=localhost;Port=5432;Database=alfresco;Username=alfresco;Password=alfresco"
                },
                Alfresco = new
                {
                    BaseUrl = AlfrescoBaseUrlTextBox.Text,
                    Username = AlfrescoUsernameTextBox.Text,
                    Password = AlfrescoPasswordBox.Password
                },
                ClientApi = new
                {
                    BaseUrl = ClientApiBaseUrlTextBox.Text,
                    GetClientDataEndpoint = "/api/Client/GetClientDetailExtended",
                    GetActiveAccountsEndpoint = "/api/Client",
                    ValidateClientEndpoint = "/api/Client/GetClientDetail",
                    TimeoutSeconds = 30,
                    ApiKey = (string)null,
                    RetryCount = 3
                },
                SqlServer = new
                {
                    ConnectionString = BuildSqlConnectionString(),
                    CommandTimeoutSeconds = 120,
                    BulkBatchSize = 1000
                }
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };

            var json = JsonSerializer.Serialize(connectionsConfig, options);
            await File.WriteAllTextAsync(_appSettingsConnectionsPath, json);
        }

        private void UpdateLog4NetConfig()
        {
            try
            {
                if (!File.Exists(_log4netConfigPath))
                {
                    StatusTextBlock.Text = "Warning: log4net.config file not found, skipping update.";
                    return;
                }

                // Load the XML document
                var doc = XDocument.Load(_log4netConfigPath);

                // Define the namespace
                XNamespace ns = "urn:log4net";

                // Find the connectionString element in SqlServerAdoAppender
                var connectionStringElement = doc.Descendants(ns + "appender")
                    .Where(a => (string)a.Attribute("name") == "SqlServerAdoAppender")
                    .Descendants(ns + "connectionString")
                    .FirstOrDefault();

                if (connectionStringElement != null)
                {
                    // Update the connection string value
                    var newConnectionString = BuildSqlConnectionString();
                    connectionStringElement.SetAttributeValue("value", newConnectionString);

                    // Save the document
                    doc.Save(_log4netConfigPath);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't stop the save process
                StatusTextBlock.Text = $"Warning: Could not update log4net.config: {ex.Message}";
            }
        }

        private void RestartApplication()
        {
            try
            {
                // Get the path to the current executable
                var exePath = Process.GetCurrentProcess().MainModule.FileName;

                // Start a new instance
                Process.Start(new ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });

                // Close current instance
                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error restarting application: {ex.Message}\n\nPlease restart the application manually.",
                    "Restart Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the settings window
            var window = Window.GetWindow(this);
            window?.Close();
        }

        private void SqlIntegratedSecurityCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            bool isIntegrated = SqlIntegratedSecurityCheckBox.IsChecked ?? false;
            SqlUserTextBox.IsEnabled = !isIntegrated;
            SqlPasswordBox.IsEnabled = !isIntegrated;
        }

        #region INotifyPropertyChange implementation
        public event PropertyChangedEventHandler PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
