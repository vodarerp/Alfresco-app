using Alfresco.Contracts.Oracle.Models;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
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

        public ClientEnrichmentService(
            IClientApi clientApi,
            ILogger<ClientEnrichmentService> logger)
        {
            _clientApi = clientApi ?? throw new ArgumentNullException(nameof(clientApi));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
                _logger.LogDebug(
                    "Enriching folder {FolderId} ({FolderName}) with client data for CoreId: {CoreId}",
                    folder.Id, folder.Name, folder.CoreId);

                var clientData = await _clientApi.GetClientDataAsync(folder.CoreId, ct)
                    .ConfigureAwait(false);

                // Populate all client-related fields from ClientAPI response
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
                    "Failed to enrich folder {FolderId} ({FolderName}) with client data for CoreId: {CoreId}",
                    folder.Id, folder.Name, folder.CoreId);
                throw new InvalidOperationException(
                    $"Failed to enrich folder {folder.Id} with client data for CoreId: {folder.CoreId}", ex);
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
                    "Failed to enrich document {DocId} with active accounts for CoreId: {CoreId}",
                    document.Id, document.CoreId);
                throw new InvalidOperationException(
                    $"Failed to enrich document {document.Id} with accounts for CoreId: {document.CoreId}", ex);
            }
        }

        public async Task<bool> ValidateClientAsync(string coreId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
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
                    "Failed to validate client for CoreId: {CoreId}",
                    coreId);
                throw;
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
    }
}
