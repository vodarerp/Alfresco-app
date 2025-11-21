using Alfresco.Abstraction.Interfaces;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation.Document
{
    public class DocumentResolver : IDocumentResolver
    {
        private readonly IDocStagingRepository _doc;
        private readonly IAlfrescoReadApi _read;
        private readonly IAlfrescoWriteApi _write;
        private readonly IFolderManager _folderManager;
        private readonly ILogger<DocumentResolver> _logger;

        // Thread-safe cache: Key = "parentId_folderName", Value = folder ID
        // Example: "abc-123_DOSSIERS-LE" -> "def-456-ghi-789"
        private readonly ConcurrentDictionary<string, string> _folderCache = new();

        // Lock striping: Fixed 1024 locks instead of unlimited SemaphoreSlim instances
        // Prevents memory leak: 100 MB (1M locks) → 200 KB (1024 locks) = 99.8% reduction
        private readonly LockStriping _lockStriping = new(1024);

        public DocumentResolver(
            IDocStagingRepository doc,
            IAlfrescoReadApi read,
            IAlfrescoWriteApi write,
            IFolderManager folderManager,
            ILogger<DocumentResolver> logger)
        {
            _doc = doc;
            _read = read;
            _write = write;
            _folderManager = folderManager ?? throw new ArgumentNullException(nameof(folderManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        /// <summary>
        /// Resolves folder by name with backward compatibility (creates if missing)
        /// </summary>
        public async Task<string> ResolveAsync(string destinationRootId, string newFolderName, CancellationToken ct)
        {
            return await ResolveAsync(destinationRootId, newFolderName, null, true, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves folder by name with properties (creates if missing)
        /// </summary>
        public async Task<string> ResolveAsync(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, CancellationToken ct)
        {
            return await ResolveAsync(destinationRootId, newFolderName, properties, true, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Resolves folder with explicit control over creation behavior
        /// </summary>
        /// <param name="destinationRootId">Parent folder ID</param>
        /// <param name="newFolderName">Folder name to resolve</param>
        /// <param name="properties">Optional properties for folder creation</param>
        /// <param name="createIfMissing">If true, creates folder if not found. If false, throws exception.</param>
        /// <param name="ct">Cancellation token</param>
        /// <returns>Folder ID (existing or newly created)</returns>
        public async Task<string> ResolveAsync(
            string destinationRootId,
            string newFolderName,
            Dictionary<string, object>? properties,
            bool createIfMissing,
            CancellationToken ct)
        {
            // ========================================
            // Step 1: Check cache first (fast path - no locking)
            // ========================================
            var cacheKey = $"{destinationRootId}_{newFolderName}";

            if (_folderCache.TryGetValue(cacheKey, out var cachedFolderId))
            {
                _logger.LogTrace("Cache HIT for folder '{FolderName}' under parent '{ParentId}' → FolderId: {FolderId}",
                    newFolderName, destinationRootId, cachedFolderId);
                return cachedFolderId;
            }

            _logger.LogDebug("Cache MISS for folder '{FolderName}', acquiring lock...", newFolderName);

            // ========================================
            // Step 2: Acquire lock using lock striping (fixed 1024 locks, no memory leak)
            // ========================================
            var folderLock = _lockStriping.GetLock(cacheKey);

            await folderLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                // ========================================
                // Step 3: Double-check cache (another thread might have created folder while we were waiting)
                // ========================================
                if (_folderCache.TryGetValue(cacheKey, out cachedFolderId))
                {
                    _logger.LogTrace("Cache HIT after lock (folder created by another thread) for '{FolderName}' → FolderId: {FolderId}",
                        newFolderName, cachedFolderId);
                    return cachedFolderId;
                }

                // ========================================
                // Step 4: Check if folder exists in Alfresco
                // ========================================
                _logger.LogDebug("Checking if folder '{FolderName}' exists under parent '{ParentId}'...",
                    newFolderName, destinationRootId);

                var folderID = await _read.GetFolderByRelative(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(folderID))
                {
                    _logger.LogDebug("Folder '{FolderName}' already exists in Alfresco. FolderId: {FolderId}",
                        newFolderName, folderID);

                    // Cache the existing folder ID
                    _folderCache.TryAdd(cacheKey, folderID);
                    return folderID;
                }

                // ========================================
                // Step 5: Folder doesn't exist - check if we should create it
                // ========================================
                if (!createIfMissing)
                {
                    throw new InvalidOperationException(
                        $"Folder '{newFolderName}' not found under parent '{destinationRootId}' " +
                        $"and createIfMissing=false. This folder should have been created by FolderPreparationService.");
                }

                // ========================================
                // Step 6: Create folder (with or without properties)
                // ========================================
                _logger.LogDebug("Creating folder '{FolderName}' under parent '{ParentId}'...",
                    newFolderName, destinationRootId);

                if (properties != null && properties.Count > 0)
                {
                    try
                    {
                        _logger.LogDebug(
                            "Attempting to create folder '{FolderName}' with {PropertyCount} properties",
                            newFolderName, properties.Count);

                        folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, properties, ct).ConfigureAwait(false);

                        _logger.LogInformation(
                            "Successfully created folder '{FolderName}' with properties. FolderId: {FolderId}",
                            newFolderName, folderID);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to create folder '{FolderName}' with properties. " +
                            "Error: {ErrorType} - {ErrorMessage}. Attempting without properties.",
                            newFolderName, ex.GetType().Name, ex.Message);

                        // Fallback: Try without properties
                        try
                        {
                            folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, null, ct).ConfigureAwait(false);

                            _logger.LogWarning(
                                "Successfully created folder '{FolderName}' WITHOUT properties as fallback. FolderId: {FolderId}",
                                newFolderName, folderID);
                        }
                        catch (Exception fallbackEx)
                        {
                            _logger.LogError(fallbackEx,
                                "Failed to create folder '{FolderName}' even without properties. " +
                                "Error: {ErrorType} - {ErrorMessage}",
                                newFolderName, fallbackEx.GetType().Name, fallbackEx.Message);
                            throw; // Re-throw if both attempts fail
                        }
                    }
                }
                else
                {
                    // No properties provided, create normally
                    folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, null, ct).ConfigureAwait(false);

                    _logger.LogDebug(
                        "Successfully created folder '{FolderName}' without properties. FolderId: {FolderId}",
                        newFolderName, folderID);
                }

                // ========================================
                // Step 7: Cache the result
                // ========================================
                _folderCache.TryAdd(cacheKey, folderID);
                _logger.LogTrace("Cached folder ID {FolderId} for key '{CacheKey}'", folderID, cacheKey);

                return folderID;
            }
            finally
            {
                folderLock.Release();
            }
        }
    }
}
