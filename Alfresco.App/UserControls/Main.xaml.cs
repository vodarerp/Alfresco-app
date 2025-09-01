using Alfresco.Apstraction.Interfaces;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Response;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Oracle.Apstaction.Interfaces;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;
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
    /// Interaction logic for Main.xaml
    /// </summary>
    public partial class Main : UserControl, INotifyPropertyChanged
    {

        private readonly IAlfrescoApi _alfrescoService;
        private readonly IAlfrescoWriteApi _alfrescoWriteService;
        private readonly IAlfrescoReadApi _alfrescoReadService;


        private readonly IDocStagingRepository _docStagingRepository;


        //public ObservableCollection<Entry> Entries { get; set; } = new();

        #region -Documents- property
        private ObservableCollection<DocStaging> _Documents;
        public ObservableCollection<DocStaging> Documents
        {
            get { return _Documents; }
            set
            {
                if (_Documents != value)
                {
                    _Documents = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        #region -Entries- property
        private  ObservableCollection<Entry> _Entries;
        public  ObservableCollection<Entry> Entries
        {
            get { return _Entries; }
            set
            {
                if (_Entries != value)
                {
                    _Entries = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        //public static ObservableCollection<Entry> Entries { get; set; } = new();

        public Main()
        {
            DataContext = this;
            InitializeComponent();
            Entries = new();
            _alfrescoWriteService = App.AppHost.Services.GetRequiredService<IAlfrescoWriteApi>();
            _alfrescoReadService = App.AppHost.Services.GetRequiredService<IAlfrescoReadApi>();
            //_alfrescoService = App.AppHost.Services.GetRequiredService<IAlfrescoApi>(); 
            _docStagingRepository = App.AppHost.Services.GetRequiredService<IDocStagingRepository>();
        }

        private async void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            var res = await _alfrescoReadService.GetNodeChildrenAsync("8f2105b4-daaf-4874-9e8a-2152569d109b");

            //var resp = JsonConvert.DeserializeObject<NodeChildrenResponse>(res);

            Entries = new ObservableCollection<Entry>(res.List.Entries.Select(x => x.Entry));


            var x = 1;
        }

        private async void btnSearch_Click(object sender, RoutedEventArgs e)
        {
            var request = new Contracts.Request.PostSearchRequest();

            var resp = await _alfrescoService.PostSearch(request);

            if ((resp!= null ) && (resp?.List?.Entries?.Count() > 0))
                Entries = new ObservableCollection<Entry>(resp.List.Entries.Select(x => x.Entry));


        }

        private async void btnSearch_Click_1(object sender, RoutedEventArgs e)
        {
            var test =  (await _docStagingRepository.GetListAsync()).ToList();

            Documents = new ObservableCollection<DocStaging>(test);

            var x = 1;

            
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

        private async void btnLoad_Click_1(object sender, RoutedEventArgs e)
        {
            if (Documents.Any())
            {
                var x = await _docStagingRepository.AddAsync(Documents?.FirstOrDefault(o => o.Id == 1));

                var xx = 1;
            }

        }

        private async void btnInsert_Click(object sender, RoutedEventArgs e)
        {
            var forInsert = new DocStaging()
            {
                FromPath = "Test .net",
                NodeId = "Test node"
            };

            var res = await _docStagingRepository.AddAsync(forInsert);
        }

        private async void btnInsert_Click_1(object sender, RoutedEventArgs e)
        {
            //var forInsert = new List<DocStaging>()
            //{
            //    new DocStaging()
            //    {
            //        FromPath = "Test .net 1",
            //        NodeId = "Test node 1"
            //    },
            //    new DocStaging()
            //    {
            //        FromPath = "Test .net 2",
            //        NodeId = "Test node 2"
            //    },
            //    new DocStaging()
            //    {
            //        FromPath = "Test .net 3",
            //        NodeId = "Test node 3"
            //    }
            //};

            if (Entries.Any())
            {
                var forInsert = Entries.ToDocStagingList();
                var res = await _docStagingRepository.InsertManyAsync(forInsert);
            }
                

        }

        private async void btnMove_Click(object sender, RoutedEventArgs e)
        {
            var res = await _alfrescoService.MoveDocumentAsync("nodeIdToMov", "destFolderID");
        }
    }
}
