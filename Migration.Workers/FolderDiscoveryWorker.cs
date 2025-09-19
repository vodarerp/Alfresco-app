using Microsoft.Extensions.DependencyInjection;
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
        private readonly IServiceProvider _sp;

        public FolderDiscoveryWorker(IFolderDiscoveryService svc, ILogger<FolderDiscoveryWorker> logger, IServiceProvider sp)
        {
            _svc = svc;
            _logger = logger;
            _sp = sp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {

            try
            {
                _logger.LogInformation("Worker starter {time}!", DateTime.Now);
                using var scope = _sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IFolderDiscoveryService>();
                _logger.LogInformation("Starting RunLoopAsync ....");
                await svc.RunLoopAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError("Worker crashed!! {errMsg}!",ex.Message);
            }

            
        }
    }
}
