namespace Alfresco.Abstraction.Interfaces
{
    public interface IAlfrescoHealthChecker
    {
        /// <summary>
        /// Returns true if Alfresco responds with a successful HTTP status.
        /// Does NOT go through Polly — bypasses any open Circuit Breaker.
        /// </summary>
        Task<bool> IsAvailableAsync(CancellationToken ct = default);

        /// <summary>
        /// Polls Alfresco until it becomes available or maxAttempts is exhausted.
        /// Throws InvalidOperationException if Alfresco does not respond after maxAttempts.
        /// </summary>
        Task WaitUntilAvailableAsync(
            TimeSpan pollInterval,
            int maxAttempts,
            Action<int, int>? onAttempt,
            CancellationToken ct);
    }
}
