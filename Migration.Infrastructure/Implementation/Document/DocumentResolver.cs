using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Mapper;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;

//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
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
        private readonly IClientApi _clientApi;
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
            IClientApi clientApi,
            ILogger<DocumentResolver> logger)
        {
            _doc = doc;
            _read = read;
            _write = write;
            _folderManager = folderManager ?? throw new ArgumentNullException(nameof(folderManager));
            _clientApi = clientApi ?? throw new ArgumentNullException(nameof(clientApi));
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
                // Step 5: Folder doesn't exist - Call ClientAPI to notify/enrich
                // ========================================
                _logger.LogDebug("Folder '{FolderName}' doesn't exist in Alfresco, calling ClientAPI...",
                    newFolderName);

                var clientDataProps = await CallClientApiForFolderAsync(newFolderName, ct).ConfigureAwait(false);

                var cliProps = BuildPropertiesClientData(clientDataProps);

                foreach (var prop in cliProps)
                {
                    // If properties dictionary is null, initialize it
                    if (properties == null)
                    {
                        properties = new Dictionary<string, object>();
                    }
                    // Add or update the property
                    if (properties.ContainsKey(prop.Key))
                    {
                        properties[prop.Key] = prop.Value;
                    }
                    else 
                    {
                        properties.Add(prop.Key, prop.Value);
                    }

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

                        //folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, properties, ct).ConfigureAwait(false);
                        folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, properties, ct).ConfigureAwait(false);
                        _logger.LogInformation(
                            "Successfully created folder '{FolderName}' with properties. FolderId: {FolderId}",
                            newFolderName, folderID);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to create folder '{FolderName}' with properties. " +
                            "Error: {ErrorType} - {ErrorMessage}. Checking if folder was created by another thread...",
                            newFolderName, ex.GetType().Name, ex.Message);

                        // Check if folder exists (might have been created by another thread during race condition)
                        folderID = await _read.GetFolderByRelative(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                        if (!string.IsNullOrEmpty(folderID))
                        {
                            _logger.LogInformation(
                                "Folder '{FolderName}' was created by another thread during race condition. FolderId: {FolderId}",
                                newFolderName, folderID);

                            // Cache the folder ID and return
                            _folderCache.TryAdd(cacheKey, folderID);
                            return folderID;
                        }

                        // Fallback: Try without properties (folder truly doesn't exist, properties were the issue)
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

        /// <summary>
        /// Calls ClientAPI to retrieve client data for folder enrichment.
        /// Extracts CoreID from folder name and calls GetClientDataAsync.
        /// </summary>
        /// <param name="folderName">Folder name (e.g., "PI-102206", "ACC-13001926")</param>
        /// <param name="ct">Cancellation token</param>
        /// 

        private async Task<ClientData> CallClientApiForFolderAsync(string folderName, CancellationToken ct)
        {
            ClientData toRet = new();
            try
            {
                // Extract CoreID from folder name (e.g., "PI-102206" → "102206")
                var coreId = DossierIdFormatter.ExtractCoreId(folderName);

                if (string.IsNullOrWhiteSpace(coreId))
                {
                    _logger.LogWarning(
                        "Could not extract CoreID from folder name '{FolderName}', skipping ClientAPI call",
                        folderName);
                    return toRet;
                }

                _logger.LogDebug("Extracted CoreID '{CoreId}' from folder '{FolderName}', calling ClientAPI...",
                    coreId, folderName);

                // Call ClientAPI to retrieve client data
                toRet = await _clientApi.GetClientDataAsync(coreId, ct).ConfigureAwait(false);

                if (toRet != null)
                {
                    _logger.LogInformation(
                        "ClientAPI returned data for CoreID '{CoreId}': ClientName='{ClientName}', ClientType='{ClientType}'",
                        coreId, toRet.ClientName, toRet.ClientType);
                }
                else
                {
                    _logger.LogWarning(
                        "ClientAPI returned null for CoreID '{CoreId}' (folder '{FolderName}')",
                        coreId, folderName);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't throw - ClientAPI failure should not block folder creation
                _logger.LogError(ex,
                    "ClientAPI call failed for folder '{FolderName}'. Error: {ErrorType} - {ErrorMessage}. Continuing with folder creation.",
                    folderName, ex.GetType().Name, ex.Message);
            }

            return toRet;
        }

        private Dictionary<string, object> BuildPropertiesClientData(ClientData clientData) 
        {
            Dictionary<string,object> properties = new Dictionary<string,object>();

            properties.Add("ecm:bnkClientType", clientData.Segment ?? string.Empty);
            properties.Add("ecm:bnkClientSubtype", clientData.ClientSubtype ?? string.Empty);
            properties.Add("ecm:bnkOfficeId", clientData.BarCLEXOpu ?? string.Empty);            
            properties.Add("ecm:bnkStaff", clientData.Staff ?? string.Empty);
            properties.Add("ecm:bnkResidence", clientData.ClientName ?? string.Empty);
            properties.Add("ecm:bnkBarclex", $"{clientData.BarCLEXGroupCode ?? string.Empty} {clientData.BarCLEXGroupName ?? string.Empty} ");
            properties.Add("ecm:bnkContributor", $"{clientData.BarCLEXCode ?? string.Empty} {clientData.BarCLEXName ?? string.Empty} ");
           



            return properties;
        }

    }
}
