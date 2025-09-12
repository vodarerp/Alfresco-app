using Alfresco.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Apstaction.Interfaces
{
    public interface IDocumentIngestor
    {
        Task<int> InserManyAsync(IReadOnlyList<Entry> items, CancellationToken ct);

    }
}
