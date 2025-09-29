using Alfresco.App.Helpers;
using Alfresco.Apstraction.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

namespace Alfresco.App.UserControls
{
    /// <summary>
    /// Interaction logic for StatusBarUC.xaml
    /// </summary>
    public partial class StatusBarUC : UserControl, INotifyPropertyChanged
    {

        private readonly IAlfrescoReadApi _alfrescoService;

        #region -HealthItems- property
        private ObservableCollection<HealthItem> _HealthItems = new();
        public ObservableCollection<HealthItem> HealthItems
        {
            get { return _HealthItems; }
            set
            {
                if (_HealthItems != value)
                {
                    _HealthItems = value;
                    NotifyPropertyChanged();
                }
            }
        }
#endregion

        #region -HealtChecks- property
        private ObservableCollection<HealthReportEntry> _HealtChecks = new();
        public ObservableCollection<HealthReportEntry> HealtChecks
        {
            get { return _HealtChecks; }
            set
            {
                if (_HealtChecks != value)
                {
                    _HealtChecks = value;
                    NotifyPropertyChanged();
                }
            }
        }
#endregion
        //private readonly IServiceProvider _sp;
        #region -Connected- property
        private  bool _Connected;
        public  bool Connected
        {
            get { return _Connected; }
            set
            {
                if (_Connected != value)
                {
                    _Connected = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        
        public StatusBarUC()
        {
            DataContext = this;
            InitializeComponent();
            _alfrescoService = App.AppHost.Services.GetRequiredService<IAlfrescoReadApi>(); 

            this.Loaded += StatusBarUC_Loaded;

           

        }

        private async void StatusBarUC_Loaded(object sender, RoutedEventArgs e)
        {
            Connected = await _alfrescoService.PingAsync();

            var healt = App.AppHost.Services.GetRequiredService<HealthCheckService>();
            var report = await healt.CheckHealthAsync();

            foreach(var entri in report.Entries)
            {
                var val = entri.Value;
                HealthItems.Add(new HealthItem
                {
                    Name = entri.Key,
                    DurationInMs = (int)val.Duration.TotalMilliseconds,
                    Description = val.Description,
                    Error = val.Exception?.Message,
                    Tags = string.Join(", ", val.Tags)
                });
            }


            //var l = report.Entries.Values.ToList();
            //HealtChecks = new ObservableCollection<HealthReportEntry>(report.Entries.Values.ToList());
        }

        #region INotifyPropertyChange implementation
        public event PropertyChangedEventHandler PropertyChanged;
        
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var x = 123;
        }
    }
}
