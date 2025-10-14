using Alfresco.Abstraction.Interfaces;
using Microsoft.Extensions.DependencyInjection;
using Migration.Extensions.Oracle;
using Oracle.Abstraction.Interfaces;
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

namespace Alfresco.App.UserControls.TableMetrics
{
    /// <summary>
    /// Interaction logic for UCFolderStaging.xaml
    /// </summary>
    public partial class UCFolderStaging : UserControl, INotifyPropertyChanged
    {
        private readonly IFolderStagingRepository _service;

        //public ObservableCollection<KeyValuePair<string,int>> Statuses { get; set; }

        #region -Statuses- property
        private ObservableCollection<KeyValuePair<string, int>> _Statuses;
        public ObservableCollection<KeyValuePair<string, int>> Statuses
        {
            get { return _Statuses; }
            set
            {
                if (_Statuses != value)
                {
                    _Statuses = value;
                    NotifyPropertyChanged();
                }
            }
        }
#endregion

        public string TableName
        {
            get { return (string)GetValue(TableNameProperty); }
            set { SetValue(TableNameProperty, value); NotifyPropertyChanged(); }
        }

        // Using a DependencyProperty as the backing store for TableName.  This enables animation, styling, binding, etc...
        public static readonly DependencyProperty TableNameProperty =
            DependencyProperty.Register("TableName", typeof(string), typeof(UCFolderStaging), new PropertyMetadata(0));


        public UCFolderStaging()
        {
            DataContext = this;
            InitializeComponent();
            _service = App.AppHost.Services.GetRequiredService<IFolderStagingRepository>();
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

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            //var x = _service.GetFolderStatisticAsync();
        }
    }
}
