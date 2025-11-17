using Alfresco.Contracts.Enums;
using Alfresco.Contracts.Oracle.Models;
using Dapper;
using SqlServer.Abstraction.Interfaces;

namespace SqlServer.Infrastructure.Implementation
{
    public class PhaseCheckpointRepository : SqlServerRepository<PhaseCheckpoint, long>, IPhaseCheckpointRepository
    {
        public PhaseCheckpointRepository(IUnitOfWork uow) : base(uow)
        {
        }

        public async Task<PhaseCheckpoint?> GetCheckpointAsync(MigrationPhase phase, CancellationToken ct = default)
        {
            var sql = @"SELECT * FROM PhaseCheckpoints
                        WHERE Phase = @phase";

            var dp = new DynamicParameters();
            dp.Add("@phase", (int)phase);  

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            var result = await Conn.QueryFirstOrDefaultAsync<PhaseCheckpoint>(cmd).ConfigureAwait(false);

            return result;
        }

        public async Task<List<PhaseCheckpoint>> GetAllCheckpointsAsync(CancellationToken ct = default)
        {
            var sql = @"SELECT * FROM PhaseCheckpoints
                        ORDER BY Phase ASC";

            var cmd = new CommandDefinition(sql, transaction: Tx, cancellationToken: ct);
            var result = await Conn.QueryAsync<PhaseCheckpoint>(cmd).ConfigureAwait(false);

            return result.ToList();
        }

        public async Task MarkPhaseStartedAsync(MigrationPhase phase, CancellationToken ct = default)
        {
            var existing = await GetCheckpointAsync(phase, ct).ConfigureAwait(false);

            if (existing != null)
            {
                // Update existing
                var sql = @"UPDATE PhaseCheckpoints
                            SET Status = @status,
                                StartedAt = @startedAt,
                                CompletedAt = NULL,
                                ErrorMessage = NULL,
                                UpdatedAt = @updatedAt
                            WHERE Phase = @phase";

                var dp = new DynamicParameters();
                dp.Add("@phase", (int)phase);
                dp.Add("@status", (int)PhaseStatus.InProgress);
                dp.Add("@startedAt", DateTime.UtcNow);
                dp.Add("@updatedAt", DateTime.UtcNow);

                var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
                await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
            }
            else
            {
                // Insert new
                var sql = @"INSERT INTO PhaseCheckpoints
                            (Phase, Status, StartedAt, CreatedAt, UpdatedAt, TotalProcessed)
                            VALUES
                            (@phase, @status, @startedAt, @createdAt, @updatedAt, 0)";

                var dp = new DynamicParameters();
                dp.Add("@phase", (int)phase);
                dp.Add("@status", (int)PhaseStatus.InProgress);
                dp.Add("@startedAt", DateTime.UtcNow);
                dp.Add("@createdAt", DateTime.UtcNow);
                dp.Add("@updatedAt", DateTime.UtcNow);

                var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
                await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
            }
        }

        public async Task MarkPhaseCompletedAsync(MigrationPhase phase, CancellationToken ct = default)
        {
            var sql = @"UPDATE PhaseCheckpoints
                        SET Status = @status,
                            CompletedAt = @completedAt,
                            UpdatedAt = @updatedAt
                        WHERE Phase = @phase";

            var dp = new DynamicParameters();
            dp.Add("@phase", (int)phase);
            dp.Add("@status", (int)PhaseStatus.Completed);
            dp.Add("@completedAt", DateTime.UtcNow);
            dp.Add("@updatedAt", DateTime.UtcNow);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task MarkPhaseFailedAsync(MigrationPhase phase, string errorMessage, CancellationToken ct = default)
        {
            var sql = @"UPDATE PhaseCheckpoints
                        SET Status = @status,
                            CompletedAt = @completedAt,
                            ErrorMessage = @errorMessage,
                            UpdatedAt = @updatedAt
                        WHERE Phase = @phase";

            var dp = new DynamicParameters();
            dp.Add("@phase", (int)phase);
            dp.Add("@status", (int)PhaseStatus.Failed);
            dp.Add("@completedAt", DateTime.UtcNow);
            dp.Add("@errorMessage", errorMessage);
            dp.Add("@updatedAt", DateTime.UtcNow);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task UpdateProgressAsync(
            MigrationPhase phase,
            int? lastProcessedIndex,
            string? lastProcessedId,
            long totalProcessed,
            CancellationToken ct = default)
        {
            var sql = @"UPDATE PhaseCheckpoints
                        SET LastProcessedIndex = @lastProcessedIndex,
                            LastProcessedId = @lastProcessedId,
                            TotalProcessed = @totalProcessed,
                            UpdatedAt = @updatedAt
                        WHERE Phase = @phase";

            var dp = new DynamicParameters();
            dp.Add("@phase", (int)phase);
            dp.Add("@lastProcessedIndex", lastProcessedIndex);
            dp.Add("@lastProcessedId", lastProcessedId);
            dp.Add("@totalProcessed", totalProcessed);
            dp.Add("@updatedAt", DateTime.UtcNow);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task ResetAllPhasesAsync(CancellationToken ct = default)
        {
            var sql = @"UPDATE PhaseCheckpoints
                        SET Status = @status,
                            StartedAt = NULL,
                            CompletedAt = NULL,
                            ErrorMessage = NULL,
                            LastProcessedIndex = NULL,
                            LastProcessedId = NULL,
                            TotalProcessed = 0,
                            TotalItems = NULL,
                            UpdatedAt = @updatedAt";

            var dp = new DynamicParameters();
            dp.Add("@status", (int)PhaseStatus.NotStarted);
            dp.Add("@updatedAt", DateTime.UtcNow);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }

        public async Task ResetPhaseAsync(MigrationPhase phase, CancellationToken ct = default)
        {
            var sql = @"UPDATE PhaseCheckpoints
                        SET Status = @status,
                            StartedAt = NULL,
                            CompletedAt = NULL,
                            ErrorMessage = NULL,
                            LastProcessedIndex = NULL,
                            LastProcessedId = NULL,
                            TotalProcessed = 0,
                            TotalItems = NULL,
                            UpdatedAt = @updatedAt
                        WHERE Phase = @phase";

            var dp = new DynamicParameters();
            dp.Add("@phase", (int)phase);
            dp.Add("@status", (int)PhaseStatus.NotStarted);
            dp.Add("@updatedAt", DateTime.UtcNow);

            var cmd = new CommandDefinition(sql, dp, Tx, cancellationToken: ct);
            await Conn.ExecuteAsync(cmd).ConfigureAwait(false);
        }
    }
}
