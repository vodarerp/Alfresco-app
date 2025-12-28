using Alfresco.Abstraction.Interfaces;
using Alfresco.Abstraction.Models;
using Alfresco.App.Helpers;
using Alfresco.App.Logging;
using Alfresco.App.UserControls;
using Alfresco.Client;
using Alfresco.Client.Handlers;
using Alfresco.Client.Helpers;
using Alfresco.Client.Implementation;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle;
using Alfresco.Contracts.SqlServer;
using Dapper;
using log4net.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Configuration;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Services;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Infrastructure.Implementation;
using Migration.Infrastructure.Implementation.Alfresco;
using Migration.Infrastructure.Implementation.Document;
using Migration.Infrastructure.Implementation.Folder;
using Migration.Infrastructure.Implementation.Move;
using Migration.Infrastructure.Implementation.Services;
using Migration.Workers;
using Migration.Workers.Interfaces;

using SqlServer.Abstraction.Interfaces;
using SqlServer.Infrastructure.Implementation;

using System.IO;
using System.Windows;


namespace Alfresco.App
{

    public partial class App : Application
    {
        public static IHost AppHost { get; private set; } = null!;

        
        private static LiveLogViewer? _logViewer;
        private static readonly object _logViewerLock = new object();

        public static LiveLogViewer LogViewer
        {
            get
            {
                if (_logViewer == null)
                {
                    lock (_logViewerLock)
                    {
                        if (_logViewer == null)
                        {
                            // Create on UI thread if needed
                            if (Current?.Dispatcher?.CheckAccess() == true)
                            {
                                _logViewer = new LiveLogViewer();
                            }
                            else
                            {                               
                                Current?.Dispatcher?.Invoke(() => _logViewer = new LiveLogViewer());
                            }
                        }
                    }
                }
                return _logViewer!;
            }
        }

        public App()
        {
            // Ensure connections config exists in local directory
            EnsureConnectionsConfigExists();

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                   // Determine the correct path for appsettings.Connections.json
                   var basePath = AppDomain.CurrentDomain.BaseDirectory;
                   string connectionsPath;                    
                    if (basePath.Contains(@"\bin\Debug\") || basePath.Contains(@"\bin\Release\"))
                    {
                        // Development mode - go to project root
                        var projectRoot = Directory.GetParent(basePath).Parent.Parent.Parent.FullName;
                        connectionsPath = Path.Combine(projectRoot, "appsettings.Connections.json");
                    }
                    else
                    {
                        // Production mode - use file next to executable
                        connectionsPath = Path.Combine(basePath, "appsettings.Connections.json");
                    }                    
                    config.AddJsonFile(connectionsPath, optional: true, reloadOnChange: true); 
                })
                .ConfigureServices((context, services) =>
                {                   
                    services.Configure<ConnectionsOptions>(context.Configuration);
                    services.Configure<AlfrescoOptions>(context.Configuration.GetSection("Alfresco"));
                    services.Configure<SqlServerOptions>(context.Configuration.GetSection("SqlServer"));
                    services.AddTransient<BasicAuthHandler>();
                    services.AddHttpClient<IAlfrescoReadApi, AlfrescoReadApi>(cli =>
                    {
                        cli.Timeout = Timeout.InfiniteTimeSpan; // TimeOut iz polly
                    })
                        .ConfigureHttpClient((sp, cli) =>
                        {                            
                            var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
                            var credentials = Convert.ToBase64String(
                                System.Text.Encoding.ASCII.GetBytes($"{options.Username}:{options.Password}")
                            );
                            cli.BaseAddress = new Uri(options.BaseUrl);
                            cli.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                            cli.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                            cli.DefaultRequestHeaders.ConnectionClose = false; // Keep-Alive
                        })
                        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
                        {
                            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                            MaxConnectionsPerServer = 100,
                            EnableMultipleHttp2Connections = true
                        })
                        .AddHttpMessageHandler<BasicAuthHandler>()
                        .SetHandlerLifetime(TimeSpan.FromMinutes(10))
                        .AddPolicyHandler((sp, req) =>
                        {
                            //var logger = sp.GetRequiredService<ILogger<AlfrescoReadApi>>();
                            var logger = sp.GetRequiredService<ILoggerFactory>();
                            var _dbLogger = logger.CreateLogger("DbLogger");
                            var _fileLogger = logger.CreateLogger("FileLogger");
                            var _uiLogger = logger.CreateLogger("UiLogger");

                            var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;
                            // Combined policy: Timeout → Retry → Circuit Breaker → Bulkhead
                            return PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations, _fileLogger, _dbLogger, _uiLogger);
                        });
                    //.AddPolicyHandler(GetRetryPlicy())
                    //.AddPolicyHandler(GetCircuitBreakerPolicy());

                    services.AddHttpClient<IAlfrescoWriteApi, AlfrescoWriteApi>(cli =>
                    {
                        cli.Timeout = Timeout.InfiniteTimeSpan; // TimeOut iz polly
                    })
                        .ConfigureHttpClient((sp, cli) =>
                        {
                            var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
                            var credentials = Convert.ToBase64String(
                                System.Text.Encoding.ASCII.GetBytes($"{options.Username}:{options.Password}")
                            );
                            cli.BaseAddress = new Uri(options.BaseUrl);
                            cli.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                            cli.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", credentials);
                            cli.DefaultRequestHeaders.ConnectionClose = false; // Keep-Alive
                        })
                        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
                        {
                            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
                            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
                            MaxConnectionsPerServer = 100,
                            EnableMultipleHttp2Connections = true
                        })
                        .AddHttpMessageHandler<BasicAuthHandler>()
                        .SetHandlerLifetime(TimeSpan.FromMinutes(10))
                        .AddPolicyHandler((sp, req) =>
                        {
                            //var logger = sp.GetRequiredService<ILogger<AlfrescoReadApi>>();
                            var logger = sp.GetRequiredService<ILoggerFactory>();
                            var _dbLogger = logger.CreateLogger("DbLogger");
                            var _fileLogger = logger.CreateLogger("FileLogger");
                            var _uiLogger = logger.CreateLogger("UiLogger");
                            var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

                            // Combined policy: Timeout → Retry → Circuit Breaker → Bulkhead
                            return PolicyHelpers.GetCombinedWritePolicy(pollyOptions.WriteOperations, _fileLogger, _dbLogger, _uiLogger);
                        });
                        //.AddPolicyHandler(GetRetryPlicy())
                        //.AddPolicyHandler(GetCircuitBreakerPolicy());

