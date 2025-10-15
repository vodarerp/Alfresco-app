using Migration.Workers.Interfaces;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using static Migration.Workers.Enum.WorkerEnums;

namespace Alfresco.App.UserControls.WorkerState
{
    /// <summary>
    /// Interaction logic for WorkerMonitorCard.xaml
    /// </summary>
    public partial class WorkerMonitorCard : UserControl, INotifyPropertyChanged
    {
        public static DependencyProperty WorkerProperty =
            DependencyProperty.Register(
                nameof(Worker),
                typeof(IWorkerController),
                typeof(WorkerMonitorCard),
                new PropertyMetadata(null, OnWorkerChanged));

        public IWorkerController? Worker
        {
            get => (IWorkerController?)GetValue(WorkerProperty);
            set
            {
                SetValue(WorkerProperty, value);
                NotifyPropertyChanged();
            }
        }

        public WorkerMonitorCard()
        {
            InitializeComponent();
        }

        private static void OnWorkerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var ctrl = (WorkerMonitorCard)d;

            if (e.OldValue is INotifyPropertyChanged oldPc)
                oldPc.PropertyChanged -= ctrl.Worker_PropertyChanged;

            if (e.NewValue is INotifyPropertyChanged newPc)
                newPc.PropertyChanged += ctrl.Worker_PropertyChanged;

            ctrl.UpdateView();
        }

        private void Worker_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(IWorkerController.State)
                || e.PropertyName is nameof(IWorkerController.DisplayName)
                || e.PropertyName is nameof(IWorkerController.IsEnabled)
                || e.PropertyName is nameof(IWorkerController.TotalItems)
                || e.PropertyName is nameof(IWorkerController.ProcessedItems)
                || e.PropertyName is nameof(IWorkerController.RemainingItems)
                || e.PropertyName is nameof(IWorkerController.ProgressPercentage))
            {
                if (!Dispatcher.CheckAccess())
                    Dispatcher.Invoke(UpdateView);
                else
                    UpdateView();
            }
        }

        private void UpdateView()
        {
            if (Worker == null) return;

            // Update status border color based on state
            switch (Worker.State)
            {
                case Migration.Workers.Enum.WorkerEnums.WorkerState.Running:
                    statusBorder.Background = new SolidColorBrush(Color.FromRgb(76, 175, 80)); // Green
                    txtState.Foreground = Brushes.White;
                    break;
                case Migration.Workers.Enum.WorkerEnums.WorkerState.Idle:
                    statusBorder.Background = new SolidColorBrush(Color.FromRgb(158, 158, 158)); // Gray
                    txtState.Foreground = Brushes.White;
                    break;
                case Migration.Workers.Enum.WorkerEnums.WorkerState.Failed:
                    statusBorder.Background = new SolidColorBrush(Color.FromRgb(244, 67, 54)); // Red
                    txtState.Foreground = Brushes.White;
                    break;
                case Migration.Workers.Enum.WorkerEnums.WorkerState.Stopping:
                    statusBorder.Background = new SolidColorBrush(Color.FromRgb(255, 152, 0)); // Orange
                    txtState.Foreground = Brushes.White;
                    break;
                case Migration.Workers.Enum.WorkerEnums.WorkerState.Stopped:
                    statusBorder.Background = new SolidColorBrush(Color.FromRgb(96, 125, 139)); // Blue Gray
                    txtState.Foreground = Brushes.White;
                    break;
            }
        }

        #region INotifyPropertyChange implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private void tglBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton tb && tb.IsChecked.HasValue)
            {
                if (tb.IsChecked.Value)
                    Worker?.StartService();
                else
                    Worker?.StopService();
            }
        }
    }
}
