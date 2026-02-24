using Alfresco.Contracts.DtoModels;
using Microsoft.Extensions.DependencyInjection;
using SqlServer.Abstraction.Interfaces;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace Alfresco.App.Windows
{
    public partial class GroupDrilldownWindow : Window
    {
        private readonly DocumentGroupType _group;

        private const int PageSize = 50;
        private int _currentPage = 1;
        private int _totalCount = 0;
        private int _currentFilteredCount = 0;

        /// <summary>
        /// Rezultat selekcije — null ako je korisnik zatvorio bez izbora.
        /// </summary>
        public GroupSelection? ChosenSelection { get; private set; }

        public GroupDrilldownWindow(DocumentGroupType group)
        {
            InitializeComponent();
            _group = group;
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            txtGroupTitle.Text = $"Pretraga unutar grupe: \"{_group.BaseNaziv}\"";
            txtGroupStats.Text = $"{_group.VariantCount:N0} varijanti  |  {_group.TotalDocuments:N0} dokumenta";
            txtAllVariantsDesc.Text = $"Dodaje sve ~{_group.VariantCount:N0} varijante kao jedan wildcard uslov";

            await LoadVariantsAsync(null, 1);
        }

        private async void btnSearch_Click(object sender, RoutedEventArgs e)
        {
            _currentPage = 1;
            await LoadVariantsAsync(txtFilter.Text.Trim(), 1);
        }

        private async void txtFilter_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                _currentPage = 1;
                await LoadVariantsAsync(txtFilter.Text.Trim(), 1);
            }
        }

        private async void btnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1)
            {
                _currentPage--;
                await LoadVariantsAsync(txtFilter.Text.Trim(), _currentPage);
            }
        }

        private async void btnNext_Click(object sender, RoutedEventArgs e)
        {
            var totalPages = (_totalCount + PageSize - 1) / PageSize;
            if (_currentPage < totalPages)
            {
                _currentPage++;
                await LoadVariantsAsync(txtFilter.Text.Trim(), _currentPage);
            }
        }

        private async System.Threading.Tasks.Task LoadVariantsAsync(string? invoiceFilter, int page)
        {
            try
            {
                IsEnabled = false;
                var filter = string.IsNullOrWhiteSpace(invoiceFilter) ? null : invoiceFilter;

                await using var scope = App.AppHost.Services.CreateAsyncScope();
                var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
                var repository = scope.ServiceProvider.GetRequiredService<IDocumentMappingRepository>();

                await unitOfWork.BeginAsync(System.Data.IsolationLevel.ReadUncommitted);
                var (items, total) = await repository.SearchWithinGroupAsync(
                    _group.BaseNaziv,
                    filter,
                    page,
                    PageSize,
                    CancellationToken.None);
                await unitOfWork.CommitAsync();

                _totalCount = total;
                _currentFilteredCount = total;
                _currentPage = page;

                lstVariants.ItemsSource = items.Select(i => new
                {
                    Naziv = i.Naziv,
                    BrojDokumenata = i.BrojDokumenata ?? 0
                }).ToList();

                var totalPages = Math.Max(1, (total + PageSize - 1) / PageSize);
                txtPageInfo.Text = $"Str {page} / {totalPages}";
                txtFoundCount.Text = filter != null
                    ? $"Nađeno: ~{total:N0} varijanti za filter \"{filter}*\""
                    : $"Ukupno: {total:N0} varijanti";

                btnPrev.IsEnabled = page > 1;
                btnNext.IsEnabled = page < totalPages;

                // Aktiviraj "Dodaj filter" dugme samo kad postoji filter
                btnAddFilter.IsEnabled = filter != null && total > 0;
                if (filter != null && total > 0)
                {
                    btnAddFilter.Content = $"Dodaj filter \"{filter}*\" (~{total:N0} var.)";
                    txtFilterHint.Text = $"→ matchuje \"{_group.BaseNaziv} {filter}*\"";
                }
                else
                {
                    btnAddFilter.Content = "Dodaj filter";
                    txtFilterHint.Text = filter != null
                        ? $"→ matchuje \"{_group.BaseNaziv} {filter}*\""
                        : "";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Greška pri učitavanju: {ex.Message}", "Greška",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsEnabled = true;
            }
        }

        private void btnAddAll_Click(object sender, RoutedEventArgs e)
        {
            ChosenSelection = new GroupSelection
            {
                BaseNaziv = _group.BaseNaziv,
                InvoiceFilter = null,
                VariantCount = _group.VariantCount
            };
            DialogResult = true;
        }

        private void btnAddFilter_Click(object sender, RoutedEventArgs e)
        {
            var filter = txtFilter.Text.Trim();
            if (string.IsNullOrWhiteSpace(filter)) return;

            ChosenSelection = new GroupSelection
            {
                BaseNaziv = _group.BaseNaziv,
                InvoiceFilter = filter,
                VariantCount = _currentFilteredCount
            };
            DialogResult = true;
        }

        private void btnClose_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
