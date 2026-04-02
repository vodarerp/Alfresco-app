using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace Alfresco.Contracts.Oracle.Models
{
    public class PreviewDocStaging
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }
        public string? NodeId { get; set; }
        public string? Name { get; set; }
        public string? NodeType { get; set; }
        public string? ParentId { get; set; }
        public string? ParentFolderName { get; set; }
        public string? DocDescription { get; set; }
        public string? OriginalDocumentCode { get; set; }
        public string? NewDocumentCode { get; set; }
        public string? OldAlfrescoStatus { get; set; }
        public string? NewAlfrescoStatus { get; set; }
        public int IsActive { get; set; }
        public string? DocumentType { get; set; }
        public string? DocumentTypeMigration { get; set; }
        public string? DossierType { get; set; }
        public string? TargetDossierType { get; set; }
        public string? DossierDestinationFolderId { get; set; }
        public string? DossierDestinationFolderName { get; set; }
        public int DossierDestinationFolderIsCreated { get; set; }
        public string? Status { get; set; }
        public string? CoreId { get; set; }
        public string? ClientSegment { get; set; }
        public string? Source { get; set; }
        public string? CategoryCode { get; set; }
        public string? CategoryName { get; set; }
        public string? ContractNumber { get; set; }
        public string? ProductType { get; set; }
        public string? AccountNumbers { get; set; }
        public DateTime? OriginalCreatedAt { get; set; }
        public string? NewDocumentName { get; set; }
        public string? OriginalDocumentName { get; set; }
        public string? FinalDocumentType { get; set; }
        public DateTime RecordInserted { get; set; }
        public DateTime? RecordExportedMigration { get; set; }

        // Client API podaci
        public string? ClientApiMbrJmbg { get; set; }
        public string? ClientApiClientName { get; set; }
        public string? ClientApiClientType { get; set; }
        public string? ClientApiClientSubtype { get; set; }
        public string? ClientApiResidency { get; set; }
        public string? ClientApiSegment { get; set; }
        public string? ClientApiStaff { get; set; }
        public string? ClientApiOpuUser { get; set; }
        public string? ClientApiOpuRealization { get; set; }
        public string? ClientApiBarclex { get; set; }
        public string? ClientApiCollaborator { get; set; }
        public string? ClientApiBarCLEXName { get; set; }
        public string? ClientApiBarCLEXOpu { get; set; }
        public string? ClientApiBarCLEXGroupName { get; set; }
        public string? ClientApiBarCLEXGroupCode { get; set; }
        public string? ClientApiBarCLEXCode { get; set; }

        //Backup properties
        public string? Properties { get; set; }
    }
}