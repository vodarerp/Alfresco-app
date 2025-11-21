using Alfresco.Contracts.Options;        // ← PollyPolicyOptions
using Microsoft.Extensions.Logging;     // ← ILogger
using Polly;                             // ← Policy, AsyncRetryPolicy
using Polly.CircuitBreaker;              // ← AsyncCircuitBreakerPolicy
using Polly.Extensions.Http;
using Polly.Retry;                       // ← AsyncRetryPolicy
using Polly.Timeout;                     // ← AsyncTimeoutPolicy
using System.Net;
using System.Net.Http;

namespace Alfresco.App.Helpers
{
    public static class PolicyHelpers
    {
        [Obsolete("Use GetCombinedReadPolicy or GetCombinedWritePolicy with PolicyOperationOptions from appsettings.json instead", error: true)]
        public static IAsyncPolicy<HttpResponseMessage> GetRetryPlicy()
        {
            return HttpPolicyExtensions
                 .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        [Obsolete("Use GetCombinedReadPolicy or GetCombinedWritePolicy with PolicyOperationOptions from appsettings.json instead", error: true)]
        public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
        }

        public static AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy(
            int retryCount = 3,
            ILogger? logger = null)
        {
            return Policy
                .HandleResult<HttpResponseMessage>(r =>
                    r.StatusCode == HttpStatusCode.TooManyRequests ||
                    r.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    r.StatusCode == HttpStatusCode.RequestTimeout ||
                    (int)r.StatusCode >= 500)
                .Or<HttpRequestException>()
                // REMOVED: .Or<TaskCanceledException>() - Ne retry-uj timeout greške!
                // Timeout znači da je operacija predugo trajala, retry neće pomoći
                .WaitAndRetryAsync(
                    retryCount: retryCount,
                    sleepDurationProvider: retryAttempt =>
                    {
                        // Exponential backoff: 2s, 4s, 8s
                        //var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));
                        var delay = TimeSpan.FromMilliseconds(500 * retryAttempt);

                        // Jitter to avoid thundering herd (random 0-500ms)
                        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                        return delay + jitter;
                    },
                    onRetry: (outcome, timespan, retryCount, context) =>
                    {
                        var statusCode = outcome.Result?.StatusCode.ToString() ?? "Exception";
                        logger?.LogWarning(
                            "Retry {RetryCount}/{MaxRetries} after {Delay}ms due to {StatusCode}",
                            retryCount, retryCount, timespan.TotalMilliseconds, statusCode);
                    });
        }

        public static AsyncCircuitBreakerPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
            int failuresBeforeBreaking = 5,
            TimeSpan? durationOfBreak = null,
            ILogger? logger = null)
        {
            durationOfBreak ??= TimeSpan.FromSeconds(30);

            return Policy
                .HandleResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
                .Or<HttpRequestException>()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: failuresBeforeBreaking,
                    durationOfBreak: durationOfBreak.Value,
                    onBreak: (outcome, duration) =>
                    {
                        logger?.LogError(
                            " Circuit breaker OPENED for {Duration}s due to failures. " +
                            "All requests will fail immediately until reset.",
                            duration.TotalSeconds);
                    },
                    onReset: () =>
                    {
                        logger?.LogInformation(
                            " Circuit breaker CLOSED - requests will resume normally");
                    },
                    onHalfOpen: () =>
                    {
                        logger?.LogInformation(
                            " Circuit breaker HALF-OPEN - testing with next request");
                    });
        }

        public static AsyncTimeoutPolicy<HttpResponseMessage> GetTimeoutPolicy(
            TimeSpan timeout,
            ILogger? logger = null)
        {
            return Policy
                .TimeoutAsync<HttpResponseMessage>(
                    timeout,
                    TimeoutStrategy.Optimistic, // Changed to Optimistic for better cancellation handling
                    onTimeoutAsync: (context, timespan, task) =>
                    {
                        logger?.LogWarning(
                            "⏱️ Request timed out after {Timeout}s",
                            timespan.TotalSeconds);
                        return Task.CompletedTask;
                    });
        }

        public static AsyncPolicy<HttpResponseMessage> GetBulkheadPolicy(
            int maxParallelization = 50,
            int maxQueuingActions = 100,
            ILogger? logger = null)
        {
            return Policy
                .BulkheadAsync<HttpResponseMessage>(
                    maxParallelization,
                    maxQueuingActions,
                    onBulkheadRejectedAsync: context =>
                    {
                        logger?.LogWarning(
                            "🚫 Bulkhead rejected request - too many concurrent calls " +
                            "(max: {Max}, queued: {Queue})",
                            maxParallelization, maxQueuingActions);
                        return Task.CompletedTask;
                    });
        }

        public static IAsyncPolicy<HttpResponseMessage> GetCombinedReadPolicy(
            PolicyOperationOptions? options = null,
            ILogger? logger = null)
        {
            // Use defaults if options not provided
            options ??= new PolicyOperationOptions();

            var timeout = GetTimeoutPolicy(options.GetTimeout(), logger);
            var retry = GetRetryPolicy(options.RetryCount, logger);
            var circuitBreaker = GetCircuitBreakerPolicy(
                options.CircuitBreakerFailuresBeforeBreaking,
                options.GetCircuitBreakerDuration(),
                logger);
            var bulkhead = GetBulkheadPolicy(
                options.BulkheadMaxParallelization,
                options.BulkheadMaxQueuingActions,
                logger);

            // Wrap policies - inner to outer execution
            return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
        }

        public static IAsyncPolicy<HttpResponseMessage> GetCombinedWritePolicy(
            PolicyOperationOptions? options = null,
            ILogger? logger = null)
        {
            // Use defaults if options not provided
            options ??= new PolicyOperationOptions
            {
                BulkheadMaxParallelization = 100,
                BulkheadMaxQueuingActions = 200
            };

            var timeout = GetTimeoutPolicy(options.GetTimeout(), logger);
            var retry = GetRetryPolicy(options.RetryCount, logger);
            var circuitBreaker = GetCircuitBreakerPolicy(
                options.CircuitBreakerFailuresBeforeBreaking,
                options.GetCircuitBreakerDuration(),
                logger);
            var bulkhead = GetBulkheadPolicy(
                options.BulkheadMaxParallelization,
                options.BulkheadMaxQueuingActions,
                logger);

            // Write operations with retry for transient failures
            // timeout + retry + circuit breaker + bulkhead for concurrency control
            return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
        }


    }
}
