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
        private ObservableCollection<DocumentMappingViewModel> _displayedDocuments = new();

        private int _currentPage = 1;
        private const int _pageSize = 100;
        private int _totalCount = 0;
        private string _currentSearchText = "";
        private CancellationTokenSource? _searchCts;

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
                await LoadDocumentsAsync(resetPage: true);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading documents: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                DialogResult = false;
                Close();
            }
        }

        private async Task LoadDocumentsAsync(bool resetPage = false)
        {
            try
            {
                if (resetPage)
                {
                    _currentPage = 1;
                    _displayedDocuments.Clear();
                }

                // Begin transaction (read-only, but needed for connection)
                await _unitOfWork.BeginAsync(System.Data.IsolationLevel.ReadUncommitted);

                var (items, totalCount) = await _documentMappingRepository.SearchWithPagingAsync(
                    _currentSearchText,
                    _currentPage,
                    _pageSize);

                await _unitOfWork.CommitAsync();

                _totalCount = totalCount;

                // Filter out mappings without Naziv (ecm:docDesc)
                var validMappings = items.Where(m => !string.IsNullOrWhiteSpace(m.Naziv));

                foreach (var mapping in validMappings)
                {
                    _displayedDocuments.Add(new DocumentMappingViewModel
                    {
                        ID = mapping.ID,
                        Naziv = mapping.Naziv ?? "",
                        SifraDokumenta = mapping.SifraDokumenta ?? "",
                        NazivDokumenta = mapping.NazivDokumenta ?? "",
                        TipDosijea = mapping.TipDosijea ?? "",
                        BrojDokumenata = mapping.BrojDokumenata ?? 0,
                        IsSelected = false
                    });
                }

                lstDocuments.ItemsSource = _displayedDocuments;

                UpdatePagingInfo();
                UpdateSelectionCount();
            }
            catch (Exception ex)
            {
                await _unitOfWork.RollbackAsync();
                MessageBox.Show($"Failed to load documents from database: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void txtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Cancel previous search
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var currentCts = _searchCts;

            try
            {
                // Debounce - wait 500ms before searching
                await Task.Delay(500, currentCts.Token);

                _currentSearchText = txtSearch.Text?.Trim() ?? "";
                await LoadDocumentsAsync(resetPage: true);
            }
            catch (TaskCanceledException)
            {
                // Search was cancelled, ignore
            }
        }

        private void chkSelectAll_Checked(object sender, RoutedEventArgs e)
        {
            foreach (var doc in _displayedDocuments)
            {
                doc.IsSelected = true;
            }
            UpdateSelectionCount();
        }

        private void chkSelectAll_Unchecked(object sender, RoutedEventArgs e)
        {
            foreach (var doc in _displayedDocuments)
            {
                doc.IsSelected = false;
            }
            UpdateSelectionCount();
        }

        private void DocumentCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSelectionCount();
        }

        private void UpdateSelectionCount()
        {
            var selectedCount = _displayedDocuments.Count(d => d.IsSelected);

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

            // Update Select All checkbox state
            if (_displayedDocuments.Any())
            {
                if (_displayedDocuments.All(d => d.IsSelected))
                {
                    chkSelectAll.IsChecked = true;
                }
                else if (_displayedDocuments.All(d => !d.IsSelected))
                {
                    chkSelectAll.IsChecked = false;
                }
                else
                {
                    chkSelectAll.IsChecked = null; // Indeterminate state
                }
            }
        }

        private void UpdatePagingInfo()
        {
            var totalPages = (_totalCount + _pageSize - 1) / _pageSize;
            var showing = _displayedDocuments.Count;
            txtPagingInfo.Text = $"Showing {showing} of {_totalCount} documents (Page {_currentPage} of {totalPages})";

            btnPrevPage.IsEnabled = _currentPage > 1;
            btnNextPage.IsEnabled = _currentPage < totalPages;
        }

        private async void btnPrevPage_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await LoadDocumentsAsync(resetPage: true);
            }
        }

        private async void btnNextPage_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = (_totalCount + _pageSize - 1) / _pageSize;
            if (_currentPage < totalPages)
            {
                _currentPage++;
                await LoadDocumentsAsync(resetPage: true);
            }
        }

        private void btnOk_Click(object sender, RoutedEventArgs e)
        {
            // Get selected document descriptions (ecm:docDesc values)
            SelectedDocDescriptions = _displayedDocuments
                .Where(d => d.IsSelected)
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
