using Microsoft.Extensions.DependencyInjection;
using Migration.Workers.Interfaces;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Alfresco.App.UserControls.WorkerState
{
    /// <summary>
    /// Interaction logic for WorkerMonitorWrapper.xaml
    /// </summary>
    public partial class WorkerMonitorWrapper : UserControl, INotifyPropertyChanged
    {
        private readonly IReadOnlyList<IWorkerController> _workers;

        #region -Workers- property
        private ObservableCollection<IWorkerController> _Workers;
        public ObservableCollection<IWorkerController> Workers
        {
            get { return _Workers; }
            set
            {
                if (_Workers != value)
                {
                    _Workers = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        public WorkerMonitorWrapper()
        {
            DataContext = this;
            InitializeComponent();
            _workers = App.AppHost.Services.GetServices<IWorkerController>().ToArray();
        }

        #region INotifyPropertyChange implementation
        public event PropertyChangedEventHandler? PropertyChanged;

        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            Workers = new ObservableCollection<IWorkerController>(
                App.AppHost.Services.GetServices<IWorkerController>().ToList());
        }
    }
}
