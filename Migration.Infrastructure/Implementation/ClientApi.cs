using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Implementation of IClientApi for integrating with external Client API.
    /// Retrieves client data used for enriching folder and document metadata during migration.
    /// </summary>
    public class ClientApi : IClientApi
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _fileLogger;
        private readonly ILogger _dbLogger;
        private readonly ClientApiOptions _options;
        private readonly IMemoryCache _cache;

        // Cache configuration
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
        private const string CacheKeyPrefix = "ClientApi_";

        public ClientApi(
            HttpClient httpClient,
            IOptions<ClientApiOptions> options,
            ILoggerFactory logger,
            IMemoryCache cache)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _fileLogger = logger.CreateLogger("FileLogger");
            _dbLogger = logger.CreateLogger("DbLogger");
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        }

        public async Task<ClientData> GetClientDataAsync(string coreId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                _fileLogger.LogWarning("GetClientDataAsync called with null or empty CoreId, returning empty ClientData");
                return CreateEmptyClientData(coreId ?? string.Empty);
            }

            // Check cache first
            var cacheKey = $"{CacheKeyPrefix}ClientData_{coreId}";
            if (_cache.TryGetValue(cacheKey, out ClientData? cachedData) && cachedData != null)
            {
                _fileLogger.LogDebug("Cache HIT for client data CoreId: {CoreId}", coreId);
                return cachedData;
            }

            _fileLogger.LogDebug("Cache MISS for client data CoreId: {CoreId}, fetching from API", coreId);

            try
            {
                _fileLogger.LogDebug("Fetching client data for CoreId: {CoreId}", coreId);

                // Use GetClientDetailExtended endpoint which has most comprehensive data
                var endpoint = $"{_options.GetClientDataEndpoint}/{coreId}";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var mockClientData = await response.Content.ReadFromJsonAsync<ClientDetailExtendedDto>(cancellationToken: ct)
                    .ConfigureAwait(false);

                if (mockClientData == null)
                {
                    _fileLogger.LogWarning("Client API returned null data for CoreId: {CoreId}, returning empty ClientData", coreId);
                    return CreateEmptyClientData(coreId);
                }

                // Map mock API response to ClientData model
                var clientData = new ClientData
                {
                    CoreId = mockClientData.CoreId,
                    MbrJmbg = mockClientData.MbrJmbg,
                    ClientName = mockClientData.ClientName,
                    ClientType = mockClientData.ClientType,//DetermineClientType(mockClientData.ClientType),
                    ClientSubtype = mockClientData.ClientSubtype ?? string.Empty, // Use mock ClientType as subtype
                    Residency = mockClientData.Residency,//DetermineResidency(mockClientData.Residency),
                    Segment = mockClientData.Segment,
                    // Optional fields - set to null or empty if not available
                    Staff = mockClientData.Staff,
                    OpuUser = mockClientData.OpuUser,
                    OpuRealization = mockClientData.OpuRealization,
                    Barclex = mockClientData.Barclex,
                    Collaborator = mockClientData.Collaborator,
                    BarCLEXName = mockClientData.BarCLEXName,
                    BarCLEXOpu = mockClientData.BarCLEXOpu,
                    BarCLEXGroupName = mockClientData.BarCLEXGroupName,
                    BarCLEXGroupCode = mockClientData.BarCLEXGroupCode,
                    BarCLEXCode = mockClientData.BarCLEXCode
                };

                _fileLogger.LogInformation(
                    "Successfully retrieved client data for CoreId: {CoreId}, ClientName: {ClientName}, ClientType: {ClientType}",
                    coreId, clientData.ClientName, clientData.ClientType);

                // Cache the result
                _cache.Set(cacheKey, clientData, CacheDuration);
                _fileLogger.LogDebug("Cached client data for CoreId: {CoreId} with TTL: {Duration}", coreId, CacheDuration);

                return clientData;
            }
            catch (HttpRequestException ex)
            {
                _fileLogger.LogError("HTTP request failed while fetching client data for CoreId: {CoreId}. Returning empty ClientData.",
                    coreId);
                _dbLogger.LogError(ex,
                    "HTTP request failed while fetching client data for CoreId: {CoreId}",
                    coreId);
                return CreateEmptyClientData(coreId);
            }
            catch (TaskCanceledException ex)
            {
                _fileLogger.LogError("Request timeout while fetching client data for CoreId: {CoreId}. Returning empty ClientData.",
                    coreId);
                _dbLogger.LogError(ex,
                    "Request timeout while fetching client data for CoreId: {CoreId}",
                    coreId);
                return CreateEmptyClientData(coreId);
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Unexpected error while fetching client data for CoreId: {CoreId}. Returning empty ClientData.",
                    coreId);
                _dbLogger.LogError(ex,
                    "Unexpected error while fetching client data for CoreId: {CoreId}",
                    coreId);
                return CreateEmptyClientData(coreId);
            }
        }

        public async Task<List<string>> GetActiveAccountsAsync(string coreId, DateTime asOfDate, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                _fileLogger.LogWarning("GetActiveAccountsAsync called with null or empty CoreId, returning empty list");
                return new List<string>();
            }

            try
            {
                _fileLogger.LogDebug(
                    "Fetching active accounts for CoreId: {CoreId} as of date: {AsOfDate}",
                    coreId, asOfDate);

                // Mock API doesn't have active accounts endpoint, so we'll generate mock data
                // In production, this would call a real endpoint
                _fileLogger.LogWarning(
                    "GetActiveAccountsAsync called but mock API doesn't support this. Returning empty list. CoreId: {CoreId}",
                    coreId);

                return new List<string>();

                // TODO: When real API is available, uncomment this:
                /*
                var endpoint = $"{_options.GetActiveAccountsEndpoint}/{coreId}/accounts?asOfDate={asOfDate:yyyy-MM-dd}&status=active";
                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var accounts = await response.Content.ReadFromJsonAsync<List<string>>(cancellationToken: ct)
                    .ConfigureAwait(false);

                if (accounts == null)
                {
                    _fileLogger.LogWarning(
                        "Client API returned null accounts list for CoreId: {CoreId}, returning empty list",
                        coreId);
                    return new List<string>();
                }

                _fileLogger.LogInformation(
                    "Successfully retrieved {Count} active accounts for CoreId: {CoreId} as of {AsOfDate}",
                    accounts.Count, coreId, asOfDate);

                return accounts;
                */
            }
            catch (HttpRequestException ex)
            {
                _fileLogger.LogError(ex,
                    "HTTP request failed while fetching active accounts for CoreId: {CoreId}. " +
                    "Error: {ErrorMessage}. Returning empty list.",
                    coreId, ex.Message);
                return new List<string>();
            }
            catch (TaskCanceledException ex)
            {
                _fileLogger.LogError(ex,
                    "Request timeout while fetching active accounts for CoreId: {CoreId}. " +
                    "Error: {ErrorMessage}. Returning empty list.",
                    coreId, ex.Message);
                return new List<string>();
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Unexpected error while fetching active accounts for CoreId: {CoreId}. Returning empty list.",
                    coreId);
                _dbLogger.LogError(ex,
                    "Unexpected error while fetching active accounts for CoreId: {CoreId}",
                    coreId);
                return new List<string>();
            }
        }

        public async Task<bool> ValidateClientExistsAsync(string coreId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                _fileLogger.LogWarning("ValidateClientExistsAsync called with null or empty CoreId, returning false");
                return false;
            }

            // Check cache first
            var cacheKey = $"{CacheKeyPrefix}ClientExists_{coreId}";
            if (_cache.TryGetValue(cacheKey, out bool cachedExists))
            {
                _fileLogger.LogDebug("Cache HIT for client validation CoreId: {CoreId}, exists: {Exists}", coreId, cachedExists);
                return cachedExists;
            }

            _fileLogger.LogDebug("Cache MISS for client validation CoreId: {CoreId}, checking API", coreId);

            try
            {
                _fileLogger.LogDebug("Validating client exists for CoreId: {CoreId}", coreId);

                // Use GetClientDetail endpoint to check if client exists
                var endpoint = $"{_options.ValidateClientEndpoint}/{coreId}";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);

                var exists = response.IsSuccessStatusCode;

                _fileLogger.LogInformation(
                    "Client validation for CoreId: {CoreId} result: {Exists}",
                    coreId, exists);

                // Cache the result
                _cache.Set(cacheKey, exists, CacheDuration);
                _fileLogger.LogDebug("Cached client validation for CoreId: {CoreId} with TTL: {Duration}", coreId, CacheDuration);

                return exists;
            }
            catch (HttpRequestException ex)
            {
                _fileLogger.LogError("HTTP request failed while validating client for CoreId: {CoreId}. Assuming client does not exist.",
                    coreId);
                _dbLogger.LogError(ex,
                    "HTTP request failed while validating client for CoreId: {CoreId}",
                    coreId);
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _fileLogger.LogError("Request timeout while validating client for CoreId: {CoreId}. Assuming client does not exist.",
                    coreId);
                _dbLogger.LogError(ex,
                    "Request timeout while validating client for CoreId: {CoreId}",
                    coreId);
                return false;
            }
            catch (Exception ex)
            {
                _fileLogger.LogError("Unexpected error while validating client for CoreId: {CoreId}. Assuming client does not exist.",
                    coreId);
                _dbLogger.LogError(ex,
                    "Unexpected error while validating client for CoreId: {CoreId}",
                    coreId);
                return false;
            }
        }

        #region Helper Methods

        /// <summary>
        /// Creates an empty ClientData object when ClientAPI fails or returns no data
        /// </summary>
        private ClientData CreateEmptyClientData(string coreId)
        {
            return new ClientData
            {
                CoreId = coreId,
                MbrJmbg = string.Empty,
                ClientName = string.Empty,
                ClientType = string.Empty,
                ClientSubtype = string.Empty,
                Residency = string.Empty,
                Segment = string.Empty,
                Staff = null,
                OpuUser = null,
                OpuRealization = null,
                Barclex = null,
                Collaborator = null,
                BarCLEXName = null,
                BarCLEXOpu = null,
                BarCLEXGroupName = null,
                BarCLEXGroupCode = null,
                BarCLEXCode = null
            };
        }

        /// <summary>
        /// Maps mock API ClientType to banking ClientType (FL = Fizičko Lice, PL = Pravno Lice)
        /// </summary>
        private string DetermineClientType(string? mockClientType)
        {
            if (string.IsNullOrEmpty(mockClientType))
                return "FL"; // Default to natural person

            // Map mock types to FL/PL
            // Premium, VIP, Standard, Regular are typically natural persons (FL)
            // Corporate, Business types would be legal entities (PL)
            return mockClientType.ToUpperInvariant() switch
            {
                "PREMIUM" => "FL",
                "VIP" => "FL",
                "STANDARD" => "FL",
                "REGULAR" => "FL",
                "CORPORATE" => "PL",
                "BUSINESS" => "PL",
                _ => "FL" // Default to natural person
            };
        }

        /// <summary>
        /// Determines residency status based on nationality
        /// </summary>
        private string DetermineResidency(string? nationality)
        {
            if (string.IsNullOrEmpty(nationality))
                return "Resident";

            // Serbian nationals are typically residents
            return nationality.Contains("Serbian", StringComparison.OrdinalIgnoreCase)
                ? "Resident"
                : "Non-resident";
        }

        /// <summary>
        /// Maps mock ClientType to segment classification
        /// </summary>
        private string DetermineSegment(string? mockClientType)
        {
            if (string.IsNullOrEmpty(mockClientType))
                return "Standard";

            return mockClientType.ToUpperInvariant() switch
            {
                "PREMIUM" => "Premium",
                "VIP" => "VIP",
                "STANDARD" => "Standard",
                "REGULAR" => "Retail",
                "CORPORATE" => "Corporate",
                "BUSINESS" => "SME",
                _ => "Standard"
            };
        }

        #endregion

        #region DTOs for Mock API

        /// <summary>
        /// DTO matching mock API GetClientDetailExtended response
        /// </summary>
        private class ClientDetailExtendedDto
        {
           
                /// <summary>
                /// Client's Core ID (unique identifier in the core banking system)
                /// </summary>
                public string CoreId { get; set; } = string.Empty;

                /// <summary>
                /// MBR (for legal entities) or JMBG (for natural persons)
                /// </summary>
                public string MbrJmbg { get; set; } = string.Empty;

                /// <summary>
                /// Full client name
                /// </summary>
                public string ClientName { get; set; } = string.Empty;

                /// <summary>
                /// Client type: "FL" (Fizičko Lice - Natural Person) or "PL" (Pravno Lice - Legal Entity)
                /// </summary>
                public string ClientType { get; set; } = string.Empty;

                /// <summary>
                /// Client subtype for additional classification
                /// </summary>
                public string ClientSubtype { get; set; } = string.Empty;

                /// <summary>
                /// Residency status (Resident/Non-resident)
                /// </summary>
                public string Residency { get; set; } = string.Empty;

                /// <summary>
                /// Client segment classification
                /// </summary>
                public string Segment { get; set; } = string.Empty;

                /// <summary>
                /// Staff indicator (if client is a bank employee)
                /// </summary>
                public string? Staff { get; set; }

                /// <summary>
                /// OPU (Organizational Unit) of the user
                /// </summary>
                public string? OpuUser { get; set; }

                /// <summary>
                /// OPU/ID of realization
                /// </summary>
                public string? OpuRealization { get; set; }

                /// <summary>
                /// Barclex identifier
                /// </summary>
                public string? Barclex { get; set; }

                /// <summary>
                /// Collaborator/Partner information
                /// </summary>
                public string? Collaborator { get; set; }

                /// <summary>
                /// BarCLEX Name
                /// </summary>
                public string? BarCLEXName { get; set; }

                /// <summary>
                /// BarCLEX OPU
                /// </summary>
                public string? BarCLEXOpu { get; set; }

                /// <summary>
                /// BarCLEX Group Name
                /// </summary>
                public string? BarCLEXGroupName { get; set; }

                /// <summary>
                /// BarCLEX Group Code
                /// </summary>
                public string? BarCLEXGroupCode { get; set; }

                /// <summary>
                /// BarCLEX Code
                /// </summary>
                public string? BarCLEXCode { get; set; }
            
        }

        #endregion
    }

    /// <summary>
    /// Configuration options for Client API integration.
    /// Add these settings to appsettings.json when Client API becomes available.
    /// </summary>
    public class ClientApiOptions
    {
        public const string SectionName = "ClientApi";

        /// <summary>
        /// Base URL for Client API (e.g., "https://localhost:5101")
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Endpoint for getting client data (default: "/api/Client/GetClientDetailExtended")
        /// </summary>
        public string GetClientDataEndpoint { get; set; } = "/api/Client/GetClientDetailExtended";

        /// <summary>
        /// Endpoint for getting active accounts (default: "/api/Client")
        /// </summary>
        public string GetActiveAccountsEndpoint { get; set; } = "/api/Client";

        /// <summary>
        /// Endpoint for validating client exists (default: "/api/Client/GetClientDetail")
        /// </summary>
        public string ValidateClientEndpoint { get; set; } = "/api/Client/GetClientDetail";

        /// <summary>
        /// Request timeout in seconds (default: 30)
        /// </summary>
        public int TimeoutSeconds { get; set; } = 30;

        /// <summary>
        /// API Key for authentication (if required)
        /// </summary>
        public string? ApiKey { get; set; }

        /// <summary>
        /// Retry count for failed requests (default: 3)
        /// </summary>
        public int RetryCount { get; set; } = 3;
    }
}
