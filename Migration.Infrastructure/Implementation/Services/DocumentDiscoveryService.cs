using Migration.Apstaction.Interfaces.Wrappers;
using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class DocumentDiscoveryService : IDocumentDiscoveryService
    {
        public Task<DocumentBatchResult> RunBatchAsync(DocumentDiscoveryBatchRequest inRequest, CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        public Task RunLoopAsync(DocumentDiscoveryLoopOptions inOptions, CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }
}
