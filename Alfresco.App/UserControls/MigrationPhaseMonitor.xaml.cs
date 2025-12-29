using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces.Wrappers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Alfresco.Abstraction.Interfaces;
using Migration.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;

namespace Alfresco.App.UserControls
{
    public partial class MigrationPhaseMonitor : UserControl, INotifyPropertyChanged
    {
        private readonly IMigrationWorker _migrationWorker;
        private readonly IDocumentSearchService? _documentSearchService;
        private readonly MigrationOptions _migrationOptions;
        private readonly DispatcherTimer _updateTimer;
        private CancellationTokenSource? _migrationCts;

        // Services for connection validation
        private readonly IAlfrescoReadApi _alfrescoService;
        private readonly IClientApi _clientApi;
        private readonly IUnitOfWork _unitOfWork;

        // Track if migration is currently running to avoid unnecessary DB calls
        // Only query migration status while migration is active
        private bool _isMigrationRunning = false;

        #region Properties

        private string _statusMessage = "Ready to start migration";
        public string StatusMessage
        {
            get => _statusMessage;
            set { _statusMessage = value; NotifyPropertyChanged(); }
        }

        private int _overallProgress = 0;
        public int OverallProgress
        {
            get => _overallProgress;
            set { _overallProgress = value; NotifyPropertyChanged(); }
        }

        private string _overallProgressText = "0%";
        public string OverallProgressText
        {
            get => _overallProgressText;
            set { _overallProgressText = value; NotifyPropertyChanged(); }
        }

        private string _elapsedTimeText = "";
        public string ElapsedTimeText
        {
            get => _elapsedTimeText;
            set { _elapsedTimeText = value; NotifyPropertyChanged(); }
        }

        private ObservableCollection<PhaseViewModel> _phases = new();
        public ObservableCollection<PhaseViewModel> Phases
        {
            get => _phases;
            set { _phases = value; NotifyPropertyChanged(); }
        }

        private string _docDescriptions = "";
        public string DocDescriptions
        {
            get => _docDescriptions;
            set { _docDescriptions = value; NotifyPropertyChanged(); }
        }