                    //services.Configure<OracleOptions>(context.Configuration.GetSection("Oracle"));
                    services.Configure<Alfresco.Contracts.SqlServer.SqlServerOptions>(context.Configuration.GetSection("SqlServer"));
                    services.Configure<AlfrescoDbOptions>(context.Configuration.GetSection(AlfrescoDbOptions.SectionName));
                    services.AddSingleton(sp => sp.GetRequiredService<IOptions<Alfresco.Contracts.SqlServer.SqlServerOptions>>().Value);                    
                    services.Configure<PollyPolicyOptions>(context.Configuration.GetSection(PollyPolicyOptions.SectionName));
                    services.Configure<Migration.Infrastructure.Implementation.ClientApiOptions>(
                        context.Configuration.GetSection(Migration.Infrastructure.Implementation.ClientApiOptions.SectionName));

                    // ClientAPI HttpClient with Polly policies
                    services.AddHttpClient<IClientApi, Migration.Infrastructure.Implementation.ClientApi>(cli =>
                    {
                        cli.Timeout = Timeout.InfiniteTimeSpan; // Timeout handled by Polly
                    })
                        .ConfigureHttpClient((sp, cli) =>
                        {
                            // Try ConnectionsOptions first, fallback to ClientApiOptions
                            var connOptions = sp.GetService<IOptions<ConnectionsOptions>>()?.Value;
                            var baseUrl = connOptions?.ClientApi?.BaseUrl;
                            string? apiKey = connOptions?.ClientApi?.ApiKey;

                            if (string.IsNullOrEmpty(baseUrl))
                            {
                                var options = sp.GetRequiredService<IOptions<Migration.Infrastructure.Implementation.ClientApiOptions>>().Value;
                                baseUrl = options.BaseUrl;
                                apiKey = options.ApiKey;
                            }

                            cli.BaseAddress = new Uri(baseUrl);
                            cli.DefaultRequestHeaders.Accept.Add(
                                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                            // Add API key header if configured
                            if (!string.IsNullOrEmpty(apiKey))
                            {
                                cli.DefaultRequestHeaders.Add("X-API-Key", apiKey);
                            }
                        })
                        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
                        {
                            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                            MaxConnectionsPerServer = 50
                        })
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .AddPolicyHandler((sp, req) =>
                        {
                            //var logger = sp.GetRequiredService<ILogger<Migration.Infrastructure.Implementation.ClientApi>>();kkkkkkk
                            //ILoggerFactory logger
                            var logger = sp.GetRequiredService<ILoggerFactory>();
                            var _dbLogger = logger.CreateLogger("DbLogger");
                            var _fileLogger = logger.CreateLogger("FileLogger");
                            var _uiLogger = logger.CreateLogger("UiLogger");
                            var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

                            // ClientAPI uses Read policy (similar to AlfrescoReadApi)
                            return PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations, _fileLogger, _dbLogger, _uiLogger);
                        });                    

