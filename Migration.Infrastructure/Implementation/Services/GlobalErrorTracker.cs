using Alfresco.Abstraction.Models;
using Alfresco.Contracts.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Threading;

namespace Migration.Infrastructure.Implementation.Services
{
    /// <summary>
    /// Global error tracking service that monitors timeout and retry failures
    /// and determines when migration should be stopped
    /// </summary>
    public class GlobalErrorTracker
    {
        private readonly ILogger<GlobalErrorTracker> _logger;
        private readonly ILogger _uiLogger;
        private readonly ErrorThresholdsOptions _options;

        private int _timeoutCount = 0;
        private int _retryExhaustedCount = 0;
        private DateTime? _lastErrorTime = null;
        private readonly object _lock = new object();

        public GlobalErrorTracker(
            ILogger<GlobalErrorTracker> logger,
            ILoggerFactory loggerFactory,
            IOptions<ErrorThresholdsOptions> options)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _uiLogger = loggerFactory.CreateLogger("UiLogger");
            _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        }

        /// <summary>
        /// Total number of timeout errors recorded
        /// </summary>
        public int TimeoutCount => _timeoutCount;

        /// <summary>
        /// Total number of retry exhausted errors recorded
        /// </summary>
        public int RetryExhaustedCount => _retryExhaustedCount;

        /// <summary>
        /// Total number of errors (timeouts + retry exhausted)
        /// </summary>
        public int TotalErrorCount => _timeoutCount + _retryExhaustedCount;

        /// <summary>
        /// Time of the last recorded error
        /// </summary>
        public DateTime? LastErrorTime => _lastErrorTime;

        /// <summary>
        /// Determines if migration should be stopped based on error thresholds
        /// </summary>
        public bool ShouldStopMigration
        {
            get
            {
                lock (_lock)
                {
                    // Check if timeout threshold exceeded
                    if (_timeoutCount >= _options.MaxTimeoutsBeforeStop)
                    {
                        _logger.LogCritical(
                            "MIGRATION SHOULD STOP: Timeout threshold exceeded ({TimeoutCount}/{MaxTimeouts})",
                            _timeoutCount, _options.MaxTimeoutsBeforeStop);
                        return true;
                    }

                    // Check if retry failures threshold exceeded
                    if (_retryExhaustedCount >= _options.MaxRetryFailuresBeforeStop)
                    {
                        _logger.LogCritical(
                            "MIGRATION SHOULD STOP: Retry failures threshold exceeded ({RetryFailureCount}/{MaxRetryFailures})",
                            _retryExhaustedCount, _options.MaxRetryFailuresBeforeStop);
                        return true;
                    }

                    // Check if total errors threshold exceeded
                    if (TotalErrorCount >= _options.MaxTotalErrorsBeforeStop)
                    {
                        _logger.LogCritical(
                            "MIGRATION SHOULD STOP: Total errors threshold exceeded ({TotalErrors}/{MaxTotalErrors})",
                            TotalErrorCount, _options.MaxTotalErrorsBeforeStop);
                        return true;
                    }

                    return false;
                }
            }
        }

