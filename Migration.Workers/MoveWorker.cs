using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Migration.Apstraction.Interfaces.Services;
using Migration.Apstraction.Interfaces.Wrappers;
using Migration.Workers.Enum;
using Migration.Workers.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using log4net;
using static Migration.Workers.Enum.WorkerEnums;

namespace Migration.Workers
{
    public class MoveWorker : BackgroundService, IWorkerController, INotifyPropertyChanged
    {
       // private readonly ILogger<MoveWorker> _fileLogger;
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly IServiceProvider _sp;
        private CancellationTokenSource _cts = new(); 
        private readonly object _lockObj = new();       
        public string Key => "move";
        public string DisplayName => "Move worker";

        #region -State- property
        private volatile WorkerState _State = WorkerState.Idle;
        public  WorkerState State
        {
            get { return _State; }
            set
            {
                if (_State != value)
                {
                    _State = value;
                    NotifyPropertyChanged(nameof(State));
                }
            }
        }
        #endregion

        #region -IsEnabled- property
        private volatile bool _IsEnabled = false;
        public bool IsEnabled
        {
            get { return _IsEnabled; }
            set
            {
                if (_IsEnabled != value)
                {
                    _IsEnabled = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        #region -LastStarted- property
        private DateTimeOffset? _LastStarted;
        public DateTimeOffset? LastStarted
        {
            get { return _LastStarted; }
            set
            {
                if (_LastStarted != value)
                {
                    _LastStarted = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        #region -LastStopped- property
        private DateTimeOffset? _LastStopped;
        public DateTimeOffset? LastStopped
        {
            get { return _LastStopped; }
            set
            {
                if (_LastStopped != value)
                {
                    _LastStopped = value;
                    NotifyPropertyChanged();
                }
            }
        }
#endregion

        //public DateTimeOffset? LastStarted { get; private set; }
        //public DateTimeOffset? LastStopped { get; private set; }

        public Exception? LastError { get; private set; }

        public MoveWorker(ILoggerFactory logger, IServiceProvider sp)
        {
            //_fileLogger = logger;
            _dbLogger = logger.CreateLogger("DbLogger");
            _fileLogger = logger.CreateLogger("FileLogger");
            _sp = sp;
        }

        public void StartService()
        {
            lock (_lockObj)
            {
                if (State == WorkerState.Running) return;

                _fileLogger.LogInformation($"Worker {Key} started");
                _dbLogger.LogInformation($"Worker {Key} started");
                _cts = new CancellationTokenSource();
                IsEnabled = true;
                State = WorkerState.Running;
                LastStarted = DateTimeOffset.UtcNow;
                LastError = null;
                
            }
        }
        public void StopService()
        {
            lock (_lockObj)
            {
                if (State is WorkerState.Idle or WorkerState.Stopped) return;
                _fileLogger.LogInformation($"Worker {Key} stoped");
                _cts.Cancel();
                IsEnabled = false;
                State = WorkerState.Stopped;
                //LastStopped = DateTimeOffset.UtcNow;
                //LastError = null;

            }
        }      

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var workerId = $"W-MoveWorker";

            var r = RuntimeHelpers.GetHashCode(this);

            using (_dbLogger.BeginScope(new Dictionary<string, object> { ["WorkerId"] = workerId }))
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!IsEnabled || State != WorkerState.Running)
                    {
                        await Task.Delay(1000,stoppingToken);
                        continue;
                    }

                    try
                    {
                        _fileLogger.LogInformation("Worker starter {time}!", DateTime.Now);
                        using var scope = _sp.CreateScope();
                        var svc = scope.ServiceProvider.GetRequiredService<IMoveService>();
                        _fileLogger.LogInformation("Starting RunLoopAsync ....");
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cts.Token);
                        await svc.RunLoopAsync(linkedCts.Token);
                        _fileLogger.LogInformation("Worker finised {time}!", DateTime.Now);
                    }
                    catch (OperationCanceledException)
                    {
                        lock (_lockObj)
                        {
                            // cancel iz StopManually ili host shutdown
                            LastStopped = DateTimeOffset.Now;
                            LastError = null;
                            State = WorkerState.Idle;
                            IsEnabled = false;
                        }
                    }
                    catch (Exception ex)
                    {
                        _fileLogger.LogError("Exception!!! Check db  for details");
                        _dbLogger.LogError(ex, "Worker crached!!");
                        lock (_lockObj)
                        {
                            LastStopped = DateTimeOffset.Now;
                            LastError = ex;
                            State = WorkerState.Failed;
                            IsEnabled = false;
                        }
                    } 
                }
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            LogManager.Shutdown();
             await base.StopAsync(cancellationToken);
        }

        #region INotifyPropertyChange implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        // This method is called by the Set accessor of each property.
        // The CallerMemberName attribute that is applied to the optional propertyName
        // parameter causes the property name of the caller to be substituted as an argument.
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

}
