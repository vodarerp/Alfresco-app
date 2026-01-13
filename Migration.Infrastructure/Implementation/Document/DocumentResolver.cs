using Alfresco.Abstraction.Interfaces;
using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Mapper;
using Alfresco.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Migration.Abstraction.Configuration;
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
        private readonly IAlfrescoReadApi _read;
        private readonly IAlfrescoWriteApi _write;
        private readonly IClientApi _clientApi;
        private readonly ICurrentUserService _currentUserService;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;
        private readonly FolderNodeTypeMappingConfig _nodeTypeMapping;


        private readonly ConcurrentDictionary<string, (string FolderId, bool IsCreated)> _folderCache = new();
        private readonly ConcurrentDictionary<string, (Entry Folder, bool IsCreated)> _folderCache_New = new();


        private readonly LockStriping _lockStriping = new(1024);

        public DocumentResolver(
            IAlfrescoReadApi read,
            IAlfrescoWriteApi write,
            IClientApi clientApi,
            ICurrentUserService currentUserService,
            IServiceProvider serviceProvider,
            IOptions<FolderNodeTypeMappingConfig> nodeTypeMapping)
        {
            _read = read;
            _write = write;
            _clientApi = clientApi ?? throw new ArgumentNullException(nameof(clientApi));
            _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _dbLogger = loggerFactory.CreateLogger("DbLogger");
            _nodeTypeMapping = nodeTypeMapping?.Value ?? new FolderNodeTypeMappingConfig();
        }       
       
       
        public async Task<string> ResolveAsync(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, CancellationToken ct)
        {
            return await ResolveAsync(destinationRootId, newFolderName, properties, null, true, ct).ConfigureAwait(false);
        }

        public async Task<Entry> ResolveAsync_v1(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, string? customNodeType, bool createIfMissing, CancellationToken ct)
        {
            _fileLogger.LogInformation("ResolveAsync_v1: Starting - DestinationRootId: {DestinationRootId}, FolderName: {FolderName}, CustomNodeType: {NodeType}, CreateIfMissing: {CreateIfMissing}, Properties: {PropCount}",
               destinationRootId, newFolderName, customNodeType ?? "NULL", createIfMissing, properties?.Count ?? 0);

            var cacheKey = $"{destinationRootId}_{newFolderName}";     
            
            if (_folderCache_New.TryGetValue(cacheKey, out var cachedValue))
            {
                _fileLogger.LogInformation("ResolveAsync_v1: Cache HIT for folder '{FolderName}' under parent '{ParentId}' → FolderId: {FolderId}",
                    newFolderName, destinationRootId, cachedValue.Folder.Id);
                return cachedValue.Folder;
            }
            var folderLock = _lockStriping.GetLock(cacheKey);
            var lockStartTime = DateTime.UtcNow;

            await folderLock.WaitAsync(ct).ConfigureAwait(false);

            var lockWaitTime = DateTime.UtcNow - lockStartTime;
            _fileLogger.LogInformation("ResolveAsync_v1: Lock acquired for '{CacheKey}' after {WaitTime}ms", cacheKey, lockWaitTime.TotalMilliseconds);

            try
            {
                if (_folderCache_New.TryGetValue(cacheKey, out cachedValue))
                {
                    _fileLogger.LogInformation("ResolveAsync_v1: Cache HIT after lock (folder created by another thread) for '{FolderName}' → FolderId: {FolderId}",
                        newFolderName, cachedValue.Folder.Id);
                    return cachedValue.Folder;
                }
                var existingFolder = await _read.GetFolderByRelative_v1(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                if (existingFolder != null && !string.IsNullOrEmpty(existingFolder.Id))
                {
                    // Folder exists - check if it has required properties
                    _fileLogger.LogInformation("ResolveAsync_v1: Folder '{FolderName}' EXISTS in Alfresco. FolderId: {FolderId}. Checking for required properties...",
                        newFolderName, existingFolder.Id);

                    return existingFolder;
                }
               
                if (!createIfMissing)
                {
                    _fileLogger.LogWarning("ResolveAsync_v1: Folder '{FolderName}' doesn't exist and createIfMissing=FALSE. Returning empty Entry.", newFolderName);
                    return new Entry();
                }

                _fileLogger.LogInformation("ResolveAsync_v1: Folder '{FolderName}' doesn't exist. Calling ClientAPI to enrich properties before creation...",
                    newFolderName);

                var clientData = await CallClientApiForFolderAsync(newFolderName, ct).ConfigureAwait(false);
                var clientProps = BuildPropertiesClientData(clientData, newFolderName);
                
                if (properties == null)
                {
                    properties = new Dictionary<string, object>();
                }

                foreach (var prop in clientProps)
                {                   
                    properties[prop.Key] = prop.Value;
                }

                _fileLogger.LogInformation("ResolveAsync_v1: Creating folder '{FolderName}' under parent '{ParentId}' with {PropCount} properties...",
                    newFolderName, destinationRootId, properties.Count);

                Entry createdFolder;

                try
                {
                    createdFolder = await _write.CreateFolderAsync_v1(destinationRootId, newFolderName, properties, customNodeType, ct).ConfigureAwait(false);

                    _fileLogger.LogInformation("ResolveAsync_v1: Successfully created folder '{FolderName}' WITH properties. FolderId: {FolderId}, NodeType: {NodeType}",
                        newFolderName, createdFolder.Id, customNodeType ?? "cm:folder");
                }
                catch (Exception ex)
                {
                    _fileLogger.LogWarning("ResolveAsync_v1: Failed to create folder '{FolderName}' with properties - ErrorType: {ErrorType}, Message: {Message}. Checking if folder exists (race condition)...",
                        newFolderName, ex.GetType().Name, ex.Message);
                    _dbLogger.LogWarning(ex, "Failed to create folder '{FolderName}' with properties - Error: {ErrorType}",
                        newFolderName, ex.GetType().Name);

                    // Check if folder was created by another thread (race condition)
                    var raceCheckFolder = await _read.GetFolderByRelative_v1(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                    if (raceCheckFolder != null && !string.IsNullOrEmpty(raceCheckFolder.Id))
                    {
                        _fileLogger.LogInformation("ResolveAsync_v1: Folder '{FolderName}' EXISTS (race condition detected) - was created by another thread. FolderId: {FolderId}",
                            newFolderName, raceCheckFolder.Id);

                        var added = _folderCache_New.TryAdd(cacheKey, (raceCheckFolder, true));
                        _fileLogger.LogInformation("ResolveAsync_v1: Cache add result for race condition folder '{CacheKey}': {Result}", cacheKey, added ? "SUCCESS" : "FAILED");

                        return raceCheckFolder;
                    }

                    // Fallback: Try creating without properties
                    _fileLogger.LogWarning("ResolveAsync_v1: FALLBACK - Attempting to create folder '{FolderName}' WITHOUT properties (properties might be invalid)...", newFolderName);

                    try
                    {
                        createdFolder = await _write.CreateFolderAsync_v1(destinationRootId, newFolderName, null, customNodeType, ct).ConfigureAwait(false);

                        _fileLogger.LogWarning("ResolveAsync_v1: FALLBACK SUCCESS - Created folder '{FolderName}' WITHOUT properties. FolderId: {FolderId}, NodeType: {NodeType}",
                            newFolderName, createdFolder.Id, customNodeType ?? "cm:folder");
                    }
                    catch (Exception fallbackEx)
                    {
                        _fileLogger.LogError("ResolveAsync_v1: FALLBACK FAILED - Could not create folder '{FolderName}' even without properties. ErrorType: {ErrorType}, Message: {Message}",
                            newFolderName, fallbackEx.GetType().Name, fallbackEx.Message);
                        _dbLogger.LogError(fallbackEx, "Failed to create folder '{FolderName}' even without properties", newFolderName);
                        throw;
                    }
                }

                // Step 6: Cache the created folder and return
                _fileLogger.LogInformation("ResolveAsync_v1: Caching folder '{FolderName}' (FolderId: '{FolderId}') with IsCreated=TRUE for key '{CacheKey}'",
                    newFolderName, createdFolder.Id, cacheKey);

                var cacheAdded = _folderCache_New.TryAdd(cacheKey, (createdFolder, true));
                _fileLogger.LogInformation("ResolveAsync_v1: Cache add result: {Result}", cacheAdded ? "SUCCESS" : "FAILED (already exists)");

                _fileLogger.LogInformation("ResolveAsync_v1: COMPLETED - Returning Entry for folder '{FolderName}' (FolderId: {FolderId})",
                    newFolderName, createdFolder.Id);

                return createdFolder;
            }
            finally
            {
                folderLock.Release();
                _fileLogger.LogInformation("ResolveAsync_v1: Lock released for '{CacheKey}'", cacheKey);
            }
        }

        /*
        public async Task<Entry> ResolveAsync_v1(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, string? customNodeType, bool createIfMissing, CancellationToken ct)
        {
            _fileLogger.LogInformation("ResolveAsync: Starting - DestinationRootId: {DestinationRootId}, FolderName: {FolderName}, CustomNodeType: {NodeType}, CreateIfMissing: {CreateIfMissing}, Properties: {PropCount}",
               destinationRootId, newFolderName, customNodeType ?? "NULL", createIfMissing, properties?.Count ?? 0);

            var cacheKey = $"{destinationRootId}_{newFolderName}";
            bool isCreated = true, needCreate = true;
            _fileLogger.LogInformation("ResolveAsync: Checking cache with key '{CacheKey}'", cacheKey);

            if (_folderCache_New.TryGetValue(cacheKey, out var cachedValue))
            {
                _fileLogger.LogInformation("ResolveAsync: Cache HIT for folder '{FolderName}' under parent '{ParentId}' → FolderId: {FolderId}, IsCreated: {IsCreated}",
                    newFolderName, destinationRootId, cachedValue.Folder.Id, cachedValue.IsCreated);
                return cachedValue.Folder;
            }

            _fileLogger.LogInformation("ResolveAsync: Cache MISS for folder '{FolderName}', acquiring lock for key '{CacheKey}'...", newFolderName, cacheKey);

            var folderLock = _lockStriping.GetLock(cacheKey);

            _fileLogger.LogInformation("ResolveAsync: Waiting to acquire lock for '{CacheKey}'...", cacheKey);
            var lockStartTime = DateTime.UtcNow;

            await folderLock.WaitAsync(ct).ConfigureAwait(false);

            var lockWaitTime = DateTime.UtcNow - lockStartTime;
            _fileLogger.LogInformation("ResolveAsync: Lock acquired for '{CacheKey}' after {WaitTime}ms", cacheKey, lockWaitTime.TotalMilliseconds);

            try
            {

                _fileLogger.LogInformation("ResolveAsync: Double-checking cache after lock for '{CacheKey}'...", cacheKey);

                if (_folderCache_New.TryGetValue(cacheKey, out cachedValue))
                {
                    _fileLogger.LogInformation("ResolveAsync: Cache HIT after lock (folder created by another thread) for '{FolderName}' → FolderId: {FolderId}, IsCreated: {IsCreated}",
                        newFolderName, cachedValue.Folder.Id, cachedValue.IsCreated);
                    return cachedValue.Folder;
                }


                _fileLogger.LogInformation("ResolveAsync: STEP 4 - Checking if folder '{FolderName}' exists under parent '{ParentId}'...",
                    newFolderName, destinationRootId);

                var currentFolder = await _read.GetFolderByRelative_v1(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                if (currentFolder != null && !string.IsNullOrEmpty(currentFolder.Id))
                {
                    //folder postoji, proveriti da li sadrzi odredjene propertije, ako nema zvati client api za klijenta 
                    isCreated = false;
                    if (currentFolder.Properties.TryGetValue("ecm:docClientType", out var docClientType) )//&& currentFolder.Properties.TryGetValue("ecm:docStaff", out var docStaff))
                    {                        
                        _fileLogger.LogInformation("ResolveAsync: Folder '{FolderName}' already EXISTS in Alfresco. FolderId: {FolderId}, adding to cache with IsCreated=FALSE", newFolderName, currentFolder.Id);
                        // Cache the existing folder ID with IsCreated = FALSE (folder already existed)
                        var added = _folderCache_New.TryAdd(cacheKey, (currentFolder, isCreated));
                        _fileLogger.LogInformation("ResolveAsync: Cache add result for '{CacheKey}': {Result}", cacheKey, added ? "SUCCESS" : "FAILED (already exists)");
                        return currentFolder;
                    }
                    needCreate = false;
                    _fileLogger.LogInformation("ResolveAsync: Folder '{FolderName}' already EXISTS in Alfresco. FolderId: {FolderId}, adding to cache with IsCreated=FALSE", newFolderName, currentFolder.Id);

                }

                //-*-*-*-*-*-*-*-*-*               
                _fileLogger.LogInformation("ResolveAsync: STEP 5 - Folder '{FolderName}' doesn't exist in Alfresco, calling ClientAPI to enrich properties...",
                    newFolderName);

                var clientDataProps = await CallClientApiForFolderAsync(newFolderName, ct).ConfigureAwait(false);

                var cliProps = BuildPropertiesClientData(clientDataProps, newFolderName);

                _fileLogger.LogInformation("ResolveAsync: Built {Count} properties from ClientData, merging with existing properties...", cliProps.Count);

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
                        _fileLogger.LogInformation("ResolveAsync: Updating existing property '{Key}': OLD='{OldValue}' -> NEW='{NewValue}'",
                            prop.Key, properties[prop.Key], prop.Value);
                        properties[prop.Key] = prop.Value;
                    }
                    else
                    {
                        _fileLogger.LogInformation("ResolveAsync: Adding new property '{Key}' = '{Value}'", prop.Key, prop.Value);
                        properties.Add(prop.Key, prop.Value);
                    }

                }

                _fileLogger.LogInformation("ResolveAsync: Total properties after merge: {Count}", properties?.Count ?? 0);

                _fileLogger.LogInformation("ResolveAsync: STEP 6 - Creating folder '{FolderName}' under parent '{ParentId}'...",
                    newFolderName, destinationRootId);

                if (needCreate)
                {
                    Entry createdFolder = new();
                    if (properties != null && properties.Count > 0)
                    {
                        _fileLogger.LogInformation("ResolveAsync: Creating folder WITH properties ({Count} properties, NodeType: {NodeType})",
                            properties.Count, customNodeType ?? "cm:folder");

                        try
                        {
                            currentFolder = await _write.CreateFolderAsync_v1(destinationRootId, newFolderName, properties, customNodeType, ct).ConfigureAwait(false);

                            _fileLogger.LogInformation(
                                "ResolveAsync: Successfully created folder '{FolderName}' WITH properties. FolderId: {FolderId}, NodeType: {NodeType}",
                                newFolderName, createdFolder.Id, customNodeType ?? "cm:folder");
                            return createdFolder;
                        }
                        catch (Exception ex)
                        {
                            _fileLogger.LogWarning("ResolveAsync: Failed to create folder '{FolderName}' with properties - ErrorType: {ErrorType}, Message: {Message}. Checking if folder exists...",
                                newFolderName, ex.GetType().Name, ex.Message);
                            _dbLogger.LogWarning(ex,
                                "Failed to create folder '{FolderName}' with properties - Error: {ErrorType}",
                                newFolderName, ex.GetType().Name);


                            _fileLogger.LogInformation("ResolveAsync: Checking if folder '{FolderName}' exists after failed create attempt...", newFolderName);
                            currentFolder = await _read.GetFolderByRelative_v1(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                            if (!string.IsNullOrEmpty(currentFolder.Id))
                            {
                                _fileLogger.LogInformation(
                                    "ResolveAsync: Folder '{FolderName}' EXISTS (race condition detected) - was created by another thread. FolderId: {FolderId}",
                                    newFolderName, currentFolder.Id);

                                // Cache the folder ID with IsCreated = TRUE (was just created by another thread)
                                var added = _folderCache_New.TryAdd(cacheKey, (currentFolder, isCreated));
                                _fileLogger.LogInformation("ResolveAsync: Cache add result for race condition folder '{CacheKey}': {Result}", cacheKey, added ? "SUCCESS" : "FAILED");
                                return currentFolder;
                            }

                            // Fallback: Try without properties (folder truly doesn't exist, properties were the issue)
                            _fileLogger.LogWarning("ResolveAsync: FALLBACK - Attempting to create folder '{FolderName}' WITHOUT properties (properties might be invalid)...", newFolderName);
                            try
                            {
                                currentFolder = await _write.CreateFolderAsync_v1(destinationRootId, newFolderName, null, customNodeType, ct).ConfigureAwait(false);

                                _fileLogger.LogWarning(
                                    "ResolveAsync: FALLBACK SUCCESS - Created folder '{FolderName}' WITHOUT properties. FolderId: {FolderId}, NodeType: {NodeType}",
                                    newFolderName, currentFolder.Id, customNodeType ?? "cm:folder");
                            }
                            catch (Exception fallbackEx)
                            {
                                _fileLogger.LogError("ResolveAsync: FALLBACK FAILED - Could not create folder '{FolderName}' even without properties. ErrorType: {ErrorType}, Message: {Message}",
                                    newFolderName, fallbackEx.GetType().Name, fallbackEx.Message);
                                _dbLogger.LogError(fallbackEx,
                                    "Failed to create folder '{FolderName}' even without properties",
                                    newFolderName);
                                throw; // Re-throw if both attempts fail
                            }
                        }

                    }
                    else
                    {
                        // No properties provided, create normally
                        _fileLogger.LogInformation("ResolveAsync: Creating folder WITHOUT properties (NodeType: {NodeType})", customNodeType ?? "cm:folder");

                        currentFolder = await _write.CreateFolderAsync_v1(destinationRootId, newFolderName, null, customNodeType, ct).ConfigureAwait(false);

                        _fileLogger.LogInformation(
                            "ResolveAsync: Successfully created folder '{FolderName}' WITHOUT properties. FolderId: {FolderId}, NodeType: {NodeType}",
                            newFolderName, currentFolder.Id, customNodeType ?? "cm:folder");
                    } 
                }
                else // folder postoji ali mu fale propertiji
                {
                    currentFolder.Properties = properties;
                }
            }
            finally
            {
                folderLock.Release();
                _fileLogger.LogInformation("ResolveAsync: Lock released for '{CacheKey}'", cacheKey);
            }
            _fileLogger.LogInformation("ResolveAsync: STEP 7 - Caching folder ID '{FolderId}' with IsCreated=TRUE for key '{CacheKey}'", folderID, cacheKey);
            var added = _folderCache_New.TryAdd(cacheKey, (currentFolder, isCreated));
            _fileLogger.LogInformation("ResolveAsync: Cache add result: {Result}", cacheAdded ? "SUCCESS" : "FAILED (already exists)");

            _fileLogger.LogInformation("ResolveAsync: COMPLETED - Returning FolderId: {FolderId} for folder '{FolderName}'", folderID, newFolderName);
            return folderID;
            return new();
        }
        */
        public async Task<string> ResolveAsync(string destinationRootId,string newFolderName,Dictionary<string, object>? properties,string? customNodeType,bool createIfMissing,CancellationToken ct)
        {
            _fileLogger.LogInformation("ResolveAsync: Starting - DestinationRootId: {DestinationRootId}, FolderName: {FolderName}, CustomNodeType: {NodeType}, CreateIfMissing: {CreateIfMissing}, Properties: {PropCount}",
                destinationRootId, newFolderName, customNodeType ?? "NULL", createIfMissing, properties?.Count ?? 0);

            // Log properties details if they exist
            if (properties != null && properties.Count > 0)
            {
                _fileLogger.LogInformation("ResolveAsync: Properties for '{FolderName}':", newFolderName);
                foreach (var kvp in properties)
                {
                    _fileLogger.LogInformation("  {Key} = {Value}", kvp.Key, kvp.Value ?? "NULL");
                }
            }

          
            var cacheKey = $"{destinationRootId}_{newFolderName}";

            _fileLogger.LogInformation("ResolveAsync: Checking cache with key '{CacheKey}'", cacheKey);

            if (_folderCache.TryGetValue(cacheKey, out var cachedValue))
            {
                _fileLogger.LogInformation("ResolveAsync: Cache HIT for folder '{FolderName}' under parent '{ParentId}' → FolderId: {FolderId}, IsCreated: {IsCreated}",
                    newFolderName, destinationRootId, cachedValue.FolderId, cachedValue.IsCreated);
                return cachedValue.FolderId;
            }

            _fileLogger.LogInformation("ResolveAsync: Cache MISS for folder '{FolderName}', acquiring lock for key '{CacheKey}'...", newFolderName, cacheKey);

            var folderLock = _lockStriping.GetLock(cacheKey);

            _fileLogger.LogInformation("ResolveAsync: Waiting to acquire lock for '{CacheKey}'...", cacheKey);
            var lockStartTime = DateTime.UtcNow;

            await folderLock.WaitAsync(ct).ConfigureAwait(false);

            var lockWaitTime = DateTime.UtcNow - lockStartTime;
            _fileLogger.LogInformation("ResolveAsync: Lock acquired for '{CacheKey}' after {WaitTime}ms", cacheKey, lockWaitTime.TotalMilliseconds);

            try
            {
               
                _fileLogger.LogInformation("ResolveAsync: Double-checking cache after lock for '{CacheKey}'...", cacheKey);

                if (_folderCache.TryGetValue(cacheKey, out cachedValue))
                {
                    _fileLogger.LogInformation("ResolveAsync: Cache HIT after lock (folder created by another thread) for '{FolderName}' → FolderId: {FolderId}, IsCreated: {IsCreated}",
                        newFolderName, cachedValue.FolderId, cachedValue.IsCreated);
                    return cachedValue.FolderId;
                }

               
                _fileLogger.LogInformation("ResolveAsync: STEP 4 - Checking if folder '{FolderName}' exists under parent '{ParentId}'...",
                    newFolderName, destinationRootId);

                var folderID = await _read.GetFolderByRelative(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                _fileLogger.LogInformation("ResolveAsync: GetFolderByRelative returned: '{FolderId}'", folderID ?? "NULL");

                if (!string.IsNullOrEmpty(folderID))
                {
                    _fileLogger.LogInformation("ResolveAsync: Folder '{FolderName}' already EXISTS in Alfresco. FolderId: {FolderId}, adding to cache with IsCreated=FALSE",
                        newFolderName, folderID);

                    // Cache the existing folder ID with IsCreated = FALSE (folder already existed)
                    var added = _folderCache.TryAdd(cacheKey, (folderID, false));
                    _fileLogger.LogInformation("ResolveAsync: Cache add result for '{CacheKey}': {Result}", cacheKey, added ? "SUCCESS" : "FAILED (already exists)");
                    return folderID;
                }

               
                _fileLogger.LogInformation("ResolveAsync: STEP 5 - Folder '{FolderName}' doesn't exist in Alfresco, calling ClientAPI to enrich properties...",
                    newFolderName);

                var clientDataProps = await CallClientApiForFolderAsync(newFolderName, ct).ConfigureAwait(false);

                _fileLogger.LogInformation("ResolveAsync: ClientAPI call completed, building properties from ClientData");

                var cliProps = BuildPropertiesClientData(clientDataProps, newFolderName);

                _fileLogger.LogInformation("ResolveAsync: Built {Count} properties from ClientData, merging with existing properties...", cliProps.Count);

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
                        _fileLogger.LogInformation("ResolveAsync: Updating existing property '{Key}': OLD='{OldValue}' -> NEW='{NewValue}'",
                            prop.Key, properties[prop.Key], prop.Value);
                        properties[prop.Key] = prop.Value;
                    }
                    else
                    {
                        _fileLogger.LogInformation("ResolveAsync: Adding new property '{Key}' = '{Value}'", prop.Key, prop.Value);
                        properties.Add(prop.Key, prop.Value);
                    }

                }

                _fileLogger.LogInformation("ResolveAsync: Total properties after merge: {Count}", properties?.Count ?? 0);

                _fileLogger.LogInformation("ResolveAsync: STEP 6 - Creating folder '{FolderName}' under parent '{ParentId}'...",
                    newFolderName, destinationRootId);

                if (properties != null && properties.Count > 0)
                {
                    _fileLogger.LogInformation("ResolveAsync: Creating folder WITH properties ({Count} properties, NodeType: {NodeType})",
                        properties.Count, customNodeType ?? "cm:folder");

                    

                    try
                    {
                        folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, properties, customNodeType, ct).ConfigureAwait(false);
                        _fileLogger.LogInformation(
                            "ResolveAsync: Successfully created folder '{FolderName}' WITH properties. FolderId: {FolderId}, NodeType: {NodeType}",
                            newFolderName, folderID, customNodeType ?? "cm:folder");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger.LogWarning("ResolveAsync: Failed to create folder '{FolderName}' with properties - ErrorType: {ErrorType}, Message: {Message}. Checking if folder exists...",
                            newFolderName, ex.GetType().Name, ex.Message);
                        _dbLogger.LogWarning(ex,
                            "Failed to create folder '{FolderName}' with properties - Error: {ErrorType}",
                            newFolderName, ex.GetType().Name);

                       
                        _fileLogger.LogInformation("ResolveAsync: Checking if folder '{FolderName}' exists after failed create attempt...", newFolderName);
                        folderID = await _read.GetFolderByRelative(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                        if (!string.IsNullOrEmpty(folderID))
                        {
                            _fileLogger.LogInformation(
                                "ResolveAsync: Folder '{FolderName}' EXISTS (race condition detected) - was created by another thread. FolderId: {FolderId}",
                                newFolderName, folderID);

                            // Cache the folder ID with IsCreated = TRUE (was just created by another thread)
                            var added = _folderCache.TryAdd(cacheKey, (folderID, true));
                            _fileLogger.LogInformation("ResolveAsync: Cache add result for race condition folder '{CacheKey}': {Result}", cacheKey, added ? "SUCCESS" : "FAILED");
                            return folderID;
                        }

                        // Fallback: Try without properties (folder truly doesn't exist, properties were the issue)
                        _fileLogger.LogWarning("ResolveAsync: FALLBACK - Attempting to create folder '{FolderName}' WITHOUT properties (properties might be invalid)...", newFolderName);
                        try
                        {
                            folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, null, customNodeType, ct).ConfigureAwait(false);

                            _fileLogger.LogWarning(
                                "ResolveAsync: FALLBACK SUCCESS - Created folder '{FolderName}' WITHOUT properties. FolderId: {FolderId}, NodeType: {NodeType}",
                                newFolderName, folderID, customNodeType ?? "cm:folder");
                        }
                        catch (Exception fallbackEx)
                        {
                            _fileLogger.LogError("ResolveAsync: FALLBACK FAILED - Could not create folder '{FolderName}' even without properties. ErrorType: {ErrorType}, Message: {Message}",
                                newFolderName, fallbackEx.GetType().Name, fallbackEx.Message);
                            _dbLogger.LogError(fallbackEx,
                                "Failed to create folder '{FolderName}' even without properties",
                                newFolderName);
                            throw; // Re-throw if both attempts fail
                        }
                    }
                }
                else
                {
                    // No properties provided, create normally
                    _fileLogger.LogInformation("ResolveAsync: Creating folder WITHOUT properties (NodeType: {NodeType})", customNodeType ?? "cm:folder");

                    folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, null, customNodeType, ct).ConfigureAwait(false);

                    _fileLogger.LogInformation(
                        "ResolveAsync: Successfully created folder '{FolderName}' WITHOUT properties. FolderId: {FolderId}, NodeType: {NodeType}",
                        newFolderName, folderID, customNodeType ?? "cm:folder");
                }
                
                _fileLogger.LogInformation("ResolveAsync: STEP 7 - Caching folder ID '{FolderId}' with IsCreated=TRUE for key '{CacheKey}'", folderID, cacheKey);
                var cacheAdded = _folderCache.TryAdd(cacheKey, (folderID, true));
                _fileLogger.LogInformation("ResolveAsync: Cache add result: {Result}", cacheAdded ? "SUCCESS" : "FAILED (already exists)");

                _fileLogger.LogInformation("ResolveAsync: COMPLETED - Returning FolderId: {FolderId} for folder '{FolderName}'", folderID, newFolderName);
                return folderID;
            }
            finally
            {
                folderLock.Release();
                _fileLogger.LogInformation("ResolveAsync: Lock released for '{CacheKey}'", cacheKey);
            }
        }

        
        private async Task<ClientData> CallClientApiForFolderAsync(string folderName, CancellationToken ct)
        {
            _fileLogger.LogInformation("CallClientApiForFolderAsync: Starting - FolderName: '{FolderName}'", folderName);

            ClientData toRet = new();
            try
            {
                // Extract CoreID from folder name (e.g., "PI-102206" → "102206")
                _fileLogger.LogInformation("CallClientApiForFolderAsync: Extracting CoreID from folder name '{FolderName}'...", folderName);
                var coreId = DossierIdFormatter.ExtractCoreId(folderName);

                if (string.IsNullOrWhiteSpace(coreId))
                {
                    _fileLogger.LogWarning(
                        "CallClientApiForFolderAsync: Could not extract CoreID from folder name '{FolderName}', skipping ClientAPI call",
                        folderName);
                    return toRet;
                }

                _fileLogger.LogInformation("CallClientApiForFolderAsync: Extracted CoreID '{CoreId}' from folder '{FolderName}', calling ClientAPI GetClientDataAsync...",
                    coreId, folderName);

                // Call ClientAPI to retrieve client data
                toRet = await _clientApi.GetClientDataAsync(coreId, ct).ConfigureAwait(false);

                _fileLogger.LogInformation("CallClientApiForFolderAsync: GetClientDataAsync completed for CoreID '{CoreId}', calling GetClientDetailAsync...", coreId);

                var resCliDeteail = await _clientApi.GetClientDetailAsync(coreId, ct).ConfigureAwait(false);

                _fileLogger.LogInformation("CallClientApiForFolderAsync: GetClientDetailAsync completed for CoreID '{CoreId}'", coreId);

                if (resCliDeteail != null)
                {
                    if (toRet == null) toRet = new();

                    toRet.Residency = resCliDeteail.ClientGeneral.ResidentIndicator ?? "";
                    toRet.ClientName = resCliDeteail.Name ?? "";
                    toRet.MbrJmbg = resCliDeteail.ClientGeneral.ClientID ?? "";

                    _fileLogger.LogInformation(
                        "CallClientApiForFolderAsync: Enriched ClientData from ClientDetail for CoreID '{CoreId}' - Residency: '{Residency}', ClientName: '{ClientName}', MbrJmbg: '{MbrJmbg}'",
                        coreId, toRet.Residency, toRet.ClientName, toRet.MbrJmbg);
                }

                if (toRet != null)
                {
                    _fileLogger.LogInformation(
                        "CallClientApiForFolderAsync: Final ClientData for CoreID '{CoreId}':",
                        coreId);
                    _fileLogger.LogInformation("  ClientName: '{ClientName}'", toRet.ClientName ?? "NULL");
                    _fileLogger.LogInformation("  ClientType: '{ClientType}'", toRet.ClientType ?? "NULL");
                    _fileLogger.LogInformation("  MbrJmbg: '{MbrJmbg}'", toRet.MbrJmbg ?? "NULL");
                    _fileLogger.LogInformation("  Residency: '{Residency}'", toRet.Residency ?? "NULL");
                    _fileLogger.LogInformation("  Segment: '{Segment}'", toRet.Segment ?? "NULL");
                    _fileLogger.LogInformation("  ClientSubtype: '{ClientSubtype}'", toRet.ClientSubtype ?? "NULL");
                    _fileLogger.LogInformation("  BarCLEXOpu: '{BarCLEXOpu}'", toRet.BarCLEXOpu ?? "NULL");
                    _fileLogger.LogInformation("  Staff: '{Staff}'", toRet.Staff ?? "NULL");
                    _fileLogger.LogInformation("  BarCLEXGroupCode: '{BarCLEXGroupCode}'", toRet.BarCLEXGroupCode ?? "NULL");
                    _fileLogger.LogInformation("  BarCLEXGroupName: '{BarCLEXGroupName}'", toRet.BarCLEXGroupName ?? "NULL");
                    _fileLogger.LogInformation("  BarCLEXCode: '{BarCLEXCode}'", toRet.BarCLEXCode ?? "NULL");
                    _fileLogger.LogInformation("  BarCLEXName: '{BarCLEXName}'", toRet.BarCLEXName ?? "NULL");
                }
                else
                {
                    _fileLogger.LogWarning(
                        "CallClientApiForFolderAsync: ClientAPI returned null for CoreID '{CoreId}' (folder '{FolderName}')",
                        coreId, folderName);
                }
            }
            catch (ClientApiTimeoutException timeoutEx)
            {
                _fileLogger.LogError("DocumentResolver stopped - Client API Timeout: {Message}", timeoutEx.Message);
                _dbLogger.LogError(timeoutEx, "DocumentResolver stopped - Client API Timeout for folder '{FolderName}'", folderName);
                throw; // Re-throw to stop migration
            }
            catch (ClientApiRetryExhaustedException retryEx)
            {
                _fileLogger.LogError("DocumentResolver stopped - Client API Retry Exhausted: {Message}", retryEx.Message);
                _dbLogger.LogError(retryEx, "DocumentResolver stopped - Client API Retry Exhausted for folder '{FolderName}'", folderName);
                throw; // Re-throw to stop migration
            }
            catch (ClientApiException clientEx)
            {
                _fileLogger.LogError("DocumentResolver stopped - Client API Error: {Message}", clientEx.Message);
                _dbLogger.LogError(clientEx, "DocumentResolver stopped - Client API Error for folder '{FolderName}'", folderName);
                throw; // Re-throw to stop migration
            }
            catch (Exception ex)
            {
                // Log error but don't throw - Other unexpected errors should not block folder creation
                _fileLogger.LogWarning("Unexpected error calling ClientAPI for folder '{FolderName}'. Continuing with folder creation. Error: {Error}",
                    folderName, ex.Message);
                _dbLogger.LogError(ex,
                    "Unexpected error calling ClientAPI for folder '{FolderName}'",
                    folderName);
            }

            return toRet;
        }

        public async Task<(Entry Folder, bool IsCreated)> ResolveWithStatusAsync(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, UniqueFolderInfo? folderInfo, CancellationToken ct)
        {
            _fileLogger.LogInformation("ResolveWithStatusAsync: Starting - DestinationRootId: {DestinationRootId}, FolderName: {FolderName}, Properties: {PropCount}, HasFolderInfo: {HasInfo}",
                destinationRootId, newFolderName, properties?.Count ?? 0, folderInfo != null);

            // Check cache first
            var cacheKey = $"{destinationRootId}_{newFolderName}";
            _fileLogger.LogInformation("ResolveWithStatusAsync: Checking cache for key '{CacheKey}'", cacheKey);

            if (_folderCache_New.TryGetValue(cacheKey, out var cachedValue)) //TODO DOdati folder u cashe
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: Cache HIT for folder '{FolderName}' → FolderId: {FolderId}, IsCreated: {IsCreated}",
                    newFolderName, cachedValue.Folder.Id, cachedValue.IsCreated);
                return cachedValue;
            }

            _fileLogger.LogInformation("ResolveWithStatusAsync: Cache MISS, proceeding with folder resolution");

            string? customNodeType = null;
            if (folderInfo?.TargetDossierType.HasValue == true)
            {
                customNodeType = _nodeTypeMapping.GetNodeType(folderInfo.TargetDossierType.Value);
                _fileLogger.LogInformation(
                    "ResolveWithStatusAsync: Determined customNodeType '{NodeType}' for TargetDossierType {DossierType}",
                    customNodeType ?? "NULL", folderInfo.TargetDossierType.Value);
            }
            else
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: No FolderInfo or TargetDossierType provided, using default nodeType");
            }

            // Enrich properties with ECM standard properties if folderInfo is provided
            if (folderInfo != null)
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: Enriching properties with ECM data from FolderInfo...");
                properties = EnrichPropertiesWithEcmData(properties, newFolderName, folderInfo);
                _fileLogger.LogInformation("ResolveWithStatusAsync: Properties enriched, total count: {Count}", properties?.Count ?? 0);
            }
            else
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: No FolderInfo provided, skipping ECM enrichment");
            }

            // Call ResolveAsync_v1 with customNodeType (CRITICAL for Alfresco content model)
            _fileLogger.LogInformation("ResolveWithStatusAsync: Calling ResolveAsync_v1 with customNodeType '{NodeType}'...", customNodeType ?? "NULL");
            var folderEntry = await ResolveAsync_v1(destinationRootId, newFolderName, properties, customNodeType, true, ct).ConfigureAwait(false);
            _fileLogger.LogInformation("ResolveWithStatusAsync: ResolveAsync_v1 returned Entry with FolderId: {FolderId}", folderEntry.Id);

            // Retrieve from cache (should always hit now)
            _fileLogger.LogInformation("ResolveWithStatusAsync: Retrieving from cache after ResolveAsync_v1 for key '{CacheKey}'", cacheKey);
            if (_folderCache_New.TryGetValue(cacheKey, out cachedValue))
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: Cache hit after ResolveAsync_v1 - FolderId: {FolderId}, IsCreated: {IsCreated}",
                    cachedValue.Folder.Id, cachedValue.IsCreated);
                return cachedValue;
            }

            // Fallback (should never happen, but safety)
            _fileLogger.LogWarning("ResolveWithStatusAsync: Cache MISS after ResolveAsync_v1 for folder '{FolderName}' (unexpected!), returning (Entry: {FolderId}, IsCreated: false)",
                newFolderName, folderEntry.Id);
            return (folderEntry, false);
        }


        /* Old ResolveWithStatusAsync
         
        public async Task<(string FolderId, bool IsCreated)> ResolveWithStatusAsync(string destinationRootId,string newFolderName,Dictionary<string, object>? properties,UniqueFolderInfo? folderInfo,CancellationToken ct)
        {
            _fileLogger.LogInformation("ResolveWithStatusAsync: Starting - DestinationRootId: {DestinationRootId}, FolderName: {FolderName}, Properties: {PropCount}, HasFolderInfo: {HasInfo}",
                destinationRootId, newFolderName, properties?.Count ?? 0, folderInfo != null);

            // Check cache first
            var cacheKey = $"{destinationRootId}_{newFolderName}";
            _fileLogger.LogInformation("ResolveWithStatusAsync: Checking cache for key '{CacheKey}'", cacheKey);

            if (_folderCache.TryGetValue(cacheKey, out var cachedValue))
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: Cache HIT for folder '{FolderName}' → FolderId: {FolderId}, IsCreated: {IsCreated}",
                    newFolderName, cachedValue.FolderId, cachedValue.IsCreated);
                return cachedValue;
            }

            _fileLogger.LogInformation("ResolveWithStatusAsync: Cache MISS, proceeding with folder resolution");
                        
            string? customNodeType = null;
            if (folderInfo?.TargetDossierType.HasValue == true)
            {
                customNodeType = _nodeTypeMapping.GetNodeType(folderInfo.TargetDossierType.Value);
                _fileLogger.LogInformation(
                    "ResolveWithStatusAsync: Determined customNodeType '{NodeType}' for TargetDossierType {DossierType}",
                    customNodeType ?? "NULL", folderInfo.TargetDossierType.Value);
            }
            else
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: No FolderInfo or TargetDossierType provided, using default nodeType");
            }

            // Enrich properties with ECM standard properties if folderInfo is provided
            if (folderInfo != null)
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: Enriching properties with ECM data from FolderInfo...");
                properties = EnrichPropertiesWithEcmData(properties, newFolderName, folderInfo);
                _fileLogger.LogInformation("ResolveWithStatusAsync: Properties enriched, total count: {Count}", properties?.Count ?? 0);
            }
            else
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: No FolderInfo provided, skipping ECM enrichment");
            }

            // Call standard resolve with customNodeType (CRITICAL for Alfresco content model)
            _fileLogger.LogInformation("ResolveWithStatusAsync: Calling ResolveAsync with customNodeType '{NodeType}'...", customNodeType ?? "NULL");
            var folderId = await ResolveAsync(destinationRootId, newFolderName, properties, customNodeType, true, ct).ConfigureAwait(false);
            _fileLogger.LogInformation("ResolveWithStatusAsync: ResolveAsync returned FolderId: {FolderId}", folderId);

            // Retrieve from cache (should always hit now)
            _fileLogger.LogInformation("ResolveWithStatusAsync: Retrieving from cache after ResolveAsync for key '{CacheKey}'", cacheKey);
            if (_folderCache.TryGetValue(cacheKey, out cachedValue))
            {
                _fileLogger.LogInformation("ResolveWithStatusAsync: Cache hit after ResolveAsync - FolderId: {FolderId}, IsCreated: {IsCreated}",
                    cachedValue.FolderId, cachedValue.IsCreated);
                return cachedValue;
            }

            // Fallback (should never happen, but safety)
            _fileLogger.LogWarning("ResolveWithStatusAsync: Cache MISS after ResolveAsync for folder '{FolderName}' (unexpected!), returning (folderId: {FolderId}, IsCreated: false)", newFolderName, folderId);
            return (folderId, false);
        }

        */

        private Dictionary<string, object> EnrichPropertiesWithEcmData(Dictionary<string, object>? properties,string folderName,UniqueFolderInfo folderInfo)
        {
            _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Starting for folder '{FolderName}', TargetDossierType: {DossierType}",
                folderName, folderInfo.TargetDossierType?.ToString() ?? "NULL");

            properties = properties ?? new Dictionary<string, object>();

            // Extract CoreId from folder name
            var coreId = DossierIdFormatter.ExtractCoreId(folderName) ?? string.Empty;
            _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Extracted CoreId: '{CoreId}' from folder name", coreId);

            // Get bnkDossierType string (PI, LE, ACC, D)
            var bnkDossierType = MapDossierTypeToString(folderInfo.TargetDossierType);
            _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Mapped TargetDossierType {DossierTypeCode} to '{DossierType}'",
                folderInfo.TargetDossierType?.ToString() ?? "NULL", bnkDossierType);

            // Add ecm:bnkDossierType (PI, LE, ACC, D)
            if (!string.IsNullOrEmpty(bnkDossierType))
            {
                properties["ecm:bnkDossierType"] = bnkDossierType;
                _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Added ecm:bnkDossierType = '{DossierType}'", bnkDossierType);
            }

            // Add ecm:bnkSourceId = {bnkDossierType}-{bnkClientId}
            if (!string.IsNullOrEmpty(folderName))
            {
                properties["ecm:bnkSourceId"] = folderName;
                
                properties["ecm:naziv"] = folderName;
                _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Added ecm:bnkSourceId and ecm:naziv = '{FolderName}'", folderName);
            }

            if (!string.IsNullOrEmpty(folderInfo.CoreId))
            {
                properties["ecm:coreId"] = folderInfo.CoreId;
                properties["ecm:bnkClientId"] = folderInfo.CoreId;
                _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Added ecm:coreId and ecm:bnkClientId = '{CoreId}'", folderInfo.CoreId);
            }

            // Add current user information (who created the dossier folder)
            if (!string.IsNullOrEmpty(_currentUserService.UserId))
            {
                properties["ecm:kreiraoId"] = _currentUserService.UserId;
                _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Added ecm:kreiraoId = '{UserId}'", _currentUserService.UserId);
            }

            if (!string.IsNullOrEmpty(_currentUserService.DisplayName))
            {
                properties["ecm:createdByName"] = _currentUserService.DisplayName;
                _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Added ecm:createdByName = '{DisplayName}'", _currentUserService.DisplayName);
            }

            // Add deposit-specific properties if this is a deposit dossier (700)
            if (folderInfo.TargetDossierType == 700)
            {
                _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Processing DEPOSIT dossier (Type 700), adding deposit-specific properties...");

                // Add ecm:bnkTypeOfProduct from TipProizvoda
                if (!string.IsNullOrEmpty(folderInfo.TipProizvoda))
                {
                    properties["ecm:bnkTypeOfProduct"] = folderInfo.TipProizvoda;
                    _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Added ecm:bnkTypeOfProduct = '{TipProizvoda}'", folderInfo.TipProizvoda);
                }

                // Add ecm:bnkNumberOfContract formatted as YYYYMMDD
                if (folderInfo.CreationDate.HasValue)
                {
                    var contractNumber = folderInfo.CreationDate.Value.ToString("yyyyMMdd");
                    properties["ecm:bnkNumberOfContract"] = contractNumber;
                    _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Added ecm:bnkNumberOfContract = '{ContractNumber}' (from CreationDate: {CreationDate})",
                        contractNumber, folderInfo.CreationDate.Value.ToString("yyyy-MM-dd"));
                }
                else
                {
                    _fileLogger.LogWarning("EnrichPropertiesWithEcmData: CreationDate is NULL for deposit folder '{FolderName}', skipping ecm:bnkNumberOfContract", folderName);
                }
            }

            _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Enrichment completed - Total properties: {Count}", properties.Count);
            _fileLogger.LogInformation("EnrichPropertiesWithEcmData: Final enriched properties:");
            foreach (var kvp in properties)
            {
                _fileLogger.LogInformation("  {Key} = {Value}", kvp.Key, kvp.Value ?? "NULL");
            }

            return properties;
        }

        private Dictionary<string, object> BuildPropertiesClientData(ClientData clientData, string folderName)
        {
            _fileLogger.LogInformation("BuildPropertiesClientData: Starting for folder '{FolderName}'", folderName);

            Dictionary<string,object> properties = new Dictionary<string,object>();

            // Standard ECM properties for all dossiers
            properties.Add("ecm:bnkStatus", "ACTIVE");
            properties.Add("ecm:typeId", "dosije");
            properties.Add("ecm:bnkSource", "Heimdall");

            _fileLogger.LogInformation("BuildPropertiesClientData: Added standard ECM properties (bnkStatus, typeId, bnkSource)");

            // ClientAPI enriched properties
            properties.Add("ecm:bnkMTBR", clientData.MbrJmbg ?? string.Empty);
            properties.Add("ecm:bnkClientName", clientData.ClientName ?? string.Empty);
            properties.Add("ecm:bnkResidence", clientData.Residency ?? string.Empty);
            properties.Add("ecm:bnkClientType", clientData.Segment ?? string.Empty);
            properties.Add("ecm:bnkClientSubtype", clientData.ClientSubtype ?? string.Empty);
            properties.Add("ecm:bnkOfficeId", clientData.BarCLEXOpu ?? string.Empty);

            bool staf = clientData.Staff?.ToLowerInvariant() switch
            {
                "n" => false,
                null => false,
                "false" => false,
                "0" => false,
                _ => true
            };
            properties.Add("ecm:bnkStaff", staf);
            properties.Add("ecm:bnkBarclex", $"{clientData.BarCLEXGroupCode ?? string.Empty} {clientData.BarCLEXGroupName ?? string.Empty} ");
            properties.Add("ecm:bnkContributor", $"{clientData.BarCLEXCode ?? string.Empty} {clientData.BarCLEXName ?? string.Empty} ");

            _fileLogger.LogInformation("BuildPropertiesClientData: Built {Count} properties from ClientData", properties.Count);
            _fileLogger.LogInformation("BuildPropertiesClientData: Properties built:");
            foreach (var kvp in properties)
            {
                _fileLogger.LogInformation("  {Key} = {Value}", kvp.Key, kvp.Value ?? "NULL");
            }

            return properties;
        }

       
        private string MapDossierTypeToString(int? targetDossierType)
        {
            return targetDossierType switch
            {
                300 => "ACC",   // AccountPackage
                400 => "LE",    // ClientPL (Legal Entity)
                500 => "PI",    // ClientFL (Physical Individual)
                700 => "D",     // Deposit
                _ => string.Empty
            };
        }

    }
}
