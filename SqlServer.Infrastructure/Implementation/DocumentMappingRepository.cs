using Alfresco.Contracts.DtoModels;
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
            if (pageSize > 500) pageSize = 500;

            var cacheKey = $"DocSearch_{searchText?.Trim()}_{tipDosijea}_{pageNumber}_{pageSize}";
            if (_cache.TryGetValue(cacheKey, out (IReadOnlyList<DocumentMapping>, int) cached))
                return cached;

            var offset = (pageNumber - 1) * pageSize;
            var hasSearch = !string.IsNullOrWhiteSpace(searchText);
            var searchPattern = hasSearch ? $"%{searchText!.Trim()}%" : null;
            var hasTipDosijea = !string.IsNullOrWhiteSpace(tipDosijea);

            var sql = @"
                SELECT COUNT(*) AS TotalCount
                FROM DocumentMappings WITH (NOLOCK)
                WHERE (@hasSearch = 0 OR NAZIV LIKE @searchPattern)
                  AND (@hasTipDosijea = 0 OR TipDosijea = @tipDosijea);

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

            var result = (items.AsList().AsReadOnly() as IReadOnlyList<DocumentMapping>, totalCount);
            _cache.Set(cacheKey, result, TimeSpan.FromMinutes(5));
            return result;
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
        /// Vraća grupisani prikaz dokumenata (GROUP + SINGLE redovi).
        /// Koristi CTE sa CASE expression za detekciju numeričkog sufiksa — bez ALTER TABLE.
        /// Rezultat se keširata 10 minuta (full scan je skup za 70k+ redova).
        /// </summary>
        public async Task<(IReadOnlyList<GroupedDocumentRow> Items, int TotalCount)> GetGroupedViewAsync(
            string? searchText,
            string? tipDosijea,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 500) pageSize = 500;

            // Kešira CIJELI rezultat za dati filter (bez page) — straničenje se radi u memoriji.
            // Na taj način page 2, 3, ... su instant (bez ponovnog pokretanja skupog CTE-a).
            var allCacheKey = $"GroupedViewAll_{searchText?.Trim()}_{tipDosijea}";
            if (!_cache.TryGetValue(allCacheKey, out IReadOnlyList<GroupedDocumentRow>? allRows))
            {
                var hasSearch = !string.IsNullOrWhiteSpace(searchText);
                var searchPattern = hasSearch ? $"%{searchText!.Trim()}%" : null;
                var hasTipDosijea = !string.IsNullOrWhiteSpace(tipDosijea);

                // Bez OFFSET/FETCH i COUNT(*) OVER() — dohvatamo sve odjednom, paginiramo u C#
                var sql = @"
                    WITH DocWithBase AS (
                        SELECT
                            ID,
                            NAZIV,
                            ISNULL(BROJ_DOKUMENATA, 0) AS BROJ_DOKUMENATA,
                            TipDosijea,
                            sifraDokumenta,
                            CASE
                                WHEN CHARINDEX(' ', NAZIV) > 0
                                 AND LEN(SUBSTRING(NAZIV,
                                         LEN(NAZIV) + 2 - CHARINDEX(' ', REVERSE(NAZIV)),
                                         LEN(NAZIV))) > 0
                                 AND SUBSTRING(NAZIV,
                                         LEN(NAZIV) + 2 - CHARINDEX(' ', REVERSE(NAZIV)),
                                         LEN(NAZIV)) NOT LIKE '%[^0-9]%'
                                THEN LEFT(NAZIV, LEN(NAZIV) - CHARINDEX(' ', REVERSE(NAZIV)))
                                ELSE NULL
                            END AS BaseNaziv
                        FROM DocumentMappings WITH (NOLOCK)
                        WHERE NAZIV IS NOT NULL
                          AND (@hasSearch = 0 OR NAZIV LIKE @searchPattern)
                          AND (@hasTipDosijea = 0 OR TipDosijea = @tipDosijea)
                    ),
                    GroupCounts AS (
                        SELECT BaseNaziv, COUNT(*) AS Cnt
                        FROM DocWithBase
                        WHERE BaseNaziv IS NOT NULL
                        GROUP BY BaseNaziv
                    )
                    SELECT
                        'GROUP'           AS RowType,
                        gc.BaseNaziv      AS DisplayNaziv,
                        gc.Cnt            AS VariantCount,
                        SUM(d.BROJ_DOKUMENATA) AS TotalDocuments,
                        MIN(d.TipDosijea) AS TipDosijea,
                        NULL              AS SifraDokumenta,
                        NULL              AS Id,
                        0                 AS TotalCount
                    FROM DocWithBase d
                    INNER JOIN GroupCounts gc ON d.BaseNaziv = gc.BaseNaziv AND gc.Cnt > 1
                    GROUP BY gc.BaseNaziv, gc.Cnt

                    UNION ALL

                    SELECT
                        'SINGLE'          AS RowType,
                        d.NAZIV           AS DisplayNaziv,
                        1                 AS VariantCount,
                        d.BROJ_DOKUMENATA AS TotalDocuments,
                        d.TipDosijea,
                        d.sifraDokumenta  AS SifraDokumenta,
                        d.ID              AS Id,
                        0                 AS TotalCount
                    FROM DocWithBase d
                    LEFT JOIN GroupCounts gc ON d.BaseNaziv = gc.BaseNaziv
                    WHERE d.BaseNaziv IS NULL
                       OR ISNULL(gc.Cnt, 1) = 1

                    ORDER BY DisplayNaziv;";

                var cmd = new CommandDefinition(
                    sql,
                    new
                    {
                        hasSearch = hasSearch ? 1 : 0,
                        searchPattern,
                        hasTipDosijea = hasTipDosijea ? 1 : 0,
                        tipDosijea
                    },
                    transaction: Tx,
                    commandTimeout: _commandTimeoutSeconds,
                    cancellationToken: ct);

                var rows = (await Conn.QueryAsync<GroupedDocumentRow>(cmd).ConfigureAwait(false)).AsList();
                allRows = rows.AsReadOnly();
                _cache.Set(allCacheKey, allRows, TimeSpan.FromMinutes(10));
            }

            var totalCount = allRows.Count;
            var offset = (pageNumber - 1) * pageSize;
            var page = allRows.Skip(offset).Take(pageSize).ToList().AsReadOnly();
            return (page, totalCount);
        }

        /// <summary>
        /// Straničena pretraga dokumenata unutar jedne grupe.
        /// Koristi NAZIV LIKE 'BaseNaziv %' — može da iskoristi index na NAZIV koloni.
        /// </summary>
        public async Task<(IReadOnlyList<DocumentMapping> Items, int TotalCount)> SearchWithinGroupAsync(
            string baseNaziv,
            string? invoiceNumberFilter,
            int pageNumber,
            int pageSize,
            CancellationToken ct = default)
        {
            if (pageNumber < 1) pageNumber = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 500) pageSize = 500;

            var offset = (pageNumber - 1) * pageSize;
            var baseNazivLike = $"{baseNaziv.Trim()} %";
            var hasInvoiceFilter = !string.IsNullOrWhiteSpace(invoiceNumberFilter);
            var invoiceFilterLike = hasInvoiceFilter ? $"{baseNaziv.Trim()} {invoiceNumberFilter!.Trim()}%" : null;

            var sql = @"
                SELECT
                    ID,
                    NAZIV,
                    ISNULL(BROJ_DOKUMENATA, 0) AS BROJ_DOKUMENATA,
                    sifraDokumenta,
                    NazivDokumenta,
                    TipDosijea,
                    TipProizvoda,
                    SifraDokumentaMigracija,
                    NazivDokumentaMigracija,
                    ExcelFileName,
                    ExcelFileSheet,
                    PolitikaCuvanja,
                    COUNT(*) OVER() AS TotalCount
                FROM DocumentMappings WITH (NOLOCK)
                WHERE NAZIV LIKE @baseNazivLike
                  AND CHARINDEX(' ', NAZIV) > 0
                  AND SUBSTRING(NAZIV, LEN(NAZIV)+2-CHARINDEX(' ',REVERSE(NAZIV)), LEN(NAZIV))
                      NOT LIKE '%[^0-9]%'
                  AND (@hasInvoiceFilter = 0 OR NAZIV LIKE @invoiceFilterLike)
                ORDER BY NAZIV
                OFFSET @offset ROWS FETCH NEXT @pageSize ROWS ONLY;";

            var cmd = new CommandDefinition(
                sql,
                new
                {
                    baseNazivLike,
                    hasInvoiceFilter = hasInvoiceFilter ? 1 : 0,
                    invoiceFilterLike,
                    offset,
                    pageSize
                },
                transaction: Tx,
                commandTimeout: _commandTimeoutSeconds,
                cancellationToken: ct);

            // TotalCount nije polje na DocumentMapping — koristimo dynamic pa mapiramo
            var rows = (await Conn.QueryAsync<dynamic>(cmd).ConfigureAwait(false)).AsList();
            var totalCount = rows.Count > 0 ? (int)rows[0].TotalCount : 0;

            var items = rows.Select(r => new DocumentMapping
            {
                ID = (int)r.ID,
                Naziv = (string?)r.NAZIV,
                BrojDokumenata = (int?)r.BROJ_DOKUMENATA,
                SifraDokumenta = (string?)r.sifraDokumenta,
                NazivDokumenta = (string?)r.NazivDokumenta,
                TipDosijea = (string?)r.TipDosijea,
                TipProizvoda = (string?)r.TipProizvoda,
                SifraDokumentaMigracija = (string?)r.SifraDokumentaMigracija,
                NazivDokumentaMigracija = (string?)r.NazivDokumentaMigracija,
                ExcelFileName = (string?)r.ExcelFileName,
                ExcelFileSheet = (string?)r.ExcelFileSheet,
                PolitikaCuvanja = (string?)r.PolitikaCuvanja
            }).ToList().AsReadOnly() as IReadOnlyList<DocumentMapping>;

            return (items, totalCount);
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
