using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Request;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.ObjectModel;
using System.Windows;

namespace Alfresco.DocStatusUpdater
{
    public partial class MainWindow : Window
    {
        private readonly IAlfrescoReadApi _readApi;
        private readonly IAlfrescoWriteApi _writeApi;
        private CancellationTokenSource? _cts;

        private readonly List<DocStatusItem> _allItems = new();

        public MainWindow()
        {
            InitializeComponent();
            _readApi = App.AppHost.Services.GetRequiredService<IAlfrescoReadApi>();
            _writeApi = App.AppHost.Services.GetRequiredService<IAlfrescoWriteApi>();
        }

        #region Search

        private async void BtnSearch_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetBusy(true, "Pretraga...");
                _cts = new CancellationTokenSource();

                _allItems.Clear();
                DgDocuments.ItemsSource = null;
                TxtTotalFound.Text = "0";
                TxtDocStatus1Count.Text = "0";
                TxtUpdatedCount.Text = "0";
                TxtFailedCount.Text = "0";

                var config = App.AppHost.Services.GetRequiredService<IConfiguration>();
                var ancestorId = config.GetValue<string>("Search:AncestorFolderId") ?? "ea1799ba-7561-4e2c-9799-ba75619e2c9d";
                var pageSize = config.GetValue<int>("Search:BatchSize", 100);

                int totalFound = 0;
                int skipCount = 0;
                bool hasMore = true;

                AppendLog($"Pocetak pretrage dokumenata (AncestorId: {ancestorId}, BatchSize: {pageSize})...");

                while (hasMore)
                {
                    _cts.Token.ThrowIfCancellationRequested();

                    var request = new PostSearchRequest
                    {
                        Query = new QueryRequest
                        {
                            Language = "afts",
                            Query = $"((=ecm\\:docType:\"00100\" or =ecm\\:docType:\"00101\") AND ANCESTOR:\"workspace://SpacesStore/{ancestorId}\" AND TYPE:\"cm:content\")"
                        },
                        Paging = new PagingRequest
                        {
                            MaxItems = pageSize,
                            SkipCount = skipCount
                        },
                        Include = new[] { "properties", "path" },
                        Sort = new List<SortRequest>
                        {
                            new SortRequest
                            {
                                Type = "FIELD",
                                Field = "cm:created",
                                Ascending = true
                            }
                        }
                    };

                    var result = await _readApi.SearchAsync(request, _cts.Token);

                    if (result?.List?.Entries == null || result.List.Entries.Count == 0)
                    {
                        hasMore = false;
                        break;
                    }

                    foreach (var entry in result.List.Entries)
                    {
                        var node = entry.Entry;
                        if (node == null) continue;

                        totalFound++;

                        // Proveri ecm:docStatus
                        var docStatus = GetProperty(node.Properties, "ecm:docStatus");

                        if (docStatus == "1")
                        {
                            _allItems.Add(new DocStatusItem
                            {
                                RowNumber = _allItems.Count + 1,
                                NodeId = node.Id,
                                Name = node.Name,
                                DocType = GetProperty(node.Properties, "ecm:docType"),
                                DocStatus = docStatus,
                                Path = node.Path?.Name ?? "",
                                CreatedAt = node.CreatedAt.DateTime
                            });
                        }
                    }

                    hasMore = result.List.Pagination?.HasMoreItems ?? false;
                    skipCount += pageSize;

                    // Update UI
                    Dispatcher.Invoke(() =>
                    {
                        TxtTotalFound.Text = totalFound.ToString();
                        TxtDocStatus1Count.Text = _allItems.Count.ToString();
                        TxtProgress.Text = $"Pretrazeno: {totalFound}";
                        TxtStatus.Text = $"Strana {skipCount / pageSize}...";
                    });

                    AppendLog($"Strana {skipCount / pageSize}: pronadjeno {result.List.Entries.Count} dok., ukupno sa status=1: {_allItems.Count}");
                }

                // Bind to DataGrid
                DgDocuments.ItemsSource = new ObservableCollection<DocStatusItem>(_allItems);

                TxtTotalFound.Text = totalFound.ToString();
                TxtDocStatus1Count.Text = _allItems.Count.ToString();
                TxtListCount.Text = _allItems.Count.ToString();
                TxtNotUpdatedCount.Text = _allItems.Count.ToString();
                TxtProgress.Text = $"Gotovo: {totalFound} ukupno, {_allItems.Count} sa status=1";
                TxtStatus.Text = "Pretraga zavrsena";

                BtnUpdateAll.IsEnabled = _allItems.Count > 0;

