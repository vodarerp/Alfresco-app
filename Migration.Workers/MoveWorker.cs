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
        private readonly ILogger<MoveWorker> _logger;
        private readonly IServiceProvider _sp;

        public MoveWorker(ILogger<MoveWorker> logger, IServiceProvider sp)
        {
            _logger = logger;
            _sp = sp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var workerId = $"W-MoveWorker";

            using (_logger.BeginScope(new Dictionary<string, object> { ["WorkerId"] = workerId }))
            {
                try
                {
                    _logger.LogInformation("Worker starter {time}!", DateTime.Now);
                    using var scope = _sp.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<IMoveService>();
                    _logger.LogInformation("Starting RunLoopAsync ....");
                    await svc.RunLoopAsync(stoppingToken);
                    _logger.LogInformation("Worker finised {time}!", DateTime.Now);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Worker crashed!! {errMsg}!", ex.Message);
                }
            }
        }
    }
}
