namespace Alfresco.Contracts.Options
{
    /// <summary>
    /// Configuration options for Polly policies (Timeout, Retry, Circuit Breaker, Bulkhead)
    /// </summary>
    public class PollyPolicyOptions
    {
        /// <summary>
        /// Configuration section name in appsettings.json
        /// </summary>
        public const string SectionName = "PollyPolicy";

        /// <summary>
        /// Policy settings for READ operations (folder search, node read, etc.)
        /// </summary>
        public PolicyOperationOptions ReadOperations { get; set; } = new();

        /// <summary>
        /// Policy settings for WRITE operations (move, copy, create folder, update properties)
        /// </summary>
        public PolicyOperationOptions WriteOperations { get; set; } = new();
    }

    /// <summary>
    /// Policy settings for a specific operation type (Read or Write)
    /// </summary>
    public class PolicyOperationOptions
    {
        /// <summary>
        /// Timeout for HTTP operations in seconds
        /// Default: 120s
        /// </summary>
        public int TimeoutSeconds { get; set; } = 120;

        /// <summary>
        /// Number of retry attempts for transient failures
        /// Default: 3
        /// Note: Timeout exceptions (TaskCanceledException) are NOT retried
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// Number of consecutive failures before circuit breaker opens
        /// Default: 5
        /// </summary>
        public int CircuitBreakerFailuresBeforeBreaking { get; set; } = 5;

        /// <summary>
        /// Duration (in seconds) that circuit breaker stays open before attempting a test request
        /// Default: 30s
        /// </summary>
        public int CircuitBreakerDurationOfBreakSeconds { get; set; } = 30;

        /// <summary>
        /// Maximum number of parallel requests allowed (Bulkhead pattern)
        /// Default: 50 for reads, 100 for writes
        /// </summary>
        public int BulkheadMaxParallelization { get; set; } = 50;

        /// <summary>
        /// Maximum number of requests that can be queued when bulkhead is full
        /// Default: 100 for reads, 200 for writes
        /// </summary>
        public int BulkheadMaxQueuingActions { get; set; } = 100;

        /// <summary>
        /// Validates configuration values
        /// </summary>
        public void Validate()
        {
            if (TimeoutSeconds <= 0)
                throw new ArgumentException("TimeoutSeconds must be greater than 0", nameof(TimeoutSeconds));

            if (RetryCount < 0)
                throw new ArgumentException("RetryCount must be 0 or greater", nameof(RetryCount));

            if (CircuitBreakerFailuresBeforeBreaking <= 0)
                throw new ArgumentException("CircuitBreakerFailuresBeforeBreaking must be greater than 0", nameof(CircuitBreakerFailuresBeforeBreaking));

            if (CircuitBreakerDurationOfBreakSeconds <= 0)
                throw new ArgumentException("CircuitBreakerDurationOfBreakSeconds must be greater than 0", nameof(CircuitBreakerDurationOfBreakSeconds));

            if (BulkheadMaxParallelization <= 0)
                throw new ArgumentException("BulkheadMaxParallelization must be greater than 0", nameof(BulkheadMaxParallelization));

            if (BulkheadMaxQueuingActions < 0)
                throw new ArgumentException("BulkheadMaxQueuingActions must be 0 or greater", nameof(BulkheadMaxQueuingActions));
        }

        /// <summary>
        /// Returns timeout as TimeSpan
        /// </summary>
        public TimeSpan GetTimeout() => TimeSpan.FromSeconds(TimeoutSeconds);

        /// <summary>
        /// Returns circuit breaker duration as TimeSpan
        /// </summary>
        public TimeSpan GetCircuitBreakerDuration() => TimeSpan.FromSeconds(CircuitBreakerDurationOfBreakSeconds);
    }
}