                AppendLog($"Pretraga zavrsena. Ukupno pronadjeno: {totalFound}, sa docStatus=1: {_allItems.Count}");
            }
            catch (OperationCanceledException)
            {
                AppendLog("Pretraga je otkazana.");
                TxtStatus.Text = "Otkazano";
            }
            catch (Exception ex)
            {
                AppendLog($"GRESKA pri pretrazi: {ex.Message}");
                TxtStatus.Text = "Greska";
                MessageBox.Show($"Greska pri pretrazi:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        #endregion

        #region Update

        private async void BtnUpdateAll_Click(object sender, RoutedEventArgs e)
        {
            var itemsToUpdate = _allItems.Where(x => !x.IsUpdated).ToList();

            if (itemsToUpdate.Count == 0)
            {
                MessageBox.Show("Nema dokumenata za azuriranje.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Citaj MaxDegreeOfParallelism iz appsettings
            var config = App.AppHost.Services.GetRequiredService<IConfiguration>();
            var maxDop = config.GetValue<int>("Update:MaxDegreeOfParallelism", 5);

            var confirm = MessageBox.Show(
                $"Da li zelite da azurirate {itemsToUpdate.Count} dokumenata?\n\n" +
                $"ecm:docStatus ce biti postavljen na \"2\".\n" +
                $"MaxDegreeOfParallelism: {maxDop}",
                "Potvrda",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                SetBusy(true, "Azuriranje...");
                _cts = new CancellationTokenSource();

                int success = 0;
                int failed = 0;
                int processed = 0;
                int total = itemsToUpdate.Count;

                ProgressBar.Maximum = total;
                ProgressBar.Value = 0;

                AppendLog($"Pocetak paralelnog azuriranja {total} dokumenata (MaxDOP: {maxDop})...");

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDop,
                    CancellationToken = _cts.Token
                };

                await Parallel.ForEachAsync(itemsToUpdate, parallelOptions, async (item, ct) =>
                {
                    try
                    {
                        var properties = new Dictionary<string, object>
                        {
                            { "ecm:docStatus", "2" }
                        };

                        var updated = await _writeApi.UpdateNodePropertiesAsync(item.NodeId, properties, ct);

                        if (updated)
                        {
                            item.IsUpdated = true;
                            item.UpdateMessage = "OK";
                            item.DocStatus = "2";
                            Interlocked.Increment(ref success);
                        }
                        else
                        {
                            item.UpdateMessage = "Neuspesno (false)";
                            Interlocked.Increment(ref failed);
                        }
                    }
                    catch (Exception ex)
                    {
                        item.UpdateMessage = $"Greska: {ex.Message}";
                        Interlocked.Increment(ref failed);
                    }

                    var current = Interlocked.Increment(ref processed);

                    // Update UI svakih 10 ili na kraju
                    if (current % 10 == 0 || current == total)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            ProgressBar.Value = current;
                            TxtProgress.Text = $"{current} / {total}";
                            TxtUpdatedCount.Text = success.ToString();
                            TxtFailedCount.Text = failed.ToString();
                            TxtNotUpdatedCount.Text = _allItems.Count(x => !x.IsUpdated).ToString();
                            TxtStatus.Text = $"Azuriranje: {current}/{total}";
                        });
                    }

                    // Log svakih 100
                    if (current % 100 == 0)
                    {
                        AppendLog($"Progress: {current}/{total} (uspesno: {success}, neuspesno: {failed})");
                    }
                });

                // Refresh DataGrid
                DgDocuments.ItemsSource = new ObservableCollection<DocStatusItem>(_allItems);

                TxtStatus.Text = "Azuriranje zavrseno";
                TxtUpdatedCount.Text = success.ToString();
                TxtFailedCount.Text = failed.ToString();
                TxtNotUpdatedCount.Text = _allItems.Count(x => !x.IsUpdated).ToString();

                AppendLog($"Azuriranje zavrseno. Uspesno: {success}, Neuspesno: {failed}");

                if (failed > 0)
                {
                    MessageBox.Show($"Azuriranje zavrseno.\nUspesno: {success}\nNeuspesno: {failed}",
                        "Rezultat", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"Sva dokumenta su uspesno azurirana ({success}).",
                        "Rezultat", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("Azuriranje je otkazano.");
                TxtStatus.Text = "Otkazano";
                DgDocuments.ItemsSource = new ObservableCollection<DocStatusItem>(_allItems);
            }
            catch (Exception ex)
            {
                AppendLog($"GRESKA pri azuriranju: {ex.Message}");
                TxtStatus.Text = "Greska";
                MessageBox.Show($"Greska pri azuriranju:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        #endregion

        #region Cancel

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            AppendLog("Otkazivanje operacije...");
        }

        #endregion

        #region Helpers

        private string GetProperty(Dictionary<string, object>? properties, string key)
        {
            if (properties == null) return "";
            if (properties.TryGetValue(key, out var value) && value != null)
                return value.ToString() ?? "";
            return "";
        }

        private void AppendLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                TxtLog.ScrollToEnd();
            });
        }

        private void SetBusy(bool busy, string? status = null)
        {
            Dispatcher.Invoke(() =>
            {
                BtnSearch.IsEnabled = !busy;
                BtnUpdateAll.IsEnabled = !busy && _allItems.Any(x => !x.IsUpdated);
                BtnCancel.IsEnabled = busy;

                if (status != null)
                    TxtStatus.Text = status;
                else if (!busy)
                    TxtStatus.Text = "Spremno";
            });
        }

        #endregion
    }

    /// <summary>
    /// Model za prikaz u DataGrid-u
    /// </summary>
    public class DocStatusItem
    {
        public int RowNumber { get; set; }
        public string NodeId { get; set; } = "";
        public string Name { get; set; } = "";
        public string DocType { get; set; } = "";
        public string DocStatus { get; set; } = "";
        public string Path { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public bool IsUpdated { get; set; }
        public string UpdateMessage { get; set; } = "";
    }
}
