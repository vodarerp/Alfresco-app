using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Pokrenuto ucitavanje...");
                AppendLog("=== Pokretanje Faze 1: Ucitavanje dokumenata iz Alfresca ===");

                _cts = new CancellationTokenSource();

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "U toku...");
                        AppendLog($"Ucitano: {p.ProcessedItems}  |  Greske: {p.FailedCount}  |  {p.Message}");

                        if (p.TotalItems > 0)
                            ProgressBar.Value = Math.Min(100, p.ProgressPercentage);
                    });
                }

                var result = await Task.Run(
                    () => _previewLoadService.RunLoopAsync(_cts.Token, OnProgress),
                    _cts.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Faza 1 zavrsena uspesno." : "Faza 1 zavrsena sa upozorenjem (nema konfiguriranih foldera?).";
                UpdateStatus(msg);
                AppendLog($"=== {msg} ===");

                await RefreshStatisticsAsync();
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Zaustavljeno.");
                AppendLog("=== Ucitavanje zaustavljeno od strane korisnika. ===");
            }
            catch (Exception ex)
            {
                UpdateStatus($"GRESKA: {ex.Message}");
                AppendLog($"GRESKA: {ex.Message}");
                MessageBox.Show($"Greska pri ucitavanju:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetButtonsRunning(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        private void BtnStopFaza1_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            BtnStopFaza1.IsEnabled = false;
            UpdateStatus("Zaustavljanje...");
            AppendLog("Zahtev za zaustavljanje primljen...");
        }

        private async void BtnStartFaza2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Pokrenuta Faza 2: provera foldera...");
                AppendLog("=== Pokretanje Faze 2: Priprema destination foldera ===");

                _ctsFaza2 = new CancellationTokenSource();

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "U toku...");
                        AppendLog(p.Message ?? "");
                    });
                }

                var result = await Task.Run(
                    () => _folderPreparationService.RunAsync(_ctsFaza2.Token, OnProgress),
                    _ctsFaza2.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Faza 2 zavrsena uspesno." : "Faza 2 zavrsena sa upozorenjem.";
                UpdateStatus(msg);
                AppendLog($"=== {msg} ===");

                await RefreshStatisticsAsync();
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Faza 2 zaustavljena.");
                AppendLog("=== Faza 2 zaustavljena od strane korisnika. ===");
            }
            catch (Exception ex)
            {
                UpdateStatus($"GRESKA Faza 2: {ex.Message}");
                AppendLog($"GRESKA: {ex.Message}");
                MessageBox.Show($"Greska u Fazi 2:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
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
            UpdateStatus("Zaustavljanje Faze 2...");
            AppendLog("Zahtev za zaustavljanje Faze 2 primljen...");
        }

        private async void BtnStartFaza3_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Pokrenuta Faza 3: kreiranje foldera...");
                AppendLog("=== Pokretanje Faze 3: Kreiranje Alfresco foldera ===");

                _ctsFaza3 = new CancellationTokenSource();

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "U toku...");
                        AppendLog(p.Message ?? "");
                    });
                }

                var result = await Task.Run(
                    () => _folderCreationService.RunAsync(_ctsFaza3.Token, OnProgress),
                    _ctsFaza3.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Faza 3 zavrsena uspesno." : "Faza 3 zavrsena sa upozorenjem.";
                UpdateStatus(msg);
                AppendLog($"=== {msg} ===");

                await RefreshStatisticsAsync();
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Faza 3 zaustavljena.");
                AppendLog("=== Faza 3 zaustavljena od strane korisnika. ===");
            }
            catch (Exception ex)
            {
                UpdateStatus($"GRESKA Faza 3: {ex.Message}");
                AppendLog($"GRESKA: {ex.Message}");
                MessageBox.Show($"Greska u Fazi 3:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
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
            UpdateStatus("Zaustavljanje Faze 3...");
            AppendLog("Zahtev za zaustavljanje Faze 3 primljen...");
        }

        private async void BtnRollbackFaza3_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "PAZI: Ovo ce obrisati SVE Alfresco foldere kreirane u Fazi 3\ni resetovati njihov status na FOLDER_PENDING_CREATION.\n\nNastaviti?",
                "Rollback Faze 3", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                SetButtonsRunning(true);
                ProgressBar.Value = 0;
                UpdateStatus("Rollback Faze 3 u toku...");
                AppendLog("=== Pokretanje Rollbacka Faze 3: brisanje kreiranih foldera ===");

                _ctsRollback = new CancellationTokenSource();

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "U toku...");
                        AppendLog(p.Message ?? "");

                        if (p.TotalItems > 0)
                            ProgressBar.Value = Math.Min(100, p.ProgressPercentage);
                    });
                }

                var result = await Task.Run(
                    () => _rollbackService.RunAsync(_ctsRollback.Token, OnProgress),
                    _ctsRollback.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Rollback zavrsen uspesno." : "Rollback zavrsen sa greskama (videti log).";
                UpdateStatus(msg);
                AppendLog($"=== {msg} ===");

                await RefreshStatisticsAsync();
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Rollback zaustavljen.");
                AppendLog("=== Rollback zaustavljen od strane korisnika. ===");
            }
            catch (Exception ex)
            {
                UpdateStatus($"GRESKA Rollback: {ex.Message}");
                AppendLog($"GRESKA: {ex.Message}");
                MessageBox.Show($"Greska pri rollbacku:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
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
                UpdateStatus("Pokrenuto: Transfer u DocStaging...");
                AppendLog("=== Pokretanje Faze 6: Transfer PreviewDocStaging → DocStaging ===");

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
                        UpdateStatus(p.Message ?? "U toku...");
                        AppendLog($"Transfer: {p.ProcessedItems}  |  Greske: {p.FailedCount}  |  {p.Message}");

                        if (p.TotalItems > 0)
                            ProgressBar.Value = Math.Min(100, p.ProgressPercentage);
                    });
                }

                var result = await Task.Run(
                    () => _transferService.RunAsync(dossierType, targetDossierType, _ctsTransfer.Token, OnProgress),
                    _ctsTransfer.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Transfer zavrsen uspesno." : "Transfer zavrsen sa upozorenjem.";
                UpdateStatus(msg);
                AppendLog($"=== {msg} ===");

                await RefreshStatisticsAsync();
                await LoadDataAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Transfer zaustavljen.");
                AppendLog("=== Transfer zaustavljen od strane korisnika. ===");
            }
            catch (Exception ex)
            {
                UpdateStatus($"GRESKA Transfer: {ex.Message}");
                AppendLog($"GRESKA: {ex.Message}");
                MessageBox.Show($"Greska pri transferu:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
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
                UpdateStatus("Pokrenuta Faza 8: Start Migration...");
                AppendLog("=== Pokretanje Faze 8: Start Migration (MoveService) ===");

                _ctsMigration = new CancellationTokenSource();

                void OnProgress(WorkerProgress p)
                {
                    Dispatcher.Invoke(() =>
                    {
                        UpdateStatus(p.Message ?? "U toku...");
                        AppendLog($"Migration: {p.ProcessedItems}  |  Greske: {p.FailedCount}  |  {p.Message}");

                        if (p.TotalItems > 0)
                            ProgressBar.Value = Math.Min(100, p.ProgressPercentage);
                    });
                }

                var result = await Task.Run(
                    () => _moveService.RunLoopAsync(_ctsMigration.Token, OnProgress),
                    _ctsMigration.Token);

                ProgressBar.Value = 100;
                var msg = result ? "Faza 8 zavrsena uspesno." : "Faza 8 zavrsena sa upozorenjem.";
                UpdateStatus(msg);
                AppendLog($"=== {msg} ===");

                await RefreshStatisticsAsync();
            }
            catch (OperationCanceledException)
            {
                UpdateStatus("Migracija zaustavljena.");
                AppendLog("=== Migracija zaustavljena od strane korisnika. ===");
            }
            catch (Exception ex)
            {
                UpdateStatus($"GRESKA Migracija: {ex.Message}");
                AppendLog($"GRESKA: {ex.Message}");
                MessageBox.Show($"Greska pri migraciji:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
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
            UpdateStatus("Zaustavljanje migracije...");
            AppendLog("Zahtev za zaustavljanje migracije primljen...");
        }

        private void BtnStopTransfer_Click(object sender, RoutedEventArgs e)
        {
            _ctsTransfer?.Cancel();
            BtnStopTransfer.IsEnabled = false;
            UpdateStatus("Zaustavljanje transfera...");
            AppendLog("Zahtev za zaustavljanje transfera primljen...");
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
                UpdateStatus("Eksport u toku...");
                AppendLog($"=== Pokretanje eksporta u folder: {outputDirectory} ===");

                var dossierType = CmbTransferDossierType.SelectedItem is ComboBoxItem ci &&
                                  ci.Content?.ToString() != "(sve)"
                    ? ci.Content?.ToString()
                    : null;

                var targetDossierType = CmbTargetDossierType.SelectedItem is ComboBoxItem cit &&
                                        !string.IsNullOrEmpty(cit.Tag?.ToString())
                    ? cit.Tag?.ToString()
                    : null;

                var createdFiles = await Task.Run(() => _exportService.ExportAsync(dossierType, targetDossierType, outputDirectory));

                UpdateStatus("Eksport zavrsen.");
                AppendLog($"=== Eksport zavrsen: {createdFiles.Count} fajl(ova) u {outputDirectory} ===");

                var fileList = string.Join("\n", System.Linq.Enumerable.Select(createdFiles, Path.GetFileName));
                MessageBox.Show(
                    $"Eksport uspesno zavrsen.\nFolder: {outputDirectory}\n\nKreirani fajlovi ({createdFiles.Count}):\n{fileList}",
                    "Eksport", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                UpdateStatus($"GRESKA Eksport: {ex.Message}");
                AppendLog($"GRESKA: {ex.Message}");
                MessageBox.Show($"Greska pri eksportu:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
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
                "Ovo ce obrisati checkpoint - sledece pokretanje ce krenuti od pocetka.\nNastaviti?",
                "Reset Checkpoint", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await using var scope = App.AppHost.Services.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewLoadCheckpointRepository>();
                await repo.ResetAllAsync();
                AppendLog("Checkpoint resetovan.");
                MessageBox.Show("Checkpoint obrisan.", "OK", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greska pri resetovanju checkpointa:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnClearStaging_Click(object sender, RoutedEventArgs e)
        {
            var confirm = MessageBox.Show(
                "PAZI: Ovo ce obrisati SVE zapise iz PreviewDocStaging tabele!\nNastaviti?",
                "Brisanje tabele", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await using var scope = App.AppHost.Services.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repo = scope.ServiceProvider.GetRequiredService<IPreviewDocStagingRepository>();

                await uow.BeginAsync();
                await repo.DeleteAllAsync();
                await uow.CommitAsync();

                AppendLog("PreviewDocStaging tabela ociscena.");
                await RefreshStatisticsAsync();
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greska pri brisanju:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var docReadyCount = await docRepo.CountReadyForProcessingAsync(CancellationToken.None);
                var total = piCount + leCount;

                await unitOfWork.CommitAsync();

                _docReadyCount = docReadyCount;

                TxtTotalCount.Text = total.ToString("N0");
                TxtPiCount.Text = piCount.ToString("N0");
                TxtLeCount.Text = leCount.ToString("N0");
                TxtPendingCount.Text = pendingCount.ToString("N0");
                TxtFolderReadyCount.Text = (folderExistsCount + folderCreatedCount).ToString("N0");
                TxtDocReadyCount.Text = docReadyCount.ToString("N0");
                TxtDocReadyCountAction.Text = docReadyCount.ToString("N0");
                BtnStartMigration.IsEnabled = docReadyCount > 0;
            }
            catch (Exception ex)
            {
                await unitOfWork.RollbackAsync();
                AppendLog($"Greska pri ucitavanju statistike: {ex.Message}");
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

                AppendLog($"Greska pri ucitavanju podataka: {ex.Message}");
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
                TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                TxtLog.ScrollToEnd();
            });
        }

        #endregion
    }
}
