using Alfresco.Contracts.DtoModels;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.DependencyInjection;
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
using System.Windows.Media;

namespace Alfresco.App.Windows
{
    public partial class DocumentSelectionWindow : Window
    {
        private enum ViewMode { Standard, Grouped }
        private ViewMode _viewMode = ViewMode.Standard;

        // Standard mod
        private ObservableCollection<DocumentMappingViewModel> _dbDocuments = new();

        // Grupisani mod
        private ObservableCollection<GroupedRowViewModel> _groupedDocuments = new();

        // Desni panel — unified za oba moda
        private ObservableCollection<SelectedItemViewModel> _selectedItems = new();

        private int _currentPage = 1;
        private const int _pageSize = 100;
        private int _totalCount = 0;
        private string _currentSearchText = "";
        private string? _currentTipDosijea = null;
        private CancellationTokenSource? _searchCts;
        private bool _isLoading = false;
        private bool _isSyncingSelection = false;

        /// <summary>
        /// Rezultat selekcije — koristi ga MigrationPhaseMonitor.
        /// </summary>
        public DocumentSelectionResult SelectionResult { get; private set; } = new();

        /// <summary>
        /// Backwards compat — koristi se za text prikaz u MigrationPhaseMonitor.
        /// </summary>
        public List<string> SelectedDocDescriptions { get; private set; } = new();

        public DocumentSelectionWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                lstDocuments.ItemsSource = _dbDocuments;
                lstGroupedDocuments.ItemsSource = _groupedDocuments;
                lstSelected.ItemsSource = _selectedItems;

                await LoadTipDosijeaFilterAsync();
                await LoadDocumentsAsync(resetPage: true);

                // Subscribe after initial load to avoid double-loading
                cmbTipDosijea.SelectionChanged += cmbTipDosijea_SelectionChanged;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading documents: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private async Task LoadTipDosijeaFilterAsync()
        {
            await using var scope = App.AppHost.Services.CreateAsyncScope();
            var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            var repository = scope.ServiceProvider.GetRequiredService<IDocumentMappingRepository>();

            await unitOfWork.BeginAsync(System.Data.IsolationLevel.ReadUncommitted);
            var tipValues = await repository.GetDistinctTipDosijeaAsync();
            await unitOfWork.CommitAsync();

            cmbTipDosijea.Items.Clear();
            cmbTipDosijea.Items.Add("Svi");
            foreach (var tip in tipValues)
                cmbTipDosijea.Items.Add(tip);
            cmbTipDosijea.SelectedIndex = 0;
        }

        // ═══════════════════════════════════════════════════════════
        // VIEW MODE
        // ═══════════════════════════════════════════════════════════

        private async void rbStandard_Checked(object sender, RoutedEventArgs e)
        {
            if (_viewMode == ViewMode.Standard) return;
            _viewMode = ViewMode.Standard;
            SwitchToStandardView();
            await LoadDocumentsAsync(resetPage: true);
        }

        private async void rbGrouped_Checked(object sender, RoutedEventArgs e)
        {
            if (_viewMode == ViewMode.Grouped) return;
            _viewMode = ViewMode.Grouped;
            SwitchToGroupedView();
            await LoadGroupedDocumentsAsync(resetPage: true);
        }

        private void SwitchToStandardView()
        {
            pnlStandardHeader.Visibility = Visibility.Visible;
            pnlGroupedHeader.Visibility = Visibility.Collapsed;
            pnlStandardList.Visibility = Visibility.Visible;
            pnlGroupedList.Visibility = Visibility.Collapsed;
            chkSelectAll.Visibility = Visibility.Visible;
        }

        private void SwitchToGroupedView()
        {
            pnlStandardHeader.Visibility = Visibility.Collapsed;
            pnlGroupedHeader.Visibility = Visibility.Visible;
            pnlStandardList.Visibility = Visibility.Collapsed;
            pnlGroupedList.Visibility = Visibility.Visible;
            chkSelectAll.Visibility = Visibility.Collapsed;
        }

        // ═══════════════════════════════════════════════════════════
        // STANDARD MOD — Load
        // ═══════════════════════════════════════════════════════════

