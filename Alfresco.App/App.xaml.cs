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
// TODO: Uncomment when external APIs are available
// using Migration.Abstraction.Models;
// using Migration.Infrastructure.Implementation;
using Migration.Infrastructure.Implementation.Document;
using Migration.Infrastructure.Implementation.Folder;
using Migration.Infrastructure.Implementation.Move;
using Migration.Infrastructure.Implementation.Services;
using Migration.Workers;
using Migration.Workers.Interfaces;
using Polly;
using Polly.Extensions.Http;
//using Oracle.Abstraction.Interfaces;
//using Oracle.Infrastructure.Helpers;
//using Oracle.Infrastructure.Implementation;
//using Oracle.ManagedDataAccess.Client;
using SqlServer.Abstraction.Interfaces;
using SqlServer.Infrastructure.Implementation;
using System.Configuration;
using System.IO;
using System.Windows;
using System.Xml.Linq;
using static Alfresco.App.Helpers.PolicyHelpers;

namespace Alfresco.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IHost AppHost { get; private set; } = null!;

        // Lazy initialization to ensure LogViewer is created on UI thread at the right time
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
                                // Marshal to UI thread
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

                    // In Development mode, read from project source folder
                    // In Production mode, read from executable directory
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

                    // Load connections config from determined path
                    config.AddJsonFile(connectionsPath, optional: true, reloadOnChange: true);
                    //config.AddJsonFile("appsettings.Connections.json", optional: true, reloadOnChange: true);

                })
                .ConfigureServices((context, services) =>
                {
                    // Register ConnectionsOptions - loaded from appsettings.Connections.json
                    services.Configure<ConnectionsOptions>(context.Configuration);

                    // Register individual connection options for backward compatibility
                    services.Configure<AlfrescoOptions>(context.Configuration.GetSection("Alfresco"));
                    services.Configure<SqlServerOptions>(context.Configuration.GetSection("SqlServer"));

                    services.AddTransient<BasicAuthHandler>();

                    //services.AddHttpClient<IAlfrescoApi, AlfrescoAPI>(cli =>
                    //{
                    //    cli.Timeout = TimeSpan.FromSeconds(30);
                    //})
                    //    .ConfigureHttpClient((sp, cli) =>
                    //    {
                    //        var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
                    //        cli.BaseAddress = new Uri(options.BaseUrl);
                    //        cli.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                    //    })

                    //    .AddHttpMessageHandler<BasicAuthHandler>()
                    //    .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                    //    .AddPolicyHandler(GetRetryPlicy())
                    //    .AddPolicyHandler(GetCircuitBreakerPolicy());

                    services.AddHttpClient<IAlfrescoReadApi, AlfrescoReadApi>(cli =>
                    {
                        cli.Timeout = Timeout.InfiniteTimeSpan; // TimeOut iz polly
                    })
                        .ConfigureHttpClient((sp, cli) =>
                        {
                            // Try ConnectionsOptions first, fallback to AlfrescoOptions
                            //var connOptions = sp.GetService<IOptions<ConnectionsOptions>>()?.Value;
                            //var baseUrl = connOptions?.Alfresco?.BaseUrl;
                            var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
                            var credentials = Convert.ToBase64String(
                                System.Text.Encoding.ASCII.GetBytes($"{options.Username}:{options.Password}")
                            );

                            //baseUrl = options.BaseUrl;


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
                            var logger = sp.GetRequiredService<ILogger<AlfrescoReadApi>>();
                            var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

                            // Combined policy: Timeout → Retry → Circuit Breaker → Bulkhead
                            return PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations, logger);
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
                            var logger = sp.GetRequiredService<ILogger<AlfrescoReadApi>>();
                            var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

                            // Combined policy: Timeout → Retry → Circuit Breaker → Bulkhead
                            return PolicyHelpers.GetCombinedWritePolicy(pollyOptions.WriteOperations, logger);
                        });
                        //.AddPolicyHandler(GetRetryPlicy())
                        //.AddPolicyHandler(GetCircuitBreakerPolicy());

                    //services.Configure<OracleOptions>(context.Configuration.GetSection("Oracle"));
                    services.Configure<Alfresco.Contracts.SqlServer.SqlServerOptions>(context.Configuration.GetSection("SqlServer"));
                    services.Configure<AlfrescoDbOptions>(context.Configuration.GetSection(AlfrescoDbOptions.SectionName));
                    services.AddSingleton(sp => sp.GetRequiredService<IOptions<Alfresco.Contracts.SqlServer.SqlServerOptions>>().Value);

                    // Configure Polly Policy Options
                    services.Configure<PollyPolicyOptions>(context.Configuration.GetSection(PollyPolicyOptions.SectionName));

                    // =====================================================================================
                    // EXTERNAL API CLIENTS AND MIGRATION SERVICES
                    // =====================================================================================

                    // Configure ClientAPI Options
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
                            var logger = sp.GetRequiredService<ILogger<Migration.Infrastructure.Implementation.ClientApi>>();
                            var pollyOptions = sp.GetRequiredService<IOptions<PollyPolicyOptions>>().Value;

                            // ClientAPI uses Read policy (similar to AlfrescoReadApi)
                            return PolicyHelpers.GetCombinedReadPolicy(pollyOptions.ReadOperations, logger);
                        });

                    

                    services.AddTransient<SqlServer.Abstraction.Interfaces.IDocStagingRepository, SqlServer.Infrastructure.Implementation.DocStagingRepository>();
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IFolderStagingRepository, SqlServer.Infrastructure.Implementation.FolderStagingRepository>();
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IMigrationCheckpointRepository, SqlServer.Infrastructure.Implementation.MigrationCheckpointRepository>();

                    // Phase-based checkpoint repository (NEW - for refactoring)
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IPhaseCheckpointRepository, SqlServer.Infrastructure.Implementation.PhaseCheckpointRepository>();

                    // Memory Cache for DocumentMappingRepository (caching individual query results + category mappings)
                    services.AddMemoryCache();

                    // DocumentMappings - Database-driven mapping service (replaces HeimdallDocumentMapper)
                    // Automatically enriches DocumentMapping with CategoryMapping data
                    services.AddScoped<SqlServer.Abstraction.Interfaces.IDocumentMappingRepository, SqlServer.Infrastructure.Implementation.DocumentMappingRepository>();
                    services.AddScoped<Migration.Abstraction.Interfaces.IDocumentMappingService, Migration.Infrastructure.Implementation.DocumentMappingService>();

                    // Mappers that use DocumentMappingService
                    // Using OptimizedOpisToTipMapper with in-memory caching (30× faster than OpisToTipMapperV2)
                    // services.AddScoped<Migration.Abstraction.Interfaces.IOpisToTipMapper, Migration.Infrastructure.Implementation.Mappers.OptimizedOpisToTipMapper>();
                    services.AddScoped<Migration.Abstraction.Interfaces.IOpisToTipMapper, OpisToTipMapperV2>();
                    services.AddScoped<Migration.Infrastructure.Implementation.DocumentStatusDetectorV2>();

                    #region oracle DI (commented)
                    //services.AddTransient<IDocStagingRepository, DocStagingRepository>();
                    //services.AddTransient<IFolderStagingRepository, FolderStagingRepository>();
                    //services.AddTransient<IMigrationCheckpointRepository, MigrationCheckpointRepository>(); 
                    #endregion


                    services.Configure<MigrationOptions>(context.Configuration.GetSection("Migration"));
                    services.Configure<FolderNodeTypeMappingConfig>(context.Configuration.GetSection("Migration:FolderNodeTypeMapping"));
                   // services.Configure<WorkerSetting>(context.Configuration.GetSection("WorkerSetting"));

                    //services.AddScoped<IUnitOfWork>(sp => new OracleUnitOfWork(sp.GetRequiredService<OracleOptions>().ConnectionString));

                    // Use ConnectionsOptions for centralized connection management
                    services.AddScoped<IUnitOfWork>(sp =>
                    {
                        var connOptions = sp.GetRequiredService<IOptions<ConnectionsOptions>>().Value;
                        return new SqlServerUnitOfWork(connOptions.SqlServer.ConnectionString);
                    });

                    services.AddTransient<IFolderReader, FolderReader>();
                    services.AddTransient<IFolderIngestor,FolderIngestor>();
                    services.AddSingleton<IFolderDiscoveryService, FolderDiscoveryService>();

                    // Folder path and management services
                    services.AddSingleton<IFolderPathService, Migration.Infrastructure.Implementation.FolderPathService>();
                    services.AddSingleton<IFolderManager, Migration.Infrastructure.Implementation.FolderManager>();

                    services.AddTransient<IDocumentReader, DocumentReader>();
                    services.AddTransient<IDocumentResolver, DocumentResolver>();
                    services.AddTransient<IDocumentIngestor, DocumentIngestor>();
                    services.AddSingleton<IDocumentDiscoveryService, DocumentDiscoveryService>();

                    // NEW: DocumentSearchService (MigrationByDocument mode - searches by ecm:docType)
                    services.AddSingleton<IDocumentSearchService, DocumentSearchService>();

                    services.AddTransient<IMoveReader, MoveReader>();
                    services.AddTransient<IMoveExecutor, MoveExecutor>();
                    services.AddSingleton<IMoveService, MoveService>();

                    // NEW: FolderPreparationService (FAZA 3 - parallel folder creation)
                    services.AddSingleton<IFolderPreparationService, FolderPreparationService>();

                    // NEW: MigrationWorker (orchestrator za sve 4 faze)
                    services.AddSingleton<IMigrationWorker, MigrationWorker>();

                    services.AddSingleton<IAlfrescoDbReader, AlfrescoDbReader>();

                    //


                    var useMigByDoc = context.Configuration.GetValue<bool>("Migration:MigrationByDocument");

                    var options = context.Configuration.GetSection("Migration").Get<MigrationOptions>();

                    if (context.Configuration.GetValue<bool>("EnableDocumentSearchWorker") && useMigByDoc)
                    {
                        //services.AddHostedService<FolderPreparationWorker>();
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
                    
                    


                    services.AddHealthChecks()
                            //.AddOracle(connectionString: context.Configuration["Oracle:ConnectionString"],
                            //           name: "Oracle-db",
                            //
                            //           failureStatus: HealthStatus.Unhealthy,
                            //           tags: new[] {"db", "oracle"})
                            .AddSqlServer(connectionString: context.Configuration["SqlServer:ConnectionString"],
                                       name: "SqlServer-db",

                                       failureStatus: HealthStatus.Unhealthy,
                                       tags: new[] {"db", "sqlserver"})
                            .AddUrlGroup(uri: new Uri(context.Configuration["Alfresco:BaseUrl"]!),
                                         name: "alfresco-api",
                                         failureStatus: HealthStatus.Unhealthy,
                                         tags: new[] {"api","alfresco"});                     

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


            //AppHost.MapHealthChecks();
            //Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
            //OracleHelpers.RegisterFrom<DocStaging>(); // Oracle specific - not needed for SQL Server
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

        /// <summary>
        /// Ensures connections config exists in local directory, creates from template if not
        /// </summary>
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
