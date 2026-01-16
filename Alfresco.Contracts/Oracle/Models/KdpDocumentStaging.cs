using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Alfresco.Contracts.Oracle.Models
{
    
    [Table("KdpDocumentStaging")]
    public class KdpDocumentStaging
    {
        
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        public string NodeId { get; set; } = string.Empty;

        public string? DocumentName { get; set; }

        public string? DocumentPath { get; set; }

       
        public string? ParentFolderId { get; set; }

        public string? ParentFolderName { get; set; }

        public string? DocumentType { get; set; }

        public string? DocumentStatus { get; set; }

       
        public DateTime? CreatedDate { get; set; }

        public string? AccountNumbers { get; set; }

        public string? AccFolderName { get; set; }

       
        public string? CoreId { get; set; }

        public DateTime ProcessedDate { get; set; } = DateTime.Now;

        public string? Source { get; set; }

        public string? Properties { get; set; }

        public DateTime? MigrationCreationDate { get; set; }
    }
}
