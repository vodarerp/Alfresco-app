using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CA_MockData
{
    public sealed class Config
    {
        public string BaseUrl { get; set; } = default!;
        public string Username { get; set; } = default!;
        public string Password { get; set; } = default!;
        public string RootParentId { get; set; } = default!;
        public int FolderCount { get; set; }
        public int DocsPerFolder { get; set; }
        public int DegreeOfParallelism { get; set; }
        public int MaxRetries { get; set; }
        public int RetryBaseDelayMs { get; set; }
    }
}
