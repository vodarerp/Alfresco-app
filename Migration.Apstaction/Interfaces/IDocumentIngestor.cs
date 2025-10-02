using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstraction.Interfaces
{
    public interface IDocumentIngestor
    {
        Task<int> InserManyAsync(IReadOnlyList<DocStaging> items, CancellationToken ct);

    }
}
