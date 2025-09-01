using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Request
{
    public  class MoveRequest
    {
        public string TargetParentId { get; set; }
        public string? Name { get; set; }
       
    }
}
