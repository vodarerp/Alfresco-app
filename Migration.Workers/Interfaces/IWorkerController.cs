using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Migration.Workers.Enum.WorkerEnums;

namespace Migration.Workers.Interfaces
{
    public interface IWorkerController
    {
        string Key { get; }          // npr. "move"
        string DisplayName { get; }  // npr. "Move Worker"
        WorkerState State { get; }
        bool IsEnabled { get; set; }

        DateTimeOffset? LastStarted { get; }
        DateTimeOffset? LastStopped { get; }
        Exception? LastError { get; }

        void StopService();
        void StartService();
    }
}
