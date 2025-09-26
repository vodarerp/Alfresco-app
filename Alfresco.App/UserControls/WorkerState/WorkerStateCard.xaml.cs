
using Alfresco.App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Migration.Workers.Enum;
using Migration.Workers.Interfaces;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;

using System.Xml.Linq;

namespace Alfresco.App.UserControls.WorkerState
{
    /// <summary>
    /// Interaction logic for WorkerStateCard.xaml
    /// </summary>
    public partial class WorkerStateCard : UserControl, INotifyPropertyChanged
    {
        private readonly DispatcherTimer _uiTimer;
        private readonly WorkerSetting _settings;

        #region -DisplayName- property
        private String _DisplayName;
        public String DisplayName
        {
            get { return _DisplayName; }
            set
            {
                if (_DisplayName != value)
                {
                    _DisplayName = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        #region -State- property
        private String _State;
        public String State
        {
            get { return _State; }
            set
            {
                if (_State != value)
                {
                    _State = value;
                    NotifyPropertyChanged();
                }
            }
        }
        #endregion

        #region -MoveEnable- property
        private bool _MoveEnable;
        public bool MoveEnable
        {
            get { return _MoveEnable; }
            set
            {
                if (_MoveEnable != value)
                {
                    _MoveEnable = value;
                    NotifyPropertyChanged();
                }
            }
        }
#endregion
        #region -FolderEnable- property
        private bool _FolderEnable;
        public bool FolderEnable
        {
            get { return _FolderEnable; }
            set
            {
                if (_FolderEnable != value)
                {
                    _FolderEnable = value;
                    NotifyPropertyChanged();
                }
            }
        }
#endregion
        #region -DocumentEnable- property
        private bool _DocumentEnable;
        public bool DocumentEnable
        {
            get { return _DocumentEnable; }
            set
            {
                if (_DocumentEnable != value)
                {
                    _DocumentEnable = value;
                    NotifyPropertyChanged();
                }
            }
        }
#endregion


        public static readonly DependencyProperty WorkerProperty =
        DependencyProperty.Register(
            nameof(Worker),
            typeof(IWorkerController),
            typeof(WorkerStateCard),
            new PropertyMetadata(null, OnWorkerChanged));

        public IWorkerController? Worker
        {
            get => (IWorkerController?)GetValue(WorkerProperty);
            set
            {
                SetValue(WorkerProperty, value);
                NotifyPropertyChanged();
            }
        }

        #region -IsChecked- property
        private bool _IsChecked;
        public bool IsChecked
        {
            get { return _IsChecked; }
            set
            {
                if (_IsChecked != value)
                {
                    _IsChecked = value;
                    NotifyPropertyChanged();
                }
            }
        }
#endregion


        public WorkerStateCard()
        {
            InitializeComponent();
            //DataContext = this;
            _settings = App.AppHost.Services.GetRequiredService<IOptions<WorkerSetting>>().Value;
            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            //_uiTimer.Tick += (_, __) => UpdateView();
            Loaded += (_, __) => _uiTimer.Start();
            Unloaded += (_, __) => _uiTimer.Stop();


            MoveEnable = _settings.EnableMoveWorker;
            FolderEnable = _settings.EnableFolderWorker;
            DocumentEnable = _settings.EnableDocumentWorker;
        }

        //#region -Worker- property
        //private IWorkerController _Worker;
        //public IWorkerController Worker
        //{
        //    get { return _Worker; }
        //    set
        //    {
        //        if (_Worker != value)
        //        {
        //            _Worker = value;
        //            NotifyPropertyChanged();
        //        }
        //    }
        //}
        //#endregion

        

        private static void OnWorkerChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            //var ctrl = (WorkerStateCard)d;
            //ctrl.UpdateView();

            var ctrl = (WorkerStateCard)d;

            if (e.OldValue is INotifyPropertyChanged oldPc)
                oldPc.PropertyChanged -= ctrl.Worker_PropertyChanged;

            if (e.NewValue is INotifyPropertyChanged newPc)
                newPc.PropertyChanged += ctrl.Worker_PropertyChanged;

            ctrl.UpdateView();
        }

        private void Worker_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(IWorkerController.State)
                || e.PropertyName is nameof(IWorkerController.DisplayName)
                || e.PropertyName is nameof(IWorkerController.IsEnabled))
            {
                if (!Dispatcher.CheckAccess())
                    Dispatcher.Invoke(UpdateView);
                else
                    UpdateView();
            }
            
        }

        private void UpdateView()
        {

            DisplayName = Worker?.DisplayName ?? "Displaty name";
            State = $" State: {Worker?.State}";
            //if (Worker == null)
            //{
            //    txtName.Text = "(no worker)";
            //    txtState.Text = "-";
            //    btnStart.IsEnabled = false;
            //    btnStop.IsEnabled = false;
            //    return;
            //}

            //txtName.Text = Worker.DisplayName;
            //txtState.Text = $"State: {Worker.State}";

            // prosta logika za dugmad
           // btnStart.IsEnabled = !Worker!.IsEnabled || Worker.State == WorkerEnums.WorkerState.Idle || Worker.State == WorkerEnums.WorkerState.Failed;
           // btnStop.IsEnabled = Worker.IsEnabled && Worker.State == WorkerEnums.WorkerState.Running;
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

        private void btnStart_Click(object sender, RoutedEventArgs e)
        {
            Worker?.StartService();
        }

        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            Worker?.StopService();
        }

        private void tglBtn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is ToggleButton tb)
            {
                if (tb.IsChecked.HasValue)
                {
                    if (tb.IsChecked.Value)
                    {
                        Worker?.StartService();
                    }
                    else
                        Worker?.StopService();

                }
            }
        }
    }
}
