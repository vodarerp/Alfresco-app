using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Infrastructure.Implementation
{
    /// <summary>
    /// Repository za pristup DocumentMappings tabeli.
    /// Koristi direktne SQL upite sa indeksima za optimalne performanse sa 70,000+ zapisa.
    /// Keširaju se SAMO pojedinačni rezultati pretrage, NE cela tabela.
    /// Automatski obogaćuje svaki DocumentMapping sa kategorijama iz CategoryMapping tabele.
    /// </summary>
    public class DocumentMappingRepository : SqlServerRepository<DocumentMapping, int>, IDocumentMappingRepository
    {
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan DocumentCacheDuration = TimeSpan.FromHours(24); // Keširanje DocumentMapping zapisa
        private static readonly TimeSpan CategoryCacheDuration = TimeSpan.FromHours(24); // Duže keširanje za CategoryMapping (retko se menja)

        public DocumentMappingRepository(IUnitOfWork uow, IMemoryCache cache, SqlServerOptions sqlServerOptions) : base(uow, sqlServerOptions)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        /// <summary>
        /// Vraća sva mapiranja iz tabele.
        /// UPOZORENJE: Ova metoda vraća 70,000+ zapisa - koristi SAMO za export/admin operacije!
        /// Za regularne pretrage koristi FindByXxx metode koje koriste SQL indekse.
        /// </summary>
        public async Task<IReadOnlyList<DocumentMapping>> GetAllMappingsAsync(CancellationToken ct = default)
        {
            var sql = @"SELECT
                            ID,
                            NAZIV,
                            BROJ_DOKUMENATA,
                            sifraDokumenta,
                            NazivDokumenta,
                            TipDosijea,
                            TipProizvoda,
                            SifraDokumentaMigracija,
                            NazivDokumentaMigracija,
                            ExcelFileName,
                            ExcelFileSheet,
                            PolitikaCuvanja
                        FROM DocumentMappings WITH (NOLOCK)
                        ORDER BY ID";

            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var result = await Conn.QueryAsync<DocumentMapping>(cmd).ConfigureAwait(false);

            return result.AsList().AsReadOnly();
        }

        /// <summary>
        /// Pronalazi mapping po originalnom imenu dokumenta (NAZIV polje).
        /// Koristi SQL indeks za brzu pretragu (umesto LINQ kroz 70,000 zapisa).
        /// Kešira pojedinačne rezultate.
        /// </summary>
        public async Task<DocumentMapping?> FindByOriginalNameAsync(string originalName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(originalName))
                return null;

            var cacheKey = $"DocMapping_Name_{originalName.Trim().ToUpperInvariant()}";

            if (_cache.TryGetValue(cacheKey, out DocumentMapping? cached))
            {
                // Obogati sa kategorijom (CategoryMappingRepository ima svoj cache)
                await EnrichWithCategoryAsync(cached, ct).ConfigureAwait(false);
                return cached;
            }

            // SQL Server će koristiti indeks na NAZIV koloni (preporučuje se kreirati indeks)
            var sql = @"SELECT TOP 1
                            ID,
                            NAZIV,
                            BROJ_DOKUMENATA,
                            sifraDokumenta,
                            NazivDokumenta,
                            TipDosijea,
                            TipProizvoda,
                            SifraDokumentaMigracija,
                            NazivDokumentaMigracija,
                            ExcelFileName,
                            ExcelFileSheet,
                            PolitikaCuvanja
                        FROM DocumentMappings WITH (NOLOCK)
                        WHERE UPPER(NAZIV) = UPPER(@originalName)";

            var cmd = new CommandDefinition(
                sql,
                new { originalName = originalName.Trim() },
                transaction: Tx,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: ct);

            var result = await Conn.QueryFirstOrDefaultAsync<DocumentMapping>(cmd).ConfigureAwait(false);

            if (result != null)
            {
                // Obogati sa kategorijom PRE keširanja
                await EnrichWithCategoryAsync(result, ct).ConfigureAwait(false);
                _cache.Set(cacheKey, result, DocumentCacheDuration);
            }

            return result;
        }

        /// <summary>
        /// Pronalazi mapping po originalnoj šifri dokumenta (sifraDokumenta polje).
        /// Koristi SQL indeks za brzu pretragu. Kešira pojedinačne rezultate.
        /// </summary>
        public async Task<DocumentMapping?> FindByOriginalCodeAsync(string originalCode, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(originalCode))
                return null;

            var cacheKey = $"DocMapping_Code_{originalCode.Trim().ToUpperInvariant()}";

            if (_cache.TryGetValue(cacheKey, out DocumentMapping? cached))
            {
                // Obogati sa kategorijom (CategoryMappingRepository ima svoj cache)
                await EnrichWithCategoryAsync(cached, ct).ConfigureAwait(false);
                return cached;
            }

            var sql = @"SELECT TOP 1
                            ID,
                            NAZIV,
                            BROJ_DOKUMENATA,
                            sifraDokumenta,
                            NazivDokumenta,
                            TipDosijea,
                            TipProizvoda,
                            SifraDokumentaMigracija,
                            NazivDokumentaMigracija,
                            ExcelFileName,
                            ExcelFileSheet,
                            PolitikaCuvanja
                        FROM DocumentMappings WITH (NOLOCK)
                        WHERE UPPER(sifraDokumenta) = UPPER(@originalCode)";

            var cmd = new CommandDefinition(
                sql,
                new { originalCode = originalCode.Trim() },
                transaction: Tx,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: ct);

            var result = await Conn.QueryFirstOrDefaultAsync<DocumentMapping>(cmd).ConfigureAwait(false);

            if (result != null)
            {
                // Obogati sa kategorijom PRE keširanja
                await EnrichWithCategoryAsync(result, ct).ConfigureAwait(false);
                _cache.Set(cacheKey, result, DocumentCacheDuration);
            }

            return result;
        }

        /// <summary>
        /// Pronalazi mapping po srpskom nazivu dokumenta (NazivDokumenta polje).
        /// Koristi SQL indeks za brzu pretragu. Kešira pojedinačne rezultate.
        /// </summary>
        public async Task<DocumentMapping?> FindBySerbianNameAsync(string serbianName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(serbianName))
                return null;

            var cacheKey = $"DocMapping_SerbName_{serbianName.Trim().ToUpperInvariant()}";

            if (_cache.TryGetValue(cacheKey, out DocumentMapping? cached))
            {
                // Obogati sa kategorijom (CategoryMappingRepository ima svoj cache)
                await EnrichWithCategoryAsync(cached, ct).ConfigureAwait(false);
                return cached;
            }

            var sql = @"SELECT TOP 1
                            ID,
                            NAZIV,
                            BROJ_DOKUMENATA,
                            sifraDokumenta,
                            NazivDokumenta,
                            TipDosijea,
                            TipProizvoda,
                            SifraDokumentaMigracija,
                            NazivDokumentaMigracija,
                            ExcelFileName,
                            ExcelFileSheet,
                            PolitikaCuvanja
                        FROM DocumentMappings WITH (NOLOCK)
                        WHERE UPPER(NazivDokumenta) = UPPER(@serbianName)";

            var cmd = new CommandDefinition(
                sql,
                new { serbianName = serbianName.Trim() },
                transaction: Tx,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: ct);

            var result = await Conn.QueryFirstOrDefaultAsync<DocumentMapping>(cmd).ConfigureAwait(false);

            if (result != null)
            {
                // Obogati sa kategorijom PRE keširanja
                await EnrichWithCategoryAsync(result, ct).ConfigureAwait(false);
                _cache.Set(cacheKey, result, DocumentCacheDuration);
            }

            return result;
        }

        /// <summary>
        /// Pronalazi mapping po migriranom nazivu dokumenta (NazivDokumentaMigracija polje).
        /// Koristi SQL indeks za brzu pretragu. Kešira pojedinačne rezultate.
        /// </summary>
        public async Task<DocumentMapping?> FindByMigratedNameAsync(string migratedName, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(migratedName))
                return null;

            var cacheKey = $"DocMapping_MigName_{migratedName.Trim().ToUpperInvariant()}";

            if (_cache.TryGetValue(cacheKey, out DocumentMapping? cached))
            {
                // Obogati sa kategorijom (CategoryMappingRepository ima svoj cache)
                await EnrichWithCategoryAsync(cached, ct).ConfigureAwait(false);
                return cached;
            }

            var sql = @"SELECT TOP 1
                            ID,
                            NAZIV,
                            BROJ_DOKUMENATA,
                            sifraDokumenta,
                            NazivDokumenta,
                            TipDosijea,
                            TipProizvoda,
                            SifraDokumentaMigracija,
                            NazivDokumentaMigracija,
                            ExcelFileName,
                            ExcelFileSheet,
                            PolitikaCuvanja
                        FROM DocumentMappings WITH (NOLOCK)
                        WHERE UPPER(NazivDokumentaMigracija) = UPPER(@migratedName)";

            var cmd = new CommandDefinition(
                sql,
                new { migratedName = migratedName.Trim() },
                transaction: Tx,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: ct);

            var result = await Conn.QueryFirstOrDefaultAsync<DocumentMapping>(cmd).ConfigureAwait(false);

            if (result != null)
            {
                // Obogati sa kategorijom PRE keširanja
                await EnrichWithCategoryAsync(result, ct).ConfigureAwait(false);
                _cache.Set(cacheKey, result, DocumentCacheDuration);
            }

            return result;
        }

        /// <summary>
        /// Vraća straničeni rezultat sa pretrakom po NAZIV, NazivDokumenta, sifraDokumenta, TipDosijea
        /// </summary>
        public async Task<(IReadOnlyList<DocumentMapping> Items, int TotalCount)> SearchWithPagingAsync(
            string? searchText,
            int pageNumber,
            int pageSize,
            string? tipDosijea = null,
            CancellationToken ct = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 500) pageSize = 500; // Max limit

            var offset = (pageNumber - 1) * pageSize;
            var hasSearch = !string.IsNullOrWhiteSpace(searchText);
            var searchPattern = hasSearch ? $"%{searchText!.Trim()}%" : null;
            var hasTipDosijea = !string.IsNullOrWhiteSpace(tipDosijea);

            // Combined query to avoid MultipleActiveResultSets issue
            var sql = @"
                -- Get total count
                SELECT COUNT(*) AS TotalCount
                FROM DocumentMappings WITH (NOLOCK)
                WHERE (@hasSearch = 0 OR NAZIV LIKE @searchPattern)
                  AND (@hasTipDosijea = 0 OR TipDosijea = @tipDosijea);

                -- Get paged data
                SELECT
                    ID,
                    NAZIV,
                    BROJ_DOKUMENATA,
                    sifraDokumenta,
                    NazivDokumenta,
                    TipDosijea,
                    TipProizvoda,
                    SifraDokumentaMigracija,
                    NazivDokumentaMigracija,
                    ExcelFileName,
                    ExcelFileSheet,
                    PolitikaCuvanja
                FROM DocumentMappings WITH (NOLOCK)
                WHERE (@hasSearch = 0 OR NAZIV LIKE @searchPattern)
                  AND (@hasTipDosijea = 0 OR TipDosijea = @tipDosijea)
                ORDER BY NAZIV
                OFFSET @offset ROWS
                FETCH NEXT @pageSize ROWS ONLY;";

            var cmd = new CommandDefinition(
                sql,
                new { hasSearch = hasSearch ? 1 : 0, searchPattern, hasTipDosijea = hasTipDosijea ? 1 : 0, tipDosijea, offset, pageSize },
                transaction: Tx,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: ct);

            using var multi = await Conn.QueryMultipleAsync(cmd).ConfigureAwait(false);
            var totalCount = await multi.ReadFirstAsync<int>().ConfigureAwait(false);
            var items = await multi.ReadAsync<DocumentMapping>().ConfigureAwait(false);

            return (items.AsList().AsReadOnly(), totalCount);
        }

        public async Task<IReadOnlyList<string>> GetDistinctTipDosijeaAsync(CancellationToken ct = default)
        {
            var sql = @"
                SELECT DISTINCT TipDosijea
                FROM DocumentMappings WITH (NOLOCK)
                WHERE TipDosijea IS NOT NULL AND TipDosijea <> ''
                ORDER BY TipDosijea";

            var cmd = new CommandDefinition(
                sql,
                transaction: Tx,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: ct);

            var results = await Conn.QueryAsync<string>(cmd).ConfigureAwait(false);
            return results.AsList().AsReadOnly();
        }

        /// <summary>
        /// Obogaćuje DocumentMapping sa kategorijama iz CategoryMapping tabele.
        /// Popunjava OznakaKategorije i NazivKategorije properties.
        ///
        /// Koristi lazy loading sa MemoryCache - svaka OznakaTipa se učitava samo jednom iz baze.
        /// Cache TTL: 2 sata (kategorije se retko menjaju).
        /// </summary>
        /// <param name="mapping">DocumentMapping objekat za obogaćivanje</param>
        /// <param name="ct">Cancellation token</param>
        private async Task EnrichWithCategoryAsync(DocumentMapping mapping, CancellationToken ct = default)
        {
            if (mapping == null || string.IsNullOrWhiteSpace(mapping.SifraDokumentaMigracija))
                return;

            var cacheKey = $"CategoryMapping_{mapping.SifraDokumentaMigracija.Trim().ToUpperInvariant()}";

            // 1. Proveri cache prvo
            if (_cache.TryGetValue(cacheKey, out CategoryMapping? cachedCategory))
            {
                // Cache HIT - nema SQL poziva
                if (cachedCategory != null)
                {
                    mapping.OznakaKategorije = cachedCategory.OznakaKategorije;
                    mapping.NazivKategorije = cachedCategory.NazivKategorije;
                }
                return;
            }

            // 2. Cache MISS → poziv ka CategoryMapping tabeli
            var category = await FindCategoryByOznakaTipaAsync(mapping.SifraDokumentaMigracija, ct).ConfigureAwait(false);

            // 3. Keširaj rezultat (i null da izbegneš uzastopne SQL pozive)
            if (category != null)
            {
                _cache.Set(cacheKey, category, CategoryCacheDuration);
                mapping.OznakaKategorije = category.OznakaKategorije;
                mapping.NazivKategorije = category.NazivKategorije;
            }
            else
            {
                // Keširaj i null rezultat na kraće vreme (10 min)
                _cache.Set(cacheKey, (CategoryMapping?)null, TimeSpan.FromMinutes(10));
            }
        }

        /// <summary>
        /// Direktan SQL poziv ka CategoryMapping tabeli.
        /// PRIVATNA metoda - koristi se samo iz EnrichWithCategoryAsync().
        /// Povezuje se preko OznakaTipa = SifraDokumentaMigracija.
        /// </summary>
        /// <param name="oznakaTipa">Oznaka tipa dokumenta (SifraDokumentaMigracija)</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>CategoryMapping objekat ili null</returns>
        private async Task<CategoryMapping?> FindCategoryByOznakaTipaAsync(string oznakaTipa, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(oznakaTipa))
                return null;

            var sql = @"
                SELECT TOP 1
                    OznakaTipa,
                    NazivTipa,
                    OznakaKategorije,
                    NazivKategorije,
                    PolitikaCuvanja,
                    DatumIstekaObavezan,
                    PeriodIstekaMeseci,
                    PeriodObnoveMeseci,
                    PeriodCuvanjaMeseci,
                    Kreator,
                    DatumKreiranja,
                    DatumIzmene,
                    Aktivan
                FROM CategoryMapping WITH (NOLOCK)
                WHERE UPPER(OznakaTipa) = UPPER(@oznakaTipa)";

            var cmd = new CommandDefinition(
                sql,
                new { oznakaTipa = oznakaTipa.Trim() },
                transaction: Tx,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: ct);

            return await Conn.QueryFirstOrDefaultAsync<CategoryMapping>(cmd).ConfigureAwait(false);
        }
    }
}
