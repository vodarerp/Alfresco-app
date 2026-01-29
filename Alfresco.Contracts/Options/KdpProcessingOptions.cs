using System;
using System.Collections.Generic;

namespace Alfresco.Contracts.Options
{
   
    public class KdpProcessingOptions
    {
       
        public string? AncestorFolderId { get; set; }        
        public int BatchSize { get; set; } 
        public List<string> DocTypes { get; set; }

        public int MaxDegreeOfParallelism { get; set; }
    }
}
