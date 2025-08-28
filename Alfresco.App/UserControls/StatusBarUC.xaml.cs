using Alfresco.Apstraction.Interfaces;
using Microsoft.Extensions.DependencyInjection;
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

namespace Alfresco.App.UserControls
{
    /// <summary>
    /// Interaction logic for StatusBarUC.xaml
    /// </summary>
    public partial class StatusBarUC : UserControl, INotifyPropertyChanged
    {

        private readonly IAlfrescoApi _alfrescoService;
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
            _alfrescoService = App.AppHost.Services.GetRequiredService<IAlfrescoApi>(); 

            this.Loaded += StatusBarUC_Loaded;

        }

        private async void StatusBarUC_Loaded(object sender, RoutedEventArgs e)
        {
            Connected = await _alfrescoService.PingAsync();
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
