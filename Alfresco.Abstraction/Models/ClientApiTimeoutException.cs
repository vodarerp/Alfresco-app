namespace Alfresco.Abstraction.Models
{
    /// <summary>
    /// Exception thrown when a Client API operation times out
    /// </summary>
    public class ClientApiTimeoutException : ClientApiException
    {
        /// <summary>
        /// The operation that timed out (e.g., "GetClientData", "ValidateClientExists")
        /// </summary>
        public string Operation { get; }

        /// <summary>
        /// Timeout duration that was exceeded
        /// </summary>
        public TimeSpan TimeoutDuration { get; }

        /// <summary>
        /// Time elapsed before timeout occurred
        /// </summary>
        public TimeSpan ElapsedTime { get; }

        public ClientApiTimeoutException(
            string operation,
            TimeSpan timeoutDuration,
            TimeSpan elapsedTime,
            string? additionalDetails = null)
            : base(
                message: $"Client API operation '{operation}' timed out after {elapsedTime.TotalSeconds:F2}s (timeout: {timeoutDuration.TotalSeconds}s). {additionalDetails}",
                statusCode: 408, // Request Timeout
                responseBody: additionalDetails)
        {
            Operation = operation;
            TimeoutDuration = timeoutDuration;
            ElapsedTime = elapsedTime;
        }

        public ClientApiTimeoutException(
            string operation,
            TimeSpan timeoutDuration,
            Exception innerException,
            string? additionalDetails = null)
            : base(
                message: $"Client API operation '{operation}' timed out after {timeoutDuration.TotalSeconds}s. {additionalDetails}",
                statusCode: 408,
                responseBody: innerException?.Message,
                innerException: innerException)
        {
            Operation = operation;
            TimeoutDuration = timeoutDuration;
            ElapsedTime = timeoutDuration; // We don't know exact elapsed time, use timeout duration
        }
    }
}