                    services.AddTransient<SqlServer.Abstraction.Interfaces.IDocStagingRepository, SqlServer.Infrastructure.Implementation.DocStagingRepository>();
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IFolderStagingRepository, SqlServer.Infrastructure.Implementation.FolderStagingRepository>();
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IMigrationCheckpointRepository, SqlServer.Infrastructure.Implementation.MigrationCheckpointRepository>();
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IPhaseCheckpointRepository, SqlServer.Infrastructure.Implementation.PhaseCheckpointRepository>();
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IKdpDocumentStagingRepository, SqlServer.Infrastructure.Implementation.KdpDocumentStagingRepository>();
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IKdpExportResultRepository, SqlServer.Infrastructure.Implementation.KdpExportResultRepository>();
                    services.AddMemoryCache();
                    services.AddScoped<SqlServer.Abstraction.Interfaces.IDocumentMappingRepository, SqlServer.Infrastructure.Implementation.DocumentMappingRepository>();
                    services.AddScoped<Migration.Abstraction.Interfaces.IDocumentMappingService, Migration.Infrastructure.Implementation.DocumentMappingService>();
                    services.AddScoped<Migration.Abstraction.Interfaces.IOpisToTipMapper, OpisToTipMapperV2>();
                    services.AddScoped<Migration.Infrastructure.Implementation.DocumentStatusDetectorV2>();
                    services.Configure<MigrationOptions>(context.Configuration.GetSection("Migration"));
                    services.Configure<ErrorThresholdsOptions>(context.Configuration.GetSection(ErrorThresholdsOptions.SectionName));
                    services.Configure<FolderNodeTypeMappingConfig>(context.Configuration.GetSection("Migration:FolderNodeTypeMapping"));
              
                    services.AddScoped<IUnitOfWork>(sp =>
                    {
                        var connOptions = sp.GetRequiredService<IOptions<ConnectionsOptions>>().Value;
                        return new SqlServerUnitOfWork(connOptions.SqlServer.ConnectionString);
                    });

                    services.AddTransient<IFolderReader, FolderReader>();
                    services.AddTransient<IFolderIngestor,FolderIngestor>();
                    services.AddSingleton<IFolderDiscoveryService, FolderDiscoveryService>();           
                    services.AddSingleton<IFolderPathService, Migration.Infrastructure.Implementation.FolderPathService>();
                    services.AddSingleton<IFolderManager, Migration.Infrastructure.Implementation.FolderManager>();
                    services.AddTransient<IDocumentReader, DocumentReader>();
                    services.AddTransient<IDocumentResolver, DocumentResolver>();
                    services.AddTransient<IDocumentIngestor, DocumentIngestor>();
                    services.AddSingleton<IDocumentDiscoveryService, DocumentDiscoveryService>();                   
                    services.AddSingleton<IDocumentSearchService, DocumentSearchService>();
                    services.AddSingleton<IKdpDocumentProcessingService, KdpDocumentProcessingService>();
                    services.AddTransient<IMoveReader, MoveReader>();
                    services.AddTransient<IMoveExecutor, MoveExecutor>();
                    services.AddSingleton<IMoveService, MoveService>();                  
                    services.AddSingleton<IFolderPreparationService, FolderPreparationService>();                    
                    services.AddSingleton<GlobalErrorTracker>();
                    services.AddSingleton<IMigrationWorker, MigrationWorker>();
                    services.AddSingleton<IAlfrescoDbReader, AlfrescoDbReader>();
                  
