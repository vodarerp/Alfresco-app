using Alfresco.App.Helpers;
using Alfresco.App.UserControls;
using Alfresco.Apstraction.Interfaces;
using Alfresco.Apstraction.Models;
using Alfresco.Client;
using Alfresco.Client.Handlers;
using Alfresco.Client.Helpers;
using Alfresco.Client.Implementation;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Interfaces.Services;
using Migration.Apstaction.Interfaces.Wrappers;
using Migration.Infrastructure.Implementation.Document;
using Migration.Infrastructure.Implementation.Folder;
using Migration.Infrastructure.Implementation.Move;
using Migration.Infrastructure.Implementation.Services;
using Migration.Workers;
using Migration.Workers.Interfaces;
using Oracle.Apstaction.Interfaces;
using Oracle.Infractructure.Helpers;
using Oracle.Infractructure.Implementation;
using Oracle.ManagedDataAccess.Client;
using Polly;
using Polly.Extensions.Http;
using System.Configuration;
using System.Windows;
using static Alfresco.App.Helpers.PolicyHelpers;

namespace Alfresco.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IHost AppHost { get; private set; } = null!;

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
                        cli.Timeout = TimeSpan.FromSeconds(30);
                    })
                        .ConfigureHttpClient((sp, cli) =>
                        {
                            var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
                            cli.BaseAddress = new Uri(options.BaseUrl);
                            cli.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                        })

                        .AddHttpMessageHandler<BasicAuthHandler>()
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5));
                    //.AddPolicyHandler(GetRetryPlicy())
                    //.AddPolicyHandler(GetCircuitBreakerPolicy());

                    services.AddHttpClient<IAlfrescoWriteApi, AlfrescoWriteApi>(cli =>
                    {
                        cli.Timeout = TimeSpan.FromSeconds(30);
                    })
                        .ConfigureHttpClient((sp, cli) =>
                        {
                            var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
                            cli.BaseAddress = new Uri(options.BaseUrl);
                            cli.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
                        })

                        .AddHttpMessageHandler<BasicAuthHandler>()
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5));
                        //.AddPolicyHandler(GetRetryPlicy())
                        //.AddPolicyHandler(GetCircuitBreakerPolicy());

                    services.Configure<OracleOptions>(context.Configuration.GetSection("Oracle"));

                    services.AddSingleton(sp => sp.GetRequiredService<IOptions<OracleOptions>>().Value);

                    services.AddTransient<OracleConnection>((sp) =>
                    {
                        var options = sp.GetRequiredService<OracleOptions>();
                        var conn = new OracleConnection(options.ConnectionString);
                        conn.Open();
                        return conn;
                    });

                    services.AddTransient<OracleTransaction>(sp =>
                    {
                        var conn = sp.GetRequiredService<OracleConnection>();
                        return conn.BeginTransaction();  // IsolationLevel.ReadCommitted po defaultu
                    });

                    services.AddTransient<IDocStagingRepository, DocStagingRepository>();
                    services.AddTransient<IFolderStagingRepository, FolderStagingRepository>();


                    services.Configure<MigrationOptions>(context.Configuration.GetSection("Migration"));
                   // services.Configure<WorkerSetting>(context.Configuration.GetSection("WorkerSetting"));

                    services.AddScoped<IUnitOfWork>(sp => new OracleUnitOfWork(sp.GetRequiredService<OracleOptions>().ConnectionString));

                    services.AddTransient<IFolderReader, FolderReader>();
                    services.AddTransient<IFolderIngestor,FolderIngestor>();
                    services.AddSingleton<IFolderDiscoveryService, FolderDiscoveryService>();


                    services.AddTransient<IDocumentReader, DocumentReader>();
                    services.AddTransient<IDocumentResolver, DocumentResolver>();
                    services.AddTransient<IDocumentIngestor, DocumentIngestor>();
                    services.AddSingleton<IDocumentDiscoveryService, DocumentDiscoveryService>();

                    
                    
                    services.AddTransient<IMoveReader, MoveReader>();
                    services.AddTransient<IMoveExecutor, MoveExecutor>();
                    services.AddSingleton<IMoveService, MoveService>();

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
                    
                    


                    services.AddTransient<MainWindow>();

                })
                .ConfigureLogging((context,logging) =>
                {
                    logging.ClearProviders();
                    logging.AddLog4Net("log4net.config");
                    logging.AddFilter("System.Net.Http.HttpClient", LogLevel.Warning);
                    logging.AddFilter("Microsoft", LogLevel.Warning);
                    logging.AddFilter("System", LogLevel.Warning);

                })
                .Build();


            //Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
            //OracleHelpers.RegisterFrom<DocStaging>();
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
