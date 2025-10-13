using Alfresco.App.UserControls;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;

namespace Alfresco.App.Logging
{
    /// <summary>
    /// Custom logger provider that selectively sends logs to LiveLogViewer
    /// based on logger category name (e.g., only "FileLogger", not "DbLogger")
    /// </summary>
    public class SelectiveLiveLoggerProvider : ILoggerProvider
    {
        private readonly LiveLogViewer _logViewer;
        private readonly HashSet<string> _allowedCategories;
        private readonly bool _allowAll;

        /// <summary>
        /// Create provider that allows all loggers
        /// </summary>
        public SelectiveLiveLoggerProvider(LiveLogViewer logViewer)
        {
            _logViewer = logViewer ?? throw new ArgumentNullException(nameof(logViewer));
            _allowAll = true;
            _allowedCategories = new HashSet<string>();
        }

        /// <summary>
        /// Create provider that only allows specific logger categories
        /// </summary>
        /// <param name="logViewer">LiveLogViewer instance</param>
        /// <param name="allowedCategories">Logger names to allow (e.g., "FileLogger")</param>
        public SelectiveLiveLoggerProvider(LiveLogViewer logViewer, params string[] allowedCategories)
        {
            _logViewer = logViewer ?? throw new ArgumentNullException(nameof(logViewer));
            _allowedCategories = new HashSet<string>(allowedCategories ?? Array.Empty<string>());
            _allowAll = _allowedCategories.Count == 0;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new SelectiveLiveLogger(categoryName, _logViewer, _allowAll, _allowedCategories);
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        private class SelectiveLiveLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly LiveLogViewer _logViewer;
            private readonly bool _allowAll;
            private readonly HashSet<string> _allowedCategories;

            public SelectiveLiveLogger(
                string categoryName,
                LiveLogViewer logViewer,
                bool allowAll,
                HashSet<string> allowedCategories)
            {
                _categoryName = categoryName;
                _logViewer = logViewer;
                _allowAll = allowAll;
                _allowedCategories = allowedCategories;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception exception,
                Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                // Check if this logger category is allowed
                if (!_allowAll && !_allowedCategories.Contains(_categoryName))
                {
                    // Skip this log - not in allowed list
                    return;
                }

                var message = formatter(state, exception);
                _logViewer.AddLog(logLevel, message, exception, _categoryName);
            }
        }
    }
}
