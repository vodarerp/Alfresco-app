# Performance Tuning Guide - Alfresco Migration

## 🔧 Timeout Issues - "delegate executed asynchronously through TimeoutPolicy did not complete within the timeout"

### Root Cause
When running high-concurrency move operations (50+ parallel tasks), some HTTP requests may exceed the Polly timeout policy threshold (was 30s).

### Solutions Applied

#### 1. Increased Timeout for Write Operations
**File**: `Alfresco.App\Helpers\PolicyHelpers.cs`

```csharp
// BEFORE: 30 seconds
var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(30), logger);

// AFTER: 120 seconds (2 minutes)
var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(120), logger);
```

**Rationale**: Move operations through Alfresco API can be slow, especially under high load. 120s timeout provides sufficient buffer for:
- Network latency
- Alfresco server processing time
- Retry attempts (3 retries with exponential backoff)
- Queue wait time in bulkhead

#### 2. Changed Timeout Strategy to Optimistic
**File**: `Alfresco.App\Helpers\PolicyHelpers.cs`

```csharp
// BEFORE: Pessimistic (forcefully cancels task)
TimeoutStrategy.Pessimistic

// AFTER: Optimistic (relies on CancellationToken propagation)
TimeoutStrategy.Optimistic
```

**Rationale**:
- **Pessimistic**: Wraps delegate in Task.Run and forcefully abandons it (can leave orphaned operations)
- **Optimistic**: Cooperatively cancels via CancellationToken (cleaner, but requires all async code to honor ct)

Our code properly propagates CancellationToken throughout, so Optimistic is safe and more efficient.

#### 3. Reduced MaxDegreeOfParallelism
**File**: `Alfresco.App\appsettings.json`

```json
// BEFORE
"MaxDegreeOfParallelism": 80

// AFTER
"MaxDegreeOfParallelism": 50
```

**Rationale**:
- 80 parallel tasks was overwhelming the Bulkhead queue (max 100 concurrent + 200 queued)
- Alfresco server may struggle with 80+ concurrent move operations
- 50 parallel tasks still provides excellent throughput while reducing timeout risk

#### 4. Reduced BatchSize
**File**: `Alfresco.App\appsettings.json`

```json
// BEFORE
"BatchSize": 500

// AFTER
"BatchSize": 200
```

**Rationale**:
- Smaller batches = more frequent checkpoints = better resume granularity
- Reduces memory pressure
- Faster batch completion = quicker feedback on errors
- Still efficient (200 docs × 50 parallel = 10,000 moves per batch cycle)

## 📊 Performance Expectations

### Current Configuration (Optimized)
- **Batch Size**: 200 documents
- **MaxDegreeOfParallelism**: 50
- **Timeout**: 120 seconds
- **Bulkhead**: 100 concurrent + 200 queued

### Expected Throughput
Assuming ~500-1000ms per document move operation:
- **Per batch**: 200 documents
- **Batch duration**: ~4-8 seconds (with 50 parallel workers)
- **Throughput**: ~1,500-3,000 documents/minute
- **For 10,000 documents**: ~3-7 minutes

### Monitoring Batch Performance
Check logs for timing breakdown:
```
Move batch TOTAL: acquire=250ms, move=6200ms, update=150ms, total=6600ms |
Success=195, Failed=5 | Overall: 2450 moved, 23 failed
```

## ⚙️ Tuning Recommendations

### If Still Getting Timeouts

#### Option 1: Increase Timeout Further
```csharp
// In PolicyHelpers.cs GetCombinedWritePolicy()
var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(180), logger); // 3 minutes
```

#### Option 2: Reduce Parallelism
```json
// In appsettings.json
"MaxDegreeOfParallelism": 30
```

#### Option 3: Increase Bulkhead Limits
```csharp
// In App.xaml.cs AddHttpClient for WriteApi
return PolicyHelpers.GetCombinedWritePolicy(logger, bulkheadLimit: 150);
```

### If Performance is Too Slow

#### Option 1: Increase Parallelism (Monitor for Timeouts!)
```json
"MaxDegreeOfParallelism": 70,
"BatchSize": 300
```

#### Option 2: Optimize Alfresco Server
- Increase Alfresco server resources (CPU, RAM)
- Check Alfresco database performance
- Verify network latency between app and Alfresco

