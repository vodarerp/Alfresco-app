namespace Alfresco.Abstraction.Models
{
    /// <summary>
    /// Exception thrown when all retry attempts have been exhausted for a Client API operation
    /// </summary>
    public class ClientApiRetryExhaustedException : ClientApiException
    {
        /// <summary>
        /// The operation that failed (e.g., "GetClientData", "ValidateClientExists")
        /// </summary>
        public string Operation { get; }

        /// <summary>
        /// Number of retry attempts that were made
        /// </summary>
        public int RetryCount { get; }

        /// <summary>
        /// The last exception that occurred before retries were exhausted
        /// </summary>
        public Exception? LastException { get; }

        /// <summary>
        /// HTTP status code from the last failed attempt (if available)
        /// </summary>
        public int? LastStatusCode { get; }

        public ClientApiRetryExhaustedException(
            string operation,
            int retryCount,
            Exception lastException,
            int? lastStatusCode = null,
            string? additionalDetails = null)
            : base(
                message: $"Client API operation '{operation}' failed after {retryCount} retry attempts. Last error: {lastException?.Message}. {additionalDetails}",
                statusCode: lastStatusCode ?? 500,
                responseBody: lastException?.ToString(),
                innerException: lastException)
        {
            Operation = operation;
            RetryCount = retryCount;
            LastException = lastException;
            LastStatusCode = lastStatusCode;
        }

        public ClientApiRetryExhaustedException(
            string operation,
            int retryCount,
            string lastErrorMessage,
            int? lastStatusCode = null,
            string? additionalDetails = null)
            : base(
                message: $"Client API operation '{operation}' failed after {retryCount} retry attempts. Last error: {lastErrorMessage}. {additionalDetails}",
                statusCode: lastStatusCode ?? 500,
                responseBody: lastErrorMessage)
        {
            Operation = operation;
            RetryCount = retryCount;
            LastException = null;
            LastStatusCode = lastStatusCode;
        }
    }
}
