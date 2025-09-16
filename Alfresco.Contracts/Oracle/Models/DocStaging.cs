using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Alfresco.Contracts.Oracle.Models
{
    //[Table("DOCSTAGING11")]
    public class DocStaging
    {

        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public string NodeId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public bool IsFile { get; set; }
        public string NodeType { get; set; } = string.Empty;
        public string ParentId { get; set; } = string.Empty;
        public string FromPath { get; set; } = string.Empty;
        public string ToPath { get; set; } = string.Empty;
        public string Status { get; set; } = "READY"; // NEW, DONE, ERR
        public int RetryCount { get; set; }
        public string? ErrorMsg { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}



