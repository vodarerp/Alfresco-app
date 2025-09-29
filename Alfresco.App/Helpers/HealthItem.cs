using Microsoft.Extensions.Diagnostics.HealthChecks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.App.Helpers
{
    public class HealthItem
    {
        public string? Name { get; set; }
        public  HealthStatus Status { get; set; }
        public int DurationInMs { get; set; }
        public string? Description { get; set; }
        public string? Error { get; set; }
        public string? Tags { get; set; }
    }
}