#### Option 3: Profile Bottleneck
Check which phase takes longest in logs:
- **acquire**: Database query performance → Add indexes, tune query
- **move**: Alfresco API performance → Server-side optimization
- **update**: Batch update performance → Already using Oracle array binding (optimal)

## 🔍 Diagnostics

### Check if Timeout is the Issue
Look for these log messages:
```
⏱️ Request timed out after 120s
```

### Check if Bulkhead is Rejecting
Look for:
```
🚫 Bulkhead rejected request - too many concurrent calls (max: 100, queued: 200)
```

### Check if Circuit Breaker is Tripping
Look for:
```
🔥 Circuit breaker OPENED for 30s due to failures
```

### Check Overall Batch Health
Query checkpoint status:
```sql
SELECT ServiceName, TotalProcessed, TotalFailed, BatchCounter,
       TO_CHAR(UpdatedAt, 'YYYY-MM-DD HH24:MI:SS') AS LastUpdate
FROM MigrationCheckpoint
WHERE ServiceName = 'Move';
```

## 🎯 Optimal Configuration Matrix

| Scenario | DOP | BatchSize | Timeout | Expected Throughput |
|----------|-----|-----------|---------|---------------------|
| Conservative (Stable) | 30 | 100 | 120s | ~1,000 docs/min |
| **Balanced (Recommended)** | **50** | **200** | **120s** | **~2,000 docs/min** |
| Aggressive (Fast) | 70 | 300 | 180s | ~3,000 docs/min |
| Maximum (Risky) | 100 | 500 | 300s | ~4,000 docs/min |

**DOP** = Degree of Parallelism (MaxDegreeOfParallelism)

## 🚨 Red Flags

Watch out for these issues:

1. **High Failure Rate** (>5%)
   - Reduce parallelism
   - Increase timeout
   - Check Alfresco server logs

2. **Slow Batch Times** (>30s per batch)
   - Profile which phase is slow (acquire/move/update)
   - Check network latency
   - Verify Alfresco server health

3. **Memory Growth**
   - Reduce batch size
   - Check for memory leaks (all HttpResponseMessage should be disposed)

4. **Stuck Items Accumulating**
   - Check `vw_StuckItems` view
   - Verify StuckItemsTimeoutMinutes is appropriate (default: 10 minutes)
   - Ensure application isn't crashing mid-batch

## 📝 Configuration Reference

### appsettings.json
```json
{
  "Migration": {
    "StuckItemsTimeoutMinutes": 10,
    "MoveService": {
      "MaxDegreeOfParallelism": 50,
      "BatchSize": 200,
      "DelayBetweenBatchesInMs": 0
    }
  },
  "Oracle": {
    "ConnectionString": "...;Pooling=true;Min Pool Size=10;Max Pool Size=100;..."
  }
}
```

### PolicyHelpers.cs
```csharp
// Write Policy Configuration
public static IAsyncPolicy<HttpResponseMessage> GetCombinedWritePolicy(
   ILogger? logger = null, int bulkheadLimit = 100)
{
    var timeout = GetTimeoutPolicy(TimeSpan.FromSeconds(120), logger);
    var retry = GetRetryPolicy(logger); // 3 retries with exponential backoff
    var circuitBreaker = GetCircuitBreakerPolicy(logger); // Opens after 5 failures
    var bulkhead = GetBulkheadPolicy(bulkheadLimit, bulkheadLimit * 2, logger);

    return Policy.WrapAsync(timeout, retry, circuitBreaker, bulkhead);
}
```

### SocketsHttpHandler Configuration
```csharp
.ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
{
    PooledConnectionLifetime = TimeSpan.FromMinutes(10),
    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(5),
    MaxConnectionsPerServer = 100, // Critical for high concurrency!
    EnableMultipleHttp2Connections = true
})
```

## 📞 Need Help?

If issues persist after tuning:
1. Check application logs in `logs/app.log`
2. Query database views: `SELECT * FROM vw_MigrationProgress;`
3. Check stuck items: `SELECT * FROM vw_StuckItems;`
4. Review error analysis: `SELECT * FROM vw_ErrorAnalysis;`
5. Check Alfresco server logs for server-side issues
