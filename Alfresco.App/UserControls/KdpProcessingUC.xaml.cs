using Migration.Abstraction.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Alfresco.App.UserControls
{
    /// <summary>
    /// Interaction logic for KdpProcessingUC.xaml
    /// </summary>
    public partial class KdpProcessingUC : UserControl
    {
        private readonly IKdpDocumentProcessingService _kdpService;
        private CancellationTokenSource? _cts;

        public KdpProcessingUC()
        {
            InitializeComponent();

            // Dependency injection - get service from App host
            _kdpService = App.AppHost.Services.GetService(typeof(IKdpDocumentProcessingService)) as IKdpDocumentProcessingService
                ?? throw new InvalidOperationException("IKdpDocumentProcessingService nije registrovan u DI kontejneru");

            Loaded += KdpProcessingUC_Loaded;
        }

        private async void KdpProcessingUC_Loaded(object sender, RoutedEventArgs e)
        {
            // Učitaj statistiku pri otvaranju
            await RefreshStatisticsAsync();
        }

        /// <summary>
        /// Učitaj KDP dokumente iz Alfresca u staging tabelu
        /// </summary>
        private async void BtnLoadDocuments_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DisableButtons();
                AppendLog("Početak učitavanja KDP dokumenata iz Alfresca...");
                UpdateStatus("Učitavanje...");

                _cts = new CancellationTokenSource();
                var count = await _kdpService.LoadKdpDocumentsToStagingAsync(_cts.Token);

                AppendLog($"Uspešno učitano {count} dokumenata u staging tabelu.");
                UpdateStatus("Učitavanje završeno");

                await RefreshStatisticsAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"GREŠKA: {ex.Message}");
                UpdateStatus("Greška");
                MessageBox.Show($"Greška pri učitavanju dokumenata: {ex.Message}", "Greška", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableButtons();
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Pokreni obradu KDP dokumenata (poziva sp_ProcessKdpDocuments)
        /// </summary>
        private async void BtnRunProcess_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DisableButtons();
                AppendLog("Pokretanje obrade KDP dokumenata (sp_ProcessKdpDocuments)...");
                UpdateStatus("Obrada...");

                _cts = new CancellationTokenSource();
                var (totalCandidates, totalDocuments) = await _kdpService.ProcessKdpDocumentsAsync(_cts.Token);

                AppendLog($"Obrada završena:");
                AppendLog($"  - Kandidat folderi: {totalCandidates}");
                AppendLog($"  - Ukupno dokumenata: {totalDocuments}");
                UpdateStatus("Obrada završena");

                await RefreshStatisticsAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"GREŠKA: {ex.Message}");
                UpdateStatus("Greška");
                MessageBox.Show($"Greška pri obradi dokumenata: {ex.Message}", "Greška", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableButtons();
                _cts?.Dispose();
                _cts = null;
            }
        }

        /// <summary>
        /// Osveži statistiku
        /// </summary>
        private async void BtnRefreshStats_Click(object sender, RoutedEventArgs e)
        {
            await RefreshStatisticsAsync();
        }

        /// <summary>
        /// Eksport u Excel (placeholder - nije implementirano)
        /// </summary>
        private void BtnExportExcel_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Excel export funkcionalnost će biti implementirana kasnije.",
                "Informacija", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Očisti staging tabelu
        /// </summary>
        private async void BtnClearStaging_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Da li ste sigurni da želite da obrišete sve zapise iz staging tabele?",
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

                AppendLog("Staging tabela uspešno obrisana.");
                UpdateStatus("Spremno");

                await RefreshStatisticsAsync();
            }
            catch (Exception ex)
            {
                AppendLog($"GREŠKA: {ex.Message}");
                UpdateStatus("Greška");
                MessageBox.Show($"Greška pri brisanju staging tabele: {ex.Message}", "Greška", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EnableButtons();
                _cts?.Dispose();
                _cts = null;
            }
        }

        // ============================================
        // HELPER METHODS
        // ============================================

        /// <summary>
        /// Osveži statistiku sa servera
        /// </summary>
        private async Task RefreshStatisticsAsync()
        {
            try
            {
                UpdateStatus("Učitavanje statistike...");

                var stats = await _kdpService.GetStatisticsAsync(CancellationToken.None);

                // Update UI
                TxtStagingCount.Text = stats.TotalDocumentsInStaging.ToString();
                TxtCandidateFolders.Text = stats.TotalCandidateFolders.ToString();
                TxtTotalDocuments.Text = stats.TotalDocumentsInCandidateFolders.ToString();
                TxtInactiveCount.Text = stats.InactiveDocumentsCount.ToString();
                TxtActiveCount.Text = stats.ActiveDocumentsCount.ToString();
                TxtType00824Count.Text = stats.Type00824Count.ToString();
                TxtType00099Count.Text = stats.Type00099Count.ToString();

                UpdateStatus("Spremno");
                AppendLog("Statistika osvežena.");
            }
            catch (Exception ex)
            {
                AppendLog($"GREŠKA pri osvežavanju statistike: {ex.Message}");
                UpdateStatus("Greška");
            }
        }

        /// <summary>
        /// Dodaj log poruku
        /// </summary>
        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                TxtLog.ScrollToEnd();
            });
        }

        /// <summary>
        /// Ažuriraj status
        /// </summary>
        private void UpdateStatus(string status)
        {
            Dispatcher.Invoke(() =>
            {
                TxtStatus.Text = status;
            });
        }

        /// <summary>
        /// Onemogući dugmad tokom izvršavanja operacija
        /// </summary>
        private void DisableButtons()
        {
            Dispatcher.Invoke(() =>
            {
                BtnLoadDocuments.IsEnabled = false;
                BtnRunProcess.IsEnabled = false;
                BtnRefreshStats.IsEnabled = false;
                BtnClearStaging.IsEnabled = false;
            });
        }

        /// <summary>
        /// Omogući dugmad nakon izvršavanja operacija
        /// </summary>
        private void EnableButtons()
        {
            Dispatcher.Invoke(() =>
            {
                BtnLoadDocuments.IsEnabled = true;
                BtnRunProcess.IsEnabled = true;
                BtnRefreshStats.IsEnabled = true;
                BtnClearStaging.IsEnabled = true;
            });
        }
    }
}
