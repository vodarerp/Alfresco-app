using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Alfresco.App.UserControls
{
    /// <summary>
    /// Live Log Viewer UserControl with filtering and real-time updates
    /// </summary>
    public partial class LiveLogViewer : UserControl
    {
        private readonly ObservableCollection<LogEntry> _allLogs;
        private readonly ObservableCollection<LogEntry> _filteredLogs;
        private readonly DispatcherTimer _updateTimer;
        private LogLevel _currentFilter = LogLevel.Trace; // All levels
        private string _searchText = string.Empty;
        private bool _isPaused = false;
        private const int MaxBufferSize = 1000;

        // Statistics
        private int _debugCount = 0;
        private int _infoCount = 0;
        private int _warnCount = 0;
        private int _errorCount = 0;

        public LiveLogViewer()
        {
            InitializeComponent();

            _allLogs = new ObservableCollection<LogEntry>();
            _filteredLogs = new ObservableCollection<LogEntry>();
            LogListBox.ItemsSource = _filteredLogs;

            // Setup timer for periodic updates (in case of batch logging)
            _updateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _updateTimer.Tick += UpdateTimer_Tick;
            _updateTimer.Start();

            // Subscribe to custom log event (if using custom logger)
            // CustomLogger.LogReceived += OnLogReceived;
        }

        /// <summary>
        /// Add a log entry (call this from your logging infrastructure)
        /// </summary>
        public void AddLog(LogLevel level, string message, string loggerName = "")
        {
            if (_isPaused)
                return;

            Dispatcher.InvokeAsync(() =>
            {
                var logEntry = new LogEntry
                {
                    Timestamp = DateTime.Now,
                    Level = level.ToString().ToUpper(),
                    Message = message,
                    LoggerName = loggerName,
                    LevelColor = GetColorForLevel(level)
                };

                // Add to all logs
                _allLogs.Add(logEntry);

                // Update statistics
                UpdateStatistics(level, 1);

                // Enforce buffer limit
                if (_allLogs.Count > MaxBufferSize)
                {
                    var toRemove = _allLogs.First();
                    _allLogs.RemoveAt(0);
                    UpdateStatistics(Enum.Parse<LogLevel>(toRemove.Level, true), -1);
                }

                // Apply filter
                if (ShouldShowLog(logEntry))
                {
                    _filteredLogs.Add(logEntry);

                    // Auto-scroll if enabled
                    if (ChkAutoScroll.IsChecked == true && _filteredLogs.Count > 0)
                    {
                        LogListBox.ScrollIntoView(_filteredLogs.Last());
                    }
                }

                UpdateFooter();
            });
        }

        /// <summary>
        /// Overload for structured logging
        /// </summary>
        public void AddLog(LogLevel level, string message, Exception exception, string loggerName = "")
        {
            var fullMessage = exception != null
                ? $"{message}\n{exception.GetType().Name}: {exception.Message}\n{exception.StackTrace}"
                : message;

            AddLog(level, fullMessage, loggerName);
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            // Placeholder for any periodic updates
            // Could be used to fetch logs from a queue or external source
        }

        private bool ShouldShowLog(LogEntry logEntry)
        {
            // Apply level filter
            if (_currentFilter != LogLevel.Trace)
            {
                var logLevel = Enum.Parse<LogLevel>(logEntry.Level, true);
                if (logLevel < _currentFilter)
                    return false;
            }

            // Apply search filter
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                return logEntry.Message.Contains(_searchText, StringComparison.OrdinalIgnoreCase) ||
                       logEntry.LoggerName.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
            }

            return true;
        }

        private void ApplyFilters()
        {
            _filteredLogs.Clear();

            foreach (var log in _allLogs)
            {
                if (ShouldShowLog(log))
                {
                    _filteredLogs.Add(log);
                }
            }

            UpdateFooter();
        }

        private void UpdateStatistics(LogLevel level, int delta)
        {
            switch (level)
            {
                case LogLevel.Debug:
                case LogLevel.Trace:
                    _debugCount += delta;
                    break;
                case LogLevel.Information:
                    _infoCount += delta;
                    break;
                case LogLevel.Warning:
                    _warnCount += delta;
                    break;
                case LogLevel.Error:
                case LogLevel.Critical:
                    _errorCount += delta;
                    break;
            }
        }

        private void UpdateFooter()
        {
            TxtTotalLogs.Text = $"Total: {_allLogs.Count}";
            TxtDebugCount.Text = $"🔍 DEBUG: {_debugCount}";
            TxtInfoCount.Text = $"ℹ️ INFO: {_infoCount}";
            TxtWarnCount.Text = $"⚠️ WARN: {_warnCount}";
            TxtErrorCount.Text = $"❌ ERROR: {_errorCount}";
            TxtBufferInfo.Text = $"Buffer: {_allLogs.Count} / {MaxBufferSize}";
        }

        private Brush GetColorForLevel(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace or LogLevel.Debug => new SolidColorBrush(Color.FromRgb(158, 158, 158)), // Gray
                LogLevel.Information => new SolidColorBrush(Color.FromRgb(33, 150, 243)), // Blue
                LogLevel.Warning => new SolidColorBrush(Color.FromRgb(255, 152, 0)), // Orange
                LogLevel.Error or LogLevel.Critical => new SolidColorBrush(Color.FromRgb(244, 67, 54)), // Red
                _ => Brushes.Black
            };
        }

        private void SetActiveFilterButton(Button activeButton)
        {
            // Reset all buttons
            BtnFilterAll.IsEnabled = true;
            BtnFilterDebug.IsEnabled = true;
            BtnFilterInfo.IsEnabled = true;
            BtnFilterWarn.IsEnabled = true;
            BtnFilterError.IsEnabled = true;

            // Set active button
            activeButton.IsEnabled = false;
        }

        #region Event Handlers

        private void BtnFilterAll_Click(object sender, RoutedEventArgs e)
        {
            _currentFilter = LogLevel.Trace;
            SetActiveFilterButton(BtnFilterAll);
            ApplyFilters();
        }

        private void BtnFilterDebug_Click(object sender, RoutedEventArgs e)
        {
            _currentFilter = LogLevel.Debug;
            SetActiveFilterButton(BtnFilterDebug);
            ApplyFilters();
        }

        private void BtnFilterInfo_Click(object sender, RoutedEventArgs e)
        {
            _currentFilter = LogLevel.Information;
            SetActiveFilterButton(BtnFilterInfo);
            ApplyFilters();
        }

        private void BtnFilterWarn_Click(object sender, RoutedEventArgs e)
        {
            _currentFilter = LogLevel.Warning;
            SetActiveFilterButton(BtnFilterWarn);
            ApplyFilters();
        }

        private void BtnFilterError_Click(object sender, RoutedEventArgs e)
        {
            _currentFilter = LogLevel.Error;
            SetActiveFilterButton(BtnFilterError);
            ApplyFilters();
        }

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = TxtSearch.Text;
            ApplyFilters();
        }

        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Are you sure you want to clear all logs?",
                "Clear Logs",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _allLogs.Clear();
                _filteredLogs.Clear();
                _debugCount = 0;
                _infoCount = 0;
                _warnCount = 0;
                _errorCount = 0;
                UpdateFooter();
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"logs_{DateTime.Now:yyyyMMdd_HHmmss}",
                    DefaultExt = ".txt",
                    Filter = "Text files (*.txt)|*.txt|CSV files (*.csv)|*.csv|All files (*.*)|*.*"
                };

                if (dialog.ShowDialog() == true)
                {
                    var lines = _filteredLogs.Select(log =>
                        $"{log.Timestamp:yyyy-MM-dd HH:mm:ss.fff}\t{log.Level}\t{log.LoggerName}\t{log.Message}");

                    File.WriteAllLines(dialog.FileName, lines);

                    MessageBox.Show(
                        $"Logs exported successfully to:\n{dialog.FileName}",
                        "Export Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to export logs:\n{ex.Message}",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void BtnPause_Click(object sender, RoutedEventArgs e)
        {
            _isPaused = !_isPaused;

            if (_isPaused)
            {
                BtnPause.Content = "▶️ Resume";
                TxtStatus.Text = "⏸️ Monitoring paused";
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(255, 152, 0));
            }
            else
            {
                BtnPause.Content = "⏸️ Pause";
                TxtStatus.Text = "Monitoring active...";
                TxtStatus.Foreground = new SolidColorBrush(Color.FromRgb(102, 102, 102));
            }
        }

        #endregion

        /// <summary>
        /// Log entry model for binding
        /// </summary>
        public class LogEntry : INotifyPropertyChanged
        {
            private string _level;
            private string _message;
            private string _loggerName;
            private DateTime _timestamp;
            private Brush _levelColor;

            public DateTime Timestamp
            {
                get => _timestamp;
                set
                {
                    _timestamp = value;
                    OnPropertyChanged(nameof(Timestamp));
                }
            }

            public string Level
            {
                get => _level;
                set
                {
                    _level = value;
                    OnPropertyChanged(nameof(Level));
                }
            }

            public string Message
            {
                get => _message;
                set
                {
                    _message = value;
                    OnPropertyChanged(nameof(Message));
                }
            }

            public string LoggerName
            {
                get => _loggerName;
                set
                {
                    _loggerName = value;
                    OnPropertyChanged(nameof(LoggerName));
                }
            }

            public Brush LevelColor
            {
                get => _levelColor;
                set
                {
                    _levelColor = value;
                    OnPropertyChanged(nameof(LevelColor));
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            protected virtual void OnPropertyChanged(string propertyName)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }

    /// <summary>
    /// Custom logger provider that integrates with LiveLogViewer
    /// Usage: Register in DI and inject ILogger<T> in services
    /// </summary>
    public class LiveLoggerProvider : ILoggerProvider
    {
        private readonly LiveLogViewer _logViewer;

        public LiveLoggerProvider(LiveLogViewer logViewer)
        {
            _logViewer = logViewer;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new LiveLogger(categoryName, _logViewer);
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        private class LiveLogger : ILogger
        {
            private readonly string _categoryName;
            private readonly LiveLogViewer _logViewer;

            public LiveLogger(string categoryName, LiveLogViewer logViewer)
            {
                _categoryName = categoryName;
                _logViewer = logViewer;
            }

            public IDisposable BeginScope<TState>(TState state) => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
                Exception exception, Func<TState, Exception, string> formatter)
            {
                if (!IsEnabled(logLevel))
                    return;

                var message = formatter(state, exception);
                _logViewer.AddLog(logLevel, message, exception, _categoryName);
            }
        }
    }
}
