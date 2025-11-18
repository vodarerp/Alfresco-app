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
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces.Services;
using Migration.Abstraction.Interfaces.Wrappers;
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
//using Oracle.Abstraction.Interfaces;
//using Oracle.Infrastructure.Helpers;
//using Oracle.Infrastructure.Implementation;
//using Oracle.ManagedDataAccess.Client;
using SqlServer.Abstraction.Interfaces;
using SqlServer.Infrastructure.Implementation;
using Polly;
using Polly.Extensions.Http;
using System.Configuration;
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

            AppHost = Host.CreateDefaultBuilder()
                .ConfigureServices((context, services) =>
                {

                    

                    services.Configure<AlfrescoOptions>(context.Configuration.GetSection("Alfresco"));

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
                            var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
                            cli.BaseAddress = new Uri(options.BaseUrl);
                            cli.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
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

                            // Combined policy: Timeout → Retry → Circuit Breaker → Bulkhead
                            return PolicyHelpers.GetCombinedReadPolicy(logger);
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
                            cli.BaseAddress = new Uri(options.BaseUrl);
                            cli.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
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

                            // Combined policy: Timeout → Retry → Circuit Breaker → Bulkhead
                            return PolicyHelpers.GetCombinedWritePolicy(logger);
                        });
                        //.AddPolicyHandler(GetRetryPlicy())
                        //.AddPolicyHandler(GetCircuitBreakerPolicy());

                    //services.Configure<OracleOptions>(context.Configuration.GetSection("Oracle"));
                    services.Configure<Alfresco.Contracts.SqlServer.SqlServerOptions>(context.Configuration.GetSection("SqlServer"));
                    services.Configure<AlfrescoDbOptions>(context.Configuration.GetSection(AlfrescoDbOptions.SectionName));
                    services.AddSingleton(sp => sp.GetRequiredService<IOptions<Alfresco.Contracts.SqlServer.SqlServerOptions>>().Value);

                    // =====================================================================================
                    // EXTERNAL API CLIENTS AND MIGRATION SERVICES
                    // =====================================================================================

                    // Configure ClientAPI Options
                    services.Configure<Migration.Infrastructure.Implementation.ClientApiOptions>(
                        context.Configuration.GetSection(Migration.Infrastructure.Implementation.ClientApiOptions.SectionName));

                    // ClientAPI HttpClient with Polly policies
                    services.AddHttpClient<IClientApi, Migration.Infrastructure.Implementation.ClientApi>(cli =>
                    {
                        cli.Timeout = TimeSpan.FromSeconds(
                            context.Configuration.GetValue<int>("ClientApi:TimeoutSeconds", 30));
                    })
                        .ConfigureHttpClient((sp, cli) =>
                        {
                            var options = sp.GetRequiredService<IOptions<Migration.Infrastructure.Implementation.ClientApiOptions>>().Value;
                            cli.BaseAddress = new Uri(options.BaseUrl);
                            cli.DefaultRequestHeaders.Accept.Add(
                                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                            // Add API key header if configured
                            if (!string.IsNullOrEmpty(options.ApiKey))
                            {
                                cli.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                            }
                        })
                        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
                        {
                            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                            MaxConnectionsPerServer = 50
                        })
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .AddPolicyHandler(GetRetryPlicy())
                        .AddPolicyHandler(GetCircuitBreakerPolicy());

                    // TODO: Uncomment when DUT API becomes available
                    /*
                    // Configure DUT API Options
                    services.Configure<DutApiOptions>(
                        context.Configuration.GetSection(DutApiOptions.SectionName));

                    // DUT API HttpClient with Polly policies
                    services.AddHttpClient<IDutApi, DutApi>(cli =>
                    {
                        cli.Timeout = TimeSpan.FromSeconds(
                            context.Configuration.GetValue<int>("DutApi:TimeoutSeconds", 30));
                    })
                        .ConfigureHttpClient((sp, cli) =>
                        {
                            var options = sp.GetRequiredService<IOptions<DutApiOptions>>().Value;
                            cli.BaseAddress = new Uri(options.BaseUrl);
                            cli.DefaultRequestHeaders.Accept.Add(
                                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

                            // Add API key header if configured
                            if (!string.IsNullOrEmpty(options.ApiKey))
                            {
                                cli.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
                            }
                        })
                        .ConfigurePrimaryHttpMessageHandler(() => new System.Net.Http.SocketsHttpHandler
                        {
                            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
                            MaxConnectionsPerServer = 50
                        })
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .AddPolicyHandler(GetRetryPlicy())
                        .AddPolicyHandler(GetCircuitBreakerPolicy());

                    // Migration Services (uncomment when DUT API is available)
                    services.AddScoped<IClientEnrichmentService, ClientEnrichmentService>();
                    services.AddScoped<IDocumentTypeTransformationService, DocumentTypeTransformationService>();
                    services.AddScoped<IUniqueFolderIdentifierService, UniqueFolderIdentifierService>();
                    */

                    // Health checks for external APIs (optional)
                    // services.AddHealthChecks()
                    //     .AddUrlGroup(
                    //         uri: new Uri(context.Configuration["ClientApi:BaseUrl"]!),
                    //         name: "clientapi",
                    //         failureStatus: HealthStatus.Degraded,
                    //         tags: new[] {"api", "external"})
                    //     .AddUrlGroup(
                    //         uri: new Uri(context.Configuration["DutApi:BaseUrl"]!),
                    //         name: "dutapi",
                    //         failureStatus: HealthStatus.Degraded,
                    //         tags: new[] {"api", "external"});

                    // =====================================================================================
                    // END OF EXTERNAL API CONFIGURATION
                    // =====================================================================================

                    // OracleConnection and OracleTransaction lifecycle is managed by OracleUnitOfWork (Scoped)
                    // No need to register them separately in DI - they are created lazily on BeginAsync()

                    services.AddTransient<SqlServer.Abstraction.Interfaces.IDocStagingRepository, SqlServer.Infrastructure.Implementation.DocStagingRepository>();
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IFolderStagingRepository, SqlServer.Infrastructure.Implementation.FolderStagingRepository>();
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IMigrationCheckpointRepository, SqlServer.Infrastructure.Implementation.MigrationCheckpointRepository>();

                    // Phase-based checkpoint repository (NEW - for refactoring)
                    services.AddTransient<SqlServer.Abstraction.Interfaces.IPhaseCheckpointRepository, SqlServer.Infrastructure.Implementation.PhaseCheckpointRepository>();

                    // Memory Cache for DocumentMappingRepository (caching individual query results)
                    services.AddMemoryCache();

                    // DocumentMappings - Database-driven mapping service (replaces HeimdallDocumentMapper)
                    services.AddScoped<SqlServer.Abstraction.Interfaces.IDocumentMappingRepository, SqlServer.Infrastructure.Implementation.DocumentMappingRepository>();
                    services.AddScoped<Migration.Abstraction.Interfaces.IDocumentMappingService, Migration.Infrastructure.Implementation.DocumentMappingService>();

                    // Mappers that use DocumentMappingService
                    services.AddScoped<Migration.Abstraction.Interfaces.IOpisToTipMapper, Migration.Infrastructure.Implementation.OpisToTipMapperV2>();
                    services.AddScoped<Migration.Infrastructure.Implementation.DocumentStatusDetectorV2>();

                    #region oracle DI (commented)
                    //services.AddTransient<IDocStagingRepository, DocStagingRepository>();
                    //services.AddTransient<IFolderStagingRepository, FolderStagingRepository>();
                    //services.AddTransient<IMigrationCheckpointRepository, MigrationCheckpointRepository>(); 
                    #endregion


                    services.Configure<MigrationOptions>(context.Configuration.GetSection("Migration"));
                   // services.Configure<WorkerSetting>(context.Configuration.GetSection("WorkerSetting"));

                    //services.AddScoped<IUnitOfWork>(sp => new OracleUnitOfWork(sp.GetRequiredService<OracleOptions>().ConnectionString));
                    services.AddScoped<IUnitOfWork>(sp => new SqlServerUnitOfWork(sp.GetRequiredService<Alfresco.Contracts.SqlServer.SqlServerOptions>().ConnectionString));

                    services.AddTransient<IFolderReader, FolderReader>();
                    services.AddTransient<IFolderIngestor,FolderIngestor>();
                    services.AddScoped<IFolderDiscoveryService, FolderDiscoveryService>();

                    // Folder path and management services
                    services.AddSingleton<IFolderPathService, Migration.Infrastructure.Implementation.FolderPathService>();
                    services.AddSingleton<IFolderManager, Migration.Infrastructure.Implementation.FolderManager>();

                    services.AddTransient<IDocumentReader, DocumentReader>();
                    services.AddTransient<IDocumentResolver, DocumentResolver>();
                    services.AddTransient<IDocumentIngestor, DocumentIngestor>();
                    services.AddScoped<IDocumentDiscoveryService, DocumentDiscoveryService>();

                    
                    
                    services.AddTransient<IMoveReader, MoveReader>();
                    services.AddTransient<IMoveExecutor, MoveExecutor>();
                    services.AddScoped<IMoveService, MoveService>();

                    // NEW: FolderPreparationService (FAZA 3 - parallel folder creation)
                    services.AddSingleton<IFolderPreparationService, FolderPreparationService>();

                    // NEW: MigrationWorker (orchestrator za sve 4 faze)
                    services.AddSingleton<IMigrationWorker, MigrationWorker>();

                    services.AddSingleton<IAlfrescoDbReader, AlfrescoDbReader>();

                    //




                    if (context.Configuration.GetValue<bool>("EnableFolderWorker"))
                    {
                        //services.AddHostedService<FolderDiscoveryWorker>();
                        services.AddSingleton<IWorkerController, FolderDiscoveryWorker>();
                        services.AddHostedService(sp => (FolderDiscoveryWorker)sp.GetServices<IWorkerController>().First(o => o is FolderDiscoveryWorker));

                    }
                    if (context.Configuration.GetValue<bool>("EnableDocumentWorker"))
                    {
                        //services.AddHostedService<DocumentDiscoveryWorker>();
                        services.AddSingleton<IWorkerController, DocumentDiscoveryWorker>();
                        services.AddHostedService(sp => (DocumentDiscoveryWorker)sp.GetServices<IWorkerController>().First(o => o is DocumentDiscoveryWorker));
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
                    logging.AddLog4Net("log4net.config");
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

    }

}
