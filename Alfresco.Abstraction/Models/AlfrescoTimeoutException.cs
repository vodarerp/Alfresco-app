namespace Alfresco.Abstraction.Models
{
    /// <summary>
    /// Exception thrown when an Alfresco operation times out
    /// </summary>
    public class AlfrescoTimeoutException : AlfrescoException
    {
        /// <summary>
        /// The operation that timed out (e.g., "ReadFolder", "WriteDocument", "MoveNode")
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

        public AlfrescoTimeoutException(
            string operation,
            TimeSpan timeoutDuration,
            TimeSpan elapsedTime,
            string? additionalDetails = null)
            : base(
                message: $"Operation '{operation}' timed out after {elapsedTime.TotalSeconds:F2}s (timeout: {timeoutDuration.TotalSeconds}s). {additionalDetails}",
                statusCode: 408, // Request Timeout
                responseBody: additionalDetails ?? "Operation exceeded timeout limit")
        {
            Operation = operation;
            TimeoutDuration = timeoutDuration;
            ElapsedTime = elapsedTime;
        }

        public AlfrescoTimeoutException(
            string operation,
            TimeSpan timeoutDuration,
            Exception innerException,
            string? additionalDetails = null)
            : base(
                message: $"Operation '{operation}' timed out after {timeoutDuration.TotalSeconds}s. {additionalDetails}",
                statusCode: 408,
                responseBody: innerException?.Message ?? "Operation exceeded timeout limit")
        {
            Operation = operation;
            TimeoutDuration = timeoutDuration;
            ElapsedTime = timeoutDuration; // We don't know exact elapsed time, use timeout duration
        }
    }
}
