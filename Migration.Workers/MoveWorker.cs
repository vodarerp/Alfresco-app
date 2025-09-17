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
    public class MoveWorker : BackgroundService
    {
        private readonly ILogger<FolderDiscoveryWorker> _logger;
        private readonly IServiceProvider _sp;

        public MoveWorker(ILogger<FolderDiscoveryWorker> logger, IServiceProvider sp)
        {
            _logger = logger;
            _sp = sp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var svc = scope.ServiceProvider.GetRequiredService<IMoveService>();
                await svc.RunLoopAsync(stoppingToken);
            }
            catch (Exception ex)
            {

            }
        }
    }
}
