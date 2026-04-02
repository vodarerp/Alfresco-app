using Alfresco.Contracts.DtoModels;
using Migration.Abstraction.Models;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Abstraction.Interfaces.Wrappers
{
   
    public interface IDocumentSearchService
    {
        void SetDocDescriptions(List<string> docDescriptions);       
        void SetDocumentSelection(DocumentSelectionResult selection);       
        List<string> GetCurrentDocDescriptions();       
        Task<DocumentSearchBatchResult> RunBatchAsync(CancellationToken ct);
        Task<bool> RunLoopAsync(CancellationToken ct);
        Task<bool> RunLoopAsync(CancellationToken ct, Action<WorkerProgress>? progressCallback);
    }

    
   
}
