using System;
using System.Collections.Generic;

namespace SqlServer.Abstraction.Interfaces
{
    public record CheckpointState(
        long FetchedCount,
        HashSet<int> ProcessedSkips,
        HashSet<int> FailedSkips,
        DateTime? LastUpdatedAt);
}
