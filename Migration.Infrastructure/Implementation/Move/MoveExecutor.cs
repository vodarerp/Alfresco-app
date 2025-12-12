using Alfresco.Abstraction.Interfaces;
using Microsoft.Extensions.DependencyInjection;
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

        public MoveExecutor(IDocStagingRepository doc, IAlfrescoWriteApi wirte, IServiceProvider serviceProvider)
        {
            _docRepo = doc;
            _write = wirte;

            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _dbLogger = loggerFactory.CreateLogger("DbLogger");
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

        public async Task<bool> CopyAsync(string DocumentId, string DestFolderId, CancellationToken ct)
        {
            try
            {
                _fileLogger.LogDebug("Copying document {DocumentId} to folder {DestFolderId}", DocumentId, DestFolderId);

                var toRet = await _write.CopyDocumentAsync(DocumentId, DestFolderId, null, ct).ConfigureAwait(false);

                if (toRet)
                {
                    _fileLogger.LogDebug("Successfully copied document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                }
                else
                {
                    _fileLogger.LogWarning("Copy operation returned false for document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                }

                return toRet;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to copy document {DocumentId} to {DestFolderId}: {Error}", DocumentId, DestFolderId, ex.Message);
                _dbLogger.LogError(ex, "Failed to copy document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                throw;
            }
        }
    }
}
