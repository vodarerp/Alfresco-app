using Alfresco.Abstraction.Interfaces;
using Alfresco.Contracts.Models;
using Alfresco.Contracts.Oracle.Models;
using Alfresco.Contracts.Request;
using Mapper;
using Microsoft.Extensions.DependencyInjection;
//using Oracle.Abstraction.Interfaces;
using SqlServer.Abstraction.Interfaces;
using SqlServer.Infrastructure.Implementation;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

namespace Alfresco.App.UserControls
{
    /// <summary>
    /// Interaction logic for Main.xaml
    /// </summary>
    public partial class Main : UserControl, INotifyPropertyChanged
    {
        private volatile bool _isLiveLoggerActive; // set to true kada je LiveLogger tab aktivan
        private int _unreadPending;
        #region -UnreadErrors- property
        private int _UnreadErrors = 0;
        public int UnreadErrors
        {
            get { return _UnreadErrors; }
            set
            {
                if (_UnreadErrors != value)
                {
                    _UnreadErrors = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        private readonly IAlfrescoApi _alfrescoService;
        private readonly IAlfrescoWriteApi _alfrescoWriteService;
        private readonly IAlfrescoReadApi _alfrescoReadService;


        private readonly IDocStagingRepository _docStagingRepository;

        private readonly IFolderStagingRepository _folderStagingRepository;



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
        private ObservableCollection<Entry> _Entries;
        public ObservableCollection<Entry> Entries
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


        #region -Entries- property
        private ObservableCollection<Entry> _Folders;
        public ObservableCollection<Entry> Folders
        {
            get { return _Folders; }
            set
            {
                if (_Folders != value)
                {
                    _Folders = value;
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
            _folderStagingRepository = App.AppHost.Services.GetRequiredService<IFolderStagingRepository>();
            _isLiveLoggerActive = false;

            // Add LogViewer programmatically to avoid XAML binding thread issues
            // This ensures LogViewer is created and bound on the same UI thread
            Loaded += Main_Loaded;
        }

        private void Main_Loaded(object sender, RoutedEventArgs e)
        {
            // Set LogViewer content after control is loaded (on UI thread)
            LiveLoggerTab.Content = App.LogViewer;
            App.LogViewer.IncrementIfInactiveAction = IncrementUnreadIfInactive;
        }

        public void IncrementUnreadIfInactive()
        {
            if (!Volatile.Read(ref _isLiveLoggerActive))
            {
                Interlocked.Increment(ref _unreadPending);

                Application.Current.Dispatcher.BeginInvoke((Action)(() =>
                {
                    var x = Interlocked.Exchange(ref _unreadPending, 0);
                    if (x > 0) UnreadErrors += x;
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        public void ResetUnread()
        {
            // obavezno na UI thread-u
            if (!Application.Current.Dispatcher.CheckAccess())
            {
                Application.Current.Dispatcher.BeginInvoke((Action)ResetUnread);
                return;
            }

            // poništi i pending i prikazani broj
            Interlocked.Exchange(ref _unreadPending, 0);
            UnreadErrors = 0;
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

            if ((resp != null) && (resp?.List?.Entries?.Count() > 0))
                Entries = new ObservableCollection<Entry>(resp.List.Entries.Select(x => x.Entry));


        }

        private async void btnSearch_Click_1(object sender, RoutedEventArgs e)
        {
            var test = (await _docStagingRepository.GetListAsync()).ToList();

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
            
            
            var folders = await _folderStagingRepository.GetListAsync();

            var docs = await _docStagingRepository.GetListAsync();

            foreach(var doc in docs)
            {
                var move = await _alfrescoService.MoveDocumentAsync(doc.NodeId, doc.ToPath);
            }


            //var res = await _alfrescoService.MoveDocumentAsync("nodeIdToMov", "destFolderID");
        }

        private async void btnFolders_Click(object sender, RoutedEventArgs e)
        {

            for (var x = 0; x < 2000; x++)
            {
                var res = await _alfrescoWriteService.CreateFolderAsync("75caf18f-835d-46e0-8af1-8f835d46e088", $"TestFolder-{x}");

                if (res != "")
                {
                    for (var y = 0; y < 10; y++)
                    {
                        var fileRes = await _alfrescoWriteService.CreateFileAsync(res, $"TestFile{x}-{y}.txt");
                    }
                }
            }

            MessageBox.Show("Gotovo");

        }

        private async void btnGetFolders_Click(object sender, RoutedEventArgs e)
        {
            var req = new PostSearchRequest()
            {
                Query = new Contracts.Request.QueryRequest()
                {
                    Language = "cmis",
                    Query = "SELECT * FROM cmis:folder WHERE cmis:name LIKE '%TestFolder-%'"
                },
                Paging = new Contracts.Request.PagingRequest()
                {
                    MaxItems = 100,
                    SkipCount = 0
                },
                Sort = null
            };

            var resp = await _alfrescoReadService.SearchAsync(req);

            if (resp?.List?.Entries?.Count > 0)
            {

                Folders = new ObservableCollection<Entry>(resp.List.Entries.Select(x => x.Entry));

                var FoldersOrtacle = Folders.ToFolderStagingList();

                var resOracle = await _folderStagingRepository.InsertManyAsync(FoldersOrtacle);
                var rr = 1;
            }
        }


      
        private async void GetDocsFromFolder(object sender, RoutedEventArgs e)
        {
            if (Folders.Any())
            {

                 //Root new folder

                foreach (var folder in Folders)
                {
                    var newFolderName = folder.Name.Replace("-", "");
                    var newFolderId = await _alfrescoReadService.GetFolderByRelative("caac4e9d-27a3-4e9c-ac4e-9d27a30e9ca0", newFolderName);
                    if (string.IsNullOrEmpty(newFolderId))
                    {
                        newFolderId = await _alfrescoWriteService.CreateFolderAsync("caac4e9d-27a3-4e9c-ac4e-9d27a30e9ca0", newFolderName);
                    }
                    var res = await _alfrescoReadService.GetNodeChildrenAsync(folder.Id);
                    if (res?.List?.Entries?.Count > 0)
                    {
                       

                        var docs = res.List.Entries.Select(x => x.Entry).ToDocStagingList();
                        foreach (var item in docs)
                        {
                            item.FromPath = folder.Id;
                            item.ToPath = newFolderId;
                        }
                        var resOracle = await _docStagingRepository.InsertManyAsync(docs);
                        var rr = 1;
                    }
                }
            }
        }

        private async void Button_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var readApi = App.AppHost.Services.GetRequiredService<IAlfrescoReadApi>();

                // Test API call - Polly će automatski retry ako fail-uje!
                var result = await readApi.GetNodeChildrenAsync("e52b3112-77ee-45c1-ab31-1277ee15c1ee", CancellationToken.None);

                MessageBox.Show("Success! Polly is working! ✅");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}");
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _isLiveLoggerActive = (tcMain.SelectedIndex == 1);
            if (_isLiveLoggerActive)
            {
                ResetUnread();
                // Refresh the log view to show accumulated logs when tab becomes active
                App.LogViewer.RefreshView();
            }
        }
    }
}
