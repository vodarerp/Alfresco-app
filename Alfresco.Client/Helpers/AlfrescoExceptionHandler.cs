using Alfresco.Abstraction.Models;
using Microsoft.Extensions.Logging;

namespace Alfresco.Client.Helpers
{
    /// <summary>
    /// Centralized exception handler for Alfresco API operations
    /// Catches Polly-generated exceptions and adds operation context
    /// </summary>
    public static class AlfrescoExceptionHandler
    {
        /// <summary>
        /// Wraps an async operation with exception handling for Timeout and Retry exceptions
        /// </summary>
        public static async Task<T> ExecuteWithExceptionHandlingAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            ILogger logger,
            Dictionary<string, object>? context = null)
        {
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (AlfrescoTimeoutException timeoutEx)
            {
                // Log with operation context
                var contextStr = FormatContext(context);
                logger.LogError(
                    "⏱️ TIMEOUT in {Operation}: {Message}. Context: {Context}",
                    operationName, timeoutEx.Message, contextStr);

                // Re-throw with enhanced message
                throw new AlfrescoTimeoutException(
                    operation: $"{timeoutEx.Operation ?? "Unknown"} → {operationName}",
                    timeoutDuration: timeoutEx.TimeoutDuration,
                    innerException: timeoutEx,
                    additionalDetails: $"Operation: {operationName}. {contextStr}. Original: {timeoutEx.Message}");
            }
            catch (AlfrescoRetryExhaustedException retryEx)
            {
                // Log with operation context
                var contextStr = FormatContext(context);
                logger.LogError(
                    "❌ RETRY EXHAUSTED in {Operation}: {Message}. Retry count: {RetryCount}. Context: {Context}",
                    operationName, retryEx.Message, retryEx.RetryCount, contextStr);

                // Re-throw with enhanced message
                throw new AlfrescoRetryExhaustedException(
                    operation: $"{retryEx.Operation ?? "Unknown"} → {operationName}",
                    retryCount: retryEx.RetryCount,
                    lastException: retryEx.LastException ?? retryEx,
                    lastStatusCode: retryEx.LastStatusCode,
                    additionalDetails: $"Operation: {operationName}. {contextStr}. Original: {retryEx.Message}");
            }
        }

        /// <summary>
        /// Wraps an async operation (void) with exception handling
        /// </summary>
        public static async Task ExecuteWithExceptionHandlingAsync(
            Func<Task> operation,
            string operationName,
            ILogger logger,
            Dictionary<string, object>? context = null)
        {
            await ExecuteWithExceptionHandlingAsync(
                async () =>
                {
                    await operation().ConfigureAwait(false);
                    return true; // Dummy return value
                },
                operationName,
                logger,
                context).ConfigureAwait(false);
        }

        private static string FormatContext(Dictionary<string, object>? context)
        {
            if (context == null || context.Count == 0)
                return "No additional context";

            return string.Join(", ", context.Select(kvp => $"{kvp.Key}={kvp.Value}"));
        }
    }
}
