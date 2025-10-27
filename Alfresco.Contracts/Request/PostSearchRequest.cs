using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Request
{
    public class PostSearchRequest
    {
        public QueryRequest? Query { get; set; } = new();

        public PagingRequest? Paging { get; set; } = new();
        public List<SortRequest>? Sort { get; set; } = new() { new() };

        public string[] Include { get; set;  } = new string[] {  };
    }
}
