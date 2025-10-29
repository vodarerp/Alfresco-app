using Alfresco.Abstraction.Interfaces;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Move
{
    public class MoveExecutor : IMoveExecutor
    {
        private readonly IDocStagingRepository _docRepo;
        private readonly IAlfrescoWriteApi _write;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;

        public MoveExecutor(IDocStagingRepository doc, IAlfrescoWriteApi wirte, ILoggerFactory logger)
        {
            _docRepo = doc;
            _write = wirte;
            _fileLogger = logger.CreateLogger("FileLogger");
            _dbLogger = logger.CreateLogger("DbLogger");
        }

        public async Task<bool> MoveAsync(string DocumentId, string DestFolderId, CancellationToken ct)
        {
            try
            {
                _fileLogger.LogDebug("Moving document {DocumentId} to folder {DestFolderId}", DocumentId, DestFolderId);

                var toRet = await _write.MoveDocumentAsync(DocumentId, DestFolderId, null, ct).ConfigureAwait(false);

                if (toRet)
                {
                    _fileLogger.LogDebug("Successfully moved document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                }
                else
                {
                    _fileLogger.LogWarning("Move operation returned false for document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                }

                return toRet;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to move document {DocumentId} to {DestFolderId}: {Error}", DocumentId, DestFolderId, ex.Message);
                _dbLogger.LogError(ex, "Failed to move document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                throw;
            }
        }
    }
}
