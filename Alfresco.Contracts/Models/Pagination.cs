using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Models
{
    public class Pagination
    {
        public int Count { get; set; }
        public bool HasMoreItems { get; set; }
        public int TotalItems { get; set; }
        public int SkipCount { get; set; }
        public int MaxItems { get; set; }
    }
}
