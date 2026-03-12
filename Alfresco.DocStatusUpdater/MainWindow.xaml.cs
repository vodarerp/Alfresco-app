using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Request;
using Alfresco.Contracts.SqlServer;
using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;

namespace Alfresco.DocStatusUpdater
{
    public partial class MainWindow : Window
    {
        private readonly IAlfrescoReadApi _readApi;
        private readonly IAlfrescoWriteApi _writeApi;
        private readonly ICurrentUserService _currentUserService;
        private readonly SqlServerOptions _sqlServerOptions;
        private CancellationTokenSource? _cts;

        private readonly List<DocStatusItem> _allItems = new();
        private readonly List<DossierUpdateItem> _dossierItems = new();

        public MainWindow()
        {
            InitializeComponent();
            _readApi = App.AppHost.Services.GetRequiredService<IAlfrescoReadApi>();
            _writeApi = App.AppHost.Services.GetRequiredService<IAlfrescoWriteApi>();
            _currentUserService = App.AppHost.Services.GetRequiredService<ICurrentUserService>();
            _sqlServerOptions = App.AppHost.Services.GetRequiredService<IOptions<SqlServerOptions>>().Value;
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

                // Sacuvaj listu u txt fajl kao JSON
                if (_allItems.Count > 0)
                {
                    try
                    {
                        var fileName = $"DocStatusSearch_{DateTime.Now:yyyyMMdd_HHmmss}.txt";
                        var filePath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
                        var json = JsonConvert.SerializeObject(_allItems, Formatting.Indented);
                        await File.WriteAllTextAsync(filePath, json);
                        AppendLog($"Lista sacuvana u fajl: {filePath}");
                    }
                    catch (Exception fileEx)
                    {
                        AppendLog($"UPOZORENJE: Nije moguce sacuvati fajl: {fileEx.Message}");
                    }
                }
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
                        //var tmpDoctype = item.DocType == "00825" ? "00101" : "00100";
                        var properties = new Dictionary<string, object>
                        {
                            { "ecm:docStatus", "2" }
                            //{ "ecm:docType", tmpDoctype }
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

        #region Dossier Deposit - Load

        private async void BtnDossierLoad_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetDossierBusy(true, "Ucitavanje dosieja iz baze...");
                _cts = new CancellationTokenSource();

                _dossierItems.Clear();
                DgDossiers.ItemsSource = null;

                AppendDossierLog("Ucitavanje distinct DestinationFolderId iz DocStaging tabele...");

                var sql = @"SELECT DISTINCT DestinationFolderId, DossierDestFolderId
                            FROM DocStaging
                            WHERE TipDosijea = 'Dosije Depozita'
                              AND Status = 'DONE'
                              AND ISNULL(DossierDestFolderIsCreated, 0) = 1";

                await using var connection = new SqlConnection(_sqlServerOptions.ConnectionString);
                await connection.OpenAsync(_cts.Token);

                var rows = (await connection.QueryAsync<(string DestinationFolderId, string DossierDestFolderId)>(
                    new CommandDefinition(sql, cancellationToken: _cts.Token))).ToList();

                AppendDossierLog($"Pronadjeno {rows.Count} distinct dosieja za azuriranje.");

                int rowNum = 0;
                foreach (var row in rows)
                {
                    if (string.IsNullOrWhiteSpace(row.DestinationFolderId)) continue;

                    rowNum++;
                    _dossierItems.Add(new DossierUpdateItem
                    {
                        RowNumber = rowNum,
                        DestinationFolderId = row.DestinationFolderId,
                        DossierDestFolderId = row.DossierDestFolderId ?? ""
                    });
                }

                DgDossiers.ItemsSource = new ObservableCollection<DossierUpdateItem>(_dossierItems);
                TxtDossierTotalCount.Text = _dossierItems.Count.ToString();
                TxtDossierListCount.Text = _dossierItems.Count.ToString();
                TxtDossierNotUpdatedCount.Text = _dossierItems.Count.ToString();
                TxtDossierStatus.Text = "Ucitavanje zavrseno";
                TxtDossierProgress.Text = $"Ucitano: {_dossierItems.Count}";

                BtnDossierUpdate.IsEnabled = _dossierItems.Count > 0;

                AppendDossierLog($"Ucitano {_dossierItems.Count} dosieja spremnih za azuriranje propertija.");
            }
            catch (OperationCanceledException)
            {
                AppendDossierLog("Ucitavanje je otkazano.");
                TxtDossierStatus.Text = "Otkazano";
            }
            catch (Exception ex)
            {
                AppendDossierLog($"GRESKA pri ucitavanju: {ex.Message}");
                TxtDossierStatus.Text = "Greska";
                MessageBox.Show($"Greska pri ucitavanju dosieja:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetDossierBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        #endregion

        #region Dossier Deposit - Update

        private async void BtnDossierUpdate_Click(object sender, RoutedEventArgs e)
        {
            var itemsToUpdate = _dossierItems.Where(x => !x.IsUpdated).ToList();

            if (itemsToUpdate.Count == 0)
            {
                MessageBox.Show("Nema dosieja za azuriranje.", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {               
                var config = App.AppHost.Services.GetRequiredService<IConfiguration>();
                var maxDop = config.GetValue<int>("Update:MaxDegreeOfParallelism", 5);

                var confirm = MessageBox.Show(
                    $"Da li zelite da azurirate propertije na {itemsToUpdate.Count} dosiejima?",
                    "Potvrda",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;

                SetDossierBusy(true, "Azuriranje propertija...");
                _cts = new CancellationTokenSource();

                int success = 0;
                int failed = 0;
                int processed = 0;
                int total = itemsToUpdate.Count;

                DossierProgressBar.Maximum = total;
                DossierProgressBar.Value = 0;

                AppendDossierLog($"Pocetak paralelnog azuriranja {total} dosieja (MaxDOP: {maxDop})...");

                var parallelOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = maxDop,
                    CancellationToken = _cts.Token
                };

                await Parallel.ForEachAsync(itemsToUpdate, parallelOptions, async (item, ct) =>
                {
                    try
                    {
                        // Citaj docCreator i docCreatorName iz child dokumenata dosieja
                        string docCreator = "";
                        string docCreatorName = "";

                        try
                        {
                            var children = await _readApi.GetNodeChildrenAsync(item.DestinationFolderId, 0, 50, ct);
                            if (children?.List?.Entries != null)
                            {
                                foreach (var child in children.List.Entries)
                                {
                                    var childNode = child.Entry;
                                    if (childNode?.Properties == null) continue;

                                    var creator = GetProperty(childNode.Properties, "ecm:docCreator");
                                    var creatorName = GetProperty(childNode.Properties, "ecm:docCreatorName");

                                    if (!string.IsNullOrWhiteSpace(creator) || !string.IsNullOrWhiteSpace(creatorName))
                                    {
                                        docCreator = creator;
                                        docCreatorName = creatorName;
                                        break;
                                    }
                                }
                            }
                        }
                        catch (Exception childEx)
                        {
                            AppendDossierLog($"WARN: greska citanja node: {item.DossierDestFolderId}: {childEx.Message}");
                        }

                        var properties = new Dictionary<string, object>
                        {
                            { "ecm:bnkSource", "DUTN" },
                            { "ecm:kreiraoId", docCreator },
                            { "ecm:createdByName", docCreatorName },
                            { "ecm:bnkStatus", "AKTIVAN" }
                            // TODO: ecm:bnkRealizationOPUID - vrednost jos nije poznata, mozda ce se pozivati servis
                            // { "ecm:bnkRealizationOPUID", "<vrednost>" }
                        };

                        var updated = await _writeApi.UpdateNodePropertiesAsync(item.DestinationFolderId, properties, ct);

                        if (updated)
                        {
                            item.IsUpdated = true;
                            item.UpdateMessage = "OK";
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

                    if (current % 10 == 0 || current == total)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            DossierProgressBar.Value = current;
                            TxtDossierProgress.Text = $"{current} / {total}";
                            TxtDossierUpdatedCount.Text = success.ToString();
                            TxtDossierFailedCount.Text = failed.ToString();
                            TxtDossierNotUpdatedCount.Text = _dossierItems.Count(x => !x.IsUpdated).ToString();
                            TxtDossierStatus.Text = $"Azuriranje: {current}/{total}";
                        });
                    }

                    if (current % 100 == 0)
                    {
                        AppendDossierLog($"Progress: {current}/{total} (uspesno: {success}, neuspesno: {failed})");
                    }
                });

                // Refresh DataGrid
                DgDossiers.ItemsSource = new ObservableCollection<DossierUpdateItem>(_dossierItems);

                TxtDossierStatus.Text = "Azuriranje zavrseno";
                TxtDossierUpdatedCount.Text = success.ToString();
                TxtDossierFailedCount.Text = failed.ToString();
                TxtDossierNotUpdatedCount.Text = _dossierItems.Count(x => !x.IsUpdated).ToString();

                AppendDossierLog($"Azuriranje zavrseno. Uspesno: {success}, Neuspesno: {failed}");

                if (failed > 0)
                {
                    MessageBox.Show($"Azuriranje zavrseno.\nUspesno: {success}\nNeuspesno: {failed}",
                        "Rezultat", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"Svi dosieji su uspesno azurirani ({success}).",
                        "Rezultat", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (OperationCanceledException)
            {
                AppendDossierLog("Azuriranje je otkazano.");
                TxtDossierStatus.Text = "Otkazano";
                DgDossiers.ItemsSource = new ObservableCollection<DossierUpdateItem>(_dossierItems);
            }
            catch (Exception ex)
            {
                AppendDossierLog($"GRESKA pri azuriranju: {ex.Message}");
                TxtDossierStatus.Text = "Greska";
                MessageBox.Show($"Greska pri azuriranju:\n{ex.Message}", "Greska", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetDossierBusy(false);
                _cts?.Dispose();
                _cts = null;
            }
        }

        #endregion

        #region Dossier Cancel

        private void BtnDossierCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            AppendDossierLog("Otkazivanje operacije...");
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

        private void AppendDossierLog(string message)
        {
            Dispatcher.Invoke(() =>
            {
                TxtDossierLog.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}\n");
                TxtDossierLog.ScrollToEnd();
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

        private void SetDossierBusy(bool busy, string? status = null)
        {
            Dispatcher.Invoke(() =>
            {
                BtnDossierLoad.IsEnabled = !busy;
                BtnDossierUpdate.IsEnabled = !busy && _dossierItems.Any(x => !x.IsUpdated);
                BtnDossierCancel.IsEnabled = busy;

                if (status != null)
                    TxtDossierStatus.Text = status;
                else if (!busy)
                    TxtDossierStatus.Text = "Spremno";
            });
        }

        #endregion
    }

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

    public class DossierUpdateItem
    {
        public int RowNumber { get; set; }
        public string DossierDestFolderId { get; set; } = "";
        public string DestinationFolderId { get; set; } = "";
        public bool IsUpdated { get; set; }
        public string UpdateMessage { get; set; } = "";
    }
}
