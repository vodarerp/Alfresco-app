using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Options;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces.Wrappers;
using Migration.Abstraction.Models;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Alfresco.App.UserControls
{
    public partial class PreviewMigrationUC : UserControl, INotifyPropertyChanged
    {
        private readonly IPreviewLoadService _previewLoadService;
        private readonly IPreviewFolderPreparationService _folderPreparationService;
        private readonly IPreviewFolderCreationService _folderCreationService;
        private readonly IPreviewToStagingTransferService _transferService;
        private readonly IPreviewExportService _exportService;
        private readonly IPreviewFolderRollbackService _rollbackService;
        private readonly IMoveService _moveService;
        private readonly IAlfrescoHealthChecker _healthChecker;
        private readonly IOptions<PollyPolicyOptions> _pollyOptions;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _ctsFaza2;
        private CancellationTokenSource? _ctsFaza3;
        private CancellationTokenSource? _ctsTransfer;
        private CancellationTokenSource? _ctsRollback;
        private CancellationTokenSource? _ctsMigration;
        private long _docReadyCount = 0;

        // Pagination state
        private int _currentPage = 1;
        private int _pageSize = 25;
        private int _totalPages = 1;
        private int _totalRecords = 0;

        private ObservableCollection<PreviewDocStaging> _previewDocs = new();
        public ObservableCollection<PreviewDocStaging> PreviewDocs
        {
            get => _previewDocs;
            set { _previewDocs = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public PreviewMigrationUC()
        {
            InitializeComponent();
            DataContext = this;

            _previewLoadService = App.AppHost.Services.GetRequiredService<IPreviewLoadService>();
            _folderPreparationService = App.AppHost.Services.GetRequiredService<IPreviewFolderPreparationService>();
            _folderCreationService = App.AppHost.Services.GetRequiredService<IPreviewFolderCreationService>();
            _transferService = App.AppHost.Services.GetRequiredService<IPreviewToStagingTransferService>();
            _exportService = App.AppHost.Services.GetRequiredService<IPreviewExportService>();
            _rollbackService = App.AppHost.Services.GetRequiredService<IPreviewFolderRollbackService>();
            _moveService = App.AppHost.Services.GetRequiredService<IMoveService>();
            _healthChecker = App.AppHost.Services.GetRequiredService<IAlfrescoHealthChecker>();
            _pollyOptions = App.AppHost.Services.GetRequiredService<IOptions<PollyPolicyOptions>>();

            var config = App.AppHost.Services.GetRequiredService<IConfiguration>();
            if (config.GetValue<bool>("EnablePreviewFolderRollback"))
                BtnRollbackFaza3.Visibility = Visibility.Visible;

            Loaded += PreviewMigrationUC_Loaded;
        }

        private async void PreviewMigrationUC_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshStatisticsAsync();
            await LoadDataAsync();
        }

        #region Button Handlers

        private async void BtnStartFaza1_Click(object sender, RoutedEventArgs e)
        {
            // Max auto-recovery attempts before giving up entirely
            const int MaxRecoveryAttempts = 10;
            // How many times to poll Alfresco while waiting for it to come back
            const int HealthPollAttempts = 60;

            int recoveryAttempt = 10;

            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Phase 1: loading...");
                AppendLog("Phase 1 started.");

                _cts = new CancellationTokenSource();

                var folderFilter = CmbFaza1FolderFilter.SelectedItem is ComboBoxItem fi &&
                                   !string.IsNullOrEmpty(fi.Tag?.ToString())
                    ? fi.Tag.ToString()
                    : null;

                AppendLog($"Filter: {(folderFilter ?? "all")}.");

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "Running...");
                        AppendLog(p.Message ?? "");

                        if (p.TotalItems > 0)
                            ProgressBar.Value = Math.Min(100, p.ProgressPercentage);
                    });
                }

                while (true)
                {
                    try
                    {
                        var result = await Task.Run(
                            () => _previewLoadService.RunLoopAsync(_cts.Token, OnProgress, folderFilter),
                            _cts.Token);

                        ProgressBar.Value = 100;
                        var msg = result ? "Phase 1 done." : "Phase 1 done with warnings.";
                        UpdateStatus(msg);
                        AppendLog(msg);
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        throw; // korisnik je prekinuo — ne recovery
                    }
                    catch (Alfresco.Abstraction.Models.AlfrescoTimeoutException ex)
                    {
                        recoveryAttempt++;
                        if (recoveryAttempt > MaxRecoveryAttempts)
                            throw;

                        AppendLog($"Recovery {recoveryAttempt}: connection timeout.");
                        await WaitForAlfrescoRecoveryAsync(HealthPollAttempts, recoveryAttempt);
                    }
                    catch (Alfresco.Abstraction.Models.AlfrescoRetryExhaustedException ex)
                    {
                        recoveryAttempt++;
                        if (recoveryAttempt > MaxRecoveryAttempts)
                            throw;

                        AppendLog($"Recovery {recoveryAttempt}: retries exhausted.");
                        await WaitForAlfrescoRecoveryAsync(HealthPollAttempts, recoveryAttempt);
                    }
                }

                await RefreshStatisticsAsync();
                AppendLog($"Loaded: {TxtTotalCount.Text} docs (PI: {TxtPiCount.Text}, LE: {TxtLeCount.Text}), pending: {TxtPendingCount.Text}.");
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Stopped.");
                AppendLog("Phase 1 stopped.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                AppendLog($"Error: {ex.Message}");
                MessageBox.Show(ex.Message, "Phase 1 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetButtonsRunning(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Ceka da Alfresco postane dostupan pre ponovnog pokretanja Faze 1.
        /// Najpre saceka da se Circuit Breaker resetuje, zatim poluje health endpoint.
        /// </summary>
        private async Task WaitForAlfrescoRecoveryAsync(int healthPollAttempts, int recoveryAttempt)
        {
            // Sacekaj da CB istekne pre nego sto krenemo sa ping-om
            // (posle C-1 fixa CB ce biti singleton — ovo ce biti kljucno)
            var cbBreakSeconds = _pollyOptions.Value.ReadOperations.CircuitBreakerDurationOfBreakSeconds;
            var stabilizationDelay = TimeSpan.FromSeconds(cbBreakSeconds + 5);

            UpdateStatus($"Recovery {recoveryAttempt}: waiting {stabilizationDelay.TotalSeconds}s...");
            AppendLog($"Recovery {recoveryAttempt}: waiting {stabilizationDelay.TotalSeconds}s.");

            await Task.Delay(stabilizationDelay, _cts!.Token);

            var pollInterval = TimeSpan.FromSeconds(30);
            UpdateStatus($"Recovery {recoveryAttempt}: checking server...");
            AppendLog($"Recovery {recoveryAttempt}: checking server.");

            await _healthChecker.WaitUntilAvailableAsync(
                pollInterval,
                healthPollAttempts,
                (attempt, max) =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus($"Recovery {recoveryAttempt}: offline, attempt {attempt}/{max}...");
                        AppendLog($"Recovery {recoveryAttempt}: no response, attempt {attempt}/{max}.");
                    });
                },
                _cts!.Token);

            UpdateStatus($"Recovery {recoveryAttempt}: back online, resuming...");
            AppendLog($"Recovery {recoveryAttempt}: connected, resuming from checkpoint.");
        }

        private void BtnStopFaza1_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            BtnStopFaza1.IsEnabled = false;
            UpdateStatus("Stopping...");
            AppendLog("Phase 1: stopping.");
        }

        private async void BtnStartFaza2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Phase 2: running...");
                AppendLog("Phase 2 started.");

                _ctsFaza2 = new CancellationTokenSource();

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "Running...");
                        AppendLog(p.Message ?? "");
                    });
                }

                var result = await Task.Run(
                    () => _folderPreparationService.RunAsync(_ctsFaza2.Token, OnProgress),
                    _ctsFaza2.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Phase 2 done." : "Phase 2 done with warnings.";
                UpdateStatus(msg);

                await RefreshStatisticsAsync();
                AppendLog($"{msg} Processed: {TxtFaza2FolderProcessed.Text}, pending: {TxtFaza2FolderPending.Text}, errors: {TxtFaza2Errors.Text}.");
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Stopped.");
                AppendLog("Phase 2 stopped.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                AppendLog($"Error: {ex.Message}");
                MessageBox.Show(ex.Message, "Phase 2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetButtonsRunning(false);
                _ctsFaza2?.Dispose();
                _ctsFaza2 = null;
            }
        }

        private void BtnStopFaza2_Click(object sender, RoutedEventArgs e)
        {
            _ctsFaza2?.Cancel();
            BtnStopFaza2.IsEnabled = false;
            UpdateStatus("Stopping...");
            AppendLog("Phase 2: stopping.");
        }

        private async void BtnStartFaza3_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Phase 3: running...");
                AppendLog("Phase 3 started.");

                _ctsFaza3 = new CancellationTokenSource();

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "Running...");
                        AppendLog(p.Message ?? "");
                    });
                }

                var result = await Task.Run(
                    () => _folderCreationService.RunAsync(_ctsFaza3.Token, OnProgress),
                    _ctsFaza3.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Phase 3 done." : "Phase 3 done with warnings.";
                UpdateStatus(msg);

                await RefreshStatisticsAsync();
                AppendLog($"{msg} Created: {TxtFaza3Created.Text}, errors: {TxtFaza3Errors.Text}, pending: {TxtFaza3Pending.Text}.");
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Stopped.");
                AppendLog("Phase 3 stopped.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                AppendLog($"Error: {ex.Message}");
                MessageBox.Show(ex.Message, "Phase 3 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetButtonsRunning(false);
                _ctsFaza3?.Dispose();
                _ctsFaza3 = null;
            }
        }

        private void BtnStopFaza3_Click(object sender, RoutedEventArgs e)
        {
            _ctsFaza3?.Cancel();
            BtnStopFaza3.IsEnabled = false;
            UpdateStatus("Stopping...");
            AppendLog("Phase 3: stopping.");
        }

        private async void BtnRollbackFaza3_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "This will delete all Alfresco folders created in Phase 3 and reset their status to FOLDER_PENDING_CREATION.\n\nContinue?",
                "Rollback Phase 3", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Rollback: running...");
                AppendLog("Rollback started.");

                _ctsRollback = new CancellationTokenSource();

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "Running...");
                        AppendLog(p.Message ?? "");

                        if (p.TotalItems > 0)
                            ProgressBar.Value = Math.Min(100, p.ProgressPercentage);
                    });
                }

                var result = await Task.Run(
                    () => _rollbackService.RunAsync(_ctsRollback.Token, OnProgress),
                    _ctsRollback.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Rollback done." : "Rollback done with errors.";
                UpdateStatus(msg);

                await RefreshStatisticsAsync();
                AppendLog($"{msg} Pending creation: {TxtFaza3Pending.Text}.");
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Stopped.");
                AppendLog("Rollback stopped.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                AppendLog($"Error: {ex.Message}");
                MessageBox.Show(ex.Message, "Rollback Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetButtonsRunning(false);
                _ctsRollback?.Dispose();
                _ctsRollback = null;
            }
        }

        private async void BtnStartTransfer_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Phase 6: transferring...");
                AppendLog("Phase 6 started.");

                _ctsTransfer = new CancellationTokenSource();

                var dossierType = CmbTransferDossierType.SelectedItem is ComboBoxItem ci &&
                                  ci.Content?.ToString() != "(sve)"
                    ? ci.Content?.ToString()
                    : null;

                var targetDossierType = CmbTargetDossierType.SelectedItem is ComboBoxItem cit &&
                                        !string.IsNullOrEmpty(cit.Tag?.ToString())
                    ? cit.Tag?.ToString()
                    : null;

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "Running...");
                        AppendLog(p.Message ?? "");

                        if (p.TotalItems > 0)
                            ProgressBar.Value = Math.Min(100, p.ProgressPercentage);
                    });
                }

                var result = await Task.Run(
                    () => _transferService.RunAsync(dossierType, targetDossierType, _ctsTransfer.Token, OnProgress),
                    _ctsTransfer.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Transfer done." : "Transfer done with warnings.";
                UpdateStatus(msg);

                await RefreshStatisticsAsync();
                AppendLog($"{msg} Ready for move: {TxtDocReadyCountAction.Text}.");
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Stopped.");
                AppendLog("Transfer stopped.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                AppendLog($"Error: {ex.Message}");
                MessageBox.Show(ex.Message, "Transfer Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetButtonsRunning(false);
                _ctsTransfer?.Dispose();
                _ctsTransfer = null;
            }
        }

        private async void BtnStartMigration_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Phase 8: migrating...");
                AppendLog("Phase 8 started.");

                _ctsMigration = new CancellationTokenSource();

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "Running...");
                        AppendLog(p.Message ?? "");

                        if (p.TotalItems > 0)
                            ProgressBar.Value = Math.Min(100, p.ProgressPercentage);
                    });
                }

                var result = await Task.Run(
                    () => _moveService.RunLoopAsync(_ctsMigration.Token, OnProgress),
                    _ctsMigration.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Phase 8 done." : "Phase 8 done with warnings.";
                UpdateStatus(msg);

                await RefreshStatisticsAsync();
                AppendLog($"{msg} Migrated: {TxtFaza8Migrated.Text}, errors: {TxtFaza8Errors.Text}.");
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Stopped.");
                AppendLog("Phase 8 stopped.");
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                AppendLog($"Error: {ex.Message}");
                MessageBox.Show(ex.Message, "Phase 8 Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetButtonsRunning(false);
                _ctsMigration?.Dispose();
                _ctsMigration = null;
            }
        }

        private void BtnStopMigration_Click(object sender, RoutedEventArgs e)
        {
            _ctsMigration?.Cancel();
            BtnStopMigration.IsEnabled = false;
            UpdateStatus("Stopping...");
            AppendLog("Phase 8: stopping.");
        }

        private void BtnStopTransfer_Click(object sender, RoutedEventArgs e)
        {
            _ctsTransfer?.Cancel();
            BtnStopTransfer.IsEnabled = false;
            UpdateStatus("Stopping...");
            AppendLog("Phase 6: stopping.");
        }

        private async void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Izaberi folder za eksport",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop)
            };

            if (dlg.ShowDialog() != true) return;

            var outputDirectory = dlg.FolderName;

            try
            {
                BtnExport.IsEnabled = false;
                UpdateStatus("Exporting...");
                AppendLog("Export started.");

                var dossierType = CmbTransferDossierType.SelectedItem is ComboBoxItem ci &&
                                  ci.Content?.ToString() != "(sve)"
                    ? ci.Content?.ToString()
                    : null;

                var targetDossierType = CmbTargetDossierType.SelectedItem is ComboBoxItem cit &&
                                        !string.IsNullOrEmpty(cit.Tag?.ToString())
                    ? cit.Tag?.ToString()
                    : null;

                var createdFiles = await Task.Run(() => _exportService.ExportAsync(dossierType, targetDossierType, outputDirectory));

                UpdateStatus("Export done.");
                AppendLog($"Export done: {createdFiles.Count} files.");

                var fileList = string.Join("\n", System.Linq.Enumerable.Select(createdFiles, Path.GetFileName));
                MessageBox.Show(
                    $"Export complete.\nFolder: {outputDirectory}\n\nFiles ({createdFiles.Count}):\n{fileList}",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus($"Error: {ex.Message}");
                AppendLog($"Error: {ex.Message}");
                MessageBox.Show(ex.Message, "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnExport.IsEnabled = true;
            }
        }

        private async void BtnRefreshStats_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatisticsAsync();
        }

        private async void BtnRefreshData_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            await LoadDataAsync();
        }

        private async void BtnResetCheckpoint_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "This will delete the checkpoint. Next run will start from the beginning.\n\nContinue?",
                "Reset Checkpoint", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await using var scope = App.AppHost.Services.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewLoadCheckpointRepository>();
                await repo.ResetAllAsync();
                AppendLog("Checkpoint reset.");
                MessageBox.Show("Checkpoint deleted.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Reset Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnClearStaging_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "This will delete ALL records from PreviewDocStaging.\n\nContinue?",
                "Clear Staging", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await using var scope = App.AppHost.Services.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await uow.BeginAsync();
                await repo.DeleteAllAsync();
                await uow.CommitAsync();

                AppendLog("Staging cleared.");
                await RefreshStatisticsAsync();
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Clear Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        #endregion

        #region Pagination

        private async void CmbPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbPageSize.SelectedItem is ComboBoxItem item && int.TryParse(item.Content?.ToString(), out var size))
            {
                _pageSize = size;
                _currentPage = 1;
                await LoadDataAsync();
            }
        }

        private async void BtnFirstPage_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            await LoadDataAsync();
        }

        private async void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1) { _currentPage--; await LoadDataAsync(); }
        }

        private async void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages) { _currentPage++; await LoadDataAsync(); }
        }

        private async void BtnLastPage_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = _totalPages;
            await LoadDataAsync();
        }

        #endregion

        #region Data Loading

        private async Task RefreshStatisticsAsync()
        {
            await using var scope = App.AppHost.Services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();
            var docRepo = scope.ServiceProvider.GetRequiredService<IDocStagingRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            try
            {
                await unitOfWork.BeginAsync();

                var piCount = await repo.GetCountByDossierTypeAsync("PI");
                var leCount = await repo.GetCountByDossierTypeAsync("LE");
                var pendingCount = await repo.GetCountByStatusAsync("PENDING");
                var folderExistsCount = await repo.GetCountByStatusAsync("FOLDER_EXISTS");
                var folderCreatedCount = await repo.GetCountByStatusAsync("FOLDER_CREATED");
                var docPendingCount = await docRepo.CountByStatusAsync("PREPARED", CancellationToken.None);
                var folderDistinctCounts = await repo.GetDistinctFolderCountsPerStatusAsync();
                var total = piCount + leCount;

                await unitOfWork.CommitAsync();

                _docReadyCount = docPendingCount;

                var fpc = folderDistinctCounts;
                var distinctPendingExists  = fpc.GetValueOrDefault("FOLDER_PENDING_EXISTS",   0); // Faza 2 out, Faza 3 in
                var distinctPending        = fpc.GetValueOrDefault("FOLDER_PENDING_CREATION", 0); // Faza 2 out, Faza 3 in
                var distinctExists         = fpc.GetValueOrDefault("FOLDER_EXISTS",           0); // Faza 3 done (vec postojao)
                var distinctCreated        = fpc.GetValueOrDefault("FOLDER_CREATED",          0); // Faza 3 done (novokreiran)

                TxtTotalCount.Text = total.ToString("N0");
                TxtPiCount.Text = piCount.ToString("N0");
                TxtLeCount.Text = leCount.ToString("N0");
                TxtPendingCount.Text = pendingCount.ToString("N0");
                TxtFolderReadyCount.Text = (folderExistsCount + folderCreatedCount).ToString("N0");
                TxtDocReadyCount.Text = docPendingCount.ToString("N0");
                TxtDocReadyCountAction.Text = docPendingCount.ToString("N0");
                BtnStartMigration.IsEnabled = docPendingCount > 0;

                // Faza 2 — folder statistika (obradjeni = svi koji su prosli Fazu 2)
                TxtFaza2FolderProcessed.Text = (distinctPendingExists + distinctPending + distinctExists + distinctCreated).ToString("N0");
                TxtFaza2FolderPending.Text   = distinctPending.ToString("N0");

                // Faza 3 — folder statistika (ceka = PENDING_EXISTS + PENDING_CREATION; zavrseno = EXISTS + CREATED)
                TxtFaza3Total.Text   = (distinctPendingExists + distinctPending + distinctExists + distinctCreated).ToString("N0");
                TxtFaza3Pending.Text = distinctPending.ToString("N0");
                TxtFaza3Created.Text = distinctCreated.ToString("N0");
            }
            catch (Exception ex)
            {
                await unitOfWork.RollbackAsync();
                AppendLog($"Stats error: {ex.Message}");
            }
        }

        private async Task LoadDataAsync()
        {
            await using var scope = App.AppHost.Services.CreateAsyncScope();
            var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            try
            {

                await unitOfWork.BeginAsync();

                var (items, totalCount) = await repo.GetPagedAsync(_currentPage, _pageSize);

                _totalRecords = totalCount;
                _totalPages = Math.Max(1, (totalCount + _pageSize - 1) / _pageSize);
                if (_currentPage > _totalPages) _currentPage = _totalPages;

                PreviewDocs = new ObservableCollection<PreviewDocStaging>(items);

                TxtTotalRecords.Text = totalCount.ToString("N0");
                TxtDisplayedRecords.Text = PreviewDocs.Count.ToString("N0");
                TxtCurrentPage.Text = _currentPage.ToString();
                TxtTotalPages.Text = _totalPages.ToString();

                BtnFirstPage.IsEnabled = _currentPage > 1;
                BtnPrevPage.IsEnabled = _currentPage > 1;
                BtnNextPage.IsEnabled = _currentPage < _totalPages;
                BtnLastPage.IsEnabled = _currentPage < _totalPages;
            }
            catch (Exception ex)
            {
                await unitOfWork.RollbackAsync();

                AppendLog($"Load error: {ex.Message}");
            }
        }

        #endregion

        #region Helpers

        private void SetButtonsRunning(bool isRunning)
        {
            BtnStartFaza1.IsEnabled = !isRunning;
            BtnStopFaza1.IsEnabled = isRunning;
            BtnStartFaza2.IsEnabled = !isRunning;
            BtnStopFaza2.IsEnabled = isRunning;
            BtnStartFaza3.IsEnabled = !isRunning;
            BtnStopFaza3.IsEnabled = isRunning;
            BtnStartTransfer.IsEnabled = !isRunning;
            BtnStopTransfer.IsEnabled = isRunning;
            BtnStartMigration.IsEnabled = !isRunning && _docReadyCount > 0;
            BtnStopMigration.IsEnabled = isRunning;
            BtnResetCheckpoint.IsEnabled = !isRunning;
            BtnClearStaging.IsEnabled = !isRunning;
            BtnRefreshStats.IsEnabled = !isRunning;
            BtnRefreshData.IsEnabled = !isRunning;
            if (BtnRollbackFaza3.Visibility == Visibility.Visible)
                BtnRollbackFaza3.IsEnabled = !isRunning;
        }

        private void UpdateStatus(string status)   
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = status;
                TxtProgressDetail.Text = $"[{DateTime.Now:HH:mm:ss}] {status}";
            });
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {message}\n");
                TxtLog.ScrollToEnd();
            });
        }

        #endregion
    }
}
