using SqlServer.Abstraction.Interfaces;
using Microsoft.Data.SqlClient;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SqlServer.Infrastructure.Implementation
{
    public class SqlServerUnitOfWork : IUnitOfWork
    {

        private readonly string _connString;

        private SqlConnection? _conn;
        private SqlTransaction? _tx;

        public SqlServerUnitOfWork(string inConnString)
        {
            _connString = inConnString;
        }

        public IDbConnection Connection => _conn ?? throw new InvalidOperationException("DBConnection not set.");

        public IDbTransaction? Transaction => _tx;

        public bool IsActive => _tx is not null;

        public async Task BeginAsync(IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default)
        {
            if (IsActive)
            {
                // Transaction already active - no-op to prevent nested transactions
                return;
            }

            // Check if cancellation was already requested before attempting connection
            if (ct.IsCancellationRequested)
            {
                throw new OperationCanceledException(
                    "Cannot begin database transaction: Operation was canceled before connection attempt. " +
                    "This may indicate the application is shutting down or the operation was explicitly canceled.",
                    ct);
            }

            // Create new connection if not exists or was disposed
            if (_conn is null || _conn.State == ConnectionState.Closed || _conn.State == ConnectionState.Broken)
            {
                _conn?.Dispose(); // Clean up broken connection if exists
                _conn = new SqlConnection(_connString);
            }

            if (_conn.State != ConnectionState.Open)
            {
                try
                {
                    await _conn.OpenAsync(ct).ConfigureAwait(false);
                }
                catch (TaskCanceledException ex)
                {
                    // Provide more context about why the connection failed
                    var connBuilder = new SqlConnectionStringBuilder(_connString);
                    var serverInfo = $"Server: {connBuilder.DataSource}, Database: {connBuilder.InitialCatalog}";
                    var timeoutInfo = $"Connection Timeout: {connBuilder.ConnectTimeout} seconds";

                    throw new OperationCanceledException(
                        $"Database connection was canceled. {serverInfo}, {timeoutInfo}. " +
                        $"This may be caused by: (1) Network connectivity issues, (2) SQL Server not responding, " +
                        $"(3) Firewall blocking the connection, (4) Connection timeout too short, or (5) Application shutdown. " +
                        $"Original error: {ex.Message}",
                        ex,
                        ct);
                }
                catch (SqlException sqlEx)
                {
                    // Handle SQL-specific errors with detailed information
                    var connBuilder = new SqlConnectionStringBuilder(_connString);
                    var serverInfo = $"Server: {connBuilder.DataSource}, Database: {connBuilder.InitialCatalog}";

                    throw new InvalidOperationException(
                        $"Failed to connect to SQL Server. {serverInfo}. " +
                        $"SQL Error Code: {sqlEx.Number}, Message: {sqlEx.Message}. " +
                        $"Common causes: (1) SQL Server not running, (2) Incorrect server name or port, " +
                        $"(3) Authentication failure, (4) Database does not exist, (5) Network issues.",
                        sqlEx);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // Handle any other unexpected errors
                    var connBuilder = new SqlConnectionStringBuilder(_connString);
                    var serverInfo = $"Server: {connBuilder.DataSource}, Database: {connBuilder.InitialCatalog}";

                    throw new InvalidOperationException(
                        $"Unexpected error while opening database connection. {serverInfo}. " +
                        $"Error Type: {ex.GetType().Name}, Message: {ex.Message}",
                        ex);
                }
            }

            _tx = (SqlTransaction)_conn.BeginTransaction(isolation);
        }

        public async Task CommitAsync(CancellationToken ct = default)
        {
            if (!IsActive) return;

            _tx?.Commit();
            _tx?.Dispose();
            _tx = null;

            // Close connection after commit to return it to the pool immediately
            if (_conn is not null)
            {
                await _conn.DisposeAsync().ConfigureAwait(false);
                _conn = null;
            }
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_tx is not null) _tx.Rollback();
            }
            catch { /* ignore */ }
            _tx?.Dispose(); _tx = null;

            if (_conn is not null)
            {
                await _conn.DisposeAsync().ConfigureAwait(false);
                _conn = null;
            }
        }

        public async Task RollbackAsync(CancellationToken ct = default)
        {
            if (!IsActive) return;

            _tx?.Rollback();
            _tx?.Dispose();
            _tx = null;

            // Close connection after rollback to return it to the pool immediately
            if (_conn is not null)
            {
                await _conn.DisposeAsync().ConfigureAwait(false);
                _conn = null;
            }
        }
    }
}
