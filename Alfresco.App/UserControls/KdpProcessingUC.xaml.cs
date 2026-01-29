using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
using Migration.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
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

namespace Alfresco.App.UserControls
{
    public partial class KdpProcessingUC : UserControl, INotifyPropertyChanged
    {
        private readonly IKdpDocumentProcessingService _kdpService;
        private CancellationTokenSource? _cts;
        private CancellationTokenSource? _searchCts;

        // Observable collection for DataGrid binding
        private ObservableCollection<KdpExportResult> _exportResults = new();
        public ObservableCollection<KdpExportResult> ExportResults
        {
            get => _exportResults;
            set { _exportResults = value; OnPropertyChanged(); }
        }

        // Pagination state
        private int _currentPage = 1;
        private int _pageSize = 25;
        private int _totalPages = 1;
        private int _totalRecords = 0;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public KdpProcessingUC()
        {
            InitializeComponent();

            // Set DataContext for binding
            DataContext = this;

            _kdpService = App.AppHost.Services.GetService(typeof(IKdpDocumentProcessingService)) as IKdpDocumentProcessingService
                ?? throw new InvalidOperationException("IKdpDocumentProcessingService nije registrovan u DI kontejneru");

            Loaded += KdpProcessingUC_Loaded;
        }

        private async void KdpProcessingUC_Loaded(object sender, RoutedEventArgs e)
        {
            await RefreshStatisticsAsync();
            await LoadDataAsync();
        }

        #region Button Click Handlers

        private async void BtnLoadDocuments_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DisableButtons();
                AppendLog("Pocetak ucitavanja KDP dokumenata iz Alfresca...");
                UpdateStatus("Ucitavanje...");

                _cts = new CancellationTokenSource();
                var count = await _kdpService.LoadKdpDocumentsToStagingAsync(_cts.Token);

                AppendLog($"Uspesno ucitano {count} dokumenata u staging tabelu.");
                UpdateStatus("Ucitavanje zavrseno");

