using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.VisualBasic;

namespace Alfresco.App.Helpers
{
    public class SystemPerformanceMonitor : IDisposable
    {
        private readonly PerformanceCounter _cpuTotal =
            new("Processor", "% Processor Time", "_Total", readOnly: true);
        private readonly PerformanceCounter _memAvail =
            new("Memory", "Available MBytes", readOnly: true);
        private bool _primed;

        public async Task PrimeAsync(int sampleMs = 1000, CancellationToken ct = default)
        {
            if (_primed) return;
            _ = _cpuTotal.NextValue();
            _ = _memAvail.NextValue();
            await Task.Delay(sampleMs, ct);
            _primed = true;
        }

        /// <summary> CPU svih jezgara u % (0–100). Pozivati periodično. </summary>
        public float GetCpuTotalPercent() => _cpuTotal.NextValue();

        /// <summary> (UsedGB, TotalGB) memorije na nivou sistema. </summary>
        public (double UsedGB, double TotalGB) GetSystemMemory()
        {
            double total = 0, free = 0;
            using var searcher = new ManagementObjectSearcher(
                "SELECT TotalVisibleMemorySize, FreePhysicalMemory FROM Win32_OperatingSystem");
            foreach (var obj in searcher.Get())
            {
                total = Convert.ToDouble(obj["TotalVisibleMemorySize"]) / 1024 / 1024;
                free = Convert.ToDouble(obj["FreePhysicalMemory"]) / 1024 / 1024;
            }

            return (Math.Round(total - free, 2), Math.Round(total, 2));

        }

        /// <summary> Radni set tvog procesa u GB (ono što većina dashboarda zove “Memory Usage”). </summary>
        public double GetCurrentProcessWorkingSetGB()
        {
            using var p = Process.GetCurrentProcess();
            return Math.Round(p.WorkingSet64 / 1024d / 1024d / 1024d, 2);
        }

        public void Dispose()
        {
            _cpuTotal?.Dispose();
            _memAvail?.Dispose();
        }
    }
}
