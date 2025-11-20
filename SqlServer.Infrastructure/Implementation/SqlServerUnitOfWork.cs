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
    public class SqlServerUnitOfWork : IUnitOfWork
    {

        private readonly string _connString;

        private SqlConnection? _conn;
        private SqlTransaction? _tx;

        public SqlServerUnitOfWork(string inConnString)
        {
            _connString = inConnString;
        }

        public IDbConnection Connection
        {
            get
            {
                if (_conn is null)
                    throw new InvalidOperationException("DBConnection not set. Call BeginAsync first.");

                if (_conn.State == ConnectionState.Closed || _conn.State == ConnectionState.Broken)
                    throw new InvalidOperationException($"DBConnection is {_conn.State}. Call BeginAsync first.");

                return _conn;
            }
        }

        public IDbTransaction? Transaction => _tx;

        public bool IsActive => _tx is not null;

        public async Task BeginAsync(IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default)
        {
            if (IsActive)
            {
                // Transaction already active - no-op to prevent nested transactions
                return;
            }

            // Create new connection if not exists or was disposed/closed
            if (_conn is null || _conn.State == ConnectionState.Closed || _conn.State == ConnectionState.Broken)
            {
                // Dispose old connection if it exists
                if (_conn is not null)
                {
                    try
                    {
                        await _conn.DisposeAsync().ConfigureAwait(false);
                    }
                    catch { /* ignore disposal errors */ }
                }

                _conn = new SqlConnection(_connString);
                await _conn.OpenAsync(ct).ConfigureAwait(false);
            }
            else if (_conn.State != ConnectionState.Open)
            {
                await _conn.OpenAsync(ct).ConfigureAwait(false);
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

            try
            {
                _tx?.Rollback();
            }
            catch (InvalidOperationException)
            {
                // Transaction already completed or connection closed - ignore
            }
            catch (SqlException ex) when (ex.Number == 3903)
            {
                // Error 3903: "The ROLLBACK TRANSACTION request has no corresponding BEGIN TRANSACTION"
                // This can happen if transaction was already completed - ignore
            }
            finally
            {
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
}
