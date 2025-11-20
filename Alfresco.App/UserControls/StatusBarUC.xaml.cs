using Alfresco.Abstraction.Interfaces;
using Alfresco.App.Helpers;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
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
        private readonly IOptions<Alfresco.Contracts.SqlServer.SqlServerOptions> _options;
        private readonly IDocumentMappingService _mappingService;
        private readonly IClientApi _clientApi;

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

        #region -DbConnected- property
        private bool _DbConnected;
        public bool DbConnected
        {
            get { return _DbConnected; }
            set
            {
                if (_DbConnected != value)
                {
                    _DbConnected = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        #region -ClientApiConnected- property
        private bool _ClientApiConnected;
        public bool ClientApiConnected
        {
            get { return _ClientApiConnected; }
            set
            {
                if (_ClientApiConnected != value)
                {
                    _ClientApiConnected = value;
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
            _mappingService = App.AppHost.Services.GetRequiredService<IDocumentMappingService>();
            _clientApi = App.AppHost.Services.GetRequiredService<IClientApi>();

            _options = App.AppHost.Services.GetRequiredService<IOptions<Alfresco.Contracts.SqlServer.SqlServerOptions>>();

            this.Loaded += StatusBarUC_Loaded;



        }

        private async void TestConnection()
        {
            try
            {
                ClientApiConnected = await _clientApi.ValidateClientExistsAsync("test");
                //ClientApiConnected = true;
            }
            catch
            {
                ClientApiConnected = false;
            }

            try
            {
                using (var conn = new SqlConnection(_options.Value.ConnectionString))
                {
                    conn.Open();

                    DbConnected = conn.State == System.Data.ConnectionState.Open;
                }
            }
            catch 
            {

                DbConnected = false;
            }

            try
            {
                Connected = await _alfrescoService.PingAsync();   
            }
            catch 
            {
                Connected = false;                
            }
        }

        private async void StatusBarUC_Loaded(object sender, RoutedEventArgs e)
        {
            TestConnection();
            
            //var healt = App.AppHost.Services.GetRequiredService<HealthCheckService>();
            //var report = await healt.CheckHealthAsync();

            //foreach(var entri in report.Entries)
            //{
            //    var val = entri.Value;
            //    HealthItems.Add(new HealthItem
            //    {
            //        Name = entri.Key,
            //        DurationInMs = (int)val.Duration.TotalMilliseconds,
            //        Description = val.Description,
            //        Error = val.Exception?.Message,
            //        Tags = string.Join(", ", val.Tags)
            //    });
            //}


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

        private async void TextBlock_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var x = await _mappingService.FindByOriginalNameAsync("Personal Notice");
            var xx = await _mappingService.GetAllMappingsAsync();
        }

       
      
    }
}
