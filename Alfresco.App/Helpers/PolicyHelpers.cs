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
        public static IAsyncPolicy<HttpResponseMessage> GetRetryPlicy()
        {
            return HttpPolicyExtensions
                 .HandleTransientHttpError()
                .OrResult(msg => msg.StatusCode == HttpStatusCode.TooManyRequests)
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
        }

        public static IAsyncPolicy<HttpResponseMessage> GetCircuitBreakerPolicy()
        {
            return HttpPolicyExtensions
                .HandleTransientHttpError()
                .CircuitBreakerAsync(5, TimeSpan.FromSeconds(30));
        }

        public static AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy(ILogger? logger = null)
        {
            return Policy
                .HandleResult<HttpResponseMessage>(r =>
                    r.StatusCode == HttpStatusCode.TooManyRequests ||
                    r.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    r.StatusCode == HttpStatusCode.RequestTimeout ||
                    (int)r.StatusCode >= 500)
                .Or<HttpRequestException>()
                .Or<TaskCanceledException>()
                .WaitAndRetryAsync(
                    retryCount: 3,
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
                            "Retry {RetryCount}/3 after {Delay}ms due to {StatusCode}",
                            retryCount, timespan.TotalMilliseconds, statusCode);
                    });
        }

        public static AsyncCircuitBreakerPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
            ILogger? logger = null)
        {
            return Policy
                .HandleResult<HttpResponseMessage>(r => (int)r.StatusCode >= 500)
                .Or<HttpRequestException>()
                .CircuitBreakerAsync(
                    handledEventsAllowedBeforeBreaking: 5,
                    durationOfBreak: TimeSpan.FromSeconds(30),
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
                    TimeoutStrategy.Pessimistic,
                    onTimeoutAsync: (context, timespan, task) =>
                    {
                        logger?.LogWarning(
                            "⏱️ Request timed out after {Timeout}s",
                            timespan.TotalSeconds);
                        return Task.CompletedTask;
                    });
        }

        public static AsyncPolicy<HttpResponseMessage> GetBulkheadPolicy(
            int maxParallelization = 30,
            int maxQueuingActions = 50,
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
            ILogger? logger = null, int bulkheadLimit = 30)
        {
            var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(10), logger);
            var retry = GetRetryPolicy(logger);
            var circuitBreaker = GetCircuitBreakerPolicy(logger);
            var bulkhead = GetBulkheadPolicy(bulkheadLimit, bulkheadLimit*2, logger);

            // Wrap policies - inner to outer execution
            return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
        }

        public static IAsyncPolicy<HttpResponseMessage> GetCombinedWritePolicy(
           ILogger? logger = null, int bulkheadLimit = 100)
        {
            var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(30), logger);
            var retry = GetRetryPolicy(logger);
            var circuitBreaker = GetCircuitBreakerPolicy(logger);
            var bulkhead = GetBulkheadPolicy(bulkheadLimit, bulkheadLimit * 2, logger);

            // Write operations with retry for transient failures
            // timeout + retry + circuit breaker + bulkhead for concurrency control
            return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
        }


    }
}