        /// <summary>
        /// Records a timeout error
        /// </summary>
        public void RecordTimeout(AlfrescoTimeoutException exception, string? additionalContext = null)
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _timeoutCount);
                _lastErrorTime = DateTime.UtcNow;

                _logger.LogWarning(
                    "⏱️ TIMEOUT #{Count}: Operation '{Operation}' timed out after {Timeout}s. {Context}",
                    _timeoutCount, exception.Operation, exception.TimeoutDuration.TotalSeconds, additionalContext ?? "");

                _uiLogger.LogWarning(
                    "Timeout #{Count}: {Operation} ({Timeout}s)",
                    _timeoutCount, exception.Operation, exception.TimeoutDuration.TotalSeconds);

                // Check if threshold is approaching
                CheckThresholdWarning();

                // Check if should stop
                if (ShouldStopMigration)
                {
                    _logger.LogCritical(
                        "🛑 CRITICAL: Migration should be stopped! Timeout count: {TimeoutCount}/{MaxTimeouts}",
                        _timeoutCount, _options.MaxTimeoutsBeforeStop);

                    _uiLogger.LogCritical(
                        "🛑 KRITIČNO: Migracija treba da se zaustavi! Previše timeout grešaka: {TimeoutCount}/{MaxTimeouts}",
                        _timeoutCount, _options.MaxTimeoutsBeforeStop);
                }
            }
        }

        /// <summary>
        /// Records a retry exhausted error
        /// </summary>
        public void RecordRetryExhausted(AlfrescoRetryExhaustedException exception, string? additionalContext = null)
        {
            lock (_lock)
            {
                Interlocked.Increment(ref _retryExhaustedCount);
                _lastErrorTime = DateTime.UtcNow;

                _logger.LogWarning(
                    "❌ RETRY EXHAUSTED #{Count}: Operation '{Operation}' failed after {RetryCount} retries. {Context}",
                    _retryExhaustedCount, exception.Operation, exception.RetryCount, additionalContext ?? "");

                _uiLogger.LogWarning(
                    "Retry exhausted #{Count}: {Operation} ({RetryCount} pokušaja)",
                    _retryExhaustedCount, exception.Operation, exception.RetryCount);

                // Check if threshold is approaching
                CheckThresholdWarning();

                // Check if should stop
                if (ShouldStopMigration)
                {
                    _logger.LogCritical(
                        "🛑 CRITICAL: Migration should be stopped! Retry failure count: {RetryFailureCount}/{MaxRetryFailures}",
                        _retryExhaustedCount, _options.MaxRetryFailuresBeforeStop);

                    _uiLogger.LogCritical(
                        "🛑 KRITIČNO: Migracija treba da se zaustavi! Previše retry grešaka: {RetryFailureCount}/{MaxRetryFailures}",
                        _retryExhaustedCount, _options.MaxRetryFailuresBeforeStop);
                }
            }
        }

        /// <summary>
        /// Checks if error count is approaching threshold and logs warning
        /// </summary>
        private void CheckThresholdWarning()
        {
            // Warn at 75% of threshold
            var timeoutThreshold75 = (int)(_options.MaxTimeoutsBeforeStop * 0.75);
            var retryThreshold75 = (int)(_options.MaxRetryFailuresBeforeStop * 0.75);

            if (_timeoutCount == timeoutThreshold75 && timeoutThreshold75 > 0)
            {
                _logger.LogWarning(
                    "⚠️ WARNING: Approaching timeout threshold! Current: {TimeoutCount}/{MaxTimeouts} (75%)",
                    _timeoutCount, _options.MaxTimeoutsBeforeStop);

                _uiLogger.LogWarning(
                    "⚠️ UPOZORENJE: Približava se limit za timeout-e! {TimeoutCount}/{MaxTimeouts}",
                    _timeoutCount, _options.MaxTimeoutsBeforeStop);
            }

            if (_retryExhaustedCount == retryThreshold75 && retryThreshold75 > 0)
            {
                _logger.LogWarning(
                    "⚠️ WARNING: Approaching retry failure threshold! Current: {RetryFailureCount}/{MaxRetryFailures} (75%)",
                    _retryExhaustedCount, _options.MaxRetryFailuresBeforeStop);

                _uiLogger.LogWarning(
                    "⚠️ UPOZORENJE: Približava se limit za retry greške! {RetryFailureCount}/{MaxRetryFailures}",
                    _retryExhaustedCount, _options.MaxRetryFailuresBeforeStop);
            }
        }

        /// <summary>
        /// Resets all error counters (useful when starting new migration)
        /// </summary>
        public void Reset()
        {
            lock (_lock)
            {
                _timeoutCount = 0;
                _retryExhaustedCount = 0;
                _lastErrorTime = null;

                _logger.LogInformation("GlobalErrorTracker reset - all counters cleared");
            }
        }

        /// <summary>
        /// Gets current error metrics for UI display
        /// </summary>
        public ErrorMetrics GetMetrics()
        {
            lock (_lock)
            {
                return new ErrorMetrics
                {
                    TimeoutCount = _timeoutCount,
                    RetryExhaustedCount = _retryExhaustedCount,
                    TotalErrorCount = TotalErrorCount,
                    MaxTimeouts = _options.MaxTimeoutsBeforeStop,
                    MaxRetryFailures = _options.MaxRetryFailuresBeforeStop,
                    MaxTotalErrors = _options.MaxTotalErrorsBeforeStop,
                    RemainingTimeoutsBeforeStop = Math.Max(0, _options.MaxTimeoutsBeforeStop - _timeoutCount),
                    RemainingRetryFailuresBeforeStop = Math.Max(0, _options.MaxRetryFailuresBeforeStop - _retryExhaustedCount),
                    LastErrorTime = _lastErrorTime,
                    ShouldStopMigration = ShouldStopMigration
                };
            }
        }
    }

    /// <summary>
    /// Error metrics for UI display
    /// </summary>
    public class ErrorMetrics
    {
        public int TimeoutCount { get; set; }
        public int RetryExhaustedCount { get; set; }
        public int TotalErrorCount { get; set; }
        public int MaxTimeouts { get; set; }
        public int MaxRetryFailures { get; set; }
        public int MaxTotalErrors { get; set; }
        public int RemainingTimeoutsBeforeStop { get; set; }
        public int RemainingRetryFailuresBeforeStop { get; set; }
        public DateTime? LastErrorTime { get; set; }
        public bool ShouldStopMigration { get; set; }

        /// <summary>
        /// Percentage of timeout threshold reached (0-100)
        /// </summary>
        public double TimeoutPercentage => MaxTimeouts > 0 ? (TimeoutCount * 100.0 / MaxTimeouts) : 0;

        /// <summary>
        /// Percentage of retry failure threshold reached (0-100)
        /// </summary>
        public double RetryFailurePercentage => MaxRetryFailures > 0 ? (RetryExhaustedCount * 100.0 / MaxRetryFailures) : 0;
    }
}
