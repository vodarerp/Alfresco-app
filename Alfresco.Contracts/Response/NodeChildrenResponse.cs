using Alfresco.Contracts.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Response
{
    public class NodeChildrenResponse
    {
        public NodeChildrenList List { get; set; } = default;
    }
}
