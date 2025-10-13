using Alfresco.App.Helpers;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
using System.Windows.Threading;

namespace Alfresco.App.UserControls
{
    /// <summary>
    /// Interaction logic for UsageHeader.xaml
    /// </summary>
    public partial class UsageHeader : UserControl, INotifyPropertyChanged
    {

        private readonly SystemPerformanceMonitor _monitor = new();
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(2) };


        #region -CpuUsage- property
        private string _CpuUsage;
        public string CpuUsage
        {
            get { return _CpuUsage; }
            set
            {
                if (_CpuUsage != value)
                {
                    _CpuUsage = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        #region -MemoryUsage- property
        private string _MemoryUsage;
        public string MemoryUsage
        {
            get { return _MemoryUsage; }
            set
            {
                if (_MemoryUsage != value)
                {
                    _MemoryUsage = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion
        public UsageHeader()
        {
            DataContext = this;
            InitializeComponent();

            

        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            var cpu = _monitor.GetCpuTotalPercent();

            CpuUsage = $"{cpu:F0} %";


            var mem = _monitor.GetSystemMemory();
            
            MemoryUsage = $"{mem.UsedGB:F2} GB";
            //MemoryUsage = $"{mem.UsedGB:F2} GB / {mem.TotalGB:F2} GB";

            //throw new NotImplementedException();
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

        private  async void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            await _monitor.PrimeAsync();
            _timer.Tick += Timer_Tick;
            _timer.Start();

        }

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            _monitor.Dispose();
            _timer.Stop();
        }
    }
}
