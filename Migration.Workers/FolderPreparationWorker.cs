using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Workers.Enum;
using Migration.Workers.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using static Migration.Workers.Enum.WorkerEnums;

namespace Migration.Workers
{
    public class FolderPreparationWorker : BackgroundService, IWorkerController, INotifyPropertyChanged
    {
        private readonly ILogger _dbLogger;
        private readonly ILogger _fileLogger;
        private readonly ILogger _uiLogger;
        private readonly IServiceProvider _sp;
        private CancellationTokenSource _cts = new();
        private readonly object _lockObj = new();

        public string Key => "folderPreparation";

        public string DisplayName => "Folder Preparation worker";

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

        public FolderPreparationWorker(ILoggerFactory logger, IServiceProvider sp)
        {
            _dbLogger = logger.CreateLogger("DbLogger");
            _fileLogger = logger.CreateLogger("FileLogger");
            _uiLogger = logger.CreateLogger("UiLogger");
            _sp = sp;
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

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var workerId = $"W-FolderPreparationWorker";

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
                        _fileLogger.LogInformation("Worker started {time}!", DateTime.Now);
                        _dbLogger.LogInformation("Worker started {time}!", DateTime.Now);

                        using var scope = _sp.CreateScope();
                        var svc = scope.ServiceProvider.GetRequiredService<IFolderPreparationService>();
                        _fileLogger.LogInformation("Starting PrepareAllFoldersAsync ....");
                        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, _cts.Token);

                        // Get total count first
                        var totalCount = await svc.GetTotalFolderCountAsync(linkedCts.Token).ConfigureAwait(false);
                        TotalItems = totalCount;

                        // Start the folder preparation task
                        var preparationTask = svc.PrepareAllFoldersAsync(linkedCts.Token);

                        // Poll for progress while the task is running
                        while (!preparationTask.IsCompleted)
                        {
                            try
                            {
                                var (created, total) = await svc.GetProgressAsync(linkedCts.Token).ConfigureAwait(false);
                                TotalItems = total;
                                ProcessedItems = created;

                                await Task.Delay(500, linkedCts.Token).ConfigureAwait(false); // Poll every 500ms
                            }
                            catch (OperationCanceledException)
                            {
                                // Cancellation requested, break the polling loop
                                break;
                            }
                        }

                        // Wait for the preparation task to complete
                        await preparationTask.ConfigureAwait(false);

                        // Get final progress
                        var (finalCreated, finalTotal) = await svc.GetProgressAsync(linkedCts.Token).ConfigureAwait(false);
                        TotalItems = finalTotal;
                        ProcessedItems = finalCreated;

                        _fileLogger.LogInformation("Worker finished {time}!", DateTime.Now);

                        // Folder preparation completes in one run, so stop the worker
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