        private async Task LoadDocumentsAsync(bool resetPage = false)
        {
            if (_isLoading) return;
            try
            {
                _isLoading = true;
                if (resetPage) _currentPage = 1;
                _dbDocuments.Clear();

                await using var scope = App.AppHost.Services.CreateAsyncScope();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repository = scope.ServiceProvider.GetRequiredService<IDocumentMappingRepository>();

                await unitOfWork.BeginAsync(System.Data.IsolationLevel.ReadUncommitted);
                var (items, totalCount) = await repository.SearchWithPagingAsync(
                    _currentSearchText, _currentPage, _pageSize, _currentTipDosijea);
                await unitOfWork.CommitAsync();

                _totalCount = totalCount;
                foreach (var mapping in items.Where(m => !string.IsNullOrWhiteSpace(m.Naziv)))
                {
                    var isAlreadySelected = _selectedItems.Any(s => !s.IsGroup && s.DocumentId == mapping.ID);
                    _dbDocuments.Add(new DocumentMappingViewModel
                    {
                        ID = mapping.ID,
                        Naziv = mapping.Naziv ?? "",
                        SifraDokumenta = mapping.SifraDokumenta ?? "",
                        NazivDokumenta = mapping.NazivDokumenta ?? "",
                        TipDosijea = mapping.TipDosijea ?? "",
                        BrojDokumenata = mapping.BrojDokumenata ?? 0,
                        IsSelected = isAlreadySelected
                    });
                }
                UpdatePagingInfo();
                UpdateSelectionCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load documents: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { _isLoading = false; }
        }

        // ═══════════════════════════════════════════════════════════
        // GRUPISANI MOD — Load
        // ═══════════════════════════════════════════════════════════

        private async Task LoadGroupedDocumentsAsync(bool resetPage = false)
        {
            if (_isLoading) return;
            try
            {
                _isLoading = true;
                if (resetPage) _currentPage = 1;
                _groupedDocuments.Clear();

                await using var scope = App.AppHost.Services.CreateAsyncScope();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repository = scope.ServiceProvider.GetRequiredService<IDocumentMappingRepository>();

                await unitOfWork.BeginAsync(System.Data.IsolationLevel.ReadUncommitted);
                var (items, totalCount) = await repository.GetGroupedViewAsync(
                    _currentSearchText, _currentTipDosijea, _currentPage, _pageSize);
                await unitOfWork.CommitAsync();

                _totalCount = totalCount;
                foreach (var row in items)
                {
                    var isGroup = row.RowType == "GROUP";
                    var vm = new GroupedRowViewModel
                    {
                        IsGroup = isGroup,
                        DisplayNaziv = row.DisplayNaziv,
                        VariantCount = row.VariantCount,
                        TotalDocuments = row.TotalDocuments,
                        TipDosijea = row.TipDosijea,
                        SifraDokumenta = row.SifraDokumenta ?? "",
                        DocumentId = row.Id,
                        BaseNaziv = isGroup ? row.DisplayNaziv : null,
                        // Reflect existing SINGLE selection
                        IsSelected = !isGroup && _selectedItems.Any(s => !s.IsGroup && s.DocumentId == row.Id)
                    };
                    _groupedDocuments.Add(vm);
                }
                UpdatePagingInfo();
                UpdateSelectionCount();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load grouped documents: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { _isLoading = false; }
        }

        // ═══════════════════════════════════════════════════════════
        // EVENT HANDLERS
        // ═══════════════════════════════════════════════════════════

        private async void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var currentCts = _searchCts;
            try
            {
                await Task.Delay(500, currentCts.Token);
                _currentSearchText = txtSearch.Text?.Trim() ?? "";
                if (_viewMode == ViewMode.Standard)
                    await LoadDocumentsAsync(resetPage: true);
                else
                    await LoadGroupedDocumentsAsync(resetPage: true);
            }
            catch (TaskCanceledException) { }
        }

        private async void cmbTipDosijea_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = cmbTipDosijea.SelectedItem as string;
            _currentTipDosijea = selected == "Svi" ? null : selected;
            if (_viewMode == ViewMode.Standard)
                await LoadDocumentsAsync(resetPage: true);
            else
                await LoadGroupedDocumentsAsync(resetPage: true);
        }

        private void chkSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            _isSyncingSelection = true;
            foreach (var doc in _dbDocuments)
            {
                doc.IsSelected = true;
                if (!_selectedItems.Any(s => !s.IsGroup && s.DocumentId == doc.ID))
                {
                    _selectedItems.Add(new SelectedItemViewModel
                    {
                        IsGroup = false,
                        DocumentId = doc.ID,
                        Naziv = doc.Naziv,
                        SifraDokumenta = doc.SifraDokumenta,
                        TipDosijea = doc.TipDosijea
                    });
                }
            }
            _isSyncingSelection = false;
            UpdateSelectionCount();
        }

