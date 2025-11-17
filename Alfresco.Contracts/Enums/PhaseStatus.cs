namespace Alfresco.Contracts.Enums
{
    /// <summary>
    /// Defines the status of a migration phase.
    /// Tracks lifecycle: NOT_STARTED → IN_PROGRESS → COMPLETED/FAILED
    /// </summary>
    public enum PhaseStatus
    {
        /// <summary>
        /// Phase has not been started yet
        /// </summary>
        NotStarted = 0,

        /// <summary>
        /// Phase is currently executing
        /// </summary>
        InProgress = 1,

        /// <summary>
        /// Phase completed successfully
        /// </summary>
        Completed = 2,

        /// <summary>
        /// Phase failed with error
        /// </summary>
        Failed = 3
    }

    public static class PhaseStatusExtensions
    {
        /// <summary>
        /// Convert enum to database string value
        /// </summary>
        public static string ToDbString(this PhaseStatus status)
        {
            return status switch
            {
                PhaseStatus.NotStarted => "NOT_STARTED",
                PhaseStatus.InProgress => "IN_PROGRESS",
                PhaseStatus.Completed => "COMPLETED",
                PhaseStatus.Failed => "FAILED",
                _ => throw new ArgumentOutOfRangeException(nameof(status))
            };
        }

        /// <summary>
        /// Parse database string to enum
        /// </summary>
        public static PhaseStatus FromDbString(string dbValue)
        {
            return dbValue?.ToUpperInvariant() switch
            {
                "NOT_STARTED" => PhaseStatus.NotStarted,
                "IN_PROGRESS" => PhaseStatus.InProgress,
                "COMPLETED" => PhaseStatus.Completed,
                "FAILED" => PhaseStatus.Failed,
                _ => throw new ArgumentException($"Unknown phase status: {dbValue}", nameof(dbValue))
            };
        }

        /// <summary>
        /// Check if phase is terminal (no further processing needed)
        /// </summary>
        public static bool IsTerminal(this PhaseStatus status)
        {
            return status is PhaseStatus.Completed or PhaseStatus.Failed;
        }

        /// <summary>
        /// Check if phase can be retried
        /// </summary>
        public static bool CanRetry(this PhaseStatus status)
        {
            return status is PhaseStatus.Failed or PhaseStatus.InProgress;
        }
    }
}
