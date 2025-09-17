using Oracle.Apstaction.Interfaces;
using Oracle.ManagedDataAccess.Client;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oracle.Infractructure.Implementation
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
            if (!IsActive)
            {
                _conn = new OracleConnection(_connString);
                if(_conn.State != ConnectionState.Open) await _conn.OpenAsync(ct).ConfigureAwait(false);
                _tx = _conn.BeginTransaction(isolation);
            }
            
        }

        public Task CommitAsync(CancellationToken ct = default)
        {

            if (!IsActive) return Task.CompletedTask;
            _tx?.Commit();
            _tx?.Dispose();
            _tx = null;
            return Task.CompletedTask;
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
                await _conn.DisposeAsync();
                _conn = null;
            }
        }

        public Task RollbackAsync(CancellationToken ct = default)
        {
            if (!IsActive) return Task.CompletedTask;
            _tx?.Rollback();
            _tx?.Dispose();
            _tx = null;
            return Task.CompletedTask;
        }
    }
}
