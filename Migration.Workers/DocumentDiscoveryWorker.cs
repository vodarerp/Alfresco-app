using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Migration.Apstaction.Interfaces.Services;
using Migration.Apstaction.Interfaces.Wrappers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Workers
{
    public class DocumentDiscoveryWorker : BackgroundService
    {
        private readonly IDocumentDiscoveryService _svc;
        private readonly ILogger<FolderDiscoveryWorker> _logger;
        private readonly IServiceProvider _sp;


        public DocumentDiscoveryWorker(IDocumentDiscoveryService svc, ILogger<FolderDiscoveryWorker> logger, IServiceProvider sp)
        {
            _svc = svc;
            _logger = logger;
            _sp = sp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await using var scope = _sp.CreateAsyncScope();
                var svc = scope.ServiceProvider.GetRequiredService<IDocumentDiscoveryService>();
                await svc.RunLoopAsync(stoppingToken);
            }
            catch (Exception ex)
            {

            }
        }
    }
}
