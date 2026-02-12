using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces
{
    
    public interface IClientApi
    {
       
        Task<ClientData> GetClientDataAsync(string coreId, CancellationToken ct = default);
        Task<List<string>> GetActiveAccountsAsync(string coreId, DateTime asOfDate, CancellationToken ct = default);
        Task<bool> ValidateClientExistsAsync(string coreId, CancellationToken ct = default);
        Task<ClientDetailResponse> GetClientDetailAsync(string coreId, CancellationToken ct = default);
    }
}
