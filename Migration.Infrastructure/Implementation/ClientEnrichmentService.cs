using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Service for enriching folder and document metadata with client data from ClientAPI.
    ///
    /// IMPORTANT: This service is ready but integration with ClientAPI should be enabled
    /// by uncommenting the constructor injection and method calls in existing services
    /// when ClientAPI becomes available.
    /// </summary>
    public class ClientEnrichmentService : IClientEnrichmentService
    {
        private readonly IClientApi _clientApi;
        private readonly ILogger<ClientEnrichmentService> _logger;
        private readonly IAlfrescoReadApi? _alfrescoReadApi;
        private readonly IAlfrescoWriteApi? _alfrescoWriteApi;

        public ClientEnrichmentService(
            IClientApi clientApi,
            ILogger<ClientEnrichmentService> logger,
            IAlfrescoReadApi? alfrescoReadApi = null,
            IAlfrescoWriteApi? alfrescoWriteApi = null)
        {
            _clientApi = clientApi ?? throw new ArgumentNullException(nameof(clientApi));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _alfrescoReadApi = alfrescoReadApi;
            _alfrescoWriteApi = alfrescoWriteApi;
        }

        public async Task<FolderStaging> EnrichFolderWithClientDataAsync(
            FolderStaging folder,
            CancellationToken ct = default)
        {
            if (folder == null)
            {
                throw new ArgumentNullException(nameof(folder));
            }

            if (string.IsNullOrWhiteSpace(folder.CoreId))
            {
                _logger.LogWarning(
                    "Cannot enrich folder {FolderId} ({FolderName}) - CoreId is missing",
                    folder.Id, folder.Name);
                return folder;
            }

            try
            {
                // Check if new folder exists before calling ClientAPI
                // If old folder is "PI-123", check if new folder "PI123" exists
                if (_alfrescoReadApi != null && _alfrescoWriteApi != null &&
                    !string.IsNullOrWhiteSpace(folder.Name) &&
                    !string.IsNullOrWhiteSpace(folder.DossierDestFolderId) &&
                    !string.IsNullOrWhiteSpace(folder.NodeId))
                {
                    // Remove dashes from folder name to get new folder name (PI-123 -> PI123)
                    var newFolderName = folder.Name.Replace("-", string.Empty);

                    _logger.LogDebug(
                        "Checking if new folder '{NewFolderName}' exists for old folder '{OldFolderName}' in parent '{ParentId}'",
                        newFolderName, folder.Name, folder.DossierDestFolderId);

                    var newFolderExists = await _alfrescoReadApi.FolderExistsAsync(
                        folder.DossierDestFolderId,
                        newFolderName,
                        ct).ConfigureAwait(false);

                    if (newFolderExists)
                    {
                        _logger.LogInformation(
                            "New folder '{NewFolderName}' already exists for folder {FolderId}. Skipping ClientAPI call and folder creation.",
                            newFolderName, folder.Id);
                        return folder;
                    }

                    _logger.LogDebug(
                        "New folder '{NewFolderName}' does not exist. Will copy properties from old folder and enrich with ClientAPI.",
                        newFolderName);

                    // Get old folder with properties
                    var oldFolder = await _alfrescoReadApi.GetNodeByIdAsync(folder.NodeId, ct)
                        .ConfigureAwait(false);

                    if (oldFolder?.Entry?.Properties == null)
                    {
                        _logger.LogWarning(
                            "Could not retrieve properties from old folder {FolderId} (NodeId: {NodeId}). Proceeding with ClientAPI only.",
                            folder.Id, folder.NodeId);
                    }
                    else
                    {
                        _logger.LogDebug(
                            "Retrieved {Count} properties from old folder '{OldFolderName}'",
                            oldFolder.Entry.Properties.Count, folder.Name);

                        // Get ClientAPI data
                        var clientData = await _clientApi.GetClientDataAsync(folder.CoreId, ct)
                            .ConfigureAwait(false);

                        // Build properties for new folder combining old folder properties and ClientAPI data
                        var newFolderProperties = BuildNewFolderProperties(oldFolder.Entry.Properties, clientData, folder);

                        // Create new folder with properties
                        var newFolderId = await _alfrescoWriteApi.CreateFolderAsync(
                            folder.DossierDestFolderId,
                            newFolderName,
                            newFolderProperties,
                            ct).ConfigureAwait(false);

                        _logger.LogInformation(
                            "Created new folder '{NewFolderName}' (NodeId: {NewFolderId}) with {Count} properties copied from old folder '{OldFolderName}' and enriched with ClientAPI data",
                            newFolderName, newFolderId, newFolderProperties.Count, folder.Name);

                        // Update folder staging with new folder ID
                        folder.DestFolderId = newFolderId;

                        // Populate folder staging with ClientAPI data
                        PopulateFolderWithClientData(folder, clientData);

                        return folder;
                    }
                }

                _logger.LogDebug(
                    "Enriching folder {FolderId} ({FolderName}) with client data for CoreId: {CoreId}",
                    folder.Id, folder.Name, folder.CoreId);

                var clientDataFallback = await _clientApi.GetClientDataAsync(folder.CoreId, ct)
                    .ConfigureAwait(false);

                // Populate all client-related fields from ClientAPI response
                PopulateFolderWithClientData(folder, clientDataFallback);

                _logger.LogInformation(
                    "Successfully enriched folder {FolderId} with client data: " +
                    "CoreId={CoreId}, ClientName={ClientName}, ClientType={ClientType}, " +
                    "MbrJmbg={MbrJmbg}, Residency={Residency}",
                    folder.Id, folder.CoreId, folder.ClientName, folder.ClientType,
                    folder.MbrJmbg, folder.Residency);

                return folder;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to enrich folder {FolderId} ({FolderName}) with client data for CoreId: {CoreId}. " +
                    "Continuing without ClientAPI properties. Error: {ErrorType} - {ErrorMessage}",
                    folder.Id, folder.Name, folder.CoreId, ex.GetType().Name, ex.Message);

                // Return folder without ClientAPI properties - application continues
                return folder;
            }
        }

        public async Task<DocStaging> EnrichDocumentWithAccountsAsync(
            DocStaging document,
            CancellationToken ct = default)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            // Only enrich KDP documents (00824, 00099)
            // Per documentation line 121-129: Only these document types need account numbers
            if (!IsKdpDocument(document.DocumentType))
            {
                _logger.LogDebug(
                    "Skipping account enrichment for document {DocId} - not a KDP document (type: {DocumentType})",
                    document.Id, document.DocumentType);
                return document;
            }

            if (string.IsNullOrWhiteSpace(document.CoreId))
            {
                _logger.LogWarning(
                    "Cannot enrich document {DocId} with accounts - CoreId is missing",
                    document.Id);
                return document;
            }

            if (!document.OriginalCreatedAt.HasValue)
            {
                _logger.LogWarning(
                    "Cannot enrich document {DocId} with accounts - OriginalCreatedAt is missing",
                    document.Id);
                return document;
            }

            try
            {
                _logger.LogDebug(
                    "Enriching document {DocId} (type: {DocumentType}) with active accounts for CoreId: {CoreId} as of {Date}",
                    document.Id, document.DocumentType, document.CoreId, document.OriginalCreatedAt);

                var accounts = await _clientApi.GetActiveAccountsAsync(
                        document.CoreId,
                        document.OriginalCreatedAt.Value,
                        ct)
                    .ConfigureAwait(false);

                if (accounts == null || !accounts.Any())
                {
                    _logger.LogWarning(
                        "No active accounts found for document {DocId}, CoreId: {CoreId}, Date: {Date}",
                        document.Id, document.CoreId, document.OriginalCreatedAt);
                    document.AccountNumbers = string.Empty;
                    return document;
                }

                // Per documentation line 123-129: "racuni su odvojeni zarezom"
                document.AccountNumbers = string.Join(",", accounts);

                _logger.LogInformation(
                    "Successfully enriched document {DocId} with {Count} active accounts: {AccountNumbers}",
                    document.Id, accounts.Count, document.AccountNumbers);

                return document;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to enrich document {DocId} with active accounts for CoreId: {CoreId}. " +
                    "Continuing without account numbers. Error: {ErrorType} - {ErrorMessage}",
                    document.Id, document.CoreId, ex.GetType().Name, ex.Message);

                // Set empty account numbers and continue - application continues
                document.AccountNumbers = string.Empty;
                return document;
            }
        }

        public async Task<bool> ValidateClientAsync(string coreId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                _logger.LogWarning("Cannot validate client - CoreId is null or empty");
                return false;
            }

            try
            {
                _logger.LogDebug("Validating client exists for CoreId: {CoreId}", coreId);

                var exists = await _clientApi.ValidateClientExistsAsync(coreId, ct)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Client validation for CoreId: {CoreId} result: {Exists}",
                    coreId, exists);

                return exists;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Failed to validate client for CoreId: {CoreId}. " +
                    "Assuming client does not exist. Error: {ErrorType} - {ErrorMessage}",
                    coreId, ex.GetType().Name, ex.Message);

                // Return false on error - treat as non-existent client
                return false;
            }
        }

        /// <summary>
        /// Checks if document type is KDP (Kartica Deponovanog Potpisa) requiring account enrichment.
        /// Per documentation: Only types 00099 and 00824 need account numbers populated.
        /// </summary>
        private bool IsKdpDocument(string? documentType)
        {
            if (string.IsNullOrWhiteSpace(documentType))
            {
                return false;
            }

            // KDP document types that require account enrichment
            return documentType == "00099" || documentType == "00824";
        }

        /// <summary>
        /// Populates FolderStaging object with ClientData properties
        /// </summary>
        private void PopulateFolderWithClientData(FolderStaging folder, ClientData clientData)
        {
            folder.ClientName = clientData.ClientName;
            folder.MbrJmbg = clientData.MbrJmbg;
            folder.ClientType = clientData.ClientType;
            folder.ClientSubtype = clientData.ClientSubtype;
            folder.Residency = clientData.Residency;
            folder.Segment = clientData.Segment;
            folder.Staff = clientData.Staff;
            folder.OpuUser = clientData.OpuUser;
            folder.OpuRealization = clientData.OpuRealization;
            folder.Barclex = clientData.Barclex;
            folder.Collaborator = clientData.Collaborator;
            folder.BarCLEXName = clientData.BarCLEXName;
            folder.BarCLEXOpu = clientData.BarCLEXOpu;
            folder.BarCLEXGroupName = clientData.BarCLEXGroupName;
            folder.BarCLEXGroupCode = clientData.BarCLEXGroupCode;
            folder.BarCLEXCode = clientData.BarCLEXCode;
        }

        /// <summary>
        /// Builds properties dictionary for new folder by copying from old folder and enriching with ClientAPI data
        /// Maps ClientAPI properties to Alfresco bnk* properties
        /// </summary>
        private Dictionary<string, object> BuildNewFolderProperties(
            Dictionary<string, object> oldFolderProperties,
            ClientData clientData,
            FolderStaging folder)
        {
            var properties = new Dictionary<string, object>();

            // Helper to safely get value from old properties
            object? GetOldProperty(string key)
            {
                return oldFolderProperties.TryGetValue(key, out var value) ? value : null;
            }

            // Copy relevant properties from old folder and override with ClientAPI data where available

            // ecm:coreId - from folder or old properties
            properties["ecm:coreId"] = folder.CoreId ?? GetOldProperty("ecm:coreId") ?? string.Empty;

            // ecm:jmbg / ecm:mbrJmbg - Client API "bnkJmbg"
            properties["ecm:jmbg"] = clientData.MbrJmbg ?? GetOldProperty("ecm:jmbg") ?? string.Empty;
            properties["ecm:mbrJmbg"] = clientData.MbrJmbg ?? GetOldProperty("ecm:mbrJmbg") ?? string.Empty;

            // ecm:clientName - Client API "bnkClientName"
            properties["ecm:clientName"] = clientData.ClientName ?? GetOldProperty("ecm:clientName") ?? string.Empty;

            // ecm:bnkClientType - Client API "segment"
            properties["ecm:bnkClientType"] = clientData.Segment ?? GetOldProperty("ecm:bnkClientType") ?? string.Empty;

            // ecm:clientSubtype - Client API "clientSubType"
            properties["ecm:clientSubtype"] = clientData.ClientSubtype ?? GetOldProperty("ecm:clientSubtype") ?? string.Empty;

            // ecm:bnkOfficeId - Client API "barCLEXOpu"
            properties["ecm:bnkOfficeId"] = clientData.BarCLEXOpu ?? GetOldProperty("ecm:bnkOfficeId") ?? string.Empty;

            // ecm:staff / ecm:docStaff - Client API "staff"
            properties["ecm:staff"] = clientData.Staff ?? GetOldProperty("ecm:staff") ?? string.Empty;
            properties["ecm:docStaff"] = clientData.Staff ?? GetOldProperty("ecm:docStaff") ?? string.Empty;

            // ecm:bnkTypeOfProduct - from old folder
            properties["ecm:bnkTypeOfProduct"] = folder.ProductType ?? GetOldProperty("ecm:bnkTypeOfProduct") ?? string.Empty;

            // ecm:bnkAccountNumber - from old folder (not in requirements, keeping for compatibility)
            properties["ecm:bnkAccountNumber"] = GetOldProperty("ecm:bnkAccountNumber") ?? string.Empty;

            // ecm:barclex - Client API "barCLEXGroupCode" + "barCLEXGroupName"
            var barclexValue = string.Empty;
            if (!string.IsNullOrWhiteSpace(clientData.BarCLEXGroupCode) && !string.IsNullOrWhiteSpace(clientData.BarCLEXGroupName))
            {
                barclexValue = $"{clientData.BarCLEXGroupCode} - {clientData.BarCLEXGroupName}";
            }
            else if (!string.IsNullOrWhiteSpace(clientData.BarCLEXGroupCode))
            {
                barclexValue = clientData.BarCLEXGroupCode;
            }
            else if (!string.IsNullOrWhiteSpace(clientData.BarCLEXGroupName))
            {
                barclexValue = clientData.BarCLEXGroupName;
            }
            properties["ecm:barclex"] = barclexValue != string.Empty ? barclexValue : GetOldProperty("ecm:barclex") ?? string.Empty;

            // ecm:collaborator - Client API "barCLEXCode" + "barCLEXName"
            var contributorValue = string.Empty;
            if (!string.IsNullOrWhiteSpace(clientData.BarCLEXCode) && !string.IsNullOrWhiteSpace(clientData.BarCLEXName))
            {
                contributorValue = $"{clientData.BarCLEXCode} - {clientData.BarCLEXName}";
            }
            else if (!string.IsNullOrWhiteSpace(clientData.BarCLEXCode))
            {
                contributorValue = clientData.BarCLEXCode;
            }
            else if (!string.IsNullOrWhiteSpace(clientData.BarCLEXName))
            {
                contributorValue = clientData.BarCLEXName;
            }
            properties["ecm:collaborator"] = contributorValue != string.Empty ? contributorValue : GetOldProperty("ecm:collaborator") ?? string.Empty;

            // ecm:bnkSourceId / ecm:source - from old folder
            properties["ecm:bnkSourceId"] = folder.Source ?? GetOldProperty("ecm:bnkSourceId") ?? string.Empty;
            properties["ecm:source"] = folder.Source ?? GetOldProperty("ecm:source") ?? string.Empty;

            // ecm:opuRealization - from old folder or ClientAPI
            properties["ecm:opuRealization"] = clientData.OpuRealization ?? GetOldProperty("ecm:opuRealization") ?? string.Empty;

            // ecm:contractNumber / ecm:bnkNumberOfContract - from folder
            properties["ecm:contractNumber"] = folder.ContractNumber ?? GetOldProperty("ecm:contractNumber") ?? string.Empty;
            properties["ecm:bnkNumberOfContract"] = folder.ContractNumber ?? GetOldProperty("ecm:bnkNumberOfContract") ?? string.Empty;

            // ecm:residency / ecm:bnkResidence - Client API
            properties["ecm:residency"] = clientData.Residency ?? GetOldProperty("ecm:residency") ?? string.Empty;
            properties["ecm:bnkResidence"] = clientData.Residency ?? GetOldProperty("ecm:bnkResidence") ?? string.Empty;

            // ecm:bnkSource - from folder
            properties["ecm:bnkSource"] = folder.Source ?? GetOldProperty("ecm:bnkSource") ?? string.Empty;

            // ecm:status / ecm:bnkStatus - from old folder
            properties["ecm:status"] = GetOldProperty("ecm:status") ?? string.Empty;
            properties["ecm:bnkStatus"] = GetOldProperty("ecm:bnkStatus") ?? string.Empty;

            // ecm:bnkDossierType - from folder
            properties["ecm:bnkDossierType"] = folder.TipDosijea ?? GetOldProperty("ecm:bnkDossierType") ?? string.Empty;

            // Additional important properties from old folder
            properties["ecm:uniqueFolderId"] = folder.UniqueIdentifier ?? GetOldProperty("ecm:uniqueFolderId") ?? string.Empty;
            properties["ecm:clientType"] = clientData.ClientType ?? GetOldProperty("ecm:clientType") ?? string.Empty;
            properties["ecm:segment"] = clientData.Segment ?? GetOldProperty("ecm:segment") ?? string.Empty;
            properties["ecm:productType"] = folder.ProductType ?? GetOldProperty("ecm:productType") ?? string.Empty;
            properties["ecm:batch"] = folder.Batch ?? GetOldProperty("ecm:batch") ?? string.Empty;

            // Copy other properties that might be important
            var additionalPropertiesToCopy = new[]
            {
                "ecm:creator", "ecm:createdByName", "ecm:kreiraoId",
                "ecm:depositProcessedDate", "ecm:datumKreiranja", "ecm:archiveDate",
                "ecm:active", "ecm:exported"
            };

            foreach (var prop in additionalPropertiesToCopy)
            {
                var value = GetOldProperty(prop);
                if (value != null && !properties.ContainsKey(prop))
                {
                    properties[prop] = value;
                }
            }

            return properties;
        }
    }
}
