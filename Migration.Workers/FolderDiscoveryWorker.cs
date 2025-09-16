using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Migration.Apstaction.Interfaces.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Workers
{
    public class FolderDiscoveryWorker : BackgroundService
    {

        private readonly IFolderDiscoveryService _svc;
        private readonly ILogger<FolderDiscoveryWorker> _logger;

        public FolderDiscoveryWorker(IFolderDiscoveryService svc, ILogger<FolderDiscoveryWorker> logger)
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
