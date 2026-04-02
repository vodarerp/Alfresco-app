using System;
using System.Collections.Generic;

namespace Alfresco.Contracts.Options
{
   
    public class DocumentSearchOptions
    {
        
        public List<string> DocTypes { get; set; } = new();

        
        public List<string> FolderTypes { get; set; } = new();

       
        public int BatchSize { get; set; } = 100;

        
        public int MaxDegreeOfParallelism { get; set; } = 5;

        
        public int DelayBetweenBatchesInMs { get; set; } = 0;

       
        public bool UseDateFilter { get; set; } = false;

       
        public string? DateFrom { get; set; }

       
        public string? DateTo { get; set; }
    }
}
