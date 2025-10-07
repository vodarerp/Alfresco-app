using Oracle.Abstraction.Interfaces;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oracle.Infrastructure.Implementation
{
    public class OracleUnitOfWork : IUnitOfWork
    {

        private readonly string _connString;

        private OracleConnection? _conn;
        private OracleTransaction? _tx;

        public OracleUnitOfWork(string inConnString)
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

            // Create new connection if not exists or was disposed
            if (_conn is null || _conn.State == ConnectionState.Closed || _conn.State == ConnectionState.Broken)
            {
                _conn?.Dispose(); // Clean up broken connection if exists
                _conn = new OracleConnection(_connString);
            }

            if (_conn.State != ConnectionState.Open)
            {
                await _conn.OpenAsync(ct).ConfigureAwait(false);
            }

            _tx = _conn.BeginTransaction(isolation);
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
