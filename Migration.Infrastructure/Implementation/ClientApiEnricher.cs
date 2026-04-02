using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Mapper;
using Microsoft.Extensions.Logging;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    public class ClientApiEnricher : IClientApiEnricher
    {
        private readonly IClientApi _clientApi;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;

        public ClientApiEnricher(IClientApi clientApi, ILoggerFactory loggerFactory)
        {
            _clientApi = clientApi ?? throw new ArgumentNullException(nameof(clientApi));
            _fileLogger = loggerFactory.CreateLogger("FileLogger");
            _dbLogger = loggerFactory.CreateLogger("DbLogger");
        }

        public async Task<ClientData> EnrichFromFolderNameAsync(string folderName, CancellationToken ct = default)
        {
            _fileLogger.LogInformation("ClientApiEnricher: Starting enrichment for folder '{FolderName}'", folderName);

            var toRet = new ClientData();

            try
            {
                var coreId = DossierIdFormatter.ExtractCoreId(folderName);

                if (string.IsNullOrWhiteSpace(coreId))
                {
                    _fileLogger.LogWarning(
                        "ClientApiEnricher: Could not extract CoreId from folder name '{FolderName}', skipping ClientAPI call",
                        folderName);
                    return toRet;
                }

                _fileLogger.LogInformation(
                    "ClientApiEnricher: Extracted CoreId '{CoreId}' from '{FolderName}', calling GetClientDataAsync...",
                    coreId, folderName);

                toRet = await _clientApi.GetClientDataAsync(coreId, ct).ConfigureAwait(false);

                var clientDetail = await _clientApi.GetClientDetailAsync(coreId, ct).ConfigureAwait(false);

                if (clientDetail != null)
                {
                    if (toRet == null) toRet = new ClientData();

                    toRet.Residency = DetermineResidency(clientDetail.ClientGeneral.ResidentIndicator ?? "");
                    toRet.ClientName = clientDetail.Name ?? string.Empty;
                    toRet.MbrJmbg = clientDetail.ClientGeneral.ClientID ?? string.Empty;

                    if (clientDetail.HasError && !toRet.HasError)
                    {
                        toRet.HasError = true;
                        toRet.ErrorMessage = clientDetail.ErrorMessage;
                    }

                    _fileLogger.LogInformation(
                        "ClientApiEnricher: Enriched from ClientDetail - CoreId '{CoreId}', Residency: '{Residency}', ClientName: '{ClientName}'",
                        coreId, toRet.Residency, toRet.ClientName);
                }

                if (toRet != null && toRet.HasError && !string.IsNullOrEmpty(toRet.ErrorMessage))
                {
                    _fileLogger.LogWarning(
                        "ClientApiEnricher: ClientAPI error for folder '{FolderName}', CoreId '{CoreId}': {Error}",
                        folderName, coreId, toRet.ErrorMessage);
                    _dbLogger.LogWarning(
                        "ClientAPI nije vratio podatke za CoreId: {CoreId}, folder: {FolderName}. Error: {Error}",
                        coreId, folderName, toRet.ErrorMessage);
                }
            }
            catch (ClientApiTimeoutException timeoutEx)
            {
                _fileLogger.LogError("ClientApiEnricher: Timeout za folder '{FolderName}': {Message}", folderName, timeoutEx.Message);
                _dbLogger.LogError(timeoutEx, "ClientApiEnricher: Timeout za folder '{FolderName}'", folderName);
                throw;
            }
            catch (ClientApiRetryExhaustedException retryEx)
            {
                _fileLogger.LogError("ClientApiEnricher: Retry exhausted za folder '{FolderName}': {Message}", folderName, retryEx.Message);
                _dbLogger.LogError(retryEx, "ClientApiEnricher: Retry exhausted za folder '{FolderName}'", folderName);
                throw;
            }
            catch (ClientApiException clientEx)
            {
                _fileLogger.LogError("ClientApiEnricher: ClientAPI error za folder '{FolderName}': {Message}", folderName, clientEx.Message);
                _dbLogger.LogError(clientEx, "ClientApiEnricher: ClientAPI error za folder '{FolderName}'", folderName);
                throw;
            }
            catch (Exception ex)
            {
                // Neočekivane greške ne blokiraju tok - logujemo i vraćamo prazan ClientData
                _fileLogger.LogWarning(
                    "ClientApiEnricher: Unexpected error za folder '{FolderName}': {Error}. Nastavljamo sa praznim ClientData.",
                    folderName, ex.Message);
                _dbLogger.LogError(ex, "ClientApiEnricher: Unexpected error za folder '{FolderName}'", folderName);
            }

            return toRet ?? new ClientData();
        }

        public Dictionary<string, object> BuildFolderProperties(ClientData clientData, string folderName)
        {
            _fileLogger.LogInformation("ClientApiEnricher: BuildFolderProperties za folder '{FolderName}'", folderName);

            var properties = new Dictionary<string, object>
            {
                ["ecm:bnkStatus"] = "ACTIVE",
                ["ecm:typeId"] = "dosije",
                ["ecm:bnkSource"] = folderName.StartsWith("DE", StringComparison.OrdinalIgnoreCase) ? "DUT" : "Heimdall"
            };

            properties["ecm:bnkMTBR"] = clientData.MbrJmbg ?? string.Empty;
            properties["ecm:bnkClientName"] = clientData.ClientName ?? string.Empty;
            properties["ecm:bnkResidence"] = clientData.Residency ?? string.Empty;
            properties["ecm:bnkClientType"] = clientData.Segment ?? string.Empty;
            properties["ecm:bnkClientSubtype"] = clientData.ClientSubtype ?? string.Empty;
            properties["ecm:bnkOfficeId"] = clientData.BarCLEXOpu ?? string.Empty;

            bool isStaff = clientData.Staff?.ToLowerInvariant() switch
            {
                "n" => false,
                null => false,
                "false" => false,
                "0" => false,
                _ => true
            };

            var barClex = $"{clientData.BarCLEXGroupCode ?? string.Empty} {clientData.BarCLEXGroupName ?? string.Empty}".Trim();
            var contributor = $"{clientData.BarCLEXCode ?? string.Empty} {clientData.BarCLEXName ?? string.Empty}".Trim();

            properties["ecm:bnkStaff"] = isStaff;
            properties["ecm:bnkBarclex"] = barClex;
            properties["ecm:bnkContributor"] = contributor;

            _fileLogger.LogInformation(
                "ClientApiEnricher: Izgrađeno {Count} properties za folder '{FolderName}'",
                properties.Count, folderName);

            return properties;
        }

        private static string DetermineResidency(string? residencyIndicator)
        {
            return residencyIndicator?.ToUpperInvariant() switch
            {
                string s when s.StartsWith("R") => "REZIDENT",
                string s when s.StartsWith("N") => "NEREZIDENT",
                _ => string.Empty
            };
        }
    }
}
