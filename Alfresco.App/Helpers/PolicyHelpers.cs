using Alfresco.Abstraction.Models;       // ← Custom Exceptions
using Alfresco.Contracts.Options;        // ← PollyPolicyOptions
using Microsoft.Extensions.Logging;     // ← ILogger
using Migration.Infrastructure.Implementation.Services;
using Polly;                             // ← Policy, AsyncRetryPolicy
using Polly.CircuitBreaker;              // ← AsyncCircuitBreakerPolicy
using Polly.Extensions.Http;
using Polly.Retry;                       // ← AsyncRetryPolicy
using Polly.Timeout;                     // ← AsyncTimeoutPolicy, TimeoutRejectedException
using System.Net;
using System.Net.Http;

namespace Alfresco.App.Helpers
{
    public static class PolicyHelpers
    {
        

        public static AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy(
            int retryCount = 3,
            ILogger? fileLogger = null, ILogger? dbLogger = null, ILogger? uiLogger = null)
        {
            return Policy
                .HandleResult<HttpResponseMessage>(r =>
                    r.StatusCode == HttpStatusCode.TooManyRequests ||
                    r.StatusCode == HttpStatusCode.ServiceUnavailable ||
                    r.StatusCode == HttpStatusCode.RequestTimeout ||
                    (int)r.StatusCode >= 500)
                .Or<HttpRequestException>()
                .Or<TimeoutRejectedException>() // Retry timeout exceptions - server može da se oporavi
                .WaitAndRetryAsync(
                    retryCount: retryCount,
                    sleepDurationProvider: retryAttempt =>
                    {
                        // Exponential backoff: 2s, 4s, 8s
                        var delay = TimeSpan.FromSeconds(Math.Pow(2, retryAttempt));

                        // Jitter to avoid thundering herd (random 0-500ms)
                        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(0, 500));
                        return delay + jitter;
                    },
                    onRetry: (outcome, timespan, retryAttempt, context) =>
                    {
                        var operation = context.PolicyKey ?? "Unknown";
                        var statusCode = outcome.Result?.StatusCode.ToString() ?? "N/A";
                        var exceptionType = outcome.Exception?.GetType().Name ?? "N/A";

                        if (outcome.Exception is TimeoutRejectedException)
                        {
                            fileLogger?.LogWarning(
                                "⚠️ Retry {RetryAttempt}/{MaxRetries} for operation '{Operation}' - TIMEOUT. " +
                                "Waiting {Delay}s before next attempt.",
                                retryAttempt, retryCount, operation, timespan.TotalSeconds);

                            uiLogger?.LogWarning(
                                "Retry pokušaj {RetryAttempt} od {MaxRetries} - Timeout na operaciji '{Operation}'",
                                retryAttempt, retryCount, operation);
                        }
                        else if (outcome.Exception != null)
                        {
                            fileLogger?.LogWarning(
                                "⚠️ Retry {RetryAttempt}/{MaxRetries} for operation '{Operation}' - {ExceptionType}: {Message}. " +
                                "Waiting {Delay}s before next attempt.",
                                retryAttempt, retryCount, operation, exceptionType, outcome.Exception.Message, timespan.TotalSeconds);
                        }
                        else
                        {
                            fileLogger?.LogWarning(
                                "⚠️ Retry {RetryAttempt}/{MaxRetries} for operation '{Operation}' - HTTP {StatusCode}. " +
                                "Waiting {Delay}s before next attempt.",
                                retryAttempt, retryCount, operation, statusCode, timespan.TotalSeconds);
                        }

                        // Store retry count in context for fallback policy
                        context["RetryAttempts"] = retryAttempt;
                    });
        }

