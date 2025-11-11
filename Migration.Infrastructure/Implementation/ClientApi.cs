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
        private readonly ILogger<ClientApi> _logger;
        private readonly ClientApiOptions _options;

        public ClientApi(
            HttpClient httpClient,
            IOptions<ClientApiOptions> options,
            ILogger<ClientApi> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<ClientData> GetClientDataAsync(string coreId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                _logger.LogWarning("GetClientDataAsync called with null or empty CoreId, returning empty ClientData");
                return CreateEmptyClientData(coreId ?? string.Empty);
            }

            try
            {
                _logger.LogDebug("Fetching client data for CoreId: {CoreId}", coreId);

                // Use GetClientDetailExtended endpoint which has most comprehensive data
                var endpoint = $"{_options.GetClientDataEndpoint}/{coreId}";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var mockClientData = await response.Content.ReadFromJsonAsync<ClientDetailExtendedDto>(cancellationToken: ct)
                    .ConfigureAwait(false);

                if (mockClientData == null)
                {
                    _logger.LogWarning("Client API returned null data for CoreId: {CoreId}, returning empty ClientData", coreId);
                    return CreateEmptyClientData(coreId);
                }

                // Map mock API response to ClientData model
                var clientData = new ClientData
                {
                    CoreId = mockClientData.CoreId,
                    MbrJmbg = mockClientData.IdentityNumber,
                    ClientName = $"{mockClientData.FirstName} {mockClientData.LastName}",
                    ClientType = DetermineClientType(mockClientData.ClientType),
                    ClientSubtype = mockClientData.ClientType ?? string.Empty, // Use mock ClientType as subtype
                    Residency = DetermineResidency(mockClientData.Nationality),
                    Segment = DetermineSegment(mockClientData.ClientType),
                    // Optional fields - set to null or empty if not available
                    Staff = null,
                    OpuUser = null,
                    OpuRealization = null,
                    Barclex = null,
                    Collaborator = null,
                    BarCLEXName = mockClientData.BarCLEXName,
                    BarCLEXOpu = mockClientData.BarCLEXOpu,
                    BarCLEXGroupName = mockClientData.BarCLEXGroupName,
                    BarCLEXGroupCode = mockClientData.BarCLEXGroupCode,
                    BarCLEXCode = mockClientData.BarCLEXCode
                };

                _logger.LogInformation(
                    "Successfully retrieved client data for CoreId: {CoreId}, ClientName: {ClientName}, ClientType: {ClientType}",
                    coreId, clientData.ClientName, clientData.ClientType);

                return clientData;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while fetching client data for CoreId: {CoreId}. " +
                    "Error: {ErrorMessage}. Returning empty ClientData.",
                    coreId, ex.Message);
                return CreateEmptyClientData(coreId);
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex,
                    "Request timeout while fetching client data for CoreId: {CoreId}. " +
                    "Error: {ErrorMessage}. Returning empty ClientData.",
                    coreId, ex.Message);
                return CreateEmptyClientData(coreId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while fetching client data for CoreId: {CoreId}. " +
                    "Error: {ErrorType} - {ErrorMessage}. Returning empty ClientData.",
                    coreId, ex.GetType().Name, ex.Message);
                return CreateEmptyClientData(coreId);
            }
        }

        public async Task<List<string>> GetActiveAccountsAsync(string coreId, DateTime asOfDate, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                _logger.LogWarning("GetActiveAccountsAsync called with null or empty CoreId, returning empty list");
                return new List<string>();
            }

            try
            {
                _logger.LogDebug(
                    "Fetching active accounts for CoreId: {CoreId} as of date: {AsOfDate}",
                    coreId, asOfDate);

                // Mock API doesn't have active accounts endpoint, so we'll generate mock data
                // In production, this would call a real endpoint
                _logger.LogWarning(
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
                    _logger.LogWarning(
                        "Client API returned null accounts list for CoreId: {CoreId}, returning empty list",
                        coreId);
                    return new List<string>();
                }

                _logger.LogInformation(
                    "Successfully retrieved {Count} active accounts for CoreId: {CoreId} as of {AsOfDate}",
                    accounts.Count, coreId, asOfDate);

                return accounts;
                */
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while fetching active accounts for CoreId: {CoreId}. " +
                    "Error: {ErrorMessage}. Returning empty list.",
                    coreId, ex.Message);
                return new List<string>();
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex,
                    "Request timeout while fetching active accounts for CoreId: {CoreId}. " +
                    "Error: {ErrorMessage}. Returning empty list.",
                    coreId, ex.Message);
                return new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while fetching active accounts for CoreId: {CoreId}. " +
                    "Error: {ErrorType} - {ErrorMessage}. Returning empty list.",
                    coreId, ex.GetType().Name, ex.Message);
                return new List<string>();
            }
        }

        public async Task<bool> ValidateClientExistsAsync(string coreId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                _logger.LogWarning("ValidateClientExistsAsync called with null or empty CoreId, returning false");
                return false;
            }

            try
            {
                _logger.LogDebug("Validating client exists for CoreId: {CoreId}", coreId);

                // Use GetClientDetail endpoint to check if client exists
                var endpoint = $"{_options.ValidateClientEndpoint}/{coreId}";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);

                var exists = response.IsSuccessStatusCode;

                _logger.LogInformation(
                    "Client validation for CoreId: {CoreId} result: {Exists}",
                    coreId, exists);

                return exists;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while validating client for CoreId: {CoreId}. " +
                    "Error: {ErrorMessage}. Assuming client does not exist.",
                    coreId, ex.Message);
                return false;
            }
            catch (TaskCanceledException ex)
            {
                _logger.LogError(ex,
                    "Request timeout while validating client for CoreId: {CoreId}. " +
                    "Error: {ErrorMessage}. Assuming client does not exist.",
                    coreId, ex.Message);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while validating client for CoreId: {CoreId}. " +
                    "Error: {ErrorType} - {ErrorMessage}. Assuming client does not exist.",
                    coreId, ex.GetType().Name, ex.Message);
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
            public string CoreId { get; set; } = string.Empty;
            public string IdentityNumber { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public string? MiddleName { get; set; }
            public string Email { get; set; } = string.Empty;
            public string PhoneNumber { get; set; } = string.Empty;
            public string? MobileNumber { get; set; }
            public DateTime DateOfBirth { get; set; }
            public string? Gender { get; set; }
            public string? Nationality { get; set; }
            public string Address { get; set; } = string.Empty;
            public string City { get; set; } = string.Empty;
            public string Country { get; set; } = string.Empty;
            public string PostalCode { get; set; } = string.Empty;
            public string? Region { get; set; }
            public string ClientStatus { get; set; } = string.Empty;
            public string ClientType { get; set; } = string.Empty;
            public DateTime RegistrationDate { get; set; }
            public DateTime LastModifiedDate { get; set; }
            public string? TaxNumber { get; set; }
            public string? BankAccount { get; set; }
            public string? Notes { get; set; }
            public bool IsActive { get; set; }
            public decimal CreditLimit { get; set; }
            public string PreferredLanguage { get; set; } = "sr-RS";
            public string? BarCLEXName { get; set; }
            public string? BarCLEXOpu { get; set; }
            public string? BarCLEXGroupName { get; set; }
            public string? BarCLEXGroupCode { get; set; }
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
