using Alfresco.Abstraction.Interfaces;
using Alfresco.Abstraction.Models;
using Alfresco.Client.Handlers;
using Alfresco.Client.Implementation;
using Alfresco.Contracts.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.IO;
using System.Net.Http;
using System.Windows;

namespace Alfresco.DocStatusUpdater
{
    public partial class App : Application
    {
        public static IHost AppHost { get; private set; } = null!;

        public App()
        {
            AppHost = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    var basePath = AppDomain.CurrentDomain.BaseDirectory;
                    string configPath;

                    if (basePath.Contains(@"\bin\Debug\") || basePath.Contains(@"\bin\Release\"))
                    {
                        var projectRoot = Directory.GetParent(basePath)!.Parent!.Parent!.Parent!.FullName;
                        configPath = Path.Combine(projectRoot, "appsettings.json");
                    }
                    else
                    {
                        configPath = Path.Combine(basePath, "appsettings.json");
                    }

                    config.AddJsonFile(configPath, optional: false, reloadOnChange: true);
                })
                .ConfigureServices((context, services) =>
                {
                    services.Configure<ConnectionsOptions>(context.Configuration);
                    services.Configure<AlfrescoOptions>(context.Configuration.GetSection("Alfresco"));

                    services.AddTransient<BasicAuthHandler>();

                    // Read API
                    services.AddHttpClient<IAlfrescoReadApi, AlfrescoReadApi>(cli =>
                    {
                        cli.Timeout = TimeSpan.FromSeconds(120);
                    })
                    .ConfigureHttpClient((sp, cli) =>
                    {
                        var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
                        var credentials = Convert.ToBase64String(
                            System.Text.Encoding.ASCII.GetBytes($"{options.Username}:{options.Password}"));
                        cli.BaseAddress = new Uri(options.BaseUrl);
                        cli.DefaultRequestHeaders.Accept.Add(
                            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                        cli.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                        cli.DefaultRequestHeaders.ConnectionClose = false;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                        MaxConnectionsPerServer = 20
                    })
                    .AddHttpMessageHandler<BasicAuthHandler>();

                    // Write API
                    services.AddHttpClient<IAlfrescoWriteApi, AlfrescoWriteApi>(cli =>
                    {
                        cli.Timeout = TimeSpan.FromSeconds(120);
                    })
                    .ConfigureHttpClient((sp, cli) =>
                    {
                        var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
                        var credentials = Convert.ToBase64String(
                            System.Text.Encoding.ASCII.GetBytes($"{options.Username}:{options.Password}"));
                        cli.BaseAddress = new Uri(options.BaseUrl);
                        cli.DefaultRequestHeaders.Accept.Add(
                            new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                        cli.DefaultRequestHeaders.Authorization =
                            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                        cli.DefaultRequestHeaders.ConnectionClose = false;
                    })
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                        PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                        MaxConnectionsPerServer = 20
                    })
                    .AddHttpMessageHandler<BasicAuthHandler>();

                    services.AddTransient<MainWindow>();
                })
                .ConfigureLogging(logging =>
                {
                    logging.AddFilter("System.Net.Http.HttpClient", Microsoft.Extensions.Logging.LogLevel.Warning);
                    logging.AddFilter("Microsoft", Microsoft.Extensions.Logging.LogLevel.Warning);
                })
                .Build();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            AppHost.Start();
            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (AppHost is not null) await AppHost.StopAsync();
            base.OnExit(e);
        }
    }
}
