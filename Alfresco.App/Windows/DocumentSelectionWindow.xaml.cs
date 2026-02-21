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

namespace Alfresco.App.Windows
{
    public partial class DocumentSelectionWindow : Window
    {
        private readonly IDocumentMappingRepository _documentMappingRepository;
        private readonly IUnitOfWork _unitOfWork;

        // Three separate collections
        private ObservableCollection<DocumentMappingViewModel> _dbDocuments = new();
        private ObservableCollection<DocumentMappingViewModel> _selectedDocuments = new();

        private int _currentPage = 1;
        private const int _pageSize = 100;
        private int _totalCount = 0;
        private string _currentSearchText = "";
        private string? _currentTipDosijea = null;
        private CancellationTokenSource? _searchCts;
        private bool _isLoading = false;
        private bool _isSyncingSelection = false;

        // toRet - populated on OK
        public List<string> SelectedDocDescriptions { get; private set; } = new();

        public DocumentSelectionWindow()
        {
            InitializeComponent();
            var scope = App.AppHost.Services.CreateScope();
            _unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
            _documentMappingRepository = scope.ServiceProvider.GetRequiredService<IDocumentMappingRepository>();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                lstDocuments.ItemsSource = _dbDocuments;
                lstSelected.ItemsSource = _selectedDocuments;

                await LoadTipDosijeaFilterAsync();
                await LoadDocumentsAsync(resetPage: true);

                // Subscribe to filter changes after initial load to avoid double-loading
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
            await _unitOfWork.BeginAsync(System.Data.IsolationLevel.ReadUncommitted);
            var tipValues = await _documentMappingRepository.GetDistinctTipDosijeaAsync();
            await _unitOfWork.CommitAsync();

            cmbTipDosijea.Items.Clear();
            cmbTipDosijea.Items.Add("Svi");
            foreach (var tip in tipValues)
            {
                cmbTipDosijea.Items.Add(tip);
            }
            cmbTipDosijea.SelectedIndex = 0;
        }

        private async Task LoadDocumentsAsync(bool resetPage = false)
        {
            if (_isLoading) return;

            try
            {
                _isLoading = true;

                if (resetPage)
                    _currentPage = 1;

                _dbDocuments.Clear();

                await _unitOfWork.BeginAsync(System.Data.IsolationLevel.ReadUncommitted);

                var (items, totalCount) = await _documentMappingRepository.SearchWithPagingAsync(
                    _currentSearchText,
                    _currentPage,
                    _pageSize,
                    _currentTipDosijea);

                await _unitOfWork.CommitAsync();

                _totalCount = totalCount;

                var validMappings = items.Where(m => !string.IsNullOrWhiteSpace(m.Naziv));

                foreach (var mapping in validMappings)
                {
                    var isAlreadySelected = _selectedDocuments.Any(s => s.ID == mapping.ID);
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
                try { await _unitOfWork.RollbackAsync(); } catch { }
                MessageBox.Show($"Failed to load documents from database: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            _isLoading = false;
        }

        private async void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var currentCts = _searchCts;

            try
            {
                await Task.Delay(500, currentCts.Token);
                _currentSearchText = txtSearch.Text?.Trim() ?? "";
                await LoadDocumentsAsync(resetPage: true);
            }
            catch (TaskCanceledException) { }
        }

        private async void cmbTipDosijea_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var selected = cmbTipDosijea.SelectedItem as string;
            _currentTipDosijea = selected == "Svi" ? null : selected;
            await LoadDocumentsAsync(resetPage: true);
        }

        private void chkSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            _isSyncingSelection = true;
            foreach (var doc in _dbDocuments)
            {
                doc.IsSelected = true;
                if (!_selectedDocuments.Any(s => s.ID == doc.ID))
                {
                    _selectedDocuments.Add(CloneViewModel(doc));
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
                var existing = _selectedDocuments.FirstOrDefault(s => s.ID == doc.ID);
                if (existing != null)
                    _selectedDocuments.Remove(existing);
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
                    if (!_selectedDocuments.Any(s => s.ID == vm.ID))
                        _selectedDocuments.Add(CloneViewModel(vm));
                }
                else
                {
                    var existing = _selectedDocuments.FirstOrDefault(s => s.ID == vm.ID);
                    if (existing != null)
                        _selectedDocuments.Remove(existing);
                }
            }
            UpdateSelectionCount();
        }

