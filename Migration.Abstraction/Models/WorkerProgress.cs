using System;

namespace Migration.Abstraction.Models
{
    /// <summary>
    /// Represents progress information for worker operations
    /// </summary>
    public class WorkerProgress
    {
        /// <summary>
        /// Total number of items to process
        /// </summary>
        public long TotalItems { get; set; }

        /// <summary>
        /// Number of items processed so far
        /// </summary>
        public long ProcessedItems { get; set; }

        /// <summary>
        /// Current batch number being processed
        /// </summary>
        public int CurrentBatch { get; set; }

        /// <summary>
        /// Size of each batch
        /// </summary>
        public int BatchSize { get; set; }

        /// <summary>
        /// Number of items in the current batch
        /// </summary>
        public int CurrentBatchCount { get; set; }

        /// <summary>
        /// Number of successful operations in current batch
        /// </summary>
        public int SuccessCount { get; set; }

        /// <summary>
        /// Number of failed operations in current batch
        /// </summary>
        public int FailedCount { get; set; }

        /// <summary>
        /// Optional message describing current operation
        /// </summary>
        public string? Message { get; set; }

        /// <summary>
        /// Timestamp when this progress was reported
        /// </summary>
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>
        /// Remaining items (calculated property)
        /// </summary>
        public long RemainingItems => Math.Max(0, TotalItems - ProcessedItems);

        /// <summary>
        /// Progress percentage (calculated property)
        /// </summary>
        public double ProgressPercentage => TotalItems > 0 ? (ProcessedItems * 100.0 / TotalItems) : 0.0;

        public WorkerProgress()
        {
        }

        public WorkerProgress(long total, long processed)
        {
            TotalItems = total;
            ProcessedItems = processed;
        }

        public WorkerProgress(long total, long processed, int currentBatch, int batchSize, int currentBatchCount)
        {
            TotalItems = total;
            ProcessedItems = processed;
            CurrentBatch = currentBatch;
            BatchSize = batchSize;
            CurrentBatchCount = currentBatchCount;
        }
    }
}