                    var useMigByDoc = context.Configuration.GetValue<bool>("Migration:MigrationByDocument");
                    var options = context.Configuration.GetSection("Migration").Get<MigrationOptions>();
                    if (context.Configuration.GetValue<bool>("EnableDocumentSearchWorker") && useMigByDoc)
                    {                       
                        services.AddSingleton<IWorkerController, DocumentSearchWorker>();
                        services.AddHostedService(sp => (DocumentSearchWorker)sp.GetServices<IWorkerController>().First(o => o is DocumentSearchWorker));
                    }
                    if (context.Configuration.GetValue<bool>("EnableFolderWorker") && !useMigByDoc)
                    {
                        //services.AddHostedService<FolderDiscoveryWorker>();
                        services.AddSingleton<IWorkerController, FolderDiscoveryWorker>();
                        services.AddHostedService(sp => (FolderDiscoveryWorker)sp.GetServices<IWorkerController>().First(o => o is FolderDiscoveryWorker));

                    }
                    if (context.Configuration.GetValue<bool>("EnableDocumentWorker") && !useMigByDoc)
                    {
                        //services.AddHostedService<DocumentDiscoveryWorker>();
                        services.AddSingleton<IWorkerController, DocumentDiscoveryWorker>();
                        services.AddHostedService(sp => (DocumentDiscoveryWorker)sp.GetServices<IWorkerController>().First(o => o is DocumentDiscoveryWorker));
                    }
                    if (context.Configuration.GetValue<bool>("EnableFolderPreparationWorker"))
                    {
                        //services.AddHostedService<FolderPreparationWorker>();
                        services.AddSingleton<IWorkerController, FolderPreparationWorker>();
                        services.AddHostedService(sp => (FolderPreparationWorker)sp.GetServices<IWorkerController>().First(o => o is FolderPreparationWorker));
                    }
                    if (context.Configuration.GetValue<bool>("EnableMoveWorker"))
                    {
                        //services.AddHostedService<MoveWorker>();
                        services.AddSingleton<IWorkerController, MoveWorker>();
                        services.AddHostedService(sp => (MoveWorker)sp.GetServices<IWorkerController>().First(o => o is MoveWorker));
                    }
                    
                    //services.AddHealthChecks()
                           
                    //        .AddSqlServer(connectionString: context.Configuration["SqlServer:ConnectionString"],
                    //                   name: "SqlServer-db",

                    //                   failureStatus: HealthStatus.Unhealthy,
                    //                   tags: new[] {"db", "sqlserver"})
                    //        .AddUrlGroup(uri: new Uri(context.Configuration["Alfresco:BaseUrl"]!),
                    //                     name: "alfresco-api",
                    //                     failureStatus: HealthStatus.Unhealthy,
                    //                     tags: new[] {"api","alfresco"});                     

                    services.AddTransient<MainWindow>();

                })
                .ConfigureLogging((context,logging) =>
                {
                    logging.ClearProviders();

                    // Determine log4net.config path (same logic as appsettings.Connections.json)
                    var basePath = AppDomain.CurrentDomain.BaseDirectory;
                    string log4netConfigPath;

                    if (basePath.Contains(@"\bin\Debug\") || basePath.Contains(@"\bin\Release\"))
                    {
                        // Development mode - use source file
                        var projectRoot = Directory.GetParent(basePath).Parent.Parent.Parent.FullName;
                        log4netConfigPath = Path.Combine(projectRoot, "log4net.config");
                    }
                    else
                    {
                        // Production mode - use file next to executable
                        log4netConfigPath = Path.Combine(basePath, "log4net.config");
                    }

                    logging.AddLog4Net(log4netConfigPath);
                    logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                    logging.AddFilter("Microsoft", LogLevel.Warning);
                    logging.AddFilter("System", LogLevel.Warning);

                    // Add LiveLogViewer provider - ONLY for UiLogger
                    logging.AddProvider(new SelectiveLiveLoggerProvider(
                        LogViewer,
                        "UiLogger"  // Only UiLogger appears in LiveLogViewer UI
                    ));

                    log4net.GlobalContext.Properties["AppInstance"] = $"Service-{Environment.ProcessId}";

                })
                .Build();


           
        }

        protected override void OnStartup(StartupEventArgs e)
        {                    

            AppHost.Start();

            //var window = AppHost.Services.GetRequiredService<MainWindow>();
            //window.Show();

            base.OnStartup(e);
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            if (AppHost is not null) await AppHost.StopAsync();

            base.OnExit(e);
        }

        
        private static void EnsureConnectionsConfigExists()
        {
            try
            {
                var currentDir = AppDomain.CurrentDomain.BaseDirectory;
                var localConnectionsPath = Path.Combine(currentDir, "appsettings.Connections.json");

                // If local connections file doesn't exist, create it from example
                if (!File.Exists(localConnectionsPath))
                {
                    var examplePath = Path.Combine(currentDir, "appsettings.Connections.Example.json");

                    if (File.Exists(examplePath))
                    {
                        // Copy example to local directory
                        File.Copy(examplePath, localConnectionsPath);
                    }
                       
                }
            }
            catch 
            {
               
            }
        }

    }

}
