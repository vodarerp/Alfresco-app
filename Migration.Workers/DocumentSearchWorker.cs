using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Workers.Enum;
using Migration.Workers.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using static Migration.Workers.Enum.WorkerEnums;

namespace Migration.Workers
{
    public class DocumentSearchWorker : BackgroundService, IWorkerController, INotifyPropertyChanged
    {
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;
        private readonly IServiceProvider _sp;
        private CancellationTokenSource _cts = new();
        private bool _isStarted = false;
        private readonly object _lockObj = new();

        public string Key => "documentSearch";

        public string DisplayName => "Document Search worker";

        #region -State- property
        private volatile WorkerState _State = WorkerState.Idle;
        public WorkerState State
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


        public Exception? LastError { get; private set; }

        #region Progress Tracking
        #region -TotalItems- property
        private long _TotalItems;
        public long TotalItems
        {
            get { return _TotalItems; }
            set
            {
                if (_TotalItems != value)
                {
                    _TotalItems = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(RemainingItems));
                    NotifyPropertyChanged(nameof(ProgressPercentage));
                }
            }
        }
        #endregion

        #region -ProcessedItems- property
        private long _ProcessedItems;
        public long ProcessedItems
        {
            get { return _ProcessedItems; }
            set
            {
                if (_ProcessedItems != value)
                {
                    _ProcessedItems = value;
                    NotifyPropertyChanged();
                    NotifyPropertyChanged(nameof(RemainingItems));
                    NotifyPropertyChanged(nameof(ProgressPercentage));
                }
            }
        }
        #endregion

        public long RemainingItems => Math.Max(0, TotalItems - ProcessedItems);

        public double ProgressPercentage => TotalItems > 0 ? (ProcessedItems * 100.0 / TotalItems) : 0.0;
        #endregion

        public DocumentSearchWorker(ILoggerFactory logger, IServiceProvider sp)
        {
            _dbLogger = logger.CreateLogger("DbLogger");
            _fileLogger = logger.CreateLogger("FileLogger");
            _uiLogger = logger.CreateLogger("UiLogger");
            _sp = sp;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var workerId = $"W-DocumentSearchWorker";

            using (_dbLogger.BeginScope(new Dictionary<string, object> { ["WorkerId"] = workerId }))
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    if (!IsEnabled || State != WorkerState.Running)
                    {
                        await Task.Delay(1000, stoppingToken).ConfigureAwait(false);
                        continue;
                    }
                    try
                    {
                        _fileLogger.LogInformation("Worker starter {time}!", DateTime.Now);
                        await using var scope = _sp.CreateAsyncScope();
                        var svc = scope.ServiceProvider.GetRequiredService<IDocumentSearchService>();
                        _fileLogger.LogInformation("Starting RunLoopAsync....");
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cts.Token);

                        // Progress callback to update UI
                        var completedSuccessfully = await svc.RunLoopAsync(linkedCts.Token, progress =>
                        {
                            TotalItems = progress.TotalItems;
                            ProcessedItems = progress.ProcessedItems;
                        }).ConfigureAwait(false);

                        _fileLogger.LogInformation("Worker end {time}!", DateTime.Now);

                        // If work completed successfully (no more items), stop the worker
                        if (completedSuccessfully)
                        {
                            lock (_lockObj)
                            {
                                LastStopped = DateTimeOffset.Now;
                                LastError = null;
                                State = WorkerState.Idle;
                                IsEnabled = false;
                            }

                            _fileLogger.LogInformation("Worker {Key} completed all work and stopped automatically", Key);
                            _dbLogger.LogInformation("Worker {Key} completed all work and stopped automatically", Key);
                            _uiLogger.LogInformation("Worker {Key} completed successfully", Key);
                        }

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
                        _fileLogger.LogError("Exception!!! Check db for details");
                        _dbLogger.LogError(ex, "Worker crashed!!");
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

        public void StartService()
        {
            bool shouldStart = false;

            lock (_lockObj)
            {
                if (State == WorkerState.Running) return;

                shouldStart = true;
                _cts = new CancellationTokenSource();
                IsEnabled = true;
                State = WorkerState.Running;
                LastStarted = DateTimeOffset.UtcNow;
                LastError = null;
            }

            // Log AFTER releasing lock to avoid deadlock with UI thread
            if (shouldStart)
            {
                _fileLogger.LogInformation($"Worker {Key} started");
                _dbLogger.LogInformation($"Worker {Key} started");
                _uiLogger.LogInformation($"Worker {Key} started");
            }
        }

        public void StopService()
        {
            bool shouldStop = false;

            lock (_lockObj)
            {
                if (State is WorkerState.Idle or WorkerState.Stopped) return;

                shouldStop = true;
                State = WorkerState.Stopping;  // Show "Stopping..." immediately
                _cts.Cancel();
                IsEnabled = false;
            }

            // Log AFTER releasing lock to avoid deadlock with UI thread
            if (shouldStop)
            {
                _fileLogger.LogInformation($"Worker {Key} stopping...");
                _uiLogger.LogInformation($"Worker {Key} stopping...");
            }
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
