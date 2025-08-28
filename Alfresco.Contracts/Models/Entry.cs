using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Models
{
    public class Entry
    {
        public DateTimeOffset CreatedAt { get; set; }
        public bool IsFolder { get; set; }
        public bool IsFile { get; set; }
        public UserInfo CreatedByUser { get; set; } = default!;
        public DateTimeOffset ModifiedAt { get; set; }
        public UserInfo ModifiedByUser { get; set; } = default!;
        public string Name { get; set; } = string.Empty;
        public string Id { get; set; } = string.Empty;
        public string NodeType { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
    }
}
