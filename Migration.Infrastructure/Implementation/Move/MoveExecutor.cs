using Alfresco.Abstraction.Interfaces;
using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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

        public async Task<Entry?> MoveAsync(string DocumentId, string DestFolderId, CancellationToken ct)
        {
            try
            {
                _fileLogger.LogDebug("Moving document {DocumentId} to folder {DestFolderId}", DocumentId, DestFolderId);

                var entry = await _write.MoveDocumentAsync(DocumentId, DestFolderId, null, ct).ConfigureAwait(false);

                if (entry != null)
                {
                    _fileLogger.LogDebug("Successfully moved document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                }
                else
                {
                    _fileLogger.LogWarning("Move operation returned null for document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                }

                return entry;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to move document {DocumentId} to {DestFolderId}: {Error}", DocumentId, DestFolderId, ex.Message);
                _dbLogger.LogError(ex, "Failed to move document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                throw;
            }
        }

        public async Task<Entry?> CopyAsync(string DocumentId, string DestFolderId, CancellationToken ct)
        {
            try
            {
                _fileLogger.LogDebug("Copying document {DocumentId} to folder {DestFolderId}", DocumentId, DestFolderId);

                var entry = await _write.CopyDocumentAsync(DocumentId, DestFolderId, null, ct).ConfigureAwait(false);

                if (entry != null)
                {
                    _fileLogger.LogDebug("Successfully copied document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                }
                else
                {
                    _fileLogger.LogWarning("Copy operation returned null for document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                }

                return entry;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Failed to copy document {DocumentId} to {DestFolderId}: {Error}", DocumentId, DestFolderId, ex.Message);
                _dbLogger.LogError(ex, "Failed to copy document {DocumentId} to {DestFolderId}", DocumentId, DestFolderId);
                throw;
            }
        }

        public async Task<Entry?> MoveWithPropertiesAsync(
            string nodeId,
            string destFolderId,
            bool useCopy,
            Dictionary<string, object> properties,
            CancellationToken ct)
        {
            var opName = useCopy ? "Copy" : "Move";

            // STEP 1: Move or Copy
            Entry? entry;
            try
            {
                entry = useCopy
                    ? await _write.CopyDocumentAsync(nodeId, destFolderId, null, ct).ConfigureAwait(false)
                    : await _write.MoveDocumentAsync(nodeId, destFolderId, null, ct).ConfigureAwait(false);

                if (entry == null)
                    throw new InvalidOperationException(
                        $"{opName} returned null for node {nodeId} — destination: {destFolderId}");

                _fileLogger.LogDebug("Node {NodeId} successfully {Op} to {Folder}.",
                    nodeId, opName.ToLower() + "d", destFolderId);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("[MoveWithPropertiesAsync] {Op} failed for node {NodeId}: {Error}",
                    opName, nodeId, ex.Message);
                _dbLogger.LogError(ex, "[MoveWithPropertiesAsync] {Op} failed for node {NodeId}", opName, nodeId);
                throw;
            }

            // STEP 2: Update properties — node is ALREADY MOVED at this point
            if (properties.Count == 0)
                return entry;

            try
            {
                await _write.UpdateNodePropertiesAsync(nodeId, properties, ct).ConfigureAwait(false);

                _fileLogger.LogDebug("Node {NodeId} properties updated ({Count} props).", nodeId, properties.Count);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError(
                    "[MoveWithPropertiesAsync] Node {NodeId} {Op} SUCCEEDED but property update FAILED: {Error}",
                    nodeId, opName.ToLower() + "d", ex.Message);
                _dbLogger.LogError(ex,
                    "[MoveWithPropertiesAsync] Node {NodeId} {Op} but properties failed", nodeId, opName.ToLower() + "d");

                throw new InvalidOperationException(
                    $"MOVED_UPDATE_PROPERTIES_FAILED: {ex.Message}", ex);
            }

            return entry;
        }
    }
}
