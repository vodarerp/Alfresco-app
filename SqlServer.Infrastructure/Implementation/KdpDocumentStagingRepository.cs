using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.SqlServer;
using Dapper;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Infrastructure.Implementation
{
    /// <summary>
    /// Repository implementacija za KdpDocumentStaging tabelu
    /// </summary>
    public class KdpDocumentStagingRepository : SqlServerRepository<KdpDocumentStaging, long>, IKdpDocumentStagingRepository
    {
        public KdpDocumentStagingRepository(IUnitOfWork uow, SqlServerOptions sqlServerOptions) : base(uow, sqlServerOptions)
        {
        }

        /// <summary>
        /// Briše sve zapise iz KdpDocumentStaging tabele
        /// </summary>
        public async Task ClearStagingAsync(CancellationToken ct = default)
        {
            var sql = "TRUNCATE TABLE KdpDocumentStaging";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        /// <summary>
        /// Vraća broj dokumenata u staging tabeli
        /// </summary>
        public async Task<long> CountAsync(CancellationToken ct = default)
        {
            var sql = "SELECT COUNT(*) FROM KdpDocumentStaging";
            var cmd = new CommandDefinition(sql, transaction: Tx, commandTimeout: _commandTimeoutSeconds, cancellationToken: ct);
            var count = await Conn.ExecuteScalarAsync<long>(cmd).ConfigureAwait(false);
            return count;
        }
    }
}
