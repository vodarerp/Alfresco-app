using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Enums
{
    public enum MigrationStatus
    {
        /// <summary>
        /// Newly inserted, not yet ready for processing
        /// </summary>
        New = 0,

        /// <summary>
        /// Ready to be picked up by worker
        /// </summary>
        Ready = 1,

        /// <summary>
        /// Currently being processed (locked by worker)
        /// </summary>
        InProgress = 2,

        /// <summary>
        /// Successfully processed
        /// </summary>
        Done = 3,

        /// <summary>
        /// Processing completed (for folders after document discovery)
        /// </summary>
        Processed = 4,

        /// <summary>
        /// Failed with error (can be retried)
        /// </summary>
        Error = 5
    }
    public static class MigrationStatusExtensions
    {        
            /// <summary>
            /// Convert enum to database string value
            /// </summary>
            public static string ToDbString(this MigrationStatus status)
            {
                return status switch
                {
                    MigrationStatus.New => "NEW",
                    MigrationStatus.Ready => "READY",
                    MigrationStatus.InProgress => "IN PROGRESS",
                    MigrationStatus.Done => "DONE",
                    MigrationStatus.Processed => "PROCESSED",
                    MigrationStatus.Error => "ERROR",
                    _ => throw new ArgumentOutOfRangeException(nameof(status))
                };
            }

            /// <summary>
            /// Parse database string to enum
            /// </summary>
            public static MigrationStatus FromDbString(string dbValue)
            {
                return dbValue?.ToUpperInvariant() switch
                {
                    "NEW" => MigrationStatus.New,
                    "READY" => MigrationStatus.Ready,
                    "IN PROGRESS" => MigrationStatus.InProgress,
                    "DONE" => MigrationStatus.Done,
                    "PROCESSED" => MigrationStatus.Processed,
                    "ERROR" => MigrationStatus.Error,
                    _ => throw new ArgumentException($"Unknown status: {dbValue}", nameof(dbValue))
                };
            }

            /// <summary>
            /// Check if status is terminal (no further processing needed)
            /// </summary>
            public static bool IsTerminal(this MigrationStatus status)
            {
                return status is MigrationStatus.Done
                    or MigrationStatus.Processed;
            }

            /// <summary>
            /// Check if status can be retried
            /// </summary>
            public static bool CanRetry(this MigrationStatus status)
            {
                return status is MigrationStatus.Error;
            }

            /// <summary>
            /// Check if status indicates active processing
            /// </summary>
            public static bool IsActive(this MigrationStatus status)
            {
                return status is MigrationStatus.InProgress;
            }
    }
}

