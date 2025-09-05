using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Models
{
    public class NodeChildrenList
    {
        public Pagination? Pagination { get; set; }
        public List<ListEntry>? Entries { get; set; }

        public Entry? Source { get; set; }
    }
}