        private void chkSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            _isSyncingSelection = true;
            foreach (var doc in _dbDocuments)
            {
                doc.IsSelected = false;
                var existing = _selectedItems.FirstOrDefault(s => !s.IsGroup && s.DocumentId == doc.ID);
                if (existing != null) _selectedItems.Remove(existing);
            }
            _isSyncingSelection = false;
            UpdateSelectionCount();
        }

        private void DocumentCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSyncingSelection) return;
            if (sender is CheckBox chk && chk.DataContext is DocumentMappingViewModel vm)
            {
                if (vm.IsSelected)
                {
                    if (!_selectedItems.Any(s => !s.IsGroup && s.DocumentId == vm.ID))
                        _selectedItems.Add(new SelectedItemViewModel
                        {
                            IsGroup = false,
                            DocumentId = vm.ID,
                            Naziv = vm.Naziv,
                            SifraDokumenta = vm.SifraDokumenta,
                            TipDosijea = vm.TipDosijea
                        });
                }
                else
                {
                    var existing = _selectedItems.FirstOrDefault(s => !s.IsGroup && s.DocumentId == vm.ID);
                    if (existing != null) _selectedItems.Remove(existing);
                }
            }
            UpdateSelectionCount();
        }

        private void GroupedDocumentCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (_isSyncingSelection) return;
            if (sender is not CheckBox chk || chk.DataContext is not GroupedRowViewModel vm) return;

            if (vm.IsGroup)
            {
                if (vm.IsSelected)
                {
                    var chosen = new GroupSelection
                    {
                        BaseNaziv = vm.DisplayNaziv,
                        InvoiceFilter = null,
                        VariantCount = vm.VariantCount
                    };
                    var existing = _selectedItems.FirstOrDefault(s => s.IsGroup &&
                        s.GroupSelection?.BaseNaziv == chosen.BaseNaziv);
                    if (existing != null) _selectedItems.Remove(existing);
                    _selectedItems.Add(new SelectedItemViewModel
                    {
                        IsGroup = true,
                        GroupSelection = chosen,
                        TipDosijea = vm.TipDosijea ?? ""
                    });
                }
                else
                {
                    var existing = _selectedItems.FirstOrDefault(s => s.IsGroup &&
                        s.GroupSelection?.BaseNaziv == vm.DisplayNaziv);
                    if (existing != null) _selectedItems.Remove(existing);
                }
            }
            else
            {
                if (vm.IsSelected)
                {
                    if (!_selectedItems.Any(s => !s.IsGroup && s.DocumentId == vm.DocumentId))
                        _selectedItems.Add(new SelectedItemViewModel
                        {
                            IsGroup = false,
                            DocumentId = vm.DocumentId,
                            Naziv = vm.DisplayNaziv,
                            SifraDokumenta = vm.SifraDokumenta,
                            TipDosijea = vm.TipDosijea ?? ""
                        });
                }
                else
                {
                    var existing = _selectedItems.FirstOrDefault(s => !s.IsGroup && s.DocumentId == vm.DocumentId);
                    if (existing != null) _selectedItems.Remove(existing);
                }
            }
            UpdateSelectionCount();
        }

        private void btnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is SelectedItemViewModel vm)
            {
                _selectedItems.Remove(vm);

                // Ukloni checkmark u listi
                if (vm.IsGroup)
                {
                    var grpItem = _groupedDocuments.FirstOrDefault(g => g.IsGroup &&
                        g.DisplayNaziv == vm.GroupSelection?.BaseNaziv);
                    if (grpItem != null) grpItem.IsSelected = false;
                }
                else
                {
                    var dbItem = _dbDocuments.FirstOrDefault(d => d.ID == vm.DocumentId);
                    if (dbItem != null) dbItem.IsSelected = false;
                    var grpItem = _groupedDocuments.FirstOrDefault(g => !g.IsGroup && g.DocumentId == vm.DocumentId);
                    if (grpItem != null) grpItem.IsSelected = false;
                }
                UpdateSelectionCount();
            }
        }

        // ═══════════════════════════════════════════════════════════
        // PAGINACIJA
        // ═══════════════════════════════════════════════════════════

        private async void btnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                if (_viewMode == ViewMode.Standard)
                    await LoadDocumentsAsync(resetPage: false);
                else
                    await LoadGroupedDocumentsAsync(resetPage: false);
            }
        }

        private async void btnNextPage_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = (_totalCount + _pageSize - 1) / _pageSize;
            if (_currentPage < totalPages)
            {
                _currentPage++;
                if (_viewMode == ViewMode.Standard)
                    await LoadDocumentsAsync(resetPage: false);
                else
                    await LoadGroupedDocumentsAsync(resetPage: false);
            }
        }

        // ═══════════════════════════════════════════════════════════
        // OK / CANCEL
        // ═══════════════════════════════════════════════════════════

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            SelectionResult = new DocumentSelectionResult
            {
                ExactDescriptions = _selectedItems
                    .Where(i => !i.IsGroup)
                    .Select(i => i.Naziv)
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Distinct()
                    .ToList(),
                GroupSelections = _selectedItems
                    .Where(i => i.IsGroup)
                    .Select(i => i.GroupSelection!)
                    .ToList()
            };

            if (!SelectionResult.HasAny)
            {
                MessageBox.Show("Please select at least one document.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Backwards compat
            SelectedDocDescriptions = SelectionResult.ExactDescriptions
                .Concat(SelectionResult.GroupSelections.Select(g => g.ToAlfrescoPattern()))
                .ToList();

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ═══════════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════════

        private void UpdateSelectionCount()
        {
            var count = _selectedItems.Count;
            txtSelectedHeader.Text = $"Selektovano ({count})";
            txtSelectionCount.Text = count == 0 ? "No documents selected"
                : count == 1 ? "1 item selected"
                : $"{count} items selected";
            btnOk.IsEnabled = count > 0;
            UpdateSelectAllCheckbox();
        }

        private void UpdateSelectAllCheckbox()
        {
            if (!_dbDocuments.Any()) return;
            chkSelectAll.Checked -= chkSelectAll_Checked;
            chkSelectAll.Unchecked -= chkSelectAll_Unchecked;

            if (_dbDocuments.All(d => d.IsSelected)) chkSelectAll.IsChecked = true;
            else if (_dbDocuments.All(d => !d.IsSelected)) chkSelectAll.IsChecked = false;
            else chkSelectAll.IsChecked = null;

            chkSelectAll.Checked += chkSelectAll_Checked;
            chkSelectAll.Unchecked += chkSelectAll_Unchecked;
        }

        private void UpdatePagingInfo()
        {
            var totalPages = Math.Max(1, (_totalCount + _pageSize - 1) / _pageSize);
            var showing = _viewMode == ViewMode.Standard ? _dbDocuments.Count : _groupedDocuments.Count;
            txtPagingInfo.Text = $"Showing {showing} of {_totalCount} (Page {_currentPage} of {totalPages})";
            btnPrevPage.IsEnabled = _currentPage > 1;
            btnNextPage.IsEnabled = _currentPage < totalPages;
        }
    }

    // ═══════════════════════════════════════════════════════════════
    // VIEW MODELS
    // ═══════════════════════════════════════════════════════════════

    public class DocumentMappingViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;
        public int ID { get; set; }
        public string Naziv { get; set; } = "";
        public string SifraDokumenta { get; set; } = "";
        public string NazivDokumenta { get; set; } = "";
        public string TipDosijea { get; set; } = "";
        public int BrojDokumenata { get; set; }
        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public class GroupedRowViewModel : INotifyPropertyChanged
    {
        private bool _isSelected;

        public bool IsGroup { get; set; }
        public bool IsSingle => !IsGroup;

        public string DisplayNaziv { get; set; } = "";
        public long TotalDocuments { get; set; }
        public string? TipDosijea { get; set; }

        // GROUP only
        public int VariantCount { get; set; }
        public string? BaseNaziv { get; set; }

        // SINGLE only
        public int? DocumentId { get; set; }
        public string SifraDokumenta { get; set; } = "";

        public bool IsSelected
        {
            get => _isSelected;
            set { if (_isSelected != value) { _isSelected = value; OnPropertyChanged(); } }
        }

        // Binding helpers
        public string VariantCountDisplay => IsGroup ? $"{VariantCount:N0}" : "-";
        public string TotalDocumentsDisplay => $"{TotalDocuments:N0}";
        public string RowBackground => IsGroup ? "#F5F0FF" : "White";
        public FontWeight NameWeight => IsGroup ? FontWeights.SemiBold : FontWeights.Normal;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }

    public class SelectedItemViewModel : INotifyPropertyChanged
    {
        public bool IsGroup { get; set; }

        // SINGLE
        public int? DocumentId { get; set; }
        public string Naziv { get; set; } = "";
        public string SifraDokumenta { get; set; } = "";
        public string TipDosijea { get; set; } = "";

        // GROUP
        public GroupSelection? GroupSelection { get; set; }

        // Binding helpers
        public string TagLabel => IsGroup ? "G" : "D";
        public string TagColor => IsGroup ? "#8E44AD" : "#2980B9";
        public string DisplayName => IsGroup ? GroupSelection!.DisplayName : Naziv;

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? p = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(p));
    }
}
