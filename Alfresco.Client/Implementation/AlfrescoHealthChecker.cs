using Alfresco.Abstraction.Interfaces;
using Microsoft.Extensions.Logging;

namespace Alfresco.Client.Implementation
{
    /// <summary>
    /// Checks Alfresco availability using a dedicated HttpClient that has NO Polly policies.
    /// This allows health checks to bypass an open Circuit Breaker and reach the server directly.
    /// </summary>
    public class AlfrescoHealthChecker : IAlfrescoHealthChecker
    {
        // AlfrescoCurrentUserClient has no Polly — direct HTTP calls only
        private const string HealthClientName = "AlfrescoCurrentUserClient";
        private const string HealthEndpoint = "/alfresco/api/discovery";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<AlfrescoHealthChecker> _logger;

        public AlfrescoHealthChecker(IHttpClientFactory httpClientFactory, ILogger<AlfrescoHealthChecker> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
        {
            try
            {
                using var client = _httpClientFactory.CreateClient(HealthClientName);
                using var response = await client.GetAsync(HealthEndpoint, ct).ConfigureAwait(false);
                return response.IsSuccessStatusCode;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Alfresco health check failed: {Message}", ex.Message);
                return false;
            }
        }

        public async Task WaitUntilAvailableAsync(
            TimeSpan pollInterval,
            int maxAttempts,
            Action<int, int>? onAttempt,
            CancellationToken ct)
        {
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                ct.ThrowIfCancellationRequested();

                if (await IsAvailableAsync(ct).ConfigureAwait(false))
                {
                    _logger.LogInformation("Alfresco je dostupan (pokusaj {Attempt}/{Max}).", attempt, maxAttempts);
                    return;
                }

                onAttempt?.Invoke(attempt, maxAttempts);

                if (attempt < maxAttempts)
                    await Task.Delay(pollInterval, ct).ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                $"Alfresco nije dostupan ni posle {maxAttempts} pokusaja (interval: {pollInterval.TotalSeconds}s).");
        }
    }
}