        public static AsyncCircuitBreakerPolicy<HttpResponseMessage> GetCircuitBreakerPolicy(
            int failuresBeforeBreaking = 5,
            TimeSpan? durationOfBreak = null,
            ILogger? fileLogger = null, ILogger? dbLogger = null, ILogger? uiLogger = null)
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
                        fileLogger?.LogError(
                            " Circuit breaker OPENED for {Duration}s due to failures. " +
                            "All requests will fail immediately until reset.",
                            duration.TotalSeconds);
                    },
                    onReset: () =>
                    {
                        fileLogger?.LogInformation(
                            " Circuit breaker CLOSED - requests will resume normally");
                    },
                    onHalfOpen: () =>
                    {
                        fileLogger?.LogInformation(
                            " Circuit breaker HALF-OPEN - testing with next request");
                    });
        }

        public static AsyncTimeoutPolicy<HttpResponseMessage> GetTimeoutPolicy(
            TimeSpan timeout,
            ILogger? fileLogger = null, ILogger? dbLogger = null, ILogger? uiLogger = null)
        {
            return Policy
                .TimeoutAsync<HttpResponseMessage>(
                    timeout,
                    TimeoutStrategy.Optimistic, // Changed to Optimistic for better cancellation handling
                    onTimeoutAsync: (context, timespan, task) =>
                    {
                        fileLogger?.LogWarning(
                            "⏱️ Request timed out after {Timeout}s",
                            timespan.TotalSeconds);
                        return Task.CompletedTask;
                    });
        }

        public static AsyncPolicy<HttpResponseMessage> GetBulkheadPolicy(
            int maxParallelization = 50,
            int maxQueuingActions = 100,
            ILogger? fileLogger = null, ILogger? dbLogger = null, ILogger? uiLogger = null)
        {
            return Policy
                .BulkheadAsync<HttpResponseMessage>(
                    maxParallelization,
                    maxQueuingActions,
                    onBulkheadRejectedAsync: context =>
                    {
                        fileLogger?.LogWarning(
                            "🚫 Bulkhead rejected request - too many concurrent calls " +
                            "(max: {Max}, queued: {Queue})",
                            maxParallelization, maxQueuingActions);
                        return Task.CompletedTask;
                    });
        }

        /// <summary>
        /// Fallback policy that throws custom exceptions when all retries are exhausted
        /// </summary>
        public static IAsyncPolicy<HttpResponseMessage> GetFallbackPolicy(
            int maxRetryCount,
            TimeSpan timeout,
            ILogger? fileLogger = null, ILogger? dbLogger = null, ILogger? uiLogger = null)
        {
            return Policy<HttpResponseMessage>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackAction: (outcome, context, token) =>
                    {
                        var operation = context.PolicyKey ?? "Unknown";
                        var retryAttempts = context.ContainsKey("RetryAttempts")
                            ? (int)context["RetryAttempts"]
                            : 0;

                        // Check if final exception is timeout-related
                        if (outcome.Exception is TimeoutRejectedException timeoutEx)
                        {
                            fileLogger?.LogError(
                                "❌ FINAL FAILURE - Operation '{Operation}' timed out after {RetryCount} attempts. " +
                                "Throwing AlfrescoTimeoutException.",
                                operation, retryAttempts);

                            uiLogger?.LogError(
                                "GREŠKA: Operacija '{Operation}' je istekla nakon {RetryCount} pokušaja (timeout: {Timeout}s)",
                                operation, retryAttempts, timeout.TotalSeconds);

                            throw new AlfrescoTimeoutException(
                                operation: operation,
                                timeoutDuration: timeout,
                                innerException: timeoutEx,
                                additionalDetails: $"Failed after {retryAttempts} retry attempts");
                        }
                        // All other exceptions - retry exhausted
                        else
                        {
                            var statusCode = (outcome.Result?.StatusCode != null)
                                ? (int)outcome.Result.StatusCode
                                : (int?)null;

                            fileLogger?.LogError(
                                "❌ FINAL FAILURE - Operation '{Operation}' failed after {RetryCount} attempts. " +
                                "Last error: {Exception}. Throwing AlfrescoRetryExhaustedException.",
                                operation, retryAttempts, outcome.Exception?.Message ?? "Unknown");

                            uiLogger?.LogError(
                                "GREŠKA: Operacija '{Operation}' je pala nakon {RetryCount} pokušaja. Greška: {Error}",
                                operation, retryAttempts, outcome.Exception?.Message ?? "Unknown");

                            throw new AlfrescoRetryExhaustedException(
                                operation: operation,
                                retryCount: retryAttempts,
                                lastException: outcome.Exception,
                                lastStatusCode: statusCode,
                                additionalDetails: "All retry attempts exhausted");
                        }
                    },
                    onFallbackAsync: (outcome, context) =>
                    {
                        var operation = context.PolicyKey ?? "Unknown";
                        fileLogger?.LogWarning("⚠️ Fallback triggered for operation '{Operation}'", operation);
                        return Task.CompletedTask;
                    });
        }

        /// <summary>
        /// Fallback policy for Client API that throws custom exceptions when all retries are exhausted
        /// </summary>
        public static IAsyncPolicy<HttpResponseMessage> GetClientApiFallbackPolicy(
            int maxRetryCount,
            TimeSpan timeout,
            ILogger? fileLogger = null, ILogger? dbLogger = null, ILogger? uiLogger = null)
        {
            return Policy<HttpResponseMessage>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackAction: (outcome, context, token) =>
                    {
                        var operation = context.PolicyKey ?? "Unknown";
                        var retryAttempts = context.ContainsKey("RetryAttempts")
                            ? (int)context["RetryAttempts"]
                            : 0;

                        // Check if final exception is timeout-related
                        if (outcome.Exception is TimeoutRejectedException timeoutEx)
                        {
                            fileLogger?.LogError(
                                "❌ FINAL FAILURE - Client API operation '{Operation}' timed out after {RetryCount} attempts. " +
                                "Throwing ClientApiTimeoutException.",
                                operation, retryAttempts);

                            uiLogger?.LogError(
                                "GREŠKA: Client API operacija '{Operation}' je istekla nakon {RetryCount} pokušaja (timeout: {Timeout}s)",
                                operation, retryAttempts, timeout.TotalSeconds);

                            throw new ClientApiTimeoutException(
                                operation: operation,
                                timeoutDuration: timeout,
                                innerException: timeoutEx,
                                additionalDetails: $"Failed after {retryAttempts} retry attempts");
                        }
                        // All other exceptions - retry exhausted
                        else
                        {
                            var statusCode = (outcome.Result?.StatusCode != null)
                                ? (int)outcome.Result.StatusCode
                                : (int?)null;

                            fileLogger?.LogError(
                                "❌ FINAL FAILURE - Client API operation '{Operation}' failed after {RetryCount} attempts. " +
                                "Last error: {Exception}. Throwing ClientApiRetryExhaustedException.",
                                operation, retryAttempts, outcome.Exception?.Message ?? "Unknown");

                            uiLogger?.LogError(
                                "GREŠKA: Client API operacija '{Operation}' je pala nakon {RetryCount} pokušaja. Greška: {Error}",
                                operation, retryAttempts, outcome.Exception?.Message ?? "Unknown");

                            throw new ClientApiRetryExhaustedException(
                                operation: operation,
                                retryCount: retryAttempts,
                                lastException: outcome.Exception,
                                lastStatusCode: statusCode,
                                additionalDetails: "All retry attempts exhausted");
                        }
                    },
                    onFallbackAsync: (outcome, context) =>
                    {
                        var operation = context.PolicyKey ?? "Unknown";
                        fileLogger?.LogWarning("⚠️ Client API Fallback triggered for operation '{Operation}'", operation);
                        return Task.CompletedTask;
                    });
        }

        public static IAsyncPolicy<HttpResponseMessage> GetCombinedReadPolicy(
            PolicyOperationOptions? options = null,
            ILogger? fileLogger = null, ILogger? dbLogger = null, ILogger? uiLogger = null)
        {
            // Use defaults if options not provided
            options ??= new PolicyOperationOptions();

            var timeout = GetTimeoutPolicy(options.GetTimeout(), fileLogger, dbLogger, uiLogger);
            var retry = GetRetryPolicy(options.RetryCount, fileLogger, dbLogger, uiLogger);
            var circuitBreaker = GetCircuitBreakerPolicy(
                options.CircuitBreakerFailuresBeforeBreaking,
                options.GetCircuitBreakerDuration(),
                 fileLogger, dbLogger, uiLogger);
            var bulkhead = GetBulkheadPolicy(
                options.BulkheadMaxParallelization,
                options.BulkheadMaxQueuingActions,
                 fileLogger, dbLogger, uiLogger);
            var fallback = GetFallbackPolicy(
                options.RetryCount,
                options.GetTimeout(),
                fileLogger, dbLogger, uiLogger);

            // Wrap policies - outer to inner execution order
            // Execution flow: Fallback → Retry → Timeout → CircuitBreaker → Bulkhead → HttpClient
            // This means: Each retry attempt has its own timeout. If timeout occurs, Retry policy catches it and retries.
            // Only after all retries are exhausted, Fallback catches and throws custom exception.
            return Policy.WrapAsync(fallback, retry, timeout, circuitBreaker, bulkhead)
                .WithPolicyKey("AlfrescoRead"); // PolicyKey for operation tracking in logs
        }

        public static IAsyncPolicy<HttpResponseMessage> GetCombinedWritePolicy(
            PolicyOperationOptions? options = null,
            ILogger? fileLogger = null, ILogger? dbLogger = null, ILogger? uiLogger = null)
        {
            // Use defaults if options not provided
            options ??= new PolicyOperationOptions
            {
                BulkheadMaxParallelization = 100,
                BulkheadMaxQueuingActions = 200
            };

            var timeout = GetTimeoutPolicy(options.GetTimeout(), fileLogger, dbLogger, uiLogger);
            var retry = GetRetryPolicy(options.RetryCount, fileLogger, dbLogger, uiLogger);
            var circuitBreaker = GetCircuitBreakerPolicy(
                options.CircuitBreakerFailuresBeforeBreaking,
                options.GetCircuitBreakerDuration(),
                 fileLogger, dbLogger, uiLogger);
            var bulkhead = GetBulkheadPolicy(
                options.BulkheadMaxParallelization,
                options.BulkheadMaxQueuingActions,
                 fileLogger, dbLogger, uiLogger);
            var fallback = GetFallbackPolicy(
                options.RetryCount,
                options.GetTimeout(),
                fileLogger, dbLogger, uiLogger);

            // Wrap policies - outer to inner execution order
            // Execution flow: Fallback → Retry → Timeout → CircuitBreaker → Bulkhead → HttpClient
            // This means: Each retry attempt has its own timeout. If timeout occurs, Retry policy catches it and retries.
            // Only after all retries are exhausted, Fallback catches and throws custom exception.
            return Policy.WrapAsync(fallback, retry, timeout, circuitBreaker, bulkhead)
                .WithPolicyKey("AlfrescoWrite"); // PolicyKey for operation tracking in logs
        }

        public static IAsyncPolicy<HttpResponseMessage> GetCombinedClientApiPolicy(
            PolicyOperationOptions? options = null,
            ILogger? fileLogger = null, ILogger? dbLogger = null, ILogger? uiLogger = null)
        {
            // Use defaults if options not provided
            options ??= new PolicyOperationOptions();

            var timeout = GetTimeoutPolicy(options.GetTimeout(), fileLogger, dbLogger, uiLogger);
            var retry = GetRetryPolicy(options.RetryCount, fileLogger, dbLogger, uiLogger);
            var circuitBreaker = GetCircuitBreakerPolicy(
                options.CircuitBreakerFailuresBeforeBreaking,
                options.GetCircuitBreakerDuration(),
                fileLogger, dbLogger, uiLogger);
            var bulkhead = GetBulkheadPolicy(
                options.BulkheadMaxParallelization,
                options.BulkheadMaxQueuingActions,
                fileLogger, dbLogger, uiLogger);
            var fallback = GetClientApiFallbackPolicy(
                options.RetryCount,
                options.GetTimeout(),
                fileLogger, dbLogger, uiLogger);

            // Wrap policies - outer to inner execution order
            // Execution flow: Fallback → Retry → Timeout → CircuitBreaker → Bulkhead → HttpClient
            // This means: Each retry attempt has its own timeout. If timeout occurs, Retry policy catches it and retries.
            // Only after all retries are exhausted, Fallback catches and throws custom ClientAPI exception.
            return Policy.WrapAsync(fallback, retry, timeout, circuitBreaker, bulkhead)
                .WithPolicyKey("ClientApi"); // PolicyKey for operation tracking in logs
        }

    }
}
