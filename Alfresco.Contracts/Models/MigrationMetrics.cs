using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Models
{
    /// <summary>
    /// Metrics for migration progress tracking
    /// </summary>
    public sealed record MigrationMetrics
    {
        public long TotalProcessed { get; init; }
        public long TotalFailed { get; init; }
        public long CurrentBatch { get; init; }
        public TimeSpan ElapsedTime { get; init; }
        public double ItemsPerSecond { get; init; }

        public static MigrationMetrics Empty => new()
        {
            TotalProcessed = 0,
            TotalFailed = 0,
            CurrentBatch = 0,
            ElapsedTime = TimeSpan.Zero,
            ItemsPerSecond = 0
        };

        public MigrationMetrics WithProcessed(long count)
        {
            return this with { TotalProcessed = TotalProcessed + count };
        }

        public MigrationMetrics WithFailed(long count)
        {
            return this with { TotalFailed = TotalFailed + count };
        }

        public MigrationMetrics WithBatch(long batch)
        {
            return this with { CurrentBatch = batch };
        }

        public MigrationMetrics WithElapsed(TimeSpan elapsed)
        {
            var totalItems = TotalProcessed + TotalFailed;
            var itemsPerSecond = elapsed.TotalSeconds > 0
                ? totalItems / elapsed.TotalSeconds
                : 0;

            return this with
            {
                ElapsedTime = elapsed,
                ItemsPerSecond = itemsPerSecond
            };
        }

        public override string ToString()
        {
            return $"Batch {CurrentBatch}: {TotalProcessed} processed, {TotalFailed} failed " +
                   $"({ItemsPerSecond:F2} items/sec, elapsed: {ElapsedTime:hh\\:mm\\:ss})";
        }
    }
}