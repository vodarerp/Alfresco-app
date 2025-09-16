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

        public DocumentDiscoveryWorker(IDocumentDiscoveryService svc, ILogger<FolderDiscoveryWorker> logger)
        {
            _svc = svc;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await _svc.RunLoopAsync(stoppingToken);
            }
            catch (Exception ex)
            {

            }
        }
    }
}
