using SqlServer.Abstraction.Interfaces;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlServer.Infrastructure.Implementation
{
    /// <summary>
    /// SQL Server implementation of Unit of Work pattern.
    ///
    /// Lifecycle:
    /// - Registered as SCOPED in DI container (one instance per worker batch scope)
    /// - Connection is opened on first BeginAsync() and kept alive for entire scope lifetime
    /// - Multiple Begin/Commit/Rollback cycles are supported within same scope
    /// - Connection is disposed only when scope ends (DisposeAsync)
    ///
    /// Usage Pattern:
    /// Worker creates scope → UnitOfWork instance created
    ///   Batch 1: BeginAsync → work → CommitAsync (transaction closed, connection stays open)
    ///   Batch 2: BeginAsync → work → CommitAsync (transaction closed, connection stays open)
    ///   Batch 3: BeginAsync → work → RollbackAsync (transaction closed, connection stays open)
    /// Scope disposed → DisposeAsync → Connection closed and returned to pool
    /// </summary>
    public class SqlServerUnitOfWork : IUnitOfWork
    {
        private readonly string _connString;

        private SqlConnection? _conn;
        private SqlTransaction? _tx;
        private bool _disposed = false;

        public SqlServerUnitOfWork(string inConnString)
        {
            _connString = inConnString ?? throw new ArgumentNullException(nameof(inConnString));
        }

        public IDbConnection Connection => _conn ?? throw new InvalidOperationException("Connection not initialized. Call BeginAsync first.");

        public IDbTransaction? Transaction => _tx;

        public bool IsActive => _tx is not null;

        /// <summary>
        /// Begins a new transaction.
        /// - Opens connection on first call (connection stays open for entire UnitOfWork lifetime)
        /// - Starts new transaction if no active transaction exists
        /// - No-op if transaction already active (prevents nested transactions)
        /// </summary>
        public async Task BeginAsync(IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlServerUnitOfWork));

            // If transaction already active, ignore (no nested transactions)
            if (IsActive)
            {
                return;
            }

            // Ensure connection is open (first call or after Commit/Rollback)
            await EnsureConnectionOpenAsync(ct).ConfigureAwait(false);

            // Start new transaction
            _tx = (SqlTransaction)_conn!.BeginTransaction(isolation);
        }

        /// <summary>
        /// Commits the active transaction.
        /// - Commits and disposes transaction
        /// - Connection stays OPEN for next transaction (optimized for multiple batches)
        /// - No-op if no active transaction
        /// </summary>
        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlServerUnitOfWork));

            if (!IsActive)
            {
                // No active transaction - this is OK, just return
                return;
            }

            try
            {
                _tx!.Commit();
            }
            finally
            {
                // Dispose transaction but keep connection open
                _tx?.Dispose();
                _tx = null;

                // NOTE: Connection stays open for reuse in next BeginAsync call
                // Connection will be closed only in DisposeAsync (when scope ends)
            }
        }

        /// <summary>
        /// Rolls back the active transaction.
        /// - Rolls back and disposes transaction
        /// - Connection stays OPEN for next transaction (optimized for multiple batches)
        /// - No-op if no active transaction
        /// </summary>
        public async Task RollbackAsync(CancellationToken ct = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(SqlServerUnitOfWork));

            if (!IsActive)
            {
                // No active transaction - this is OK, just return
                return;
            }

            try
            {
                _tx!.Rollback();
            }
            catch
            {
                // Ignore rollback errors (connection might be broken)
            }
            finally
            {
                // Dispose transaction but keep connection open
                _tx?.Dispose();
                _tx = null;

                // NOTE: Connection stays open for reuse in next BeginAsync call
                // Connection will be closed only in DisposeAsync (when scope ends)
            }
        }

        /// <summary>
        /// Disposes the UnitOfWork and closes the connection.
        /// Called automatically when DI scope ends (e.g., worker batch completes).
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            if (_disposed)
                return;

            _disposed = true;

            // Rollback any active transaction before disposing
            if (_tx is not null)
            {
                try
                {
                    _tx.Rollback();
                }
                catch
                {
                    // Ignore rollback errors
                }
                _tx?.Dispose();
                _tx = null;
            }

            // Close and dispose connection (returns to connection pool)
            if (_conn is not null)
            {
                await _conn.DisposeAsync().ConfigureAwait(false);
                _conn = null;
            }
        }

        /// <summary>
        /// Ensures connection is open and ready for use.
        /// Creates new connection on first call, reuses existing connection on subsequent calls.
        /// Recreates connection if existing one is broken.
        /// </summary>
        private async Task EnsureConnectionOpenAsync(CancellationToken ct = default)
        {
            // Create new connection if null or broken
            if (_conn is null || _conn.State == ConnectionState.Broken)
            {
                // Dispose broken connection if exists
                if (_conn is not null)
                {
                    await _conn.DisposeAsync().ConfigureAwait(false);
                }

                _conn = new SqlConnection(_connString);
            }

            // Open connection if not already open
            if (_conn.State != ConnectionState.Open)
            {
                await _conn.OpenAsync(ct).ConfigureAwait(false);
            }
        }
    }
}
