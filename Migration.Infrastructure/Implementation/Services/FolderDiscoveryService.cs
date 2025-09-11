using Migration.Apstaction.Interfaces;
using Migration.Apstaction.Interfaces.Services;
using Migration.Apstaction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Services
{
    public class FolderDiscoveryService : IFolderDiscoveryService
    {
        private readonly IFolderScanner _scanner;
        private readonly IFolderIngestor _ingestor;

        public Task<int> DiscoverAsync(DiscoverRequest inRequest)
        {
            throw new NotImplementedException();
        }
    }
}
