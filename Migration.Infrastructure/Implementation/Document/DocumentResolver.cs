using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Mapper;
using Alfresco.Contracts.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;
        private readonly FolderNodeTypeMappingConfig _nodeTypeMapping;

       
        private readonly ConcurrentDictionary<string, (string FolderId, bool IsCreated)> _folderCache = new();

       
        private readonly LockStriping _lockStriping = new(1024);

        public DocumentResolver(           
            IAlfrescoReadApi read,
            IAlfrescoWriteApi write,            
            IClientApi clientApi,
            IServiceProvider serviceProvider,
            IOptions<FolderNodeTypeMappingConfig> nodeTypeMapping)
        {           
            _read = read;
            _write = write;           
            _clientApi = clientApi ?? throw new ArgumentNullException(nameof(clientApi));
            var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _dbLogger = loggerFactory.CreateLogger("DbLogger");
            _nodeTypeMapping = nodeTypeMapping?.Value ?? new FolderNodeTypeMappingConfig();
        }       
       
       
        public async Task<string> ResolveAsync(string destinationRootId, string newFolderName, Dictionary<string, object>? properties, CancellationToken ct)
        {
            return await ResolveAsync(destinationRootId, newFolderName, properties, null, true, ct).ConfigureAwait(false);
        }
        
       
       
        public async Task<string> ResolveAsync(string destinationRootId,string newFolderName,Dictionary<string, object>? properties,string? customNodeType,bool createIfMissing,CancellationToken ct)
        {
            // ========================================
            // Step 1: Check cache first (fast path - no locking)
            // ========================================
            var cacheKey = $"{destinationRootId}_{newFolderName}";

            if (_folderCache.TryGetValue(cacheKey, out var cachedValue))
            {
                _fileLogger.LogTrace("Cache HIT for folder '{FolderName}' under parent '{ParentId}' → FolderId: {FolderId}, IsCreated: {IsCreated}",
                    newFolderName, destinationRootId, cachedValue.FolderId, cachedValue.IsCreated);
                return cachedValue.FolderId;
            }

            _fileLogger.LogDebug("Cache MISS for folder '{FolderName}', acquiring lock...", newFolderName);

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
                if (_folderCache.TryGetValue(cacheKey, out cachedValue))
                {
                    _fileLogger.LogTrace("Cache HIT after lock (folder created by another thread) for '{FolderName}' → FolderId: {FolderId}, IsCreated: {IsCreated}",
                        newFolderName, cachedValue.FolderId, cachedValue.IsCreated);
                    return cachedValue.FolderId;
                }

                // ========================================
                // Step 4: Check if folder exists in Alfresco
                // ========================================
                _fileLogger.LogDebug("Checking if folder '{FolderName}' exists under parent '{ParentId}'...",
                    newFolderName, destinationRootId);

                var folderID = await _read.GetFolderByRelative(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                if (!string.IsNullOrEmpty(folderID))
                {
                    _fileLogger.LogDebug("Folder '{FolderName}' already exists in Alfresco. FolderId: {FolderId}",
                        newFolderName, folderID);

                    // Cache the existing folder ID with IsCreated = FALSE (folder already existed)
                    _folderCache.TryAdd(cacheKey, (folderID, false));
                    return folderID;
                }

                // ========================================
                // Step 5: Folder doesn't exist - Call ClientAPI to notify/enrich
                // ========================================
                _fileLogger.LogDebug("Folder '{FolderName}' doesn't exist in Alfresco, calling ClientAPI...",
                    newFolderName);

                var clientDataProps = await CallClientApiForFolderAsync(newFolderName, ct).ConfigureAwait(false);

                var cliProps = BuildPropertiesClientData(clientDataProps, newFolderName);

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
                _fileLogger.LogDebug("Creating folder '{FolderName}' under parent '{ParentId}'...",
                    newFolderName, destinationRootId);

                if (properties != null && properties.Count > 0)
                {
                    try
                    {
                        _fileLogger.LogDebug(
                            "Attempting to create folder '{FolderName}' with {PropertyCount} properties (NodeType: {NodeType})",
                            newFolderName, properties.Count, customNodeType ?? "cm:folder");

                        folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, properties, customNodeType, ct).ConfigureAwait(false);
                        _fileLogger.LogInformation(
                            "Successfully created folder '{FolderName}' with properties. FolderId: {FolderId}, NodeType: {NodeType}",
                            newFolderName, folderID, customNodeType ?? "cm:folder");
                    }
                    catch (Exception ex)
                    {
                        _fileLogger.LogWarning("Failed to create folder '{FolderName}' with properties. Checking if exists...",
                            newFolderName);
                        _dbLogger.LogWarning(ex,
                            "Failed to create folder '{FolderName}' with properties - Error: {ErrorType}",
                            newFolderName, ex.GetType().Name);

                        // Check if folder exists (might have been created by another thread during race condition)
                        folderID = await _read.GetFolderByRelative(destinationRootId, newFolderName, ct).ConfigureAwait(false);

                        if (!string.IsNullOrEmpty(folderID))
                        {
                            _fileLogger.LogInformation(
                                "Folder '{FolderName}' was created by another thread during race condition. FolderId: {FolderId}",
                                newFolderName, folderID);

                            // Cache the folder ID with IsCreated = TRUE (was just created by another thread)
                            _folderCache.TryAdd(cacheKey, (folderID, true));
                            return folderID;
                        }

                        // Fallback: Try without properties (folder truly doesn't exist, properties were the issue)
                        try
                        {
                            folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, null, customNodeType, ct).ConfigureAwait(false);

                            _fileLogger.LogWarning(
                                "Successfully created folder '{FolderName}' WITHOUT properties as fallback. FolderId: {FolderId}, NodeType: {NodeType}",
                                newFolderName, folderID, customNodeType ?? "cm:folder");
                        }
                        catch (Exception fallbackEx)
                        {
                            _fileLogger.LogError("Failed to create folder '{FolderName}' even without properties",
                                newFolderName);
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
                    folderID = await _write.CreateFolderAsync(destinationRootId, newFolderName, null, customNodeType, ct).ConfigureAwait(false);

                    _fileLogger.LogDebug(
                        "Successfully created folder '{FolderName}' without properties. FolderId: {FolderId}, NodeType: {NodeType}",
                        newFolderName, folderID, customNodeType ?? "cm:folder");
                }

                // ========================================
                // Step 7: Cache the result with IsCreated = TRUE (folder was created in this migration)
                // ========================================
                _folderCache.TryAdd(cacheKey, (folderID, true));
                _fileLogger.LogTrace("Cached folder ID {FolderId} with IsCreated=TRUE for key '{CacheKey}'", folderID, cacheKey);

                return folderID;
            }
            finally
            {
                folderLock.Release();
            }
        }

        
        private async Task<ClientData> CallClientApiForFolderAsync(string folderName, CancellationToken ct)
        {
            ClientData toRet = new();
            try
            {
                // Extract CoreID from folder name (e.g., "PI-102206" → "102206")
                var coreId = DossierIdFormatter.ExtractCoreId(folderName);

                if (string.IsNullOrWhiteSpace(coreId))
                {
                    _fileLogger.LogWarning(
                        "Could not extract CoreID from folder name '{FolderName}', skipping ClientAPI call",
                        folderName);
                    return toRet;
                }

                _fileLogger.LogDebug("Extracted CoreID '{CoreId}' from folder '{FolderName}', calling ClientAPI...",
                    coreId, folderName);

                // Call ClientAPI to retrieve client data
                toRet = await _clientApi.GetClientDataAsync(coreId, ct).ConfigureAwait(false);

                if (toRet != null)
                {
                    _fileLogger.LogInformation(
                        "ClientAPI returned data for CoreID '{CoreId}': ClientName='{ClientName}', ClientType='{ClientType}'",
                        coreId, toRet.ClientName, toRet.ClientType);
                }
                else
                {
                    _fileLogger.LogWarning(
                        "ClientAPI returned null for CoreID '{CoreId}' (folder '{FolderName}')",
                        coreId, folderName);
                }
            }
            catch (Exception ex)
            {
                // Log error but don't throw - ClientAPI failure should not block folder creation
                _fileLogger.LogWarning("ClientAPI call failed for folder '{FolderName}'. Continuing with folder creation.",
                    folderName);
                _dbLogger.LogError(ex,
                    "ClientAPI call failed for folder '{FolderName}'",
                    folderName);
            }

            return toRet;
        }

                

        public async Task<(string FolderId, bool IsCreated)> ResolveWithStatusAsync(string destinationRootId,string newFolderName,Dictionary<string, object>? properties,UniqueFolderInfo? folderInfo,CancellationToken ct)
        {
            // Check cache first
            var cacheKey = $"{destinationRootId}_{newFolderName}";

            if (_folderCache.TryGetValue(cacheKey, out var cachedValue))
            {
                _fileLogger.LogTrace("Cache HIT (ResolveWithStatus) for folder '{FolderName}' → FolderId: {FolderId}, IsCreated: {IsCreated}",
                    newFolderName, cachedValue.FolderId, cachedValue.IsCreated);
                return cachedValue;
            }

            // Determine custom node type if folderInfo is provided
            // This is CRITICAL - customNodeType determines which Alfresco content model type is used
            // and which properties are available on the folder
            string? customNodeType = null;
            if (folderInfo?.TargetDossierType.HasValue == true)
            {
                customNodeType = _nodeTypeMapping.GetNodeType(folderInfo.TargetDossierType.Value);
                _fileLogger.LogDebug(
                    "ResolveWithStatus: Determined customNodeType '{NodeType}' for DossierType {DossierType}",
                    customNodeType, folderInfo.TargetDossierType.Value);
            }

            // Enrich properties with ECM standard properties if folderInfo is provided
            if (folderInfo != null)
            {
                properties = EnrichPropertiesWithEcmData(properties, newFolderName, folderInfo);
            }

            // Call standard resolve with customNodeType (CRITICAL for Alfresco content model)
            var folderId = await ResolveAsync(destinationRootId, newFolderName, properties, customNodeType, true, ct).ConfigureAwait(false);

            // Retrieve from cache (should always hit now)
            if (_folderCache.TryGetValue(cacheKey, out cachedValue))
            {
                return cachedValue;
            }

            // Fallback (should never happen, but safety)
            _fileLogger.LogWarning("Cache miss after ResolveAsync for folder '{FolderName}', returning (folderId, false)", newFolderName);
            return (folderId, false);
        }


        private Dictionary<string, object> EnrichPropertiesWithEcmData(Dictionary<string, object>? properties,string folderName,UniqueFolderInfo folderInfo)
        {
            properties = properties ?? new Dictionary<string, object>();

            // Extract CoreId from folder name
            var coreId = DossierIdFormatter.ExtractCoreId(folderName) ?? string.Empty;

            // Get bnkDossierType string (PI, LE, ACC, D)
            var bnkDossierType = MapDossierTypeToString(folderInfo.TargetDossierType);

            // Add ecm:bnkDossierType (PI, LE, ACC, D)
            if (!string.IsNullOrEmpty(bnkDossierType))
            {
                properties["ecm:bnkDossierType"] = bnkDossierType;
            }

            // Add ecm:bnkSourceId = {bnkDossierType}-{bnkClientId}
            if (!string.IsNullOrEmpty(folderName))
            {
                properties["ecm:bnkSourceId"] = folderName;
                properties["ecm:naziv"] = folderName;
            }

            if (!string.IsNullOrEmpty(folderInfo.CoreId))
            {
                properties["ecm:coreId"] = folderInfo.CoreId;
                properties["ecm:bnkClientId"] = folderInfo.CoreId;
            }
            // Add deposit-specific properties if this is a deposit dossier (700)
            if (folderInfo.TargetDossierType == 700)
            {
                // Add ecm:bnkTypeOfProduct from TipProizvoda
                if (!string.IsNullOrEmpty(folderInfo.TipProizvoda))
                {
                    properties["ecm:bnkTypeOfProduct"] = folderInfo.TipProizvoda;
                }

                // Add ecm:CoreId

                // Add ecm:bnkNumberOfContract formatted as YYYYMMDD
                if (folderInfo.CreationDate.HasValue)
                {
                    var contractNumber = folderInfo.CreationDate.Value.ToString("yyyyMMdd");
                    properties["ecm:bnkNumberOfContract"] = contractNumber;
                }
            }

            _fileLogger.LogDebug(
                "Enriched properties for dossier '{FolderName}': bnkDossierType={DossierType}, bnkSourceId={SourceId}",
                folderName, bnkDossierType, $"{bnkDossierType}-{coreId}");

            return properties;
        }

        private Dictionary<string, object> BuildPropertiesClientData(ClientData clientData, string folderName)
        {
            Dictionary<string,object> properties = new Dictionary<string,object>();

            // Extract CoreId from folder name (e.g., "PI-102206" → "102206")
            //var coreId = DossierIdFormatter.ExtractCoreId(folderName) ?? string.Empty;

            // Standard ECM properties for all dossiers
            //properties.Add("ecm:bnkClientId", coreId);
            properties.Add("ecm:bnkStatus", "ACTIVE");
            properties.Add("ecm:typeId", "dosije");
            properties.Add("ecm:bnkSource", "Heimdall");

            // ClientAPI enriched properties
            properties.Add("ecm:bnkClientType", clientData.Segment ?? string.Empty);
            properties.Add("ecm:bnkClientSubtype", clientData.ClientSubtype ?? string.Empty);
            properties.Add("ecm:bnkOfficeId", clientData.BarCLEXOpu ?? string.Empty);
            properties.Add("ecm:bnkStaff", clientData.Staff ?? string.Empty);
            properties.Add("ecm:bnkResidence", clientData.ClientName ?? string.Empty);
            properties.Add("ecm:bnkBarclex", $"{clientData.BarCLEXGroupCode ?? string.Empty} {clientData.BarCLEXGroupName ?? string.Empty} ");
            properties.Add("ecm:bnkContributor", $"{clientData.BarCLEXCode ?? string.Empty} {clientData.BarCLEXName ?? string.Empty} ");

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
