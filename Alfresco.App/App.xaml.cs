using Alfresco.App.UserControls;
using Alfresco.Apstraction.Interfaces;
using Alfresco.Apstraction.Models;
using Alfresco.Client;
using Alfresco.Client.Handlers;
using Alfresco.Client.Helpers;
using Alfresco.Client.Implementation;
using Alfresco.Contracts.Oracle;
using Dapper;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Oracle.Apstaction.Interfaces;
using Oracle.Infractructure.Helpers;
using Oracle.Infractructure.Implementation;
using Oracle.ManagedDataAccess.Client;
using Polly;
using Polly.Extensions.Http;
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
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .AddPolicyHandler(GetRetryPlicy())
                        .AddPolicyHandler(GetCircuitBreakerPolicy());

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
                        .SetHandlerLifetime(TimeSpan.FromMinutes(5))
                        .AddPolicyHandler(GetRetryPlicy())
                        .AddPolicyHandler(GetCircuitBreakerPolicy());

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

                    services.AddTransient<MainWindow>();

                }).Build();


            //Dapper.DefaultTypeMap.MatchNamesWithUnderscores = true;
            //OracleHelpers.RegisterFrom<DocStaging>();
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            //AppHost = Host.CreateDefaultBuilder()
            // .ConfigureAppConfiguration(config =>
            // {
            //     config.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);
            // })
            // .ConfigureServices((c, services) =>
            // {
            //     var section = c.Configuration.GetSection("Alfresco");
            //     services.AddAlfrescoClient(opts =>
            //     {
            //         opts.BaseUrl = section["BaseUrl"] ?? "";
            //         opts.Username = section["Username"] ?? "";
            //         opts.Password = section["Password"] ?? "";
            //     });

            //     services.AddTransient<MainWindow>();
            //     //DIs
            //     services.AddTransient<StatusBarUC>();
            //     services.AddTransient<Main>();

            // })
            // .Build();

            //AppHost = Host.CreateDefaultBuilder()
            //    .ConfigureServices((context, services) =>
            //    {
            //        services.Configure<AlfrescoOptions>(context.Configuration.GetSection("Alfresco"));

            //        services.AddTransient<BasicAuthHandler>();

            //        services.AddHttpClient<IAlfrescoApi, AlfrescoAPI>(cli =>
            //        {
            //            cli.Timeout = TimeSpan.FromSeconds(30);
            //        })
            //            .ConfigureHttpClient((sp, cli) =>
            //            {
            //                var options = sp.GetRequiredService<IOptions<AlfrescoOptions>>().Value;
            //                cli.BaseAddress = new Uri(options.BaseUrl);
            //                cli.DefaultRequestHeaders.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
            //            })

            //            .AddHttpMessageHandler<BasicAuthHandler>()
            //            .SetHandlerLifetime(TimeSpan.FromMinutes(5))
            //            .AddPolicyHandler(GetRetryPlicy())
            //            .AddPolicyHandler(GetCircuitBreakerPolicy());

            //        services.Configure<OracleOptions>(context.Configuration.GetSection("Oracle"));

            //        services.AddSingleton(sp => sp.GetRequiredService<IOptions<OracleOptions>>().Value);

            //        services.AddTransient<OracleConnection>((sp) =>
            //        {
            //            var options = sp.GetRequiredService<OracleOptions>();
            //            var conn = new OracleConnection(options.ConnectionString);
            //            conn.Open();
            //            return conn;
            //        });

            //        services.AddTransient<OracleTransaction>(sp =>
            //        {
            //            var conn = sp.GetRequiredService<OracleConnection>();
            //            return conn.BeginTransaction();  // IsolationLevel.ReadCommitted po defaultu
            //        });

            //        services.AddTransient<IDocStagingRepository, DocStagingRepository>();

            //        services.AddTransient<MainWindow>();

            //    }).Build();

           

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
