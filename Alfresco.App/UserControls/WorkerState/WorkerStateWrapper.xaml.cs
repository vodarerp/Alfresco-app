using Microsoft.Extensions.DependencyInjection;
using Migration.Workers.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace Alfresco.App.UserControls.WorkerState
{
    /// <summary>
    /// Interaction logic for WorkerStateWrapper.xaml
    /// </summary>
    public partial class WorkerStateWrapper : UserControl, INotifyPropertyChanged
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
        public WorkerStateWrapper()
        {
            DataContext = this;

            InitializeComponent();
            _workers = App.AppHost.Services.GetServices<IWorkerController>().ToArray();
            //var x = App.AppHost.Services.GetServices<IWorkerController>().ToList();


            //Workers = new ObservableCollection<IWorkerController>(x);

        }


        #region INotifyPropertyChange implementation
        public event PropertyChangedEventHandler PropertyChanged;

        // This method is called by the Set accessor of each property.
        // The CallerMemberName attribute that is applied to the optional propertyName
        // parameter causes the property name of the caller to be substituted as an argument.
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            Workers = new ObservableCollection<IWorkerController>(App.AppHost.Services.GetServices<IWorkerController>().ToList());

            //var m = Workers.First(o => o.Key == "move");
            //Debug.WriteLine($"REFERENCA: {RuntimeHelpers.GetHashCode(m)}");
        }
    }
}
