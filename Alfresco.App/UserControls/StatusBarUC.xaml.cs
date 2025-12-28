using Alfresco.Abstraction.Interfaces;
using Alfresco.App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Migration.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
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
        private readonly IDocumentMappingService _mappingService;
        private readonly IClientApi _clientApi;
        private readonly IUnitOfWork _unitOfWork;

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

        #region -IsResetVisible- property
        private bool _IsResetVisible;
        public bool IsResetVisible
        {
            get { return _IsResetVisible; }
            set
            {
                if (_IsResetVisible != value)
                {
                    _IsResetVisible = value;
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
            _unitOfWork = App.AppHost.Services.GetRequiredService<IUnitOfWork>();

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
                await _unitOfWork.BeginAsync(System.Data.IsolationLevel.ReadUncommitted);
                await _unitOfWork.CommitAsync();
                DbConnected = true;
            }
            catch
            {
                DbConnected = false;
                try
                {
                    await _unitOfWork.RollbackAsync();
                }
                catch { /* Ignore rollback errors */ }
            }

            try
            {
                Connected = await _alfrescoService.PingAsync();   
            }
            catch (Exception ex) 
            {
                Connected = false;                
            }
            ResetButtonVisibilitiCheck();
        }

        private void ResetButtonVisibilitiCheck()
        {
            IsResetVisible = !(DbConnected && Connected && ClientApiConnected);
        }

        private async void StatusBarUC_Loaded(object sender, RoutedEventArgs e)
        {
            TestConnection();
            
            
        }

        #region INotifyPropertyChange implementation
        public event PropertyChangedEventHandler PropertyChanged;
        
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        #endregion

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            TestConnection();
        }

        private void ConfigButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new Window
            {
                Title = "Application Settings",
                Content = new SettingsUC(),
                Width = 850,
                Height = 650,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.CanResize
            };
            settingsWindow.ShowDialog();

            // After settings window closes, test connections again
            TestConnection();
        }
    }
}
