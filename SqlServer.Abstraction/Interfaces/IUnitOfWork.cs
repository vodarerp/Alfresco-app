using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SqlServer.Abstraction.Interfaces
{
    public interface IUnitOfWork : IAsyncDisposable
    {
        IDbConnection Connection { get; }

        IDbTransaction Transaction { get; }

        bool IsActive { get; }

        Task BeginAsync(IsolationLevel isolation = IsolationLevel.ReadCommitted, CancellationToken ct = default);
        Task CommitAsync(CancellationToken ct = default);
        Task RollbackAsync(CancellationToken ct = default);
    }
}