        #region -IsMigrationByDocument- property
        private bool _IsMigrationByDocument;
        public bool IsMigrationByDocument
        {
            get { return _IsMigrationByDocument; }
            set
            {
                if (_IsMigrationByDocument != value)
                {
                    _IsMigrationByDocument = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        //public bool IsMigrationByDocument => _migrationOptions.MigrationByDocument;

#endregion

        public MigrationPhaseMonitor()
        {
            DataContext = this;
            InitializeComponent();

            _migrationWorker = App.AppHost.Services.GetRequiredService<IMigrationWorker>();
            _migrationOptions = App.AppHost.Services.GetRequiredService<IOptions<MigrationOptions>>().Value;

            // Initialize services for connection validation
            _alfrescoService = App.AppHost.Services.GetRequiredService<IAlfrescoReadApi>();
            _clientApi = App.AppHost.Services.GetRequiredService<IClientApi>();
            _unitOfWork = App.AppHost.Services.GetRequiredService<IUnitOfWork>();

            IsMigrationByDocument = false;
            // Get DocumentSearchService if in MigrationByDocument mode
            if (_migrationOptions.MigrationByDocument)
            {
                IsMigrationByDocument = true;
                _documentSearchService = App.AppHost.Services.GetService<IDocumentSearchService>();

                // Initialize DocDescriptions from appsettings (if configured)
                if (_migrationOptions.DocumentTypeDiscovery.DocTypes.Any())
                {
                    // Note: Now we expect DocDescriptions instead of DocTypes
                    // This can be initialized from settings or left empty for user to select
                    DocDescriptions = string.Join(", ", _migrationOptions.DocumentTypeDiscovery.DocTypes);
                }
            }

            // Initialize phases based on migration mode
            InitializePhases();

            // Setup update timer (refresh every 2 seconds)
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _updateTimer.Tick += UpdateTimer_Tick;
        }

        private void InitializePhases()
        {
            if (_migrationOptions.MigrationByDocument)
            {
                // MigrationByDocument mode: 3 phases
                // Phase 1: DocumentSearch (uses FolderDiscovery enum internally)
                // Phase 2: FolderPreparation
                // Phase 3: Move
                Phases = new ObservableCollection<PhaseViewModel>
                {
                    new PhaseViewModel { PhaseNumber = "1", PhaseName = "Document Search", Phase = MigrationPhase.FolderDiscovery },
                    new PhaseViewModel { PhaseNumber = "2", PhaseName = "Folder Preparation", Phase = MigrationPhase.FolderPreparation },
                    new PhaseViewModel { PhaseNumber = "3", PhaseName = "Document Move", Phase = MigrationPhase.Move }
                };
            }
            else
            {
                // MigrationByFolder mode: 4 phases (default)
                Phases = new ObservableCollection<PhaseViewModel>
                {
                    new PhaseViewModel { PhaseNumber = "1", PhaseName = "Folder Discovery", Phase = MigrationPhase.FolderDiscovery },
                    new PhaseViewModel { PhaseNumber = "2", PhaseName = "Document Discovery", Phase = MigrationPhase.DocumentDiscovery },
                    new PhaseViewModel { PhaseNumber = "3", PhaseName = "Folder Preparation", Phase = MigrationPhase.FolderPreparation },
                    new PhaseViewModel { PhaseNumber = "4", PhaseName = "Document Move", Phase = MigrationPhase.Move }
                };
            }
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            _updateTimer.Start();
            _ = UpdateStatusAsync(); // Initial update
        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _updateTimer.Stop();
            _migrationCts?.Cancel();
        }

        private async void UpdateTimer_Tick(object? sender, EventArgs e)
        {
            await UpdateStatusAsync();
        }

        private async Task UpdateStatusAsync()
        {
            // Only query migration status if migration is actually running
            if (!_isMigrationRunning)
            {
                return;
            }

            try
            {
                var status = await _migrationWorker.GetStatusAsync();

                // Update overall status
                StatusMessage = status.StatusMessage;
                ElapsedTimeText = status.ElapsedTime.HasValue
                    ? $"Elapsed: {status.ElapsedTime.Value:hh\\:mm\\:ss}"
                    : "";

                // Calculate overall progress based on total number of phases
                var totalPhases = Phases.Count;
                var progressPerPhase = 100 / totalPhases; // 33% for 3 phases, 25% for 4 phases
                var completedPhases = Phases.Count(p => p.Status == PhaseStatus.Completed);
                var currentPhaseProgress = status.CurrentPhaseProgress / totalPhases;
                OverallProgress = (completedPhases * progressPerPhase) + currentPhaseProgress;
                OverallProgressText = $"{OverallProgress}%";

                // Update each phase
                foreach (var phaseVm in Phases)
                {
                    if (phaseVm.Phase == status.CurrentPhase)
                    {
                        // Current phase
                        phaseVm.Status = status.CurrentPhaseStatus;
                        phaseVm.Progress = status.CurrentPhaseProgress;
                        phaseVm.Processed = status.TotalProcessed;
                        phaseVm.Total = 0; // Not tracking per-phase total for now
                        phaseVm.ErrorMessage = status.ErrorMessage;
                    }
                    else if ((int)phaseVm.Phase < (int)status.CurrentPhase)
                    {
                        // Previous phases - mark as completed
                        phaseVm.Status = PhaseStatus.Completed;
                        phaseVm.Progress = 100;
                    }
                    else
                    {
                        // Future phases - not started
                        phaseVm.Status = PhaseStatus.NotStarted;
                        phaseVm.Progress = 0;
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Error updating status: {ex.Message}";
            }
        }

        private async Task<(bool isConnected, string errorMessage)> ValidateAllConnectionsAsync()
        {
            var disconnectedServices = new List<string>();

            // Check Alfresco connection
            try
            {
                bool alfrescoConnected = await _alfrescoService.PingAsync();
                if (!alfrescoConnected)
                {
                    disconnectedServices.Add("Alfresco");
                }
            }
            catch
            {
                disconnectedServices.Add("Alfresco");
            }

            // Check SQL Server connection
            try
            {
                await _unitOfWork.BeginAsync(System.Data.IsolationLevel.ReadUncommitted);
                await _unitOfWork.CommitAsync();
            }
            catch
            {
                disconnectedServices.Add("SQL Server");
                try
                {
                    await _unitOfWork.RollbackAsync();
                }
                catch { /* Ignore rollback errors */ }
            }

            // Check Client API connection
            try
            {
                bool clientApiConnected = await _clientApi.ValidateClientExistsAsync("test");
                if (!clientApiConnected)
                {
                    disconnectedServices.Add("Client API");
                }
            }
            catch
            {
                disconnectedServices.Add("Client API");
            }

            // Return result
            if (disconnectedServices.Any())
            {
                var errorMessage = $"Migracija ne može biti pokrenuta!\n\n" +
                                 $"Sledeći servisi nisu povezani:\n" +
                                 string.Join("\n", disconnectedServices.Select(s => $"  • {s}")) +
                                 $"\n\nProverite konekciju i pokušajte ponovo.";
                return (false, errorMessage);
            }

            return (true, string.Empty);
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Validate all service connections before starting migration
                var (isConnected, errorMessage) = await ValidateAllConnectionsAsync();
                if (!isConnected)
                {
                    MessageBox.Show(errorMessage,
                        "Greška - Servisi nisu povezani",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }

                // If MigrationByDocument mode, validate and apply DocDescriptions from UI
                if (_migrationOptions.MigrationByDocument && _documentSearchService != null)
                {
                    if (string.IsNullOrWhiteSpace(DocDescriptions))
                    {
                        MessageBox.Show("Please enter at least one document description (ecm:docDesc) to search for, or click 'Select...' to choose from database.",
                            "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Parse DocDescriptions from UI (comma-separated)
                    var docDescList = DocDescriptions
                        .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(dt => dt.Trim())
                        .Where(dt => !string.IsNullOrWhiteSpace(dt))
                        .ToList();

                    if (!docDescList.Any())
                    {
                        MessageBox.Show("Please enter valid document descriptions separated by comma.",
                            "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    // Apply DocDescriptions override to DocumentSearchService
                    _documentSearchService.SetDocDescriptions(docDescList);
                    StatusMessage = $"DocDescriptions set: {string.Join(", ", docDescList)}";
                }

                // All validations passed - disable start button and enable stop button
                btnStart.IsEnabled = false;
                btnStop.IsEnabled = true;

                _migrationCts = new CancellationTokenSource();

                StatusMessage = "Starting migration pipeline...";

                // Set migration running flag to enable status updates
                _isMigrationRunning = true;

                // Run migration in background
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _migrationWorker.RunAsync(_migrationCts.Token);

                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "Migration completed successfully!";
                            btnStart.IsEnabled = true;
                            btnStop.IsEnabled = false;
                            MessageBox.Show($"Migration of documents with docDesc '{DocDescriptions}' finished!");
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = "Migration cancelled by user";
                            btnStart.IsEnabled = true;
                            btnStop.IsEnabled = false;
                        });
                    }
                    catch (AlfrescoTimeoutException timeoutEx)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Migration stopped - Timeout occurred: {timeoutEx.Operation}";
                            btnStart.IsEnabled = true;
                            btnStop.IsEnabled = false;
                            MessageBox.Show(
                                $"Migracija prekinuta!\n\n" +
                                $"Razlog: TIMEOUT\n" +
                                $"Operacija: {timeoutEx.Operation}\n" +
                                $"Timeout limit: {timeoutEx.TimeoutDuration.TotalSeconds}s\n" +
                                $"Proteklo vreme: {timeoutEx.ElapsedTime.TotalSeconds:F2}s\n\n" +
                                $"Proverite log za više detalja.",
                                "Migracija prekinuta - Timeout",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        });
                    }
                    catch (AlfrescoRetryExhaustedException retryEx)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Migration stopped - All retry attempts exhausted: {retryEx.Operation}";
                            btnStart.IsEnabled = true;
                            btnStop.IsEnabled = false;
                            MessageBox.Show(
                                $"Migracija prekinuta!\n\n" +
                                $"Razlog: Svi retry pokušaji iskorišćeni\n" +
                                $"Operacija: {retryEx.Operation}\n" +
                                $"Broj pokušaja: {retryEx.RetryCount}\n" +
                                $"Poslednja greška: {retryEx.LastException?.Message ?? "N/A"}\n\n" +
                                $"Proverite log za više detalja.",
                                "Migracija prekinuta - Retry Exhausted",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        });
                    }
                    catch (AlfrescoException alfrescoEx)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Migration stopped - Alfresco error occurred";
                            btnStart.IsEnabled = true;
                            btnStop.IsEnabled = false;
                            MessageBox.Show(
                                $"Migracija prekinuta!\n\n" +
                                $"Razlog: Alfresco greška\n" +
                                $"Status Code: {alfrescoEx.StatusCode}\n" +
                                $"Poruka: {alfrescoEx.Message}\n\n" +
                                $"Proverite log za više detalja.",
                                "Migracija prekinuta - Alfresco greška",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        });
                    }
                    catch (ClientApiTimeoutException clientTimeoutEx)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Migration stopped - Client API Timeout occurred: {clientTimeoutEx.Operation}";
                            btnStart.IsEnabled = true;
                            btnStop.IsEnabled = false;
                            MessageBox.Show(
                                $"Migracija prekinuta!\n\n" +
                                $"Razlog: CLIENT API TIMEOUT\n" +
                                $"Operacija: {clientTimeoutEx.Operation}\n" +
                                $"Timeout limit: {clientTimeoutEx.TimeoutDuration.TotalSeconds}s\n" +
                                $"Proteklo vreme: {clientTimeoutEx.ElapsedTime.TotalSeconds:F2}s\n\n" +
                                $"Proverite log za više detalja.",
                                "Migracija prekinuta - Client API Timeout",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        });
                    }
                    catch (ClientApiRetryExhaustedException clientRetryEx)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Migration stopped - Client API retry attempts exhausted: {clientRetryEx.Operation}";
                            btnStart.IsEnabled = true;
                            btnStop.IsEnabled = false;
                            MessageBox.Show(
                                $"Migracija prekinuta!\n\n" +
                                $"Razlog: Client API - Svi retry pokušaji iskorišćeni\n" +
                                $"Operacija: {clientRetryEx.Operation}\n" +
                                $"Broj pokušaja: {clientRetryEx.RetryCount}\n" +
                                $"Poslednja greška: {clientRetryEx.LastException?.Message ?? "N/A"}\n\n" +
                                $"Proverite log za više detalja.",
                                "Migracija prekinuta - Client API Retry Exhausted",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        });
                    }
                    catch (ClientApiException clientEx)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Migration stopped - Client API error occurred";
                            btnStart.IsEnabled = true;
                            btnStop.IsEnabled = false;
                            MessageBox.Show(
                                $"Migracija prekinuta!\n\n" +
                                $"Razlog: Client API greška\n" +
                                $"Status Code: {clientEx.StatusCode}\n" +
                                $"Poruka: {clientEx.Message}\n\n" +
                                $"Proverite log za više detalja.",
                                "Migracija prekinuta - Client API greška",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusMessage = $"Migration failed: {ex.Message}";
                            btnStart.IsEnabled = true;
                            btnStop.IsEnabled = false;
                            MessageBox.Show(
                                $"Migracija prekinuta!\n\n" +
                                $"Razlog: Neočekivana greška\n" +
                                $"Poruka: {ex.Message}\n\n" +
                                $"Proverite log za više detalja.",
                                "Migracija prekinuta",
                                MessageBoxButton.OK,
                                MessageBoxImage.Error);
                        });
                    }
                    finally
                    {
                        // Migration finished (either completed, cancelled, or failed)
                        // Stop querying migration status
                        _isMigrationRunning = false;
                    }
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to start migration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                btnStart.IsEnabled = true;
                btnStop.IsEnabled = false;
                _isMigrationRunning = false; // Reset flag if migration failed to start
            }
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _migrationCts?.Cancel();
                StatusMessage = "Stopping migration...";
                btnStop.IsEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to stop migration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void btnReset_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show(
                    "This will reset all migration phases. Are you sure?",
                    "Confirm Reset",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    _isMigrationRunning = false; // Stop querying status
                    await _migrationWorker.ResetAsync();
                    StatusMessage = "Migration reset to initial state";
                    await UpdateStatusAsync();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to reset migration: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void btnSelectDocuments_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var window = new Windows.DocumentSelectionWindow
                {
                    Owner = Window.GetWindow(this)
                };

                if (window.ShowDialog() == true)
                {
                    // Update the DocDescriptions textbox with selected values
                    DocDescriptions = string.Join(", ", window.SelectedDocDescriptions);
                    StatusMessage = $"Selected {window.SelectedDocDescriptions.Count} document(s) for migration";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open document selection window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }

    /// <summary>
    /// ViewModel for individual phase display
    /// </summary>
    public class PhaseViewModel : INotifyPropertyChanged
    {
        private PhaseStatus _status = PhaseStatus.NotStarted;
        private int _progress = 0;
        private long _processed = 0;
        private long _total = 0;
        private string? _errorMessage;
        private DateTime? _startedAt;
        private DateTime? _completedAt;

        public string PhaseNumber { get; set; } = "";
        public string PhaseName { get; set; } = "";
        public MigrationPhase Phase { get; set; }

        public PhaseStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(StatusText));
                NotifyPropertyChanged(nameof(StatusColor));
            }
        }

        public int Progress
        {
            get => _progress;
            set
            {
                _progress = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(ProgressText));
            }
        }

        public long Processed
        {
            get => _processed;
            set
            {
                _processed = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(ProcessedText));
            }
        }

        public long Total
        {
            get => _total;
            set
            {
                _total = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(ProcessedText));
            }
        }

        public string? ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(ErrorVisibility));
            }
        }

        public DateTime? StartedAt
        {
            get => _startedAt;
            set
            {
                _startedAt = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(StartedAtText));
            }
        }

        public DateTime? CompletedAt
        {
            get => _completedAt;
            set
            {
                _completedAt = value;
                NotifyPropertyChanged();
                NotifyPropertyChanged(nameof(CompletedAtText));
            }
        }

        // Computed properties for UI binding
        public string StatusText => Status switch
        {
            PhaseStatus.NotStarted => "Not Started",
            PhaseStatus.InProgress => "In Progress...",
            PhaseStatus.Completed => "✓ Completed",
            PhaseStatus.Failed => "✗ Failed",
            _ => "Unknown"
        };

        public string StatusColor => Status switch
        {
            PhaseStatus.NotStarted => "#BDC3C7",
            PhaseStatus.InProgress => "#3498DB",
            PhaseStatus.Completed => "#27AE60",
            PhaseStatus.Failed => "#E74C3C",
            _ => "#BDC3C7"
        };

        public string ProgressText => $"{Progress}%";

        public string ProcessedText => Total > 0
            ? $"Processed: {Processed:N0} / {Total:N0}"
            : $"Processed: {Processed:N0}";

        public string StartedAtText => StartedAt.HasValue
            ? $"Started: {StartedAt.Value:HH:mm:ss}"
            : "";

        public string CompletedAtText => CompletedAt.HasValue
            ? $"Completed: {CompletedAt.Value:HH:mm:ss}"
            : "";

        public Visibility ErrorVisibility => string.IsNullOrWhiteSpace(ErrorMessage)
            ? Visibility.Collapsed
            : Visibility.Visible;

        #region INotifyPropertyChanged
        public event PropertyChangedEventHandler? PropertyChanged;
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion
    }
}