        private void btnRemoveSelected_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is DocumentMappingViewModel vm)
            {
                _selectedDocuments.Remove(vm);

                // Uncheck in db list if visible
                var dbItem = _dbDocuments.FirstOrDefault(d => d.ID == vm.ID);
                if (dbItem != null)
                    dbItem.IsSelected = false;

                UpdateSelectionCount();
            }
        }

        private void UpdateSelectionCount()
        {
            var selectedCount = _selectedDocuments.Count;

            txtSelectedHeader.Text = $"Selected Documents ({selectedCount})";

            if (selectedCount == 0)
            {
                txtSelectionCount.Text = "No documents selected";
                btnOk.IsEnabled = false;
            }
            else if (selectedCount == 1)
            {
                txtSelectionCount.Text = "1 document selected";
                btnOk.IsEnabled = true;
            }
            else
            {
                txtSelectionCount.Text = $"{selectedCount} documents selected";
                btnOk.IsEnabled = true;
            }

            // Update Select All checkbox state based on current page
            UpdateSelectAllCheckbox();
        }

        private void UpdateSelectAllCheckbox()
        {
            if (!_dbDocuments.Any()) return;

            // Temporarily detach events to avoid recursion
            chkSelectAll.Checked -= chkSelectAll_Checked;
            chkSelectAll.Unchecked -= chkSelectAll_Unchecked;

            if (_dbDocuments.All(d => d.IsSelected))
                chkSelectAll.IsChecked = true;
            else if (_dbDocuments.All(d => !d.IsSelected))
                chkSelectAll.IsChecked = false;
            else
                chkSelectAll.IsChecked = null;

            chkSelectAll.Checked += chkSelectAll_Checked;
            chkSelectAll.Unchecked += chkSelectAll_Unchecked;
        }

        private void UpdatePagingInfo()
        {
            var totalPages = Math.Max(1, (_totalCount + _pageSize - 1) / _pageSize);
            var showing = _dbDocuments.Count;
            txtPagingInfo.Text = $"Showing {showing} of {_totalCount} documents (Page {_currentPage} of {totalPages})";

            btnPrevPage.IsEnabled = _currentPage > 1;
            btnNextPage.IsEnabled = _currentPage < totalPages;
        }

        private async void btnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await LoadDocumentsAsync(resetPage: false);
            }
        }

        private async void btnNextPage_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = (_totalCount + _pageSize - 1) / _pageSize;
            if (_currentPage < totalPages)
            {
                _currentPage++;
                await LoadDocumentsAsync(resetPage: false);
            }
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            // toRet - build from selected collection
            SelectedDocDescriptions = _selectedDocuments
                .Select(d => d.Naziv)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct()
                .ToList();

            if (!SelectedDocDescriptions.Any())
            {
                MessageBox.Show("Please select at least one document.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void btnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private static DocumentMappingViewModel CloneViewModel(DocumentMappingViewModel source)
        {
            return new DocumentMappingViewModel
            {
                ID = source.ID,
                Naziv = source.Naziv,
                SifraDokumenta = source.SifraDokumenta,
                NazivDokumenta = source.NazivDokumenta,
                TipDosijea = source.TipDosijea,
                BrojDokumenata = source.BrojDokumenata,
                IsSelected = true
            };
        }
    }

    /// <summary>
    /// ViewModel for displaying DocumentMapping in the UI with selection support
    /// </summary>
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
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    NotifyPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
