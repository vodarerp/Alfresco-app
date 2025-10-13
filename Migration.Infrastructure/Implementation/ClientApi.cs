using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Implementation of IClientApi for integrating with external Client API.
    /// Retrieves client data used for enriching folder and document metadata during migration.
    ///
    /// NOTE: This implementation is ready but commented out in service registrations
    /// until actual Client API endpoints are available.
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
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
            }

            try
            {
                _logger.LogDebug("Fetching client data for CoreId: {CoreId}", coreId);

                // TODO: Replace with actual Client API endpoint when available
                // Example endpoint: GET /api/clients/{coreId}
                var endpoint = $"{_options.GetClientDataEndpoint}/{coreId}";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var clientData = await response.Content.ReadFromJsonAsync<ClientData>(cancellationToken: ct)
                    .ConfigureAwait(false);

                if (clientData == null)
                {
                    throw new InvalidOperationException($"Client API returned null data for CoreId: {coreId}");
                }

                _logger.LogInformation(
                    "Successfully retrieved client data for CoreId: {CoreId}, ClientName: {ClientName}, ClientType: {ClientType}",
                    coreId, clientData.ClientName, clientData.ClientType);

                return clientData;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while fetching client data for CoreId: {CoreId}", coreId);
                throw new InvalidOperationException($"Failed to retrieve client data for CoreId: {coreId}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while fetching client data for CoreId: {CoreId}", coreId);
                throw;
            }
        }

        public async Task<List<string>> GetActiveAccountsAsync(string coreId, DateTime asOfDate, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
            }

            try
            {
                _logger.LogDebug(
                    "Fetching active accounts for CoreId: {CoreId} as of date: {AsOfDate}",
                    coreId, asOfDate);

                // TODO: Replace with actual Client API endpoint when available
                // Example endpoint: GET /api/clients/{coreId}/accounts?asOfDate={date}&status=active
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
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while fetching active accounts for CoreId: {CoreId}", coreId);
                throw new InvalidOperationException($"Failed to retrieve active accounts for CoreId: {coreId}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while fetching active accounts for CoreId: {CoreId}", coreId);
                throw;
            }
        }

        public async Task<bool> ValidateClientExistsAsync(string coreId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
            }

            try
            {
                _logger.LogDebug("Validating client exists for CoreId: {CoreId}", coreId);

                // TODO: Replace with actual Client API endpoint when available
                // Example endpoint: HEAD /api/clients/{coreId} or GET /api/clients/{coreId}/exists
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
                    "HTTP request failed while validating client for CoreId: {CoreId}", coreId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while validating client for CoreId: {CoreId}", coreId);
                throw;
            }
        }
    }

    /// <summary>
    /// Configuration options for Client API integration.
    /// Add these settings to appsettings.json when Client API becomes available.
    /// </summary>
    public class ClientApiOptions
    {
        public const string SectionName = "ClientApi";

        /// <summary>
        /// Base URL for Client API (e.g., "https://client-api.example.com")
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Endpoint for getting client data (default: "/api/clients")
        /// </summary>
        public string GetClientDataEndpoint { get; set; } = "/api/clients";

        /// <summary>
        /// Endpoint for getting active accounts (default: "/api/clients")
        /// </summary>
        public string GetActiveAccountsEndpoint { get; set; } = "/api/clients";

        /// <summary>
        /// Endpoint for validating client exists (default: "/api/clients")
        /// </summary>
        public string ValidateClientEndpoint { get; set; } = "/api/clients";

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
