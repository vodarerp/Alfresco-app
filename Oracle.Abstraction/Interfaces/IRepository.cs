using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Oracle.Abstraction.Interfaces
{
    public interface IRepository<T, TKey>
    {
        Task<IEnumerable<T>> GetListAsync(
                                object? filters = null,
                                int? skip = null,
                                int? take = null,
                                string[]? orderBy = null,
                                CancellationToken ct = default);
        Task<T?> GetByIdAsync(long id, CancellationToken cancellationToken = default);
        Task<TKey> AddAsync(T entity, CancellationToken cancellationToken = default);
        Task<int> InsertManyAsync(IEnumerable<T> entities, CancellationToken ct = default);
        Task UpdateAsync(T entity, CancellationToken cancellationToken = default);
        Task DeleteAsync(long id, CancellationToken cancellationToken = default);
    }
}
