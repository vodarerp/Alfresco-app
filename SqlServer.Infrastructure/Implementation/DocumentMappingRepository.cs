using Alfresco.Contracts.Oracle.Models;
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
    /// </summary>
    public class DocumentMappingRepository : SqlServerRepository<DocumentMapping, int>, IDocumentMappingRepository
    {
        private readonly IMemoryCache _cache;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30); // Kraće keširanje za pojedinačne upite

        public DocumentMappingRepository(IUnitOfWork uow, IMemoryCache cache) : base(uow)
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

            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);
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
                cancellationToken: ct);

            var result = await Conn.QueryFirstOrDefaultAsync<DocumentMapping>(cmd).ConfigureAwait(false);

            if (result != null)
            {
                _cache.Set(cacheKey, result, CacheDuration);
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
                cancellationToken: ct);

            var result = await Conn.QueryFirstOrDefaultAsync<DocumentMapping>(cmd).ConfigureAwait(false);

            if (result != null)
            {
                _cache.Set(cacheKey, result, CacheDuration);
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
                cancellationToken: ct);

            var result = await Conn.QueryFirstOrDefaultAsync<DocumentMapping>(cmd).ConfigureAwait(false);

            if (result != null)
            {
                _cache.Set(cacheKey, result, CacheDuration);
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
                cancellationToken: ct);

            var result = await Conn.QueryFirstOrDefaultAsync<DocumentMapping>(cmd).ConfigureAwait(false);

            if (result != null)
            {
                _cache.Set(cacheKey, result, CacheDuration);
            }

            return result;
        }
    }
}