                await RefreshStatisticsAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"GRESKA: {ex.Message}");
                UpdateStatus("Greska");
                MessageBox.Show($"Greska pri ucitavanju dokumenata: {ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableButtons();
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async void BtnRunProcess_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DisableButtons();
                AppendLog("Pokretanje obrade KDP dokumenata (sp_ProcessKdpDocuments)...");
                UpdateStatus("Obrada...");

                _cts = new CancellationTokenSource();
                var (totalCandidates, totalDocuments) = await _kdpService.ProcessKdpDocumentsAsync(_cts.Token);

                AppendLog($"Obrada zavrsena:");
                AppendLog($"  - Kandidat folderi: {totalCandidates}");
                AppendLog($"  - Ukupno dokumenata: {totalDocuments}");
                UpdateStatus("Obrada zavrsena");

                await RefreshStatisticsAsync();
                await LoadDataAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"GRESKA: {ex.Message}");
                UpdateStatus("Greska");
                MessageBox.Show($"Greska pri obradi dokumenata: {ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableButtons();
                _cts?.Dispose();
                _cts = null;
            }
        }

        private async void BtnRefreshStats_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatisticsAsync();
        }

        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Excel export funkcionalnost ce biti implementirana kasnije.",
                "Informacija", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private async void BtnClearStaging_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Da li ste sigurni da zelite da obrisete sve zapise iz staging tabele?",
                "Potvrda",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                DisableButtons();
                AppendLog("Brisanje staging tabele...");
                UpdateStatus("Brisanje...");

                _cts = new CancellationTokenSource();
                await _kdpService.ClearStagingAsync(_cts.Token);

                AppendLog("Staging tabela uspesno obrisana.");
                UpdateStatus("Spremno");

                await RefreshStatisticsAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"GRESKA: {ex.Message}");
                UpdateStatus("Greska");
                MessageBox.Show($"Greska pri brisanju staging tabele: {ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableButtons();
                _cts?.Dispose();
                _cts = null;
            }
        }

        #endregion

        #region Search and Filter

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            await LoadDataAsync();
        }

        private async void BtnClearSearch_Click(object sender, RoutedEventArgs e)
        {
            TxtSearchCoreId.Text = string.Empty;
            TxtSearchOldStatus.Text = string.Empty;
            TxtSearchNewStatus.Text = string.Empty;
            CmbSearchAction.SelectedIndex = 0;

            _currentPage = 1;
            await LoadDataAsync();
        }

        private async void BtnRefreshData_Click(object sender, RoutedEventArgs e)
        {
            await LoadDataAsync();
        }

        /// <summary>
        /// Izvlaci CoreId iz unosa (uklanja ACC- prefiks ako postoji)
        /// </summary>
        private string ExtractCoreId(string? input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var trimmed = input.Trim();

            // Ukloni ACC- prefiks (case insensitive)
            if (trimmed.StartsWith("ACC-", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed.Substring(4);
            }

            return trimmed;
        }

        /// <summary>
        /// Dobija filtere iz UI kontrola
        /// </summary>
        private (string? coreId, string? oldStatus, string? newStatus, int? action) GetSearchFilters()
        {
            var coreId = ExtractCoreId(TxtSearchCoreId.Text);
            var oldStatus = TxtSearchOldStatus.Text?.Trim();
            var newStatus = TxtSearchNewStatus.Text?.Trim();

            int? action = null;
            var actionText = (CmbSearchAction.SelectedItem as ComboBoxItem)?.Content?.ToString();
            if (!string.IsNullOrEmpty(actionText) && int.TryParse(actionText, out var actionValue))
            {
                action = actionValue;
            }

            return (
                string.IsNullOrEmpty(coreId) ? null : coreId,
                string.IsNullOrEmpty(oldStatus) ? null : oldStatus,
                string.IsNullOrEmpty(newStatus) ? null : newStatus,
                action
            );
        }

        #endregion

        #region Pagination

        private async void CmbPageSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Guard: skip if controls not yet initialized
            if (!IsLoaded || TxtTotalRecords == null)
                return;

            if (CmbPageSize.SelectedItem is ComboBoxItem item && int.TryParse(item.Content?.ToString(), out var pageSize))
            {
                _pageSize = pageSize;
                _currentPage = 1;
                await LoadDataAsync();
            }
        }

        private async void BtnFirstPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage = 1;
                await LoadDataAsync();
            }
        }

        private async void BtnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await LoadDataAsync();
            }
        }

        private async void BtnNextPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage++;
                await LoadDataAsync();
            }
        }

        private async void BtnLastPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < _totalPages)
            {
                _currentPage = _totalPages;
                await LoadDataAsync();
            }
        }

        private void UpdatePaginationUI(int totalCount, int displayedCount)
        {
            _totalRecords = totalCount;
            _totalPages = Math.Max(1, (int)Math.Ceiling((double)totalCount / _pageSize));

            if (_currentPage > _totalPages)
                _currentPage = _totalPages;

            // Update UI
            TxtTotalRecords.Text = totalCount.ToString();
            TxtDisplayedRecords.Text = displayedCount.ToString();
            TxtCurrentPage.Text = _currentPage.ToString();
            TxtTotalPages.Text = _totalPages.ToString();

            // Update button states
            BtnFirstPage.IsEnabled = _currentPage > 1;
            BtnPrevPage.IsEnabled = _currentPage > 1;
            BtnNextPage.IsEnabled = _currentPage < _totalPages;
            BtnLastPage.IsEnabled = _currentPage < _totalPages;
        }

        #endregion

        #region Data Loading

        private async Task LoadDataAsync()
        {
            // Cancel previous search if running
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var ct = _searchCts.Token;

            try
            {
                UpdateStatus("Ucitavanje podataka...");

                var (coreId, oldStatus, newStatus, action) = GetSearchFilters();
                var skip = (_currentPage - 1) * _pageSize;

                // Create a scope for scoped services (async disposal for IAsyncDisposable)
                await using var scope = App.AppHost.Services.CreateAsyncScope();
                var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repository = scope.ServiceProvider.GetRequiredService<IKdpExportResultRepository>();

                // Begin transaction
                await uow.BeginAsync(System.Data.IsolationLevel.ReadCommitted, ct);

                var (results, totalCount) = await repository.SearchAsync(
                    coreId, oldStatus, newStatus, action,
                    skip, _pageSize, ct);

                // Commit transaction
                await uow.CommitAsync(ct);

                // Check if cancelled
                if (ct.IsCancellationRequested)
                    return;

                // Update ObservableCollection on UI thread
                Dispatcher.Invoke(() =>
                {
                    ExportResults.Clear();
                    foreach (var item in results)
                    {
                        ExportResults.Add(item);
                    }

                    UpdatePaginationUI(totalCount, results.Count);
                });

                UpdateStatus("Spremno");
            }
            catch (OperationCanceledException)
            {
                // Search was cancelled, ignore
            }
            catch (Exception ex)
            {
                AppendLog($"GRESKA pri ucitavanju podataka: {ex.Message}");
                UpdateStatus("Greska");
            }
        }

        #endregion

        #region Helper Methods

        private async Task RefreshStatisticsAsync()
        {
            try
            {
                UpdateStatus("Ucitavanje statistike...");

                var stats = await _kdpService.GetStatisticsAsync(CancellationToken.None);

                TxtStagingCount.Text = stats.TotalDocumentsInStaging.ToString();
                TxtCandidateFolders.Text = stats.TotalCandidateFolders.ToString();
                TxtTotalDocuments.Text = stats.TotalDocumentsInCandidateFolders.ToString();
                TxtInactiveCount.Text = stats.InactiveDocumentsCount.ToString();
                TxtActiveCount.Text = stats.ActiveDocumentsCount.ToString();
                TxtType00824Count.Text = stats.Type00824Count.ToString();
                TxtType00099Count.Text = stats.Type00099Count.ToString();

                UpdateStatus("Spremno");
                AppendLog("Statistika osvezena.");
            }
            catch (Exception ex)
            {
                AppendLog($"GRESKA pri osvezavanju statistike: {ex.Message}");
                UpdateStatus("Greska");
            }
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                TxtLog.ScrollToEnd();
            });
        }

        private void UpdateStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = status;
            });
        }

        private void DisableButtons()
        {
            Dispatcher.Invoke(() =>
            {
                BtnLoadDocuments.IsEnabled = false;
                BtnRunProcess.IsEnabled = false;
                BtnRefreshStats.IsEnabled = false;
                BtnClearStaging.IsEnabled = false;
                BtnRefreshData.IsEnabled = false;
            });
        }

        private void EnableButtons()
        {
            Dispatcher.Invoke(() =>
            {
                BtnLoadDocuments.IsEnabled = true;
                BtnRunProcess.IsEnabled = true;
                BtnRefreshStats.IsEnabled = true;
                BtnClearStaging.IsEnabled = true;
                BtnRefreshData.IsEnabled = true;
            });
        }

        #endregion
    }
}
