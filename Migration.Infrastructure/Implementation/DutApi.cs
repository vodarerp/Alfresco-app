using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using Migration.Abstraction.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Migration.Infrastructure.Implementation
{
    /// <summary>
    /// Implementation of IDutApi for integrating with DUT (Deposit Understanding/Processing) API.
    /// Retrieves deposit offer data from OfferBO table for migration validation and enrichment.
    ///
    /// NOTE: This implementation is ready but commented out in service registrations
    /// until actual DUT API endpoints are available.
    /// </summary>
    public class DutApi : IDutApi
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DutApi> _logger;
        private readonly DutApiOptions _options;

        public DutApi(
            HttpClient httpClient,
            IOptions<DutApiOptions> options,
            ILogger<DutApi> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        public async Task<List<DutOffer>> GetBookedOffersAsync(string coreId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
            }

            try
            {
                _logger.LogDebug("Fetching booked offers for CoreId: {CoreId}", coreId);

                // TODO: Replace with actual DUT API endpoint when available
                // Example endpoint: GET /api/offers?coreId={coreId}&status=Booked
                var endpoint = $"{_options.GetOffersEndpoint}?coreId={coreId}&status=Booked";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var offers = await response.Content.ReadFromJsonAsync<List<DutOffer>>(cancellationToken: ct)
                    .ConfigureAwait(false);

                if (offers == null)
                {
                    _logger.LogWarning("DUT API returned null offers list for CoreId: {CoreId}, returning empty list", coreId);
                    return new List<DutOffer>();
                }

                // Filter to ensure only Booked status (per documentation requirement)
                var bookedOffers = offers.Where(o => o.Status.Equals("Booked", StringComparison.OrdinalIgnoreCase)).ToList();

                _logger.LogInformation(
                    "Successfully retrieved {Count} booked offers for CoreId: {CoreId}",
                    bookedOffers.Count, coreId);

                return bookedOffers;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while fetching booked offers for CoreId: {CoreId}", coreId);
                throw new InvalidOperationException($"Failed to retrieve booked offers for CoreId: {coreId}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while fetching booked offers for CoreId: {CoreId}", coreId);
                throw;
            }
        }

        public async Task<DutOfferDetails> GetOfferDetailsAsync(string offerId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(offerId))
            {
                throw new ArgumentException("OfferId cannot be null or empty", nameof(offerId));
            }

            try
            {
                _logger.LogDebug("Fetching offer details for OfferId: {OfferId}", offerId);

                // TODO: Replace with actual DUT API endpoint when available
                // Example endpoint: GET /api/offers/{offerId}
                var endpoint = $"{_options.GetOffersEndpoint}/{offerId}";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var offerDetails = await response.Content.ReadFromJsonAsync<DutOfferDetails>(cancellationToken: ct)
                    .ConfigureAwait(false);

                if (offerDetails == null)
                {
                    throw new InvalidOperationException($"DUT API returned null offer details for OfferId: {offerId}");
                }

                _logger.LogInformation(
                    "Successfully retrieved offer details for OfferId: {OfferId}, ContractNumber: {ContractNumber}",
                    offerId, offerDetails.ContractNumber);

                return offerDetails;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while fetching offer details for OfferId: {OfferId}", offerId);
                throw new InvalidOperationException($"Failed to retrieve offer details for OfferId: {offerId}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while fetching offer details for OfferId: {OfferId}", offerId);
                throw;
            }
        }

        public async Task<List<DutDocument>> GetOfferDocumentsAsync(string offerId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(offerId))
            {
                throw new ArgumentException("OfferId cannot be null or empty", nameof(offerId));
            }

            try
            {
                _logger.LogDebug("Fetching documents for OfferId: {OfferId}", offerId);

                // TODO: Replace with actual DUT API endpoint when available
                // Example endpoint: GET /api/offers/{offerId}/documents
                var endpoint = $"{_options.GetOffersEndpoint}/{offerId}/documents";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var documents = await response.Content.ReadFromJsonAsync<List<DutDocument>>(cancellationToken: ct)
                    .ConfigureAwait(false);

                if (documents == null)
                {
                    _logger.LogWarning(
                        "DUT API returned null documents list for OfferId: {OfferId}, returning empty list",
                        offerId);
                    return new List<DutDocument>();
                }

                _logger.LogInformation(
                    "Successfully retrieved {Count} documents for OfferId: {OfferId}",
                    documents.Count, offerId);

                return documents;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while fetching documents for OfferId: {OfferId}", offerId);
                throw new InvalidOperationException($"Failed to retrieve documents for OfferId: {offerId}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while fetching documents for OfferId: {OfferId}", offerId);
                throw;
            }
        }

        public async Task<List<DutOffer>> FindOffersByDateAsync(string coreId, DateTime depositDate, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(coreId))
            {
                throw new ArgumentException("CoreId cannot be null or empty", nameof(coreId));
            }

            try
            {
                _logger.LogDebug(
                    "Finding offers for CoreId: {CoreId} on date: {DepositDate}",
                    coreId, depositDate);

                // TODO: Replace with actual DUT API endpoint when available
                // Example endpoint: GET /api/offers?coreId={coreId}&depositDate={date}&status=Booked
                var endpoint = $"{_options.GetOffersEndpoint}?coreId={coreId}&depositDate={depositDate:yyyy-MM-dd}&status=Booked";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                var offers = await response.Content.ReadFromJsonAsync<List<DutOffer>>(cancellationToken: ct)
                    .ConfigureAwait(false);

                if (offers == null || offers.Count == 0)
                {
                    _logger.LogWarning(
                        "No offers found for CoreId: {CoreId} on date: {DepositDate}",
                        coreId, depositDate);
                    return new List<DutOffer>();
                }

                // Per documentation line 199-202: If multiple offers exist for the same date,
                // manual matching is required
                if (offers.Count > 1)
                {
                    _logger.LogWarning(
                        "Multiple offers ({Count}) found for CoreId: {CoreId} on date: {DepositDate} - manual matching required. " +
                        "Contract numbers: {ContractNumbers}",
                        offers.Count, coreId, depositDate, string.Join(", ", offers.Select(o => o.ContractNumber)));
                }
                else
                {
                    _logger.LogInformation(
                        "Single offer found for CoreId: {CoreId} on date: {DepositDate}, ContractNumber: {ContractNumber}",
                        coreId, depositDate, offers[0].ContractNumber);
                }

                return offers;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while finding offers for CoreId: {CoreId} on date: {DepositDate}",
                    coreId, depositDate);
                throw new InvalidOperationException(
                    $"Failed to find offers for CoreId: {coreId} on date: {depositDate:yyyy-MM-dd}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while finding offers for CoreId: {CoreId} on date: {DepositDate}",
                    coreId, depositDate);
                throw;
            }
        }

        public async Task<bool> IsOfferBookedAsync(string offerId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(offerId))
            {
                throw new ArgumentException("OfferId cannot be null or empty", nameof(offerId));
            }

            try
            {
                _logger.LogDebug("Validating offer is booked for OfferId: {OfferId}", offerId);

                // TODO: Replace with actual DUT API endpoint when available
                // Example endpoint: GET /api/offers/{offerId}/status or GET /api/offers/{offerId}
                var endpoint = $"{_options.GetOffersEndpoint}/{offerId}";

                var response = await _httpClient.GetAsync(endpoint, ct).ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("Offer {OfferId} not found or API call failed", offerId);
                    return false;
                }

                var offer = await response.Content.ReadFromJsonAsync<DutOffer>(cancellationToken: ct)
                    .ConfigureAwait(false);

                if (offer == null)
                {
                    _logger.LogWarning("Offer {OfferId} returned null from API", offerId);
                    return false;
                }

                var isBooked = offer.Status.Equals("Booked", StringComparison.OrdinalIgnoreCase);

                _logger.LogInformation(
                    "Offer {OfferId} validation result: IsBooked={IsBooked}, Status={Status}",
                    offerId, isBooked, offer.Status);

                return isBooked;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex,
                    "HTTP request failed while validating offer for OfferId: {OfferId}", offerId);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Unexpected error while validating offer for OfferId: {OfferId}", offerId);
                throw;
            }
        }
    }

    /// <summary>
    /// Configuration options for DUT API integration.
    /// Add these settings to appsettings.json when DUT API becomes available.
    /// </summary>
    public class DutApiOptions
    {
        public const string SectionName = "DutApi";

        /// <summary>
        /// Base URL for DUT API (e.g., "https://dut-api.example.com")
        /// </summary>
        public string BaseUrl { get; set; } = string.Empty;

        /// <summary>
        /// Endpoint for getting offers (default: "/api/offers")
        /// </summary>
        public string GetOffersEndpoint { get; set; } = "/api/offers";

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

        /// <summary>
        /// Enable caching of offer data (default: true)
        /// </summary>
        public bool EnableCaching { get; set; } = true;

        /// <summary>
        /// Cache duration in minutes (default: 60)
        /// </summary>
        public int CacheDurationMinutes { get; set; } = 60;
    }
}
